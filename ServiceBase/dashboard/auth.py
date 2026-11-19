import hashlib
from django.conf import settings
from django.core.cache import cache
from django.http import JsonResponse
from django.shortcuts import redirect
from .models import BridgeCredential


def local_request(
    request,  # Django 请求，包含登录账号和会话
):
    """
    判断请求是否来自本地开发模式下的回环地址。
    """
    return settings.LOCAL_MODE and request.META.get('REMOTE_ADDR') in ('127.0.0.1', '::1')


class AccessMiddleware:
    def __init__(
        self,  # 当前对象实例
        get_response,  # Django 后续请求处理器
    ):
        """
        保存 Django 后续请求处理器，供中间件完成权限校验后调用。
        """
        self.get_response = get_response

    def __call__(
        self,  # 当前对象实例
        request,  # Django 请求，包含登录账号和会话
    ):
        """
        执行入口访问控制，生产网页要求登录，并禁止缓存 API 响应。
        """
        if settings.LOCAL_MODE and not local_request(request):
            return JsonResponse({'error': '本地模式只接受本机访问'}, status=403)
        if not local_request(request) and not request.path.startswith(('/api/bridge/', '/accounts/login/', '/static/')):
            if not request.user.is_authenticated:
                if request.path.startswith('/api/'):
                    return JsonResponse({'error': '请先登录'}, status=401)
                return redirect(settings.LOGIN_URL)
        response = self.get_response(request)
        if request.path.startswith('/api/'):
            response['Cache-Control'] = 'no-store'
        return response


def native_identity(
    request,  # Django 请求，包含登录账号和会话
):
    """
    验证 Unity 原生请求头与设备凭据，返回账号和设备身份；验证失败返回 None。
    """
    if request.headers.get('Origin') or request.headers.get('X-ServiceBase-Client') != 'unity-bridge-v2':
        return None
    token = request.headers.get('Authorization', '')
    if not token and local_request(request):
        return {'owner_id': None, 'credential_id': None}
    if not token.startswith('Bearer ') or len(token) > 256:
        return None
    digest = hashlib.sha256(token[7:].encode()).hexdigest()
    key = 'credential:' + digest  # 缓存设备认证结果，撤销命令会主动清除该键。
    value = cache.get(key)
    if value is None:
        credential = BridgeCredential.objects.filter(digest=digest, active=True, owner__is_active=True).first()
        if credential is None:
            return None
        value = {'owner_id': credential.owner_id, 'credential_id': credential.pk}
        cache.set(key, value, 30)
    return value


def project_scope(
    request,  # Django 请求，包含登录账号和会话
    queryset,  # 尚未应用当前身份权限的工程查询集
):
    """
    限制工程查询范围：管理员查看全部，成员仅查看当前会话绑定设备，匿名本地仅查看无归属记录。
    """
    if request.user.is_authenticated:
        if request.user.is_active and request.user.is_staff:
            return queryset
        # Account login alone never grants access to another computer's projects.
        device_id = request.session.get('bridge_device_id')  # 当前登录会话确认的设备 ID，不能直接由工程查询参数指定。
        return queryset.filter(owner=request.user, credential_id=device_id,
                               credential__active=True, credential__owner=request.user) if device_id else queryset.none()
    # Local development can see only unowned local Bridge registrations.
    return queryset.filter(owner__isnull=True, credential__isnull=True) if local_request(request) else queryset.none()
