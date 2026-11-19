import io
import json
import hashlib
import tempfile
import uuid
import zipfile
from pathlib import Path
from django.test import override_settings, TestCase
from dashboard.tests import test_installations as base
from dashboard.tests.test_plugin_repository import unitypackage
from dashboard.plugin_repository import stage_artifact, write_json
from dashboard.plugin_delivery import prepare, recipe_for, placed_files
from dashboard.models import InstallJob, InstallStep, InstallAttempt, PluginInstallation


class PluginInstallationTests(TestCase):
    native = base.InstallationTests.native
    sync = base.InstallationTests.sync
    submit = base.InstallationTests.submit
    report = base.InstallationTests.report
    def setUp(self):
        base.InstallationTests.setUp(self)
        self.tmp=tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name); self.repo=self.root/'repository'; self.repo.mkdir()
        self.override=override_settings(PLUGIN_REPOSITORY_ROOT=self.repo,PLUGIN_DELIVERY_ROOT=self.root/'delivery')
        self.override.enable(); self.addCleanup(self.override.disable)
        self.artifact=self.root/'sample.unitypackage'; unitypackage(self.artifact)
        self.item=stage_artifact(self.repo,self.artifact,{'id':'sample','name':'Sample'})
        write_json(self.repo/'index.json',{'schema_version':1,'plugins':[self.item],'package_mappings':[]})
        self.native('upm/sync',dict(self.identity,protocol=1,executor_version=3))

    def plugin(self):
        return self.client.post(self.url,dict(operation='plugin',plugin_id='sample',build_id=self.item['build_id'],request_key=str(uuid.uuid4())),content_type='application/json')

    def test_delivery_is_deterministic_and_asset_hashes_preserved(self):
        first=prepare('sample',self.item['build_id']); second=prepare('sample',self.item['build_id'])
        self.assertEqual(first,second)
        recipe=recipe_for(first)
        self.assertEqual([f['path'] for f in recipe['files']],['Assets/Test','Assets/Test/file.txt'])
        self.assertEqual(recipe['files'][1]['guid'],'a'*32)

    def test_zip_keeps_archive_structure_without_server_layout(self):
        file=self.root/'test.zip'
        with zipfile.ZipFile(file,'w') as z: z.writestr('Plugin/data.txt','content')
        item=stage_artifact(self.repo,file,{'id':'zip','name':'ZIP'})
        index=json.loads((self.repo/'index.json').read_text()); index['plugins'].append(item);write_json(self.repo/'index.json',index)
        original=prepare('zip',item['build_id'])
        self.assertTrue(original['requires_destination'])
        write_json(self.repo/'import-layouts.json',{'builds':{'zip@'+item['build_id']:{'mode':'assets','prefix':'Packages/','strip':'Plugin/'}}})
        target=prepare('zip',item['build_id']); self.assertEqual(target['format'],'zip')
        self.assertEqual(recipe_for(target)['files'][1]['path'],'Assets/Plugin/data.txt')
        self.assertEqual(target,original)
        self.assertEqual(placed_files(target,'Assets/Plugins')[1]['path'],'Assets/Plugins/Plugin/data.txt')

    def test_plugin_requires_v3_and_shares_upm_lock(self):
        self.native('upm/sync',dict(self.identity,protocol=1,executor_version=2))
        self.assertEqual(self.plugin().status_code,409)
        self.native('upm/sync',dict(self.identity,protocol=1,executor_version=3))
        self.assertEqual(self.plugin().status_code,202)
        self.assertEqual(self.submit().status_code,409)
        # The old executor must not receive even an already queued plugin's first command.
        self.assertIsNone(self.sync().json()['work'])

    def test_receipt_required_scoped_immutable_and_creates_record(self):
        self.plugin(); response=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=3)); work=response.json()['work']
        report=self.report(work,'succeeded'); report['evidence']={}
        self.assertEqual(self.sync(report).status_code,400)
        target=InstallJob.objects.get().target
        files=recipe_for(target)['files']
        for f in files: f.update(before_sha256='',before_meta_sha256='',existed=False,meta_existed=False)
        raw=json.dumps(dict(plugin_id='sample',build_id=self.item['build_id'],attempt_id=work['attempt_id'],files=files))
        data=dict(self.identity,attempt_id=work['attempt_id'],record_json=raw)
        self.assertEqual(self.native('plugins/receipt',dict(data,session_id='wrong')).status_code,400)
        self.assertEqual(self.native('plugins/receipt',data).status_code,200)
        self.assertEqual(self.native('plugins/receipt',data).status_code,200)
        self.assertEqual(self.native('plugins/receipt',dict(data,record_json=raw+' ')).status_code,400)
        report['evidence']=dict(build_id=target['build_id'],recipe_id=target['recipe_id'],record_digest=hashlib.sha256(raw.encode()).hexdigest())
        self.assertEqual(self.sync(report).status_code,200)
        self.assertEqual(PluginInstallation.objects.count(),1)
        self.assertEqual(self.client.get('/api/projects/install-project/plugins/').json()['installations'][0]['status'],'verified_at_import')

    def test_wrong_file_evidence_and_non_assets_zip_are_rejected(self):
        self.plugin();work=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=3)).json()['work']
        files=recipe_for(InstallJob.objects.get().target)['files']
        for f in files:f.update(before_sha256='',before_meta_sha256='',existed=False,meta_existed=False)
        files[-1]['sha256']='b'*64
        raw=json.dumps(dict(plugin_id='sample',build_id=self.item['build_id'],attempt_id=work['attempt_id'],files=files))
        self.assertEqual(self.native('plugins/receipt',dict(self.identity,attempt_id=work['attempt_id'],record_json=raw)).status_code,400)

    def test_imported_meta_snapshot_requires_source_hash_and_unchanged_content_guid(self):
        import copy
        self.plugin();work=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=3)).json()['work']
        files=recipe_for(InstallJob.objects.get().target)['files']
        for f in files:f.update(before_sha256='',before_meta_sha256='',existed=False,meta_existed=False,source_meta_sha256=f['meta_sha256'])
        files[-1]['meta_sha256']='b'*64
        record=dict(plugin_id='sample',build_id=self.item['build_id'],attempt_id=work['attempt_id'],verification='unity-import-v1',files=files,omitted_files=[])
        def post(value):
            return self.native('plugins/receipt',dict(self.identity,attempt_id=work['attempt_id'],record_json=json.dumps(value)))
        for key,value in (('source_meta_sha256','c'*64),('sha256','c'*64),('guid','c'*32)):
            wrong=copy.deepcopy(record);wrong['files'][-1][key]=value
            self.assertEqual(post(wrong).status_code,400,key)
        wrong=copy.deepcopy(record);wrong['verification']=''
        self.assertEqual(post(wrong).status_code,400)
        wrong=copy.deepcopy(record);wrong['omitted_files']=[wrong['files'].pop()]
        self.assertEqual(post(wrong).status_code,400)
        self.assertEqual(post(record).status_code,200)
        # The accepted snapshot cannot be renormalized by a later receipt.
        wrong=copy.deepcopy(record);wrong['files'][-1]['meta_sha256']='c'*64
        self.assertEqual(post(wrong).status_code,400)

    def test_artifact_corruption_blocks_before_job_creation(self):
        artifact=self.repo/self.item['directory']/'artifact.unitypackage'
        value=bytearray(artifact.read_bytes()); value[-1]^=1;artifact.write_bytes(value)
        self.assertEqual(self.plugin().status_code,400)
        self.assertFalse(InstallJob.objects.exists())

    def test_archive_execution_receipt_ignores_imported_meta_but_cannot_expand_deletion_scope(self):
        import copy
        self.plugin();work=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=10)).json()['work']
        target=InstallJob.objects.get().target
        files=recipe_for(target)['files']
        for f in files:f.update(existed=True,meta_existed=False,before_sha256='c'*64,before_meta_sha256='',meta_sha256='d'*64)
        record=dict(plugin_id='sample',build_id=self.item['build_id'],attempt_id=work['attempt_id'],
            verification='archive-execution-v1',import_result='completed',files=files,omitted_files=[])
        def post(value):return self.native('plugins/receipt',dict(self.identity,attempt_id=work['attempt_id'],record_json=json.dumps(value)))
        wrong=copy.deepcopy(record);wrong['files'].append(dict(files[-1],path='Assets/Generated.dll'))
        self.assertEqual(post(wrong).status_code,400)
        wrong=copy.deepcopy(record);wrong['files'][-1]['path']='Assets/Other.txt'
        self.assertEqual(post(wrong).status_code,400)
        self.assertEqual(post(record).status_code,200)
        self.assertEqual(post(record).status_code,200)

    def test_download_is_bound_to_registered_project(self):
        self.plugin();work=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=3)).json()['work']
        identity={'instance_id':'other','session_id':'other-session'}
        self.native('register',dict(identity,project_name='Other',project_path='/other',machine_name='test',unity_version='2022.3',process_id=2))
        self.assertEqual(self.native('plugins/download',dict(identity,attempt_id=work['attempt_id'])).status_code,404)
        response=self.native('plugins/download',dict(self.identity,attempt_id=work['attempt_id']))
        self.assertEqual(response.status_code,200)
        response.close()

    def test_archive_packages_folder_is_data_under_chosen_assets_root(self):
        file=self.root/'outside.zip'
        with zipfile.ZipFile(file,'w') as z:z.writestr('Packages/manifest.json','{}')
        item=stage_artifact(self.repo,file,{'id':'outside','name':'Outside'})
        index=json.loads((self.repo/'index.json').read_text());index['plugins'].append(item);write_json(self.repo/'index.json',index)
        write_json(self.repo/'import-layouts.json',{'builds':{'outside@'+item['build_id']:{'mode':'assets','prefix':'','strip':''}}})
        target=prepare('outside',item['build_id'])
        self.assertEqual(placed_files(target,'Assets/Chosen')[-1]['path'],'Assets/Chosen/Packages/manifest.json')

    def start_zip(self):
        file=self.root/'zip.zip'
        with zipfile.ZipFile(file,'w') as z:z.writestr('Plugin/data.bytes','content')
        item=stage_artifact(self.repo,file,{'id':'zip','name':'ZIP'})
        index=json.loads((self.repo/'index.json').read_text());index['plugins'].append(item);write_json(self.repo/'index.json',index)
        # Preserve recovery coverage for jobs created by version 4 before this upgrade.
        from dashboard.models import UnityProject
        job=InstallJob.objects.create(project=UnityProject.objects.get(pk=self.identity['instance_id']),
            target=prepare('zip',item['build_id']),request_key=uuid.uuid4())
        InstallAttempt.objects.create(step=InstallStep.objects.create(job=job))
        work=self.zip_sync().json()['work']
        self.assertIsNone(self.native('upm/sync',dict(self.identity,protocol=1,executor_version=3)).json()['work'])
        self.assertEqual(self.zip_sync(self.report(work,'waiting_input')).status_code,200)
        return work

    def zip_sync(self,report=None):
        return self.native('upm/sync',dict(self.identity,protocol=1,executor_version=4,report=report))

    def test_destination_scoped_immutable_and_bounded(self):
        work=self.start_zip()
        body=dict(self.identity,attempt_id=work['attempt_id'],destination_root='Assets/Plugins')
        for path in ('','Packages','/Assets','Assets/../Packages','Assets2','Assets//Plugins','Assets/Plugins/'):
            self.assertEqual(self.native('plugins/destination',dict(body,destination_root=path)).status_code,400,path)
        self.assertEqual(self.native('plugins/destination',dict(body,session_id='wrong')).status_code,400)
        self.assertEqual(self.native('plugins/destination',body).status_code,200)
        self.assertEqual(self.native('plugins/destination',body).status_code,200)
        self.assertEqual(self.native('plugins/destination',dict(body,destination_root='Assets/Other')).status_code,400)
        self.assertEqual(self.zip_sync().json()['work']['destination_root'],'Assets/Plugins')
        # Receipts are checked against relocated paths, never merely archive-relative paths.
        job=InstallJob.objects.get(); files=placed_files(job.target,'Assets/Plugins')
        for f in files:f.update(before_sha256='',before_meta_sha256='',existed=False,meta_existed=False)
        raw=dict(plugin_id='zip',build_id=job.target['build_id'],attempt_id=work['attempt_id'],destination_root='Assets/Plugins',files=files)
        data=dict(self.identity,attempt_id=work['attempt_id'],record_json=json.dumps(raw))
        self.assertEqual(self.native('plugins/receipt',data).status_code,200)
        files[0]['path']='Assets/Other/Plugin'
        self.assertEqual(self.native('plugins/receipt',dict(data,record_json=json.dumps(raw))).status_code,400)

    def test_cancel_before_destination_releases_lock_but_cancel_after_start_is_rejected(self):
        work=self.start_zip(); report=self.report(work,'cancelled',2);report['failure_kind']='user_cancel'
        self.assertEqual(self.zip_sync(report).status_code,200)
        self.assertFalse(InstallJob.objects.get().active)
        self.assertEqual(self.zip_sync(report).status_code,200)
        self.assertEqual(self.native('plugins/destination',dict(self.identity,attempt_id=work['attempt_id'],destination_root='Assets')).status_code,400)

    def test_waiting_input_never_times_out_and_running_requires_destination(self):
        from datetime import timedelta
        from django.utils import timezone
        from django.core.cache import cache
        from dashboard.models import InstallStep
        work=self.start_zip()
        InstallStep.objects.update(updated_at=timezone.now()-timedelta(days=1));cache.clear()
        self.assertEqual(self.client.get(self.url).json()['jobs'][0]['display_state'],'waiting_input')
        self.assertEqual(self.client.post(self.url+work['job_id']+'/recheck/',{},content_type='application/json').status_code,409)
        self.assertEqual(self.zip_sync(self.report(work,'running',2)).status_code,400)
        self.native('plugins/destination',dict(self.identity,attempt_id=work['attempt_id'],destination_root='Assets'))
        self.assertEqual(self.zip_sync(self.report(work,'running',2)).status_code,200)
        cancelled=self.report(work,'cancelled',3);cancelled['failure_kind']='user_cancel'
        self.assertEqual(self.zip_sync(cancelled).status_code,400)
        self.assertEqual(self.zip_sync(self.report(work,'waiting_input',3)).status_code,400)

    def test_web_destination_is_required_fixed_and_preserved_on_retry(self):
        old=self.start_zip()
        cancelled=self.report(old,'cancelled',2); cancelled['failure_kind']='user_cancel'
        self.zip_sync(cancelled)
        target=InstallJob.objects.get().target
        body=dict(operation='plugin',plugin_id='zip',build_id=target['build_id'],request_key=str(uuid.uuid4()))
        for path in (None,'','Packages','Assets/../Other','Assets//A','Assets/A/'):
            response=self.client.post(self.url,dict(body,destination_root=path),content_type='application/json')
            self.assertEqual(response.status_code,400,path)
        body['destination_root']='Assets/Plugins'
        self.assertEqual(self.client.post(self.url,body,content_type='application/json').status_code,409)
        self.native('upm/sync',dict(self.identity,protocol=1,executor_version=5))
        response=self.client.post(self.url,body,content_type='application/json')
        self.assertEqual(response.status_code,202,response.content)
        self.assertEqual(response.json()['job']['destination_root'],'Assets/Plugins')
        self.assertEqual(self.client.post(self.url,body,content_type='application/json').status_code,200)
        self.assertEqual(self.client.post(self.url,dict(body,destination_root='Assets'),content_type='application/json').status_code,409)
        self.assertIsNone(self.zip_sync().json()['work']) # v4 must not open its native picker.
        work=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=5)).json()['work']
        self.assertEqual(work['destination_root'],'Assets/Plugins')
        self.assertEqual(self.zip_sync(self.report(work,'waiting_input')).status_code,400)
        self.assertEqual(self.native('plugins/destination',dict(self.identity,attempt_id=work['attempt_id'],destination_root='Assets/Other')).status_code,400)
        report=self.report(work,'failed');report['failure_kind']='preflight'
        self.assertEqual(self.native('upm/sync',dict(self.identity,protocol=1,executor_version=5,report=report)).status_code,200)
        response=self.client.post(self.url+work['job_id']+'/retry/',dict(attempt_id=work['attempt_id'],request_key=str(uuid.uuid4()),reason='目录已修复'),content_type='application/json')
        self.assertEqual(response.status_code,202,response.content)
        self.assertEqual(response.json()['job']['destination_root'],'Assets/Plugins')
