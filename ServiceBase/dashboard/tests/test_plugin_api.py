import json
import tempfile
from pathlib import Path

from django.contrib.auth.models import User
from django.test import TestCase, override_settings

from dashboard.plugin_repository import stage_artifact, write_json
from dashboard.tests.test_plugin_repository import unitypackage


@override_settings(LOCAL_MODE=True)
class PluginApiTests(TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        settings = override_settings(PLUGIN_REPOSITORY_ROOT=self.root)
        settings.enable()
        self.addCleanup(settings.disable)
        source = self.root / 'source.unitypackage'
        unitypackage(source, 'Packages/test/file.cs', meta=False)
        first = stage_artifact(self.root, source, {'id': 'test', 'name': 'Test plugin', 'version': ''})
        second = stage_artifact(self.root, source, {'id': 'test', 'name': 'Test plugin', 'version': '2.0'})
        third = stage_artifact(self.root, source, {'id': 'vendor-test', 'directory_name': 'vendor', 'name': 'Vendor', 'version': '1.0'}, namespace=None)
        self.build = first
        self.commit = 'a' * 40
        self.index = {'schema_version': 1, 'plugins': [first, second, third], 'package_mappings': [{
            'key': 'com.test@' + self.commit, 'package_name': 'com.test', 'package_version': '1.0',
            'commit': self.commit, 'plugins': [{**first, 'source_path': 'Editor/PluginsPackage/test.unitypackage',
                'required': None, 'phase': 'unconfirmed', 'order': 0}]}],
            'catalog_snapshot': {'mapping_keys': ['com.test@' + self.commit]}}
        self.save()

    def save(self):
        write_json(self.root / 'index.json', self.index)

    def test_groups_filter_pagination_and_read_only(self):
        before = (self.root / 'index.json').read_bytes()
        data = self.client.get('/api/plugins/groups/').json()
        self.assertEqual((data['plugin_count'], data['build_count']), (2, 3))
        self.assertEqual(data['groups'][0]['id'], 'magictavern')
        data = self.client.get('/api/plugins/', {'group': 'magictavern', 'q': 'TEST'}).json()
        self.assertEqual(data['total'], 1)
        self.assertEqual(data['plugins'][0]['versions'], ['', '2.0'])
        self.assertEqual(self.client.get('/api/plugins/', {'q': 'absent'}).json()['total'], 0)
        self.assertEqual(len(self.client.get('/api/plugins/', {'page': 2, 'page_size': 1}).json()['plugins']), 1)
        self.assertEqual(self.client.get('/api/plugins/', {'page': 'bad'}).status_code, 400)
        self.assertEqual(self.client.post('/api/plugins/').status_code, 405)
        self.assertEqual(before, (self.root / 'index.json').read_bytes())

    def test_detail_versions_reviews_and_safe_dto(self):
        response = self.client.get('/api/plugins/test/')
        data = response.json()['plugin']
        self.assertEqual(len(data['builds']), 2)
        build = data['builds'][0]
        self.assertEqual(build['version_status'], 'unconfirmed')
        self.assertEqual(build['project_installation_status'], 'not_checked')
        self.assertEqual(build['availability'], 'available')
        self.assertEqual({r['code'] for r in build['reviews']}, {'non_assets_paths', 'missing_meta_paths'})
        self.assertEqual(build['package_references'][0]['commit'], self.commit)
        self.assertNotIn(str(self.root), response.content.decode())
        self.assertNotIn('directory', response.content.decode())
        self.assertEqual(response['Cache-Control'], 'no-store')
        self.assertEqual(self.client.get('/api/plugins/absent/').status_code, 404)

    def test_exact_commit_mapping(self):
        url = '/api/plugins/package-mapping/'
        self.assertEqual(self.client.get(url, {'name': 'com.test', 'commit': self.commit[:8]}).status_code, 400)
        self.assertEqual(self.client.get(url, {'name': 'com.test', 'commit': 'b' * 40}).status_code, 404)
        data = self.client.get(url, {'name': 'com.test', 'commit': self.commit.upper()}).json()
        self.assertEqual(data['plugins'][0]['build_id'], self.build['build_id'])
        self.assertIsNone(data['plugins'][0]['required'])
        self.assertNotIn('source_path', json.dumps(data))

    def test_artifact_missing_size_mismatch_and_metadata_mismatch(self):
        folder = self.root / self.build['directory']
        artifact = folder / 'artifact.unitypackage'
        artifact.write_bytes(b'bad')
        self.assertEqual(self.client.get('/api/plugins/test/').json()['plugin']['builds'][0]['availability'], 'size_mismatch')
        artifact.unlink()
        self.assertEqual(self.client.get('/api/plugins/test/').json()['plugin']['builds'][0]['availability'], 'missing')
        manifest = json.loads((folder / 'manifest.json').read_text())
        manifest['artifact'] = '../source.unitypackage'
        write_json(folder / 'manifest.json', manifest)
        self.assertEqual(self.client.get('/api/plugins/test/').json()['plugin']['builds'][0]['availability'], 'metadata_unavailable')

    def test_bad_index_fails_without_exposing_paths(self):
        self.index['plugins'][0]['directory'] = '../outside'
        self.save()
        response = self.client.get('/api/plugins/')
        self.assertEqual(response.status_code, 503)
        self.assertNotIn(str(self.root), response.content.decode())
        (self.root / 'index.json').unlink()
        self.assertEqual(self.client.get('/api/plugins/groups/').status_code, 503)

    @override_settings(LOCAL_MODE=False)
    def test_authenticated_viewer_access(self):
        self.assertEqual(self.client.get('/api/plugins/').status_code, 401)
        self.client.force_login(User.objects.create_user('plugin-viewer'))
        self.assertEqual(self.client.get('/api/plugins/').status_code, 200)

    def test_workspace_contains_independent_plugin_tab(self):
        response = self.client.get('/')
        self.assertContains(response, 'id="plugins-panel"')
        self.assertContains(response, 'dashboard/plugins.js')
