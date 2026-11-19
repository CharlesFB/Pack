from datetime import timedelta
from django.core.management.base import BaseCommand
from django.utils import timezone
from dashboard.models import BridgeTask
class Command(BaseCommand):
    help = 'Remove task history expired more than 24 hours ago; schedule daily in deployment.'
    def handle(
        self,  # 当前对象实例
        **options,  # Django 管理命令的通用选项
    ):
        """
        删除过期超过一天的任务历史并输出删除数量。
        """
        count, _ = BridgeTask.objects.filter(expires_at__lt=timezone.now()-timedelta(days=1)).delete()
        self.stdout.write(str(count))
