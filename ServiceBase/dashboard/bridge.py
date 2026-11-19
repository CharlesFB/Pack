"""Outbound-only Bridge v2. Heartbeats live in cache; durable writes are infrequent."""
import hashlib
import json
from datetime import timedelta
from functools import wraps
from django.core.cache import cache
from django.core.exceptions import ValidationError
from django.db import transaction
from django.http import JsonResponse
from django.utils import timezone
from django.views.decorators.csrf import csrf_exempt
from django.views.decorators.http import require_POST
from .auth import native_identity
from .models import UnityProject, BridgeTask, PackageSnapshot

ONLINE_SECONDS = 45  # 在线标记过期时间；持续心跳只续期缓存，不逐次写数据库。


def session_key(
    project_id,  # 稳定工程 ID
):
    """
    生成工程注册会话的缓存键。
    """
    return 'bridge-session:' + project_id


def presence_key(
    project_id,  # 稳定工程 ID
    session,  # Unity 编辑器会话 ID
):
    """
    生成工程指定编辑器会话的在线状态缓存键，隔离旧会话心跳。
    """
    return 'presence:' + project_id + ':' + session


def presence(
    project,  # 目标 Unity 工程记录
):
    """
    读取当前工程会话的最近在线标记；过期或不存在时返回 None。
    """
    return cache.get(presence_key(project.pk, project.session_id))


def native(
    view,  # 待包装的原生 API 视图
):
    """
    包装 Unity API：仅接受经过设备身份校验的 POST JSON 请求。
    """
    @csrf_exempt
    @require_POST
    @wraps(view)
    def wrapped(
        request,  # Django 请求，包含登录账号和会话
        *args,  # 透传给视图的位置参数
        **kwargs,  # 透传给视图的关键字参数
    ):
        """
        解析原生请求并传入可信身份；把可预期的输入错误转换为 HTTP 400。
        """
        identity = native_identity(request)
        if identity is None:
            return JsonResponse({'error': 'Bridge 凭据无效'}, status=403)
        try:
            if request.content_type != 'application/json':
                raise ValueError('需要 JSON 请求')
            data = json.loads(request.body)
            if not isinstance(data, dict):
                raise ValueError('需要 JSON 对象')
            return view(request, data, identity, *args, **kwargs)
        except (ValueError, TypeError, KeyError, ValidationError) as error:
            return JsonResponse({'error': str(error)}, status=400)
    return wrapped


def checked_session(
    data,  # 已解析的 JSON 请求对象
    identity,  # 服务端验证得到的账号和设备身份
):
    """
    核对工程、编辑器会话、账号与设备归属；缓存缺失时从数据库恢复。
    """
    project_id, session = data.get('instance_id'), data.get('session_id')
    if not isinstance(project_id, str) or not isinstance(session, str) or len(project_id) > 128 or len(session) > 128:
        raise ValueError('无效工程身份')
    cached = cache.get(session_key(project_id))
    if cached is None:
        p = UnityProject.objects.filter(pk=project_id, transport='pull').first()
        if p is None:
            return None
        cached = {'session_id': p.session_id, 'owner_id': p.owner_id, 'credential_id': p.credential_id}
        cache.set(session_key(project_id), cached, 3600)
    if any(cached[key] != identity[key] for key in ('owner_id', 'credential_id')) or cached['session_id'] != session:
        return None
    return project_id


@native
def register(
    request,  # Django 请求，包含登录账号和会话
    data,  # 已解析的 JSON 请求对象
    identity,  # 服务端验证得到的账号和设备身份
):
    """
    注册或重连工程并更新在线缓存；禁止其他账号或设备接管已有工程。
    """
    import re
    limits = {'instance_id':128, 'session_id':128, 'project_name':256, 'project_path':2048,
              'machine_name':256, 'unity_version':64}
    for key, limit in limits.items():
        if not isinstance(data.get(key), str) or not 1 <= len(data[key]) <= limit:
            raise ValueError('无效字段：' + key)
    if not re.fullmatch('[a-zA-Z0-9_-]+', data['instance_id']):
        raise ValueError('无效 instance_id')
    if type(data.get('process_id')) is not int or not 1 <= data['process_id'] <= 2147483647:
        raise ValueError('无效 process_id')
    with transaction.atomic():
        p = UnityProject.objects.select_for_update().filter(pk=data['instance_id']).first()
        if p and (p.owner_id != identity['owner_id'] or p.credential_id != identity['credential_id']):
            return JsonResponse({'error':'工程已归属其他凭据'}, status=403)
        values = {key:data[key] for key in limits if key != 'instance_id'}
        values.update(identity, transport='pull', process_id=data['process_id'], port=0, token='', last_seen=timezone.now())
        if p is None:
            UnityProject.objects.create(instance_id=data['instance_id'], **values)
        else:
            # Registration occurs at startup/reconnection, never on every heartbeat.
            UnityProject.objects.filter(pk=p.pk).update(**values)
    cache.set(session_key(data['instance_id']), dict(identity, session_id=data['session_id']), 3600)
    cache.set(presence_key(data['instance_id'], data['session_id']), timezone.now().isoformat(), ONLINE_SECONDS)
    return JsonResponse({'ok':True, 'poll_interval_seconds':5})


@native
def poll(
    request,  # Django 请求，包含登录账号和会话
    data,  # 已解析的 JSON 请求对象
    identity,  # 服务端验证得到的账号和设备身份
):
    """
    续期工程在线状态并返回尚未过期的待执行任务；任务在确认前可重复投递。
    """
    project_id = checked_session(data, identity)
    if project_id is None:
        return JsonResponse({'error':'请重新注册工程'}, status=409)
    cache.set(presence_key(project_id, data['session_id']), timezone.now().isoformat(), ONLINE_SECONDS)
    tasks = BridgeTask.objects.filter(project_id=project_id, session_id=data['session_id'], state='pending',
                                      expires_at__gt=timezone.now()).order_by('created_at')[:4]
    return JsonResponse({'ok':True, 'tasks':[dict(id=str(t.pk), kind=t.kind, **t.payload) for t in tasks]})


@native
def upload_snapshot(
    request,  # Django 请求，包含登录账号和会话
    data,  # 已解析的 JSON 请求对象
    identity,  # 服务端验证得到的账号和设备身份
):
    """
    校验并保存包快照，仅内容变化时写库，拒绝旧会话覆盖新快照。
    """
    project_id = checked_session(data, identity)
    if project_id is None:
        return JsonResponse({'error':'会话已失效'}, status=409)
    snapshot = data.get('snapshot')
    if not isinstance(snapshot, dict) or snapshot.get('schema_version') != 1 or snapshot.get('state') not in (
            'ready','absent','initializing','incompatible','error') or not isinstance(snapshot.get('packages'), list):
        raise ValueError('快照格式不兼容')
    if snapshot.get('instance_id') != project_id or snapshot.get('session_id') != data['session_id']:
        raise ValueError('快照身份不匹配')
    if len(snapshot['packages']) > 5000:
        raise ValueError('快照包数超过限制')
    encoded = json.dumps(snapshot, sort_keys=True, ensure_ascii=False).encode()
    if len(encoded) > 2 * 1024 * 1024:
        raise ValueError('快照超过 2 MB')
    semantic = dict(snapshot)  # 用于内容比较的快照副本，移除采集时间以免无变化上传造成数据库写入。
    semantic.pop('captured_at', None)
    digest = hashlib.sha256(json.dumps(semantic, sort_keys=True, ensure_ascii=False).encode()).hexdigest()  # 快照内容指纹，用于跳过重复持久化。
    with transaction.atomic():
        # Serialize with re-registration so a stale upload cannot replace the current session's snapshot.
        p = UnityProject.objects.select_for_update().get(pk=project_id)
        if p.session_id != data['session_id']:
            return JsonResponse({'error':'会话已失效'}, status=409)
        previous = PackageSnapshot.objects.filter(project=p).first()
        if previous is None or previous.digest != digest:
            PackageSnapshot.objects.update_or_create(project=p, defaults={
                'session_id':data['session_id'], 'digest':digest, 'payload':snapshot})
    cache.set('snapshot-checked:' + project_id + ':' + data['session_id'], timezone.now().isoformat(), 86400)
    return JsonResponse({'ok':True})


@native
def task_result(
    request,  # Django 请求，包含登录账号和会话
    data,  # 已解析的 JSON 请求对象
    identity,  # 服务端验证得到的账号和设备身份
):
    """
    接收任务结果并幂等标记完成；拒绝不存在或尚未完成但已过期的任务。
    """
    project_id = checked_session(data, identity)
    if project_id is None:
        return JsonResponse({'error':'会话已失效'}, status=409)
    result = data.get('result')
    if not isinstance(result, dict) or len(json.dumps(result).encode()) > 16384:
        raise ValueError('无效任务结果')
    task = BridgeTask.objects.filter(pk=data.get('task_id'), project_id=project_id, session_id=data['session_id']).first()
    if task is None:
        return JsonResponse({'error':'任务不存在'}, status=404)
    if task.state == 'pending' and task.expires_at <= timezone.now():
        return JsonResponse({'error':'任务已过期'}, status=410)
    BridgeTask.objects.filter(pk=task.pk, state='pending').update(state='done', result=result)
    return JsonResponse({'ok':True})


def enqueue(
    project,  # 目标 Unity 工程记录
    kind,  # 任务类型，例如 message 或 snapshot
    payload,  # 任务负载字典
):
    """
    在锁定工程的事务内创建限量任务，复用待执行的快照刷新任务。
    """
    now = timezone.now()
    with transaction.atomic():
        p = UnityProject.objects.select_for_update().get(pk=project.pk)
        if p.session_id != project.session_id:
            return JsonResponse({'error':'工程会话已变化，请刷新'}, status=409)
        pending = BridgeTask.objects.filter(project=p, session_id=p.session_id, state='pending', expires_at__gt=now)
        task = pending.filter(kind='snapshot').first() if kind == 'snapshot' else None
        if task is None:
            if pending.count() >= 32:
                return JsonResponse({'error':'工程任务队列已满'}, status=429)
            task = BridgeTask.objects.create(project=p, session_id=p.session_id, kind=kind, payload=payload,
                                              expires_at=now + timedelta(seconds=60))
    return JsonResponse({'ok':True, 'task_id':str(task.pk), 'state':'pending'}, status=202)
