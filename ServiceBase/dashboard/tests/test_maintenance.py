import copy, hashlib, io, json, tarfile, uuid
from unittest.mock import patch
from django.test import TestCase, override_settings
from django.utils import timezone
from dashboard.models import InstallJob, InstallStep, InstallationPlan, UnityProject, PluginInventory, PluginInstallation
from dashboard.package_maintenance import managed_dependencies, uninstall_preview, prepare_cleanup, parent_first
from dashboard.installation_plans import assemble, digest
from dashboard.plugin_updates import replacement, file_diff, expected_record_files, check_package_references
from dashboard.plugin_repository import stage_artifact, write_json
from dashboard.tests.test_installation_plans import node, installed, baseline
from dashboard.tests import test_installation_plans as plan_tests
from dashboard.tests import test_plugin_management as plugin_tests


@override_settings(LOCAL_MODE=True)
class PackageMaintenanceTests(TestCase):
    setUp=plan_tests.PlanQueueTests.setUp
    native=plan_tests.PlanQueueTests.native
    report=plan_tests.PlanQueueTests.report
    def sync(self,report=None,snapshot=None,**extra):
        return self.native('upm/sync',dict(self.identity,protocol=1,executor_version=8,report=report,has_package_state=snapshot is not None,package_state=snapshot,**extra))
    def history(self,root,nodes,original=(),before=None):
        target=dict(operation='plan',name=root['target']['name'],original_direct=list(original))
        if before is not None:target['baseline_packages']=before
        job=InstallJob.objects.create(project=self.project,request_key=uuid.uuid4(),target=target,active=False)
        for i,n in enumerate(nodes):InstallStep.objects.create(job=job,position=i,target=n['target'],state='succeeded')
        return job
    def state(self,*nodes):return baseline([installed(n) for n in nodes])
    def test_service_owned_dependencies_only_and_parent_first(self):
        leaf=node('leaf');mid=node('mid',deps={'com.test.leaf':'1.0.0'});root=node('root',deps={'com.test.mid':'1.0.0'})
        self.history(root,[leaf,mid,root]);state=self.state(root,mid,leaf)
        self.assertEqual(managed_dependencies(self.project,state),{'com.test.mid','com.test.leaf'})
        plan=uninstall_preview(self.project,state,{'name':'com.test.root'})
        self.assertEqual([r['name'] for r in plan['removals']],['com.test.root','com.test.mid','com.test.leaf'])
        self.assertEqual(plan['expected_direct'],[])
    def test_user_direct_and_shared_dependency_are_retained(self):
        dep=node('dep');root=node('root',deps={'com.test.dep':'1.0.0'});other=node('other',deps={'com.test.dep':'1.0.0'})
        self.history(root,[dep,root]);state=self.state(root,dep,other)
        self.assertEqual([r['name'] for r in uninstall_preview(self.project,state,{'name':'com.test.root'})['removals']],['com.test.root'])
        InstallJob.objects.all().delete();self.history(root,[dep,root],original=['com.test.dep'])
        self.assertEqual(managed_dependencies(self.project,self.state(root,dep)),set())
    def test_explicit_promotion_and_external_repin_revoke_cleanup_ownership(self):
        dep=node('dep');root=node('root',deps={'com.test.dep':'1.0.0'});self.history(root,[dep,root])
        self.history(dep,[dep]);self.assertFalse(managed_dependencies(self.project,self.state(root,dep)))
        InstallJob.objects.all().delete();self.history(root,[dep,root])
        two=node('dep','2.0.0');self.assertFalse(managed_dependencies(self.project,self.state(root,two)))
        self.history(root,[dep],before=[installed(root),installed(two)])
        self.assertFalse(managed_dependencies(self.project,self.state(root,dep)))
    def test_referenced_root_local_and_indirect_uninstall_rejected(self):
        root=node('root');other=node('other',deps={'com.test.root':'1.0.0'})
        with self.assertRaisesRegex(ValueError,'仍被其他包引用'):uninstall_preview(self.project,self.state(root,other),{'name':'com.test.root'})
        for changes in ({'direct':False},{'source':'local'},{'source':'embedded'}):
            state=self.state(root);state['packages'][0].update(changes)
            with self.assertRaises(ValueError):uninstall_preview(self.project,state,{'name':'com.test.root'})
    def test_order_includes_paths_through_indirect_nodes(self):
        self.assertEqual(parent_first({'root','leaf'},{'root':['middle'],'middle':['leaf'],'leaf':[]}),['root','leaf'])
    def test_update_installs_new_graph_before_removing_stale_owned_dependencies(self):
        old=node('dep');root=node('root',deps={'com.test.dep':'1.0.0'});new=node('new');nextroot=node('root','2.0.0',deps={'com.test.new':'1.0.0'})
        self.history(root,[old,root]);state=self.state(root,old)
        payload=dict(root_name='com.test.root',packages=[dict(name=n['target']['name'],target=n['target'],direct=True,plugins=[],change='change',dependencies=list(n['raw']['dependencies'])) for n in (new,nextroot)],original_direct=['com.test.root','com.test.dep'],warnings=[],errors=[],digest='a'*64)
        prepare_cleanup(self.project,state,payload)
        plan=InstallationPlan(project=self.project,payload=payload)
        steps,_,_=assemble(plan,{})
        self.assertEqual([t.get('operation','upm') for t,s in steps],['upm','upm','upm_remove','plan_verify'])
        self.assertIn('com.test.dep',steps[-1][0]['absent_packages'])
        self.assertEqual(steps[-1][0]['expected_direct'],['com.test.new','com.test.root'])
    def uninstall_job(self):
        self.sync(snapshot=self.state(node('root')))
        response=self.client.post(self.base,dict(root={'operation':'uninstall','name':'com.test.root'},request_key=str(uuid.uuid4())),content_type='application/json')
        self.assertEqual(response.status_code,201,response.content);p=response.json()['plan']
        body=dict(digest=p['digest'],plugins=[],selection_digest=digest({}))
        response=self.client.post(self.base+p['id']+'/confirm/',body,content_type='application/json')
        self.assertEqual(response.status_code,202,response.content)
        return InstallJob.objects.get(pk=response.json()['job']['id'])
    def test_uninstall_requires_v8_full_proof_and_ack_barrier(self):
        job=self.uninstall_job()
        self.assertIsNone(self.native('upm/sync',dict(self.identity,protocol=1,executor_version=7)).json()['work'])
        work=self.sync().json()['work'];self.assertEqual(work['target']['operation'],'upm_remove')
        report=self.report(work);self.assertEqual(self.sync(report).status_code,400)
        report['evidence']['removed']=True
        self.assertIsNone(self.sync(report).json()['work']);self.assertIsNone(self.sync(report).json()['work'])
        self.assertEqual(self.sync().json()['work']['target']['operation'],'plan_verify')
    def test_unknown_uninstall_result_never_retries_and_known_failure_can_retry(self):
        job=self.uninstall_job();work=self.sync().json()['work'];r=self.report(work,'needs_review')
        self.sync(r);self.assertFalse(self.client.get(self.jobs).json()['jobs'][0]['can_retry'])
        r=self.report(work,'failed',sequence=2);r['failure_kind']='remove_completed';self.assertEqual(self.sync(r).status_code,200)
        self.assertTrue(self.client.get(self.jobs).json()['jobs'][0]['can_retry'])


@override_settings(LOCAL_MODE=True)
class PluginUpdateTests(TestCase):
    setUp=plugin_tests.PluginManagementTests.setUp
    native=plugin_tests.PluginManagementTests.native
    sync=plugin_tests.PluginManagementTests.sync
    submit=plugin_tests.PluginManagementTests.submit
    report=plugin_tests.PluginManagementTests.report
    plugin=plugin_tests.PluginManagementTests.plugin
    v6=plugin_tests.PluginManagementTests.v6
    imported=plugin_tests.PluginManagementTests.imported
    remove=plugin_tests.PluginManagementTests.remove
    def setup_update(self,preexisting=False):
        self.old,self.inventory,self.files=self.imported(preexisting)
        self.project=UnityProject.objects.get(pk=self.identity['instance_id'])
        self.native('upm/sync',dict(self.identity,protocol=1,executor_version=8,has_package_state=True,package_state=baseline()))
        source=self.root/'new.unitypackage'
        with tarfile.open(self.artifact) as old,tarfile.open(source,'w:gz') as new:
            for member in old.getmembers():
                data=old.extractfile(member).read() if member.isfile() else None
                if member.name.endswith('/asset'):data=b'new version'
                if data is not None:member.size=len(data)
                new.addfile(member,io.BytesIO(data) if data is not None else None)
        self.new=stage_artifact(self.repo,source,{'id':'sample','name':'Sample'})
        write_json(self.repo/'index.json',{'schema_version':1,'plugins':[self.item,self.new],'package_mappings':[]})
        return replacement(self.project,'sample',self.new['build_id'],self.old['record_key'],self.old['record_digest'])
    def test_update_diff_preserves_ownership_and_blocks_preexisting_changes(self):
        target=self.setup_update(preexisting=True)
        self.assertEqual(file_diff(self.project,target)['changed'],['Assets/Test/file.txt'])
        self.assertEqual(len(file_diff(self.project,target)['blocked']),1)
    def test_other_record_shared_file_blocks_replacement(self):
        target=self.setup_update();self.inventory['installations'].append(dict(self.old,record_key='shared-record'))
        PluginInventory.objects.update(payload=self.inventory)
        self.assertTrue(file_diff(self.project,target)['blocked'])
    def test_v8_delete_binds_package_baseline(self):
        self.setup_update()
        response=self.remove(self.old);self.assertEqual(response.status_code,202,response.content)
        self.assertEqual(InstallJob.objects.get(pk=response.json()['job']['id']).expected_fingerprint,'a'*64)
    def test_same_build_and_stale_record_rejected(self):
        self.setup_update()
        with self.assertRaisesRegex(ValueError,'另一构建'):replacement(self.project,'sample',self.old['build_id'],self.old['record_key'],self.old['record_digest'])
        with self.assertRaisesRegex(ValueError,'已变化'):replacement(self.project,'sample',self.new['build_id'],self.old['record_key'],'a'*64)
    def test_known_package_reference_prevents_standalone_delete_and_update(self):
        target=self.setup_update();package=node('root',commit='c'*40)
        self.native('upm/sync',dict(self.identity,protocol=1,executor_version=8,has_package_state=True,package_state=baseline([installed(package)])))
        mapping={'package_mappings':[dict(package_name=package['target']['name'],commit='c'*40,plugins=[dict(plugin_id='sample',build_id=self.item['build_id'])])]}
        with patch('dashboard.plugin_api.snapshot',return_value=(mapping,[])):
            with self.assertRaisesRegex(ValueError,'仍被工程包映射引用'):check_package_references(self.project,target)
            self.assertEqual(self.remove(self.old).status_code,400)
            check_package_references(self.project,target,dict(absent_packages=['com.test.root'],packages=[]))
    def test_update_requires_both_immutable_receipts_and_preserves_original_ownership(self):
        target=self.setup_update()
        base='/api/projects/'+self.project.pk+'/installation-plans/'
        body=dict(root=dict(operation='plugin_update',plugin_id='sample',build_id=self.new['build_id'],record_key=self.old['record_key'],record_digest=self.old['record_digest']),request_key=str(uuid.uuid4()))
        response=self.client.post(base,body,content_type='application/json');self.assertEqual(response.status_code,201,response.content);p=response.json()['plan']
        body=dict(digest=p['digest'],plugins=[],selection_digest=digest({}));response=self.client.post(base+p['id']+'/confirm/',body,content_type='application/json');self.assertEqual(response.status_code,202,response.content)
        def sync(report=None):return self.native('upm/sync',dict(self.identity,protocol=1,executor_version=8,report=report))
        work=sync().json()['work'];files,old=expected_record_files(self.project,target)
        for f in files:f.update({k:old[f['path']][k] for k in ('existed','meta_existed','before_sha256','before_meta_sha256')})
        raw=json.dumps(dict(plugin_id='sample',build_id=self.new['build_id'],attempt_id=work['attempt_id'],files=files));data=dict(self.identity,attempt_id=work['attempt_id'],record_json=raw)
        self.assertEqual(self.native('plugins/receipt',data).status_code,200)
        changed=copy.deepcopy(files);changed[-1]['existed']=True
        bad=json.loads(raw);bad['files']=changed
        self.assertEqual(self.native('plugins/receipt',dict(data,record_json=json.dumps(bad))).status_code,400)
        report=self.report(work);report['evidence']=dict(build_id=target['build_id'],recipe_id=target['recipe_id'],record_digest=hashlib.sha256(raw.encode()).hexdigest(),project_fingerprint='a'*64)
        self.assertEqual(sync(report).status_code,400)
        result=dict(plugin_id='sample',build_id=target['build_id'],previous_build_id=self.old['build_id'],record_key=self.old['record_key'],record_digest=self.old['record_digest'],attempt_id=work['attempt_id'],new_record_digest=report['evidence']['record_digest'],files=[dict(path=f['path'],outcome='reused' if f['folder'] else 'replaced',reason='checked') for f in self.files])
        raw=json.dumps(result);data['record_json']=raw
        self.assertEqual(self.native('plugins/update-receipt',dict(data,session_id='wrong')).status_code,400)
        self.assertEqual(self.native('plugins/update-receipt',data).status_code,200)
        self.assertEqual(self.native('plugins/update-receipt',data).status_code,200)
        self.assertEqual(self.native('plugins/update-receipt',dict(data,record_json=raw+' ')).status_code,400)
        report['evidence']['update_digest']=hashlib.sha256(raw.encode()).hexdigest()
        self.assertEqual(sync(report).status_code,200)
        self.assertEqual(list(PluginInstallation.objects.values_list('build_id',flat=True)),[target['build_id']])
        updates=self.client.get('/api/projects/'+self.project.pk+'/plugins/').json()['updates']
        self.assertEqual(updates[0]['previous_build_id'],self.old['build_id'])
        self.assertEqual(updates[0]['retained'],0)
