from unittest.mock import patch
from django.test import TestCase, Client, override_settings
from django.contrib.auth.models import User
from dashboard.models import GitLabSettings, CatalogCache
from dashboard.catalog import source, merge_snapshot


class AdminSettingsTests(TestCase):
    def setUp(self):
        self.admin = User.objects.create_user('admin', password='test-admin-password', is_staff=True)
        self.values = dict(url='https://gitlab.example/', ref='master', private_token='never-render-this-token', blacklist='12, 013\n12')

    def test_local_anonymous_and_non_staff_cannot_configure_or_sync(self):
        self.assertEqual(self.client.get('/settings/').status_code,302)
        self.assertEqual(self.client.post('/settings/',self.values).status_code,302)
        self.assertEqual(self.client.post('/api/catalog/refresh/').status_code,403)
        self.client.force_login(User.objects.create_user('viewer'))
        self.assertEqual(self.client.post('/settings/',self.values).status_code,302)
        self.assertEqual(self.client.post('/api/catalog/refresh/').status_code,403)
        self.assertFalse(GitLabSettings.objects.exists())

    def test_login_save_keep_token_and_never_render_secret(self):
        self.assertTrue(self.client.login(username='admin',password='test-admin-password'))
        response=self.client.post('/settings/',self.values)
        self.assertEqual(response.status_code,302)
        record=GitLabSettings.objects.get(pk=1)
        self.assertEqual(record.blacklist,['12','13'])
        response=self.client.get('/settings/')
        self.assertNotContains(response,'never-render-this-token')
        self.assertEqual(response.headers['Cache-Control'],'max-age=0, no-cache, no-store, must-revalidate, private')
        self.assertContains(self.client.get('/'),'设置')
        self.client.post('/settings/',dict(self.values,private_token='',blacklist=''))
        record.refresh_from_db()
        self.assertEqual(record.private_token,'never-render-this-token')
        self.assertEqual(record.blacklist,[])
        self.assertEqual(source()[1:3],('https://gitlab.example/','never-render-this-token'))

    def test_invalid_input_and_csrf(self):
        self.client.force_login(self.admin)
        response=self.client.post('/settings/',dict(self.values,blacklist='-1,abc',url='https://user:pass@gitlab.example/'))
        self.assertEqual(response.status_code,200)
        self.assertFalse(GitLabSettings.objects.exists())
        self.assertNotContains(response,'never-render-this-token')
        strict=Client(enforce_csrf_checks=True)
        strict.force_login(self.admin)
        self.assertEqual(strict.post('/settings/',self.values).status_code,403)

    def test_mtp_blacklist_preserves_installed_and_tree_debug_bypasses(self):
        config=GitLabSettings.objects.create(url='https://gitlab.example/',private_token='token',blacklist=['12'])
        packages=[dict(catalog_id='12:'+name,name=name,short_name=name,tag=name+'-1.0.0',available_tags=[name+'-1.0.0'],versions={},dependencies=[]) for name in ['installed','hidden']]
        CatalogCache.objects.create(key=source()[0],packages=packages,revision='commit')
        snapshot={'packages':[dict(name='installed',tag='installed-1.0.0',depth=0,dependencies=[{'name':'hidden','requested_tag':''}])]}
        result=merge_snapshot(snapshot)
        self.assertEqual([p['name'] for p in result['packages']],['installed'])
        self.assertEqual(result['catalog_hidden'],1)
        self.assertEqual(result['project_packages'][0]['dependencies'],snapshot['packages'][0]['dependencies'])
        config.debug_mode=True;config.save()
        self.assertEqual(len(merge_snapshot(snapshot)['packages']),2)

    def test_managed_config_does_not_depend_on_local_file(self):
        GitLabSettings.objects.create(url='https://gitlab.example/',private_token='token')
        with patch.dict('os.environ',{'MTP_CONFIG_PATH':'/missing/config'}):
            self.assertEqual(source()[2],'token')
