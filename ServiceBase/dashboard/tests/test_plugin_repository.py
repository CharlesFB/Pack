import io
import json
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path

from dashboard.plugin_repository import import_project, inspect_archive, read_json, verify


def unitypackage(path, asset_path='Assets/Test/file.txt', meta=True):
    with tarfile.open(path, 'w:gz') as archive:
        root = tarfile.TarInfo('.')
        root.type = tarfile.DIRTYPE
        archive.addfile(root)
        entries = {'pathname': asset_path.encode(), 'asset': b'content'}
        if meta:
            entries['asset.meta'] = b'guid: ' + b'a' * 32
        for name, data in entries.items():
            entry = tarfile.TarInfo('a' * 32 + '/' + name)
            entry.size = len(data)
            archive.addfile(entry, io.BytesIO(data))


class PluginRepositoryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.project, self.root = self.base / 'Unity', self.base / 'external'
        (self.project / 'Packages').mkdir(parents=True)
        self.commit = 'b' * 40
        self.cache = self.project / ('Library/PackageCache/com.magictavern.test@' + self.commit[:10])
        self.plugins = self.cache / 'Editor/PluginsPackage'
        self.plugins.mkdir(parents=True)
        (self.cache / 'package.json').write_text(json.dumps({'name': 'com.magictavern.test', 'version': '1.0'}))
        (self.project / 'Packages/packages-lock.json').write_text(json.dumps({'dependencies': {
            'com.magictavern.test': {'source': 'git', 'hash': self.commit,
                                   'version': 'https://user:SECRET@example.invalid/repo.git'}}}))
        unitypackage(self.plugins / 'test.unitypackage')
        self.definitions = {'test.unitypackage': {'id': 'test', 'name': 'Test'}}

    def run_import(self):
        return import_project(self.project, self.root, self.definitions)

    def test_import_idempotent_exact_commit_and_no_credentials(self):
        source_before = (self.project / 'Packages/packages-lock.json').read_bytes()
        first = self.run_import()
        self.assertEqual(first, self.run_import())
        self.assertEqual(verify(self.root)['plugins'], 1)
        self.assertEqual(first['package_mappings'][0]['commit'], self.commit)
        self.assertIsNone(first['package_mappings'][0]['plugins'][0]['required'])
        self.assertNotIn('SECRET', json.dumps(first))
        self.assertEqual(source_before, (self.project / 'Packages/packages-lock.json').read_bytes())

    def test_stale_cache_is_not_selected(self):
        self.cache.rename(self.cache.with_name('com.magictavern.test@' + 'c' * 10))
        with self.assertRaisesRegex(ValueError, 'Missing or ambiguous'):
            self.run_import()
        self.assertFalse((self.root / 'index.json').exists())

    def test_no_plugins_still_records_scanned_package(self):
        (self.plugins / 'test.unitypackage').unlink()
        index = self.run_import()
        self.assertEqual(index['package_mappings'][0]['plugins'], [])
        self.assertEqual(verify(self.root)['packages_with_plugins'], 0)

    def test_same_commit_changed_archive_keeps_old_index(self):
        self.run_import()
        previous = (self.root / 'index.json').read_bytes()
        unitypackage(self.plugins / 'test.unitypackage', 'Assets/Test/new.txt')
        with self.assertRaisesRegex(ValueError, 'Mapping changed'):
            self.run_import()
        self.assertEqual(previous, (self.root / 'index.json').read_bytes())

    def test_corrupt_artifact_detected(self):
        index = self.run_import()
        artifact = self.root / index['plugins'][0]['directory'] / 'artifact.unitypackage'
        artifact.write_bytes(b'broken')
        with self.assertRaisesRegex(ValueError, 'hash mismatch'):
            verify(self.root)

    def test_legacy_non_assets_and_missing_meta_are_flagged(self):
        source = self.plugins / 'test.unitypackage'
        unitypackage(source, 'Packages/com.unity.ugui/test.cs', meta=False)
        inspection = inspect_archive(source)
        self.assertEqual(inspection['non_assets_paths'], ['Packages/com.unity.ugui/test.cs'])
        self.assertEqual(inspection['missing_meta_paths'], inspection['non_assets_paths'])
        self.assertFalse(self.run_import()['package_mappings'][0]['execution_ready'])

    def test_zip_traversal_rejected_without_extracting(self):
        source = self.base / 'test.zip'
        with zipfile.ZipFile(source, 'w') as archive:
            archive.writestr('../outside.txt', 'content')
        with self.assertRaises(ValueError):
            inspect_archive(source)
        self.assertFalse((self.base / 'outside.txt').exists())

    def test_zip_layout_unknown_and_files_hashed(self):
        source = self.base / 'test.zip'
        with zipfile.ZipFile(source, 'w') as archive:
            archive.writestr('Plugin/a.txt', 'content')
        inspection = inspect_archive(source)
        self.assertEqual(inspection['layout'], 'unconfirmed')
        self.assertEqual(len(inspection['files'][0]['sha256']), 64)

    def test_repository_inside_unity_rejected(self):
        with self.assertRaisesRegex(ValueError, 'outside'):
            import_project(self.project, self.project / 'external', self.definitions)

    def test_unknown_plugin_does_not_publish_partial_mapping(self):
        unitypackage(self.plugins / 'unknown.unitypackage')
        with self.assertRaisesRegex(ValueError, 'Missing curated'):
            self.run_import()
        self.assertFalse((self.root / 'index.json').exists())
