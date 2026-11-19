from pathlib import Path
from django.conf import settings
from django.core.management.base import BaseCommand, CommandError
from dashboard.plugin_layout import migrate_layout


class Command(BaseCommand):
    help = 'Move external plugin builds to root plugin/version directories and update all indexes.'

    def add_arguments(self, parser):
        parser.add_argument('--root', type=Path, default=settings.BASE_DIR / 'external')

    def handle(self, *args, **options):
        try:
            self.stdout.write(str(migrate_layout(options['root'])))
        except (ValueError, OSError) as error:
            raise CommandError(str(error)) from error
