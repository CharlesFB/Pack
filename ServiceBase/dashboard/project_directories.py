"""Short-lived, read-only directory requests carried by the independent executor."""
import json
import uuid
from django.core.cache import cache
from django.http import JsonResponse
from django.views.decorators.http import require_http_methods
from .auth import project_scope
from .models import UnityProject
from .plugin_delivery import destination_path

TTL = 90


def prefix(project):
    return f'project-directories:{project.pk}:{project.session_id}:'


@require_http_methods(['GET', 'POST'])
def directories(request, instance_id):
    project = project_scope(request, UnityProject.objects).filter(pk=instance_id).first()
    if not project:
        return JsonResponse({'error': '工程不存在或无访问权限'}, status=404)
    base = prefix(project)
    try:
        if request.method == 'GET':
            request_id = str(uuid.UUID(request.GET['request_id']))
            item = cache.get(base + request_id)
            if not item:
                return JsonResponse({'error': '目录请求已过期或工程已重连，请重新读取'}, status=410)
            return JsonResponse(item)
        from .installations import executor_key
        if not cache.get(executor_key(project) + ':directories'):
            return JsonResponse({'error': '请连接版本 5 的独立执行器以浏览工程目录'}, status=409)
        path = destination_path(json.loads(request.body).get('path'))
        request_id = str(uuid.uuid4())
        item = dict(request_id=request_id, path=path, state='pending')
        cache.set(base + request_id, item, TTL)
        # Only the latest navigation needs delivery; older clients can retry after timeout.
        cache.set(base + 'pending', request_id, TTL)
        return JsonResponse(item, status=202)
    except (ValueError, KeyError, TypeError, AttributeError):
        return JsonResponse({'error': '请提供 Assets 内的有效目录或目录请求 ID'}, status=400)


def exchange(project, result):
    """Called only after sync authenticated and locked this exact project session."""
    base = prefix(project)
    if result:
        if not isinstance(result, dict):
            raise ValueError('无效目录回执')
        request_id = str(uuid.UUID(result.get('request_id', '')))
        item = cache.get(base + request_id)
        if item and item['state'] == 'pending':
            folders, error = result.get('folders'), result.get('error', '')
            if (result.get('path') != item['path'] or not isinstance(folders, list) or len(folders) > 200
                    or not isinstance(error, str) or len(error) > 200 or type(result.get('truncated')) is not bool):
                raise ValueError('无效目录回执')
            for folder in folders:
                if destination_path(folder).rsplit('/', 1)[0] != item['path']:
                    raise ValueError('目录回执必须是所查目录的直接子目录')
            if len(set(folders)) != len(folders):
                raise ValueError('重复目录')
            item.update(state='failed' if error else 'completed', folders=folders,
                        error=error, truncated=result['truncated'])
            cache.set(base + request_id, item, TTL)
    pending = cache.get(base + 'pending')
    item = cache.get(base + pending) if pending else None
    if item and item['state'] == 'pending':
        return dict(request_id=item['request_id'], path=item['path'])
    return None
