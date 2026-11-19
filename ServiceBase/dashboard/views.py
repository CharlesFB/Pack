from django.utils.dateparse import parse_datetime
import json
import re
import time
from datetime import timedelta
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, ProxyHandler, HTTPRedirectHandler, build_opener

from django.http import JsonResponse
from django.db.models import F
from django.shortcuts import render
from django.utils import timezone
from django.views.decorators.csrf import csrf_exempt, ensure_csrf_cookie
from django.views.decorators.http import require_GET, require_POST, require_http_methods

from .models import UnityProject, PackageSnapshot, BridgeTask
from .auth import project_scope
from . import bridge, catalog
from django.conf import settings
from django.core.cache import cache

ONLINE_SECONDS = 15


def visible_projects(
    request,  # Django 请求，包含登录账号和会话
):
    """
    在当前账号和设备的权限范围内排除本会话已被移除的工程。
    """
    return project_scope(request, UnityProject.objects.exclude(hidden_session=F("session_id")))


@require_http_methods(["DELETE"])
def delete_project(
    request,  # Django 请求，包含登录账号和会话
    instance_id,  # 路由中的目标工程 ID
):
    """
    标记工程当前会话已移除，不删除工程文件或历史数据。
    """
    # A session tombstone prevents the next heartbeat undoing the deletion.
    # A new Unity session automatically becomes visible; Django restarts do not.
    if not project_scope(request, UnityProject.objects).filter(pk=instance_id).update(hidden_session=F("session_id")):
        return JsonResponse({"error": "工程不存在"}, status=404)
    return JsonResponse({"ok": True})


@require_POST
def restore_project(
    request,  # Django 请求，包含登录账号和会话
    instance_id,  # 路由中的目标工程 ID
):
    """
    清除工程移除标记，使有权限的用户可重新查看记录。
    """
    if not project_scope(request, UnityProject.objects).filter(pk=instance_id).update(hidden_session=""):
        return JsonResponse({"error": "工程不存在"}, status=404)
    return JsonResponse({"ok": True})


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(
        self,  # 当前对象实例
        req,  # 发往 Unity 的 urllib 请求对象
        fp,  # 原响应的数据流
        code,  # 重定向响应的 HTTP 状态码
        msg,  # 重定向响应说明
        headers,  # 重定向响应头
        newurl,  # 被拒绝的重定向目标地址
    ):
        """
        拒绝 Unity 本地请求的 HTTP 重定向，避免转发到非预期主机。
        """
        return None


def read_json(
    request,  # Django 请求，包含登录账号和会话
):
    """
    读取 application/json 请求并要求顶层为对象，否则抛出输入错误。
    """
    if request.content_type != "application/json":
        raise ValueError("请求必须使用 application/json")
    data = json.loads(request.body)
    if not isinstance(data, dict):
        raise ValueError("JSON 必须为对象")
    return data


@ensure_csrf_cookie
@require_GET
def index(
    request,  # Django 请求，包含登录账号和会话
):
    """
    渲染工程工作台，并传入当前登录账号与设备绑定状态。
    """
    return render(request, "dashboard/index.html", {"account": request.user, "device_bound": request.session.get("bridge_device_id")})


@require_GET
def projects(
    request,  # Django 请求，包含登录账号和会话
):
    """
    按权限、名称和分页读取工程列表，合并缓存中的在线状态及快照摘要。
    """
    now = timezone.now()
    rows = []
    try:
        page = max(1, int(request.GET.get('page', 1)))
        size = min(100, max(1, int(request.GET.get('page_size', 50))))
    except ValueError:
        return JsonResponse({'error':'无效页码'}, status=400)
    queryset = visible_projects(request)
    query = request.GET.get('q', '')[:128]
    if query:
        queryset = queryset.filter(project_name__icontains=query)
    total = queryset.count()
    page = min(page, max(1, (total + size - 1) // size))
    items = list(queryset.order_by('project_name', 'project_path', 'instance_id')[(page-1)*size:page*size])
    keys = [bridge.presence_key(p.pk, p.session_id) for p in items if p.transport == 'pull']
    last_seen = cache.get_many(keys)
    snapshots = {row['project_id']: row for row in PackageSnapshot.objects.filter(project__in=items).values('project_id','digest','session_id')}
    from .models import ProjectPackageState
    independent = {row['project_id']:row for row in ProjectPackageState.objects.filter(project__in=items).values('project_id','session_id','payload__fingerprint','payload__captured_at')}
    for item in items:
        seen = last_seen.get(bridge.presence_key(item.pk, item.session_id))
        online = bool(seen) if item.transport == 'pull' else now - item.last_seen < timedelta(seconds=ONLINE_SECONDS)
        snapshot = snapshots.get(item.pk)
        own = independent.get(item.pk)
        if own and own['session_id']==item.session_id:
            snapshot={'session_id':item.session_id,'digest':'servicebase:'+own['payload__fingerprint']+':'+own['payload__captured_at']}
        rows.append({
            'instance_id':item.pk, 'session_id':item.session_id, 'project_name':item.project_name,
            'project_path':item.project_path, 'machine_name':item.machine_name, 'unity_version':item.unity_version,
            'process_id':item.process_id, 'port':item.port, 'transport':item.transport,
            'last_seen':seen or item.last_seen.isoformat(), 'online':online,
            'snapshot_digest':snapshot['digest'] if snapshot and snapshot['session_id'] == item.session_id else '',
        })
    response = JsonResponse({'projects':rows, 'total':total, 'page':page, 'page_size':size,
                             'pages':max(1, (total + size - 1)//size), 'online_timeout_seconds':bridge.ONLINE_SECONDS})
    response["Cache-Control"] = "no-store"
    return response


@csrf_exempt
@require_POST
def heartbeat(
    request,  # Django 请求，包含登录账号和会话
):
    """
    兼容旧版本机 Bridge 心跳；生产模式拒绝此通信协议。
    """
    if not settings.LOCAL_MODE:
        return JsonResponse({'error':'远端服务需要 Bridge v2'}, status=426)
    # Native Unity clients cannot use browser CSRF cookies. A custom header plus
    # loopback-only transport blocks cross-origin forms; no CORS is enabled.
    if request.META.get("REMOTE_ADDR") not in ("127.0.0.1", "::1"):
        return JsonResponse({"error": "只接受本机工程"}, status=403)
    if request.headers.get("X-ServiceBase-Client") != "unity-bridge-v1" or request.headers.get("Origin"):
        return JsonResponse({"error": "需要 Unity Bridge 请求头"}, status=403)
    try:
        data = read_json(request)
        limits = {"instance_id": 128, "session_id": 128, "project_name": 256,
                  "project_path": 2048, "machine_name": 256, "unity_version": 64, "token": 128}
        for key, limit in limits.items():
            if not isinstance(data.get(key), str) or not 1 <= len(data[key]) <= limit:
                raise ValueError(f"无效字段：{key}")
        if not re.fullmatch(r"[a-zA-Z0-9_-]+", data["instance_id"]):
            raise ValueError("instance_id 格式错误")
        for key in ("port", "process_id"):
            if type(data.get(key)) is not int:
                raise ValueError(f"无效字段：{key}")
        if not 1024 <= data["port"] <= 65535 or not 1 <= data["process_id"] <= 2147483647:
            raise ValueError("端口或进程号超出范围")
        UnityProject.objects.update_or_create(
            instance_id=data["instance_id"],
            defaults={**{key: data[key] for key in limits if key != "instance_id"},
                      "port": data["port"], "process_id": data["process_id"], "last_seen": timezone.now()},
        )
    except (ValueError, UnicodeError) as exc:
        return JsonResponse({"error": str(exc)}, status=400)
    return JsonResponse({"ok": True, "heartbeat_interval_seconds": 3})


@require_POST
def send_message(
    request,  # Django 请求，包含登录账号和会话
    instance_id,  # 路由中的目标工程 ID
):
    """
    向目标工程发送字符串调试任务；新版进入任务队列，旧版使用本地 HTTP 转发。
    """
    try:
        data = read_json(request)
        method = data.get("method", "POST")
        message = data.get("message")
        if method not in ("GET", "POST"):
            raise ValueError("仅支持 GET 和 POST")
        if not isinstance(message, str) or not message.strip() or len(message) > 2000:
            raise ValueError("消息需要包含 1–2000 个字符")
        project = visible_projects(request).get(pk=instance_id)
    except UnityProject.DoesNotExist:
        return JsonResponse({"error": "工程不存在"}, status=404)
    except (ValueError, UnicodeError) as exc:
        return JsonResponse({"error": str(exc)}, status=400)
    if project.transport == 'pull':
        if not bridge.presence(project):
            return JsonResponse({'error':'工程已离线'}, status=409)
        return bridge.enqueue(project, 'message', {'method':method, 'message':message})
    if timezone.now() - project.last_seen >= timedelta(seconds=ONLINE_SECONDS):
        return JsonResponse({"error": "工程已离线，请等待 Unity 重新连接"}, status=409)
    url = f"http://127.0.0.1:{project.port}/mock/"
    body = None
    if method == "GET":
        url += "?" + urlencode({"message": message})
    else:
        body = json.dumps({"message": message}).encode("utf-8")
    req = Request(url, data=body, method=method, headers={
        "Content-Type": "application/json", "X-ServiceBase-Token": project.token,
    })
    return relay(project, req)


@require_GET
def mtp_snapshot(
    request,  # Django 请求，包含登录账号和会话
    instance_id,  # 路由中的目标工程 ID
):
    """
    读取目标工程的包快照并合并服务端目录；旧 Bridge 使用本地接口读取。
    """
    try:
        project = visible_projects(request).get(pk=instance_id)
    except UnityProject.DoesNotExist:
        return JsonResponse({"error": "工程不存在"}, status=404)
    if project.transport == 'pull':
        from .models import ProjectPackageState
        independent = ProjectPackageState.objects.filter(project=project,session_id=project.session_id).first()
        if independent and independent.payload.get('error'):
            return JsonResponse({'error':independent.payload['error']},status=409)
        if independent:
            value=independent.payload
            from .package_presentation import independent_snapshot
            return JsonResponse({'ok':True,'response':catalog.merge_snapshot(independent_snapshot(project,value)),
                'received_at':independent.received_at.isoformat(),'checked_at':value['captured_at'],
                'stale':not bridge.presence(project) or (timezone.now()-parse_datetime(value['captured_at'])).total_seconds()>120})
        snapshot = PackageSnapshot.objects.filter(project=project).first()
        if snapshot is None:
            return JsonResponse({'error':'等待工程首次上传快照'}, status=409)
        return JsonResponse({'ok':True, 'response':catalog.merge_snapshot(snapshot.payload), 'received_at':snapshot.received_at.isoformat(),
                             'checked_at':cache.get('snapshot-checked:' + project.pk + ':' + snapshot.session_id) or snapshot.received_at.isoformat(),
                             'stale':snapshot.session_id != project.session_id or not bridge.presence(project)})
    if timezone.now() - project.last_seen >= timedelta(seconds=ONLINE_SECONDS):
        return JsonResponse({"error": "工程已离线；依赖快照不可刷新"}, status=409)
    req = Request(f"http://127.0.0.1:{project.port}/mtp/", headers={
        "X-ServiceBase-Token": project.token,
    })
    return relay(project, req, mtp=True)


def relay(
    project,  # 目标 Unity 工程记录
    req,  # 发往 Unity 的 urllib 请求对象
    mtp=False,  # 是否按 MTP 快照协议校验和合并响应
):
    """
    向本机 Unity 转发请求，限制大小并校验工程会话，必要时合并目录元数据。
    """
    started = time.monotonic()
    limit = 2 * 1024 * 1024 if mtp else 65536
    try:
        # Never follow redirects or environment proxies to nonlocal hosts.
        with build_opener(ProxyHandler({}), NoRedirect()).open(req, timeout=5) as result:
            payload = result.read(limit + 1)
            if len(payload) > limit:
                raise ValueError("Unity 响应过大")
            reply = json.loads(payload)
            if not isinstance(reply, dict) or reply.get("instance_id") != project.instance_id or reply.get("session_id") != project.session_id:
                raise ValueError("Unity 实例身份不匹配，请等待下一次心跳")
            if mtp and (reply.get("schema_version") != 1 or reply.get("state") not in
                        ("absent", "initializing", "incompatible", "ready", "error")
                        or not isinstance(reply.get("packages"), list)):
                raise ValueError("MTP 快照协议不兼容，请更新 Unity Bridge")
            response = JsonResponse({"ok": True, "method": req.get_method(), "status": result.status,
                                     "elapsed_ms": round((time.monotonic() - started) * 1000), "response": catalog.merge_snapshot(reply) if mtp else reply})
            response["Cache-Control"] = "no-store"
            return response
    except HTTPError as exc:
        if mtp and exc.code == 404:
            return JsonResponse({"error": "该工程的 Bridge 尚不支持 MTP 检查，请更新 ServiceBaseBridge.cs"}, status=502)
        return JsonResponse({"error": f"Unity 返回 HTTP {exc.code}"}, status=502)
    except (URLError, TimeoutError, OSError, ValueError) as exc:
        return JsonResponse({"error": f"Unity 请求失败：{exc}"}, status=502)


@require_POST
def refresh_snapshot(
    request,  # Django 请求，包含登录账号和会话
    instance_id,  # 路由中的目标工程 ID
):
    """
    验证在线状态后为新版 Bridge 排队一次快照刷新任务。
    """
    project = visible_projects(request).filter(pk=instance_id).first()
    if project is None:
        return JsonResponse({'error':'工程不存在'}, status=404)
    if project.transport != 'pull':
        return JsonResponse({'error':'旧 Bridge 请直接读取或升级脚本'}, status=409)
    if not bridge.presence(project):
        return JsonResponse({'error':'工程已离线'}, status=409)
    from .models import ProjectPackageState
    if ProjectPackageState.objects.filter(project=project,session_id=project.session_id).exists():
        # v7 publishes its own idle snapshot every ten seconds. Do not enqueue MTP work.
        return JsonResponse({'state':'completed','task_id':None})
    return bridge.enqueue(project, 'snapshot', {})


@require_GET
def task_status(
    request,  # Django 请求，包含登录账号和会话
    task_id,  # 待查询的任务 UUID
):
    """
    在工程访问权限范围内返回任务状态，识别过期或会话已变化的任务。
    """
    task = BridgeTask.objects.filter(pk=task_id, project__in=visible_projects(request)).first()
    if task is None:
        return JsonResponse({'error':'任务不存在'}, status=404)
    state = task.state
    if state == 'pending' and (task.expires_at <= timezone.now() or task.session_id != task.project.session_id):
        state = 'expired'
    return JsonResponse({'ok':True, 'state':state, 'response':task.result})


@require_POST
def refresh_catalog(
    request,  # Django 请求，包含登录账号和会话
):
    """
    仅允许管理员同步目录，失败时返回不包含凭据的通用错误。
    """
    if not request.user.is_authenticated or not request.user.is_active or not request.user.is_staff:
        return JsonResponse({'error':'仅管理员可同步共享包目录。'}, status=403)
    try:
        refreshed = catalog.refresh_catalog()
        return JsonResponse({'ok':True, 'refreshing':not refreshed})
    except Exception:
        # GitLab exceptions can contain URLs/headers; never return them to the browser.
        return JsonResponse({'error':'远端包列表同步失败，已保留上次缓存。请检查服务端凭据、GitLab 网络与 master 分支。'}, status=502)
