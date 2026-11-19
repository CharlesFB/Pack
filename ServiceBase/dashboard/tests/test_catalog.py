from unittest.mock import patch, MagicMock
from django.test import TestCase, override_settings
from django.core.cache import cache
from dashboard.catalog import normalize, merge_snapshot, refresh_catalog, latest_tag
from dashboard.models import CatalogCache


class CatalogTests(TestCase):
    def setUp(self):
        cache.clear()
        self.source = patch('dashboard.catalog.source', return_value=('test','https://gitlab.example','secret','master'))
        self.source.start()
        self.addCleanup(self.source.stop)
        self.directory = {'Packages':[
            {'PackageName':s, 'ProjectId':i, 'Tags':[s+'-1.0.0']} for i,s in enumerate(['zroot','adep','afree','broot'])]}
        self.info = {'PackageCache':{str(i):{s+'-1.0.0':{'name':s,'displayName':s,'dependenciesGit':{'dep':'https://secret@example/repo#dep-1.0.0'}}}
                                   for i,s in enumerate(['zroot','adep','afree','broot'])}}
        self.packages = normalize(self.directory,self.info)
        CatalogCache.objects.create(key='test', packages=self.packages, revision='commit')

    def test_merge_sort_and_project_tree_separation(self):
        records = [dict(name=s,tag=s+'-1.0.0',depth=d,dependencies=[]) for s,d in [('zroot',0),('adep',1),('broot',0),('orphan',0)]]
        snapshot = {'packages':records,'state':'ready'}
        result = merge_snapshot(snapshot)
        self.assertEqual([p['name'] for p in result['packages']], ['broot','zroot','adep','afree'])
        self.assertEqual([p['installation_status'] for p in result['packages']], ['installed']*3+['not_installed'])
        self.assertEqual([p['name'] for p in result['project_packages']],[p['name'] for p in records])
        self.assertTrue(all(p['dependencies'] == [] for p in result['project_packages']))
        self.assertNotIn('catalog_id',snapshot['packages'][0])
        self.assertNotIn('secret',str(result))

    def test_explicit_uninstalled_and_duplicate_names(self):
        self.packages[2]['name'] = 'zroot'
        CatalogCache.objects.filter(key='test').update(packages=self.packages)
        result=merge_snapshot({'packages':[dict(name='zroot',tag='zroot-1.0.0',depth=0,installation_status='not_installed')]})
        self.assertEqual(len(result['packages']),4)
        self.assertTrue(all(p['installation_status']=='not_installed' for p in result['packages']))
        self.assertEqual(len({p['catalog_id'] for p in result['packages']}),4)

    def test_refresh_failure_keeps_last_cache_and_unlocks(self):
        with patch('dashboard.catalog.gitlab.Gitlab',side_effect=RuntimeError('secret')):
            with self.assertRaises(RuntimeError): refresh_catalog()
        self.assertEqual(CatalogCache.objects.get(key='test').revision,'commit')
        self.assertIsNone(cache.get('catalog-refresh:test'))
        from django.contrib.auth.models import User
        self.client.force_login(User.objects.create_user('catalog-admin',is_staff=True))
        with patch('dashboard.catalog.refresh_catalog',side_effect=RuntimeError('secret')):
            response=self.client.post('/api/catalog/refresh/')
        self.assertEqual(response.status_code,502)
        self.assertNotIn('secret',response.content.decode())

    def test_both_files_use_same_commit(self):
        import json
        gl=MagicMock()
        project=gl.__enter__.return_value.projects.get.return_value
        project.commits.get.return_value.id='fixed-commit'
        def raw(file_path,ref,streamed,action):
            self.assertEqual(ref,'fixed-commit')
            action(json.dumps(self.directory if file_path=='upm-list.json' else self.info).encode())
        project.files.raw.side_effect=raw
        with patch('dashboard.catalog.gitlab.Gitlab',return_value=gl): self.assertTrue(refresh_catalog())
        self.assertEqual(project.files.raw.call_count,2)
        self.assertEqual(CatalogCache.objects.get(key='test').revision,'fixed-commit')

    def test_credential_scope_and_versions(self):
        with patch('dashboard.catalog.source',return_value=('other','url','token','master')):
            self.assertEqual(merge_snapshot({'packages':[]})['catalog_state'],'missing')
        self.assertEqual(latest_tag(['x-1.9.0','x-1.10.0','x-2.0.0-debug']),'x-1.10.0')

    @override_settings(LOCAL_MODE=False)
    def test_non_admin_cannot_refresh_shared_catalog(self):
        from django.contrib.auth.models import User
        self.client.force_login(User.objects.create_user('viewer'))
        self.assertEqual(self.client.post('/api/catalog/refresh/').status_code,403)
