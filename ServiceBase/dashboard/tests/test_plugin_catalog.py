import hashlib
import tempfile
import json
import shutil
from pathlib import Path
from unittest import TestCase
from unittest.mock import MagicMock, patch

from dashboard.plugin_catalog_sync import highest_release, download, scan_repository, import_scan
from dashboard.plugin_repository import import_project, read_json, verify
from dashboard.tests.test_plugin_repository import unitypackage


class CatalogPluginTests(TestCase):
    @patch('dashboard.plugin_catalog_sync.gitlab.Gitlab')
    @patch('dashboard.plugin_catalog_sync.download')
    def test_remote_expands_local_scope_and_both_reimports_are_idempotent(self, fetch, client):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            project, root = base / 'Unity', base / 'external'
            commit = 'b' * 40
            cache = project / ('Library/PackageCache/com.magictavern.test@' + commit[:10])
            plugins = cache / 'Editor/PluginsPackage'
            plugins.mkdir(parents=True)
            (project / 'Packages').mkdir()
            (cache / 'package.json').write_text(json.dumps({'name': 'com.magictavern.test', 'version': '1.0'}))
            (project / 'Packages/packages-lock.json').write_text(json.dumps({'dependencies': {
                'com.magictavern.test': {'source': 'git', 'hash': commit}}}))
            unitypackage(plugins / 'a.unitypackage')
            unitypackage(base / 'b.unitypackage', 'Assets/Other.txt')
            defs = {'a.unitypackage': {'id': 'a'}, 'b.unitypackage': {'id': 'b'}}
            import_project(project, root, defs)
            report = {'catalog_commit': 'catalog', 'rows': [{
                'state': 'scanned', 'package_name': 'com.magictavern.test', 'package_short_name': 'test',
                'package_version': '1.0', 'commit': commit, 'project_id': 1, 'tag': 'test-1.0',
                'package_json_sha256': 'jsonhash', 'files': [
                    {'path': 'Editor/PluginsPackage/a.unitypackage', 'blob_id': 'a'},
                    {'path': 'Runtime/b.unitypackage', 'blob_id': 'b'}]}]}
            fetch.side_effect = lambda _, item, commit, target: shutil.copyfile(
                plugins / 'a.unitypackage' if item['blob_id'] == 'a' else base / 'b.unitypackage', target)
            import_scan(root, report, defs, 'https://example.invalid', 'token', lambda _: None)
            before = read_json(root / 'index.json')
            import_scan(root, report, defs, 'https://example.invalid', 'token', lambda _: None)
            self.assertEqual(before, read_json(root / 'index.json'))
            self.assertEqual(before['package_mappings'], import_project(project, root, defs)['package_mappings'])
            self.assertEqual(verify(root)['plugins'], 2)

    def test_numeric_order_excludes_debug(self):
        tags = ['pkg-1.9.0', 'pkg-1.10.0', 'pkg-2.0.0-debug', 'pkg-1.10.0-rc1']
        self.assertEqual(highest_release('pkg', tags), 'pkg-1.10.0')

    def test_no_release_is_explicit(self):
        self.assertIsNone(highest_release('pkg', ['pkg-debug', 'pkg-develop-debug']))

    def test_named_published_variant_is_included_and_suffix_is_version(self):
        self.assertEqual(highest_release('pkg', ['pkg-3.0.2', 'pkg-original-3.8.0', 'pkg-v9.9-3.0.0']), 'pkg-original-3.8.0')
        self.assertEqual(highest_release('pkg', ['pkg-develop-1.0.0']), 'pkg-develop-1.0.0')
        self.assertEqual(highest_release('pkg', ['pkg-feature-1.0.0', 'pkg-1.0.0']), 'pkg-1.0.0')

    def test_no_cross_package_tag_selection(self):
        self.assertEqual(highest_release('spine', ['spine-3.0.2', 'spinetimeline-4.1.0']), 'spine-3.0.2')

    def test_git_blob_integrity(self):
        project = MagicMock()
        content = b'archive bytes'
        project.files.raw.side_effect = lambda **kwargs: kwargs['action'](content)
        oid = hashlib.sha1(b'blob 13\0' + content).hexdigest()
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / 'a.zip'
            download(project, {'path': 'Editor/PluginsPackage/a.zip', 'blob_id': oid}, 'commit', target)
            self.assertEqual(target.read_bytes(), content)
            with self.assertRaisesRegex(ValueError, 'checksum'):
                download(project, {'path': 'a.zip', 'blob_id': 'wrong'}, 'commit', target)

    def test_lfs_pointer_is_not_published(self):
        project = MagicMock()
        project.files.raw.side_effect = lambda **kwargs: kwargs['action'](b'version https://git-lfs.github.com/spec/v1\n')
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, 'LFS'):
                download(project, {'path': 'a.zip', 'blob_id': 'oid'}, 'commit', Path(directory) / 'a.zip')

    @patch('dashboard.plugin_catalog_sync.gitlab.Gitlab')
    def test_scan_uses_pagination_and_fixed_commit(self, client):
        project = client.return_value.__enter__.return_value.projects.get.return_value
        tag = MagicMock()
        tag.name = 'pkg-1.0.0'
        tag.commit = {'id': 'c' * 40}
        project.tags.list.return_value = [tag]
        project.files.raw.side_effect = lambda **kwargs: kwargs['action'](b'{"name":"com.test.pkg","version":"1.0.0"}')
        project.repository_tree.return_value = [
            {'type': 'blob', 'path': 'Other/a.zip', 'id': 'blob'},
            {'type': 'blob', 'path': 'Editor/PluginsPackage/b.unitypackage', 'id': 'blob2'}]
        result = scan_repository('https://example.invalid', 'private', 1, [{'PackageName': 'pkg'}])
        self.assertEqual(result[0]['state'], 'scanned')
        self.assertEqual(len(result[0]['files']), 2)
        project.repository_tree.assert_called_once_with(ref='c' * 40, recursive=True, iterator=True, per_page=100)

    @patch('dashboard.plugin_catalog_sync.gitlab.Gitlab')
    def test_explicit_historical_tag_is_scanned_and_missing_tag_is_an_error(self, client):
        project=client.return_value.__enter__.return_value.projects.get.return_value
        old,new=MagicMock(),MagicMock()
        old.name,new.name='pkg-1.0.0','pkg-2.0.0'
        old.commit,new.commit={'id':'a'*40},{'id':'b'*40}
        project.tags.list.return_value=[old,new]
        project.files.raw.side_effect=lambda **kw:kw['action'](b'{"name":"com.test.pkg","version":"1.0.0"}')
        project.repository_tree.return_value=[]
        rows=scan_repository('https://example.invalid','token',1,[{'PackageName':'pkg','Tag':'pkg-1.0.0'}])
        self.assertEqual(rows[0]['commit'],'a'*40)
        project.repository_tree.assert_called_once_with(ref='a'*40,recursive=True,iterator=True,per_page=100)
        rows=scan_repository('https://example.invalid','token',1,[{'PackageName':'pkg','Tag':'missing'}])
        self.assertEqual(rows[0]['state'],'error')

    @patch('dashboard.plugin_catalog_sync.gitlab.Gitlab')
    def test_historical_import_preserves_current_catalog_selection(self, client):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            current={'mapping_keys':['current'], 'catalog_commit':'catalog','selection':'highest'}
            (root/'index.json').write_text(json.dumps({'schema_version':1,'plugins':[],'package_mappings':[],
                                                     'project_snapshots':[],'catalog_snapshot':current}))
            report={'catalog_commit':'catalog','rows':[{'state':'scanned','package_name':'com.test.pkg',
                'package_version':'1.0.0','commit':'a'*40,'package_json_sha256':'b'*64,'files':[],
                'project_id':1,'tag':'pkg-1.0.0'}]}
            import_scan(root,report,{},'https://example.invalid','token',update_catalog_snapshot=False)
            saved=read_json(root/'index.json')
            self.assertEqual(saved['catalog_snapshot'],current)
            self.assertEqual(len(saved['package_mappings']),1)
            self.assertEqual(saved['package_mappings'][0]['plugins'],[])

    @patch('dashboard.plugin_catalog_sync.gitlab.Gitlab')
    def test_access_error_not_misreported_as_empty_repository(self, client):
        project = client.return_value.__enter__.return_value.projects.get.return_value
        project.tags.list.side_effect = RuntimeError('https://user:secret@example.invalid')
        result = scan_repository('https://example.invalid', 'private', 1, [{'PackageName': 'pkg'}])
        self.assertEqual(result[0]['state'], 'error')
        self.assertNotIn('secret', str(result))
