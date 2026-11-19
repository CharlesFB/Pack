import contextlib
import io
import subprocess
import tempfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from django.test import SimpleTestCase
from deploy.quick import quickctl


class QuickDeploymentTests(SimpleTestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        (self.root/'external').mkdir()
        (self.root/'external/index.json').write_text('{}')
        (self.root/'quick.env').write_text('test-only')
        for name, value in [('ROOT', self.root), ('ENV', self.root/'quick.env')]:
            manager = patch.object(quickctl, name, value)
            manager.start()
            self.addCleanup(manager.stop)
        self.calls = []

    def run_update(self, running='', fail=None):
        def compose(*args, **kwargs):
            self.calls.append(args)
            if fail and fail in args:
                raise subprocess.CalledProcessError(1, ['compose', *args])
            if kwargs.get('stdout'):
                kwargs['stdout'].write(b'fixture backup')
            return SimpleNamespace(stdout=running if args[0]=='ps' else '')
        with patch.object(quickctl, 'check_docker'), patch.object(quickctl, 'compose', side_effect=compose), contextlib.redirect_stdout(io.StringIO()):
            quickctl.update()

    def test_stopped_stack_updates_containers_and_returns_dependencies_to_stopped(self):
        self.run_update()
        self.assertIn(('up','--no-start','--no-deps','--no-build','web','proxy','maintenance'), self.calls)
        self.assertEqual(self.calls[-1], ('stop','redis','db'))
        self.assertFalse(any(c[:3]==('up','-d','--no-deps') for c in self.calls))
        self.assertFalse(any('tunnel' in c for c in self.calls))
        backup, = (self.root/'.runtime/backups').glob('*.dump')
        self.assertEqual(backup.read_bytes(), b'fixture backup')
        self.assertEqual(backup.stat().st_mode & 0o777, 0o600)

    def test_running_writers_stop_before_backup_and_resume_after_checks(self):
        self.run_update('db\nredis\nweb\nproxy\nmaintenance\ntunnel\n')
        stopped = self.calls.index(('stop','web','proxy','maintenance'))
        backup = next(i for i,c in enumerate(self.calls) if 'pg_dump' in c)
        verified = next(i for i,c in enumerate(self.calls) if 'check_installation_deployment' in c)
        self.assertLess(stopped, backup)
        self.assertLess(verified, len(self.calls)-1)
        self.assertEqual(self.calls[-1], ('up','-d','--no-deps','--no-build','web','proxy','maintenance'))
        self.assertFalse(any('tunnel' in c for c in self.calls))

    def test_build_failure_does_not_stop_running_application(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_update('db\nredis\nweb\n', fail='build')
        self.assertFalse(any(c[0]=='stop' for c in self.calls))

    def test_migration_failure_never_starts_app_and_stops_temporary_dependencies(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_update(fail='migrate')
        self.assertEqual(self.calls[-1], ('stop','redis','db'))
        self.assertFalse(any('collectstatic' in c or '--no-start' in c for c in self.calls))
        self.assertEqual(len(list((self.root/'.runtime/backups').glob('*.dump'))), 1)

    def test_failed_deployment_check_keeps_running_writers_stopped(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_update('db\nredis\nweb\nproxy\n', fail='check_installation_deployment')
        self.assertFalse(any(c[:3]==('up','-d','--no-deps') for c in self.calls))
        self.assertNotIn(('stop','redis','db'), self.calls)

    def test_backup_failure_does_not_migrate_or_keep_incomplete_dump(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_update(fail='pg_dump')
        self.assertFalse(any('migrate' in c for c in self.calls))
        self.assertEqual(list((self.root/'.runtime/backups').iterdir()), [])
