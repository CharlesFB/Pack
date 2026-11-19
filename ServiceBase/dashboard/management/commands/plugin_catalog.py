from pathlib import Path
from django.conf import settings
from django.core.management.base import BaseCommand, CommandError
from dashboard.catalog import source
from dashboard.plugin_catalog_sync import scan_catalog, scan_repository, import_scan
from dashboard.plugin_repository import read_json, write_json


class Command(BaseCommand):
    help = 'Scan catalog releases or explicit historical tags, then import their verified plugin mappings.'

    def add_arguments(self, parser):
        parser.add_argument('action', choices=['scan', 'import', 'import-refs'])
        parser.add_argument('--root', type=Path, default=settings.BASE_DIR / 'external')
        parser.add_argument('--metadata', type=Path, default=settings.BASE_DIR / 'external/source-metadata.json')
        parser.add_argument('--package-ref', action='append', default=[], metavar='PROJECT_ID:TAG',
                            help='Exact historical release tag; repeat for several dependencies.')

    def handle(self, *args, **options):
        _, url, token, ref = source()
        try:
            if options['action'] == 'scan':
                report = scan_catalog(options['root'], url, token, ref, self.stdout.write)
                errors = [r for r in report['rows'] if r['state'] == 'error']
                if errors:
                    raise CommandError('Scan incomplete: {} entries; see catalog-scan.json'.format(len(errors)))
                self.stdout.write('Saved {} catalog entries'.format(len(report['rows'])))
            elif options['action']=='import-refs':
                requested=options['package_ref']
                if not requested or len(requested)>100:raise ValueError('Provide 1–100 --package-ref PROJECT_ID:TAG values')
                selections={}
                for value in requested:
                    pid,separator,tag=value.partition(':')
                    if not separator or not pid.isdecimal() or not tag or len(tag)>200 or any(c.isspace() for c in tag):
                        raise ValueError('Invalid --package-ref; use PROJECT_ID:TAG')
                    entry={'PackageName':tag,'Tag':tag}
                    if entry not in selections.setdefault(int(pid),[]):selections[int(pid)].append(entry)
                rows=[]
                for pid,entries in selections.items():
                    rows.extend(scan_repository(url,token,pid,entries))
                index=read_json(options['root']/'index.json')
                report={'schema_version':1,'catalog_project_id':647,
                        'catalog_commit':index.get('catalog_snapshot',{}).get('catalog_commit',''),
                        'selection':'explicit historical tags; retain current highest-release selection','rows':rows}
                write_json(options['root']/'dependency-scan.json',report)
                result=import_scan(options['root'],report,read_json(options['metadata']),url,token,self.stdout.write,
                                   update_catalog_snapshot=False)
                self.stdout.write(str(result))
            else:
                result = import_scan(options['root'], read_json(options['root'] / 'catalog-scan.json'),
                                     read_json(options['metadata']), url, token, self.stdout.write)
                self.stdout.write(str(result))
        except ValueError as error:
            raise CommandError(str(error)) from error
        except CommandError:
            raise
        except Exception as error:
            # Remote error text can contain authenticated URLs; expose only the class.
            raise CommandError('Catalog operation failed: ' + type(error).__name__) from None
