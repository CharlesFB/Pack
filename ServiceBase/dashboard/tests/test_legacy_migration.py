import copy
import uuid
from datetime import timedelta
from django.core.cache import cache
from django.test import TestCase, override_settings
from django.utils import timezone
from dashboard.models import UnityProject, LegacyMigration, InstallJob, InstallStep, InstallAttempt
from dashboard.tests import test_installations as base_tests
from dashboard.tests.test_installation_plans import baseline, node, installed, FakeSource, mappings
from dashboard.plan_resolver import resolve
from dashboard.package_maintenance import uninstall_preview


def entry(number='a', kind='plugin'):
    return dict(id=number*64,kind=kind,name='Example.unitypackage' if kind=='plugin' else 'com.test.local',package_name='com.test.local',tag='',revision='',
        source_file='Packages/plugin-assets.json' if kind=='plugin' else 'Packages/upm-install.json',source_digest='b'*64,entry_digest='c'*64,
        warning='历史记录，仅保存基线',is_local=False,migrated=False,roots=['Assets/Example'] if kind=='plugin' else [],file_count=1 if kind=='plugin' else 0,missing=0,files=[])


def local(name='com.test.local', root='Assets/Local', deps=None):
    return dict(name=name,version='1.0.0',display_name='本地包',description='',warning='',root=root,manifest_sha256='d'*64,dependencies=deps or [])


@override_settings(LOCAL_MODE=True)
class LegacyMigrationTests(TestCase):
    native=base_tests.InstallationTests.native
    def setUp(self):
        cache.clear();self.identity=dict(instance_id='legacy-project',session_id='session')
        self.native('register',dict(self.identity,project_name='Legacy',project_path='/fixture',machine_name='fixture',unity_version='2022.3.62',process_id=1))
        self.project=UnityProject.objects.get();self.base='/api/projects/legacy-project/legacy-migrations/'
        self.sync()
    def sync(self, **values):
        return self.native('upm/sync',dict(self.identity,protocol=1,executor_version=9,migration_capable=True,diagnostic='检测到 MTP；服务安装暂被阻止。',**values))
    def preview(self):
        r=self.client.post(self.base,dict(request_key=str(uuid.uuid4())),content_type='application/json');self.assertEqual(r.status_code,202,r.content)
        row=r.json();reply=dict(request_id=row['id'],preview_digest='e'*64,error='',entries=[entry(),entry('d','root')])
        self.assertEqual(self.sync().json()['legacy_request'],{'request_id':row['id']})
        result=self.sync(has_legacy_preview=True,legacy_preview=reply);self.assertEqual(result.status_code,200,result.content)
        return row['id']
    def confirm(self,pk,selected=None):
        return self.client.post(self.base+pk+'/confirm/',dict(digest='e'*64,entries=selected if selected is not None else ['a'*64]),content_type='application/json')
    def test_no_automatic_read_or_migration_and_mtp_does_not_block_record_only_migration(self):
        self.assertIsNone(self.sync().json()['legacy_request']);self.assertFalse(LegacyMigration.objects.exists())
        self.assertFalse(self.client.get('/api/projects/legacy-project/installations/').json()['executor_ready'])
        pk=self.preview();self.assertFalse(InstallJob.objects.exists())
        self.assertEqual(self.confirm(pk,[]).status_code,400)
        response=self.confirm(pk);self.assertEqual(response.status_code,202,response.content)
        work=self.sync().json()['work'];self.assertEqual(work['target']['operation'],'legacy_migrate')
        self.assertEqual(work['target']['migration_entries'],['a'*64])
    def test_confirm_replay_preserves_selection_and_project_lock(self):
        pk=self.preview();first=self.confirm(pk);second=self.confirm(pk)
        self.assertEqual(second.status_code,200);self.assertEqual(first.json()['job']['id'],second.json()['job']['id'])
        self.assertEqual(self.confirm(pk,['d'*64]).status_code,400);self.assertEqual(InstallJob.objects.count(),1)
        response=self.client.post(self.base,dict(request_key=str(uuid.uuid4())),content_type='application/json');self.assertEqual(response.status_code,409)
    def test_preview_expiry_and_session_change_require_new_user_request(self):
        pk=self.preview();LegacyMigration.objects.filter(pk=pk).update(created_at=timezone.now()-timedelta(hours=1))
        self.assertEqual(self.confirm(pk).status_code,400)
        pk=self.preview();LegacyMigration.objects.filter(pk=pk).update(session_id='old-session');self.assertEqual(self.confirm(pk).status_code,400)
    def test_preview_is_immutable_and_raw_legacy_objects_are_never_retained(self):
        pk=self.preview();old=LegacyMigration.objects.get(pk=pk).payload
        reply=dict(request_id=pk,preview_digest='f'*64,error='',entries=[entry('f')],PrivateToken='secret')
        self.sync(has_legacy_preview=True,legacy_preview=reply)
        self.assertEqual(LegacyMigration.objects.get(pk=pk).payload,old)
        self.assertNotIn('files',old['entries'][0])
    def test_old_executor_cannot_receive_migration_and_success_requires_matching_receipt(self):
        pk=self.preview();self.confirm(pk);work=self.sync().json()['work']
        old=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=8));self.assertIsNone(old.json()['work'])
        report=dict(attempt_id=work['attempt_id'],sequence=1,state='succeeded',evidence={})
        self.assertEqual(self.sync(report=report).status_code,400);self.assertTrue(InstallJob.objects.get().active)
        report['evidence']=dict(verified=True,migration_id=pk,migration_digest='e'*64,record_digest='f'*64)
        self.assertEqual(self.sync(report=report).status_code,200);self.assertEqual(self.sync(report=report).status_code,200)
        self.assertFalse(InstallJob.objects.get().active);self.assertEqual(LegacyMigration.objects.get(pk=pk).state,'completed')
    def test_source_escape_or_url_secret_in_preview_is_rejected(self):
        for field,value in [('source_file','../config.json'),('name','https://token@example.com/a')]:
            r=self.client.post(self.base,dict(request_key=str(uuid.uuid4())),content_type='application/json')
            item=entry();item[field]=value
            response=self.sync(has_legacy_preview=True,legacy_preview=dict(request_id=r.json()['id'],preview_digest='e'*64,error='',entries=[item]))
            self.assertEqual(response.status_code,400)
    def test_fatal_executor_cannot_create_migration(self):
        self.native('upm/sync',dict(self.identity,protocol=1,executor_version=9,migration_capable=False,diagnostic='日志损坏'))
        r=self.client.post(self.base,dict(request_key=str(uuid.uuid4())),content_type='application/json');self.assertEqual(r.status_code,409)
    def test_preview_request_is_idempotent_and_old_pending_request_is_superseded(self):
        key=str(uuid.uuid4());one=self.client.post(self.base,dict(request_key=key),content_type='application/json').json()
        two=self.client.post(self.base,dict(request_key=key),content_type='application/json').json();self.assertEqual(one['id'],two['id'])
        new=self.client.post(self.base,dict(request_key=str(uuid.uuid4())),content_type='application/json').json()
        self.assertEqual(self.sync().json()['legacy_request']['request_id'],new['id']);self.assertEqual(LegacyMigration.objects.get(pk=one['id']).state,'superseded')
    def test_another_accounts_project_cannot_be_read_or_migrated(self):
        from django.contrib.auth import get_user_model
        pk=self.preview();self.project.owner=get_user_model().objects.create_user('another');self.project.save(update_fields=['owner'])
        self.assertEqual(self.client.get(self.base).status_code,404)
        self.assertEqual(self.client.post(self.base,dict(request_key=str(uuid.uuid4())),content_type='application/json').status_code,404)
        self.assertEqual(self.confirm(pk).status_code,404)


@override_settings(LOCAL_MODE=True)
class AssetsPackageTests(TestCase):
    native=base_tests.InstallationTests.native
    def setUp(self):
        cache.clear();self.identity=dict(instance_id='assets-project',session_id='session')
        self.native('register',dict(self.identity,project_name='Assets',project_path='/fixture',machine_name='fixture',unity_version='2022.3.62',process_id=1))
        self.project=UnityProject.objects.get()
    def sync(self,state,version=9):
        return self.native('upm/sync',dict(self.identity,protocol=1,executor_version=version,migration_capable=True,has_package_state=True,package_state=state))
    def test_assets_packages_show_without_mtp_and_duplicate_roots_remain_visible(self):
        state=baseline();state['assets_packages']=[local(name='com.magictavern.local'),local(name='com.magictavern.local',root='Assets/Second')]
        self.assertEqual(self.sync(state).status_code,200)
        response=self.client.get('/api/projects/assets-project/mtp/').json()['response']
        self.assertEqual(response['adapter'],'servicebase');self.assertEqual(len(response['assets_packages']),2)
        package=response['project_packages'][0];self.assertEqual(package['assets_roots'],['Assets/Local','Assets/Second']);self.assertTrue(package['is_local'])
    def test_assets_path_escape_and_duplicate_roots_rejected(self):
        for rows in [[local(root='Assets/../Packages')],[local(),local()]]:
            state=baseline();state['assets_packages']=rows;self.assertEqual(self.sync(state).status_code,400)
    def test_assets_name_collision_blocks_upm_install_plan(self):
        root=node('local');state=baseline();state['assets_packages']=[local()]
        result=resolve(FakeSource(root,[root]),{},state,'2022.3.62',mappings([root]),[])
        self.assertIn('Assets',result['errors'][0])
    def test_assets_dependency_prevents_upm_uninstall(self):
        target=node('dep');state=baseline([installed(target)])
        state['assets_packages']=[local(deps=[dict(name='com.test.dep',version='1.0.0')])]
        with self.assertRaisesRegex(ValueError,'仍被其他包引用'):uninstall_preview(self.project,state,dict(name='com.test.dep'))
    def test_assets_dependency_constrains_shared_update(self):
        root=node('root',deps={'com.test.dep':'1.0.0'});nodes=[root,node('dep'),node('dep','2.0.0')]
        state=baseline();state['assets_packages']=[local(deps=[dict(name='com.test.dep',version='2.0.0')])]
        result=resolve(FakeSource(root,nodes),{},state,'2022.3.62',mappings(nodes),[])
        self.assertEqual(result['packages'][0]['version'],'2.0.0')
    def test_v8_downgrade_cannot_bypass_assets_protection(self):
        state=baseline();state['assets_packages']=[local()];self.sync(state)
        response=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=8))
        self.assertEqual(response.status_code,200)
        jobs=self.client.get('/api/projects/assets-project/installations/').json();self.assertFalse(jobs['executor_ready']);self.assertIn('v9',jobs['executor_diagnostic'])
