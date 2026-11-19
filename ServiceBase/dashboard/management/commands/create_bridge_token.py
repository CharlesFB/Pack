import hashlib
import secrets
from django.contrib.auth import get_user_model
from django.core.management.base import BaseCommand, CommandError
from dashboard.models import BridgeCredential

class Command(BaseCommand):
    help = 'Create a device credential owned by an existing user; token is shown once.'
    def add_arguments(
        self,  # 当前对象实例
        parser,  # Django 管理命令参数解析器
    ):
        """
        声明该管理命令支持的位置参数。
        """
        parser.add_argument('username')
        parser.add_argument('name')
    def handle(
        self,  # 当前对象实例
        username,  # 设备凭据归属的账号名
        name,  # 设备的可读名称
        **options,  # Django 管理命令的通用选项
    ):
        """
        为指定账号签发设备 token；数据库只保存摘要，原文仅输出一次。
        """
        user = get_user_model().objects.filter(username=username, is_active=True).first()
        if user is None:
            raise CommandError('User not found or inactive')
        token = secrets.token_urlsafe(32)
        credential = BridgeCredential.objects.create(owner=user, name=name, digest=hashlib.sha256(token.encode()).hexdigest())
        self.stdout.write(f'Credential ID: {credential.pk}\nToken (shown once): {token}')
