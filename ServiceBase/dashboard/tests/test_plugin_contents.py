import json
import tempfile
import zipfile
from pathlib import Path
from django.contrib.auth.models import User
from django.test import TestCase, override_settings
from dashboard.plugin_repository import stage_artifact, write_json
from dashboard.tests.test_plugin_repository import unitypackage


@override_settings(LOCAL_MODE=True)
class PluginContentTests(TestCase):
    def setUp(self):
        temporary=tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root=Path(temporary.name)
        config=override_settings(PLUGIN_REPOSITORY_ROOT=self.root); config.enable(); self.addCleanup(config.disable)
        source=self.root/'sample.unitypackage'; unitypackage(source)
        self.item=stage_artifact(self.root,source,{'id':'sample','name':'Sample','version':'1.0'})
        source=self.root/'sample.zip'
        with zipfile.ZipFile(source,'w') as archive:
            archive.writestr('Plugin/readme.txt','hello')
            archive.writestr('Plugin/readme.txt.meta','metadata')
            archive.writestr('Plugin/Nested/empty.bytes',b'')
            for i in range(105): archive.writestr(f'Plugin/File{i:03}.txt','x')
        self.zip=stage_artifact(self.root,source,{'id':'sample','name':'Sample','version':'2.0'})
        write_json(self.root/'index.json',dict(schema_version=1,plugins=[self.item,self.zip],package_mappings=[]))

    def url(self, item=None):
        return f"/api/plugins/sample/builds/{(item or self.item)['build_id']}/contents/"

    def browse(self, path='', item=None, **params):
        item=item or self.item
        return self.client.get(self.url(item),dict(version=item['version'],path=path,**params))

    def test_unity_resource_paths_and_metadata_are_read_only(self):
        before=(self.root/self.item['directory']/'manifest.json').read_bytes()
        root=self.browse().json()
        self.assertEqual((root['folder_count'],root['file_count'],root['meta_count']),(2,1,1))
        self.assertEqual([(e['name'],e['folder']) for e in root['entries']],[('Assets',True)])
        response=self.browse('Assets/Test')
        self.assertEqual([(e['name'],e['size_bytes']) for e in response.json()['entries']],[('file.txt',7)])
        self.assertNotIn(str(self.root),response.content.decode())
        self.assertEqual(response['Cache-Control'],'no-store')
        meta=self.browse('Assets/Test',meta='1').json()
        self.assertEqual([e['meta'] for e in meta['entries']],[False,True])
        self.assertEqual(meta['entries'][1]['size_bytes'],38)
        self.assertEqual(before,(self.root/self.item['directory']/'manifest.json').read_bytes())
        self.assertEqual(self.client.post(self.url()).status_code,405)

    def test_zip_original_paths_folders_first_pagination_and_exact_build(self):
        root=self.browse(item=self.zip).json()
        self.assertEqual(root['entries'][0]['path'],'Plugin')
        first=self.browse('Plugin',item=self.zip).json()
        second=self.browse('Plugin',item=self.zip,page=2).json()
        self.assertEqual((first['total'],len(first['entries']),len(second['entries'])),(107,100,7))
        self.assertEqual(first['entries'][0]['name'],'Nested')
        self.assertFalse({e['path'] for e in first['entries']} & {e['path'] for e in second['entries']})
        self.assertEqual(self.browse('Plugin/Nested',item=self.zip).json()['entries'][0]['size_bytes'],0)
        self.assertEqual(self.browse('Plugin',item=self.zip,meta='1').json()['total'],108)
        self.assertEqual(self.client.get(self.url(),{'version':'2.0'}).status_code,404)
        self.assertEqual(self.browse('Plugin').status_code,404)

    def test_bad_paths_metadata_and_missing_artifact(self):
        for path in ('../secret','/etc','Assets//Test','Assets/./Test'):
            self.assertEqual(self.browse(path).status_code,400)
        self.assertEqual(self.browse(page=0).status_code,400)
        self.assertEqual(self.browse(page='bad').status_code,400)
        folder=self.root/self.item['directory']
        (folder/'artifact.unitypackage').unlink()
        self.assertEqual(self.browse('Assets/Test').status_code,200) # Recorded contents remain available.
        manifest=json.loads((folder/'manifest.json').read_text()); manifest['plugin_id']='other'
        write_json(folder/'manifest.json',manifest)
        response=self.browse()
        self.assertEqual(response.status_code,503)
        self.assertNotIn(str(self.root),response.content.decode())

    @override_settings(LOCAL_MODE=False)
    def test_contents_follow_repository_viewer_access(self):
        self.assertEqual(self.browse().status_code,401)
        self.client.force_login(User.objects.create_user('contents-viewer'))
        self.assertEqual(self.browse().status_code,200)
