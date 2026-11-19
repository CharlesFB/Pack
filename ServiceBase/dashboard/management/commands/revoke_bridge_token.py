from django.core.cache import cache
from django.core.management.base import BaseCommand, CommandError
from dashboard.models import BridgeCredential
class Command(BaseCommand):
    help = 'Revoke a device credential by its ID.'
    def add_arguments(
        self,  # 当前对象实例
        parser,  # Django 管理命令参数解析器
    ):
        """
        声明该管理命令支持的位置参数。
        """
        parser.add_argument('credential_id', type=int)
    def handle(
        self,  # 当前对象实例
        credential_id,  # 待撤销设备凭据的数据库 ID
        **options,  # Django 管理命令的通用选项
    ):
        """
        停用指定设备凭据并清除身份缓存，不删除工程历史。
        """
        credential = BridgeCredential.objects.filter(pk=credential_id).first()
        if credential is None:
            raise CommandError('Credential not found')
        credential.active = False
        credential.save(update_fields=['active'])
        cache.delete('credential:' + credential.digest)
        self.stdout.write('Revoked')
