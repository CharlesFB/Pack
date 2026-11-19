"""Configure an exact ZIP build's destination without changing repository provenance."""
from django.core.management.base import BaseCommand, CommandError

class Command(BaseCommand):
    help = 'Retired: choose ZIP destinations on the web when importing.'
    def add_arguments(self, parser):
        parser.add_argument('plugin_id'); parser.add_argument('build_id')
        parser.add_argument('--prefix', required=True, help='Destination prefix, e.g. Assets/ (empty for ZIP already rooted at Assets)')
        parser.add_argument('--strip', default='', help='Source prefix to remove, including trailing /')
    def handle(self, *args, **options):
        raise CommandError('ZIP 已改为导入时在网页中选择根目录；新任务不再读取 import-layouts.json，原记录保留用于历史追溯。')
