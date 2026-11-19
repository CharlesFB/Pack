import hashlib
import gzip
import io
import json
import tarfile
import tempfile
from pathlib import Path
from unittest import TestCase
from unittest.mock import patch

from dashboard.plugin_external_sync import requested_versions, sync_external
from dashboard.plugin_repository import read_json, version_directory, verify, inspect_archive
from dashboard.tests.test_plugin_repository import unitypackage


def document(versions=None):
    versions = versions or ['1.0', '1.0 with example']
    return {'Packages': [{'PackageName': 'Example Plugin', 'ProjectId': 2636,
                          'Versions': versions, 'Tags': versions,
                          'PluginFileNames': ['file' + str(i) + '.unitypackage' for i in range(len(versions))]}]}


class ExternalPluginTests(TestCase):
    def test_package_manager_manifest_and_store_icon_are_recorded(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'test.unitypackage'
            unitypackage(path)
            raw = io.BytesIO(gzip.decompress(path.read_bytes()))
            with tarfile.open(fileobj=raw, mode='a') as archive:
                for name, data in {'.icon.png': b'icon', 'packagemanagermanifest/asset': b'{}',
                                   'packagemanagermanifest/pathname': b'Packages/manifest.json\n00'}.items():
                    item = tarfile.TarInfo(name)
                    item.size = len(data)
                    archive.addfile(item, io.BytesIO(data))
            path.write_bytes(gzip.compress(raw.getvalue()))
            result = inspect_archive(path)
            self.assertEqual(len(result['files']), 1)
            self.assertEqual(result['auxiliary_members'][0]['path'], '.icon.png')
            self.assertIn('Packages/manifest.json\n00', result['control_character_paths'])

    def test_asset_store_extended_gzip_header(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'test.unitypackage'
            unitypackage(path)
            raw = bytearray(path.read_bytes())
            raw[3] |= 4
            extra = b'AB\x04\x00test'
            path.write_bytes(raw[:10] + len(extra).to_bytes(2, 'little') + extra + raw[10:])
            self.assertEqual(len(inspect_archive(path)['files']), 1)

    def test_all_versions_preserved_including_variants(self):
        rows = requested_versions(document())
        self.assertEqual([r['version'] for r in rows], ['1.0', '1.0 with example'])
        self.assertEqual(rows[1]['source_path'], 'plugins/Example Plugin/1.0 with example/file1.unitypackage')

    def test_misaligned_index_rejected(self):
        value = document()
        value['Packages'][0]['PluginFileNames'].pop()
        with self.assertRaisesRegex(ValueError, 'index mismatch'):
            requested_versions(value)

    def test_traversal_rejected(self):
        value = document(['../outside'])
        with self.assertRaises(ValueError):
            requested_versions(value)

    def test_version_directory_is_safe_and_keeps_variants_distinct(self):
        self.assertEqual(version_directory('5.3-Unity6'), '5.3-Unity6')
        self.assertNotEqual(version_directory('4.1 with example'), version_directory('4.1-with-example'))
        self.assertNotIn(' ', version_directory('1.82 - unity6000'))

    @patch('dashboard.plugin_external_sync.gitlab.Gitlab')
    def test_metadata_precedence_namespace_and_resume(self, client):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            artifact = base / 'sample.unitypackage'
            unitypackage(artifact)
            content = artifact.read_bytes()
            oid = hashlib.sha1(('blob ' + str(len(content)) + '\0').encode() + content).hexdigest()
            project = client.return_value.__enter__.return_value.projects.get.return_value
            project.commits.get.return_value.id = 'c' * 40
            rows = requested_versions(document())
            tree = [{'type': 'blob', 'path': r['source_path'], 'id': oid} for r in rows]
            version_metadata = 'plugins/Example Plugin/1.0/package.json'
            common_metadata = 'plugins/Example Plugin/package.json'
            tree += [{'type': 'blob', 'path': p, 'id': 'metadata'} for p in (version_metadata, common_metadata)]
            project.repository_tree.return_value = tree
            def raw(**kwargs):
                if kwargs['file_path'] == version_metadata:
                    data = b'{"description":"Version-specific","version":"wrong","unity":"2022.3"}'
                elif kwargs['file_path'] == common_metadata:
                    data = b'{"description":"Shared"}'
                else:
                    data = content
                kwargs['action'](data)
            project.files.raw.side_effect = raw
            root = base / 'external'
            root.mkdir()
            (root / 'index.json').write_text(json.dumps({'schema_version': 1, 'plugins': [], 'package_mappings': []}))
            result = sync_external(root, base / 'cache', document(), 'https://example.invalid', 'secret', lambda _: None)
            self.assertEqual(result['external_versions'], 2)
            index = read_json(root / 'index.json')
            catalog = index['external_catalogs']['2636']
            displays = [read_json(root/r['directory']/'plugin.json') for r in catalog['versions']]
            self.assertEqual(displays[0]['version'], '1.0')
            self.assertEqual(displays[0]['description'], 'Version-specific')
            self.assertEqual(displays[1]['description'], 'Shared')
            self.assertTrue(all(r['directory'].startswith('example-plugin/') for r in catalog['versions']))
            project.files.raw.reset_mock()
            sync_external(root, base / 'cache', document(), 'https://example.invalid', 'secret', lambda _: None)
            self.assertEqual(index, read_json(root / 'index.json'))
            self.assertTrue(all(c.kwargs['file_path'].endswith('package.json') for c in project.files.raw.call_args_list))
            self.assertEqual(verify(root)['plugins'], 2)
