import tempfile
from pathlib import Path
from unittest import TestCase

from dashboard.plugin_layout import migrate_layout
from dashboard.plugin_repository import read_json, stage_artifact, write_json
from dashboard.tests.test_plugin_repository import unitypackage


class PluginLayoutTests(TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        package = self.root / 'test.unitypackage'
        unitypackage(package)
        definition = {'id': 'stable-old-id', 'name': 'DOTween Pro', 'version': '1.0.335'}
        build = stage_artifact(self.root, package, definition, namespace='external-2636')
        self.old = build['directory']
        self.target = 'dotween-pro/1.0.335/' + build['build_id']
        catalog = {'versions': [dict(build, package_name='DOTween Pro')]}
        write_json(self.root / 'index.json', {'schema_version': 1, 'plugins': [build], 'package_mappings': [],
                                             'external_catalogs': {'2636': catalog}})

    def test_moves_and_reindexes_without_changing_identity(self):
        result = migrate_layout(self.root)
        self.assertEqual(result['moved_builds'], 1)
        index = read_json(self.root / 'index.json')
        self.assertEqual(index['plugins'][0]['plugin_id'], 'stable-old-id')
        self.assertEqual(index['plugins'][0]['directory'], self.target)
        self.assertFalse((self.root / 'external-2636').exists())
        self.assertEqual(read_json(self.root/'groups.json')['groups'][0]['id'], 'dotween-pro')
        self.assertEqual(migrate_layout(self.root)['moved_builds'], 0)

    def test_resumes_after_file_move_before_index_commit(self):
        write_json(self.root/'layout-migration.json', {'status': 'moving', 'moves': {self.old: self.target}})
        target = self.root/self.target
        target.parent.mkdir(parents=True)
        (self.root/self.old).rename(target)
        self.assertEqual(migrate_layout(self.root)['moved_builds'], 1)

    def test_unknown_version_uses_unversioned(self):
        build = stage_artifact(self.root, self.root/'test.unitypackage',
                               {'id': 'new-id', 'name': 'Other', 'directory_name': 'other'}, namespace=None)
        self.assertTrue(build['directory'].startswith('other/unversioned/'))
