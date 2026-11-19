from pathlib import Path
from django.conf import settings
from django.core.management.base import BaseCommand, CommandError
from dashboard.plugin_repository import import_project, read_json, verify


class Command(BaseCommand):
    help = 'Build/verify the offline plugin repository; never modifies Unity projects.'

    def add_arguments(self, parser):
        parser.add_argument('action', choices=['import-project', 'verify'])
        parser.add_argument('--root', type=Path, default=settings.BASE_DIR / 'external')
        parser.add_argument('--project', type=Path)
        parser.add_argument('--metadata', type=Path, default=settings.BASE_DIR / 'external/source-metadata.json')

    def handle(self, *args, **options):
        try:
            if options['action'] == 'import-project':
                if not options['project']:
                    raise CommandError('--project is required')
                import_project(options['project'], options['root'], read_json(options['metadata']))
            result = verify(options['root'])
        except (ValueError, OSError, KeyError) as error:
            raise CommandError(str(error)) from error
        self.stdout.write(self.style.SUCCESS(str(result)))
