from pathlib import Path
from django.conf import settings
from django.core.management.base import BaseCommand, CommandError
from dashboard.catalog import source
from dashboard.plugin_external_sync import sync_external
from dashboard.plugin_repository import read_json


class Command(BaseCommand):
    help = 'Download every indexed version from GitLab 2636 into a separate external plugin collection.'

    def add_arguments(self, parser):
        parser.add_argument('--list', type=Path, required=True)
        parser.add_argument('--root', type=Path, default=settings.BASE_DIR / 'external')
        parser.add_argument('--cache', type=Path, default=settings.BASE_DIR / '.runtime/plugin-downloads-2636')

    def handle(self, *args, **options):
        try:
            _, url, token, _ = source()
            result = sync_external(options['root'], options['cache'], read_json(options['list']),
                                   url, token, self.stdout.write)
            self.stdout.write(str(result))
        except ValueError as error:
            raise CommandError(str(error)) from None
        except Exception as error:
            raise CommandError('External sync failed: ' + type(error).__name__) from None
