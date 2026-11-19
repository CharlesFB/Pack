import copy
import hashlib
import json
import tempfile
import uuid
import zipfile
from datetime import timedelta
from pathlib import Path
from unittest.mock import patch
from django.core.cache import cache
from django.test import TestCase, SimpleTestCase, override_settings
from django.utils import timezone
from dashboard.installations import target_from
from dashboard.installation_plans import digest, assemble
from dashboard.plan_resolver import resolve, version, Source
from dashboard.models import InstallJob, InstallStep, InstallationPlan, ProjectPackageState, UnityProject
from dashboard.plugin_repository import stage_artifact, write_json
from dashboard.tests import test_installations as base_tests


def node(name, ver='1.0.0', deps=None, git=None, commit=None):
    name='com.test.'+name if '.' not in name else name
    raw=dict(name=name,version=ver,dependencies=deps or {},dependenciesGit=git or {})
    target=target_from(dict(name=name,identifier=('https://git.example.org/'+name+'.git#'+commit) if commit else name+'@'+ver))
    target['version']=ver
    return dict(target=target,raw=raw,sha=hashlib.sha256(json.dumps(raw).encode()).hexdigest())


class FakeSource:
    def __init__(self, root, nodes): self.initial=root;self.nodes=nodes
    def check(self): pass
    def close(self): pass
    def root(self, data): return copy.deepcopy(self.initial)
    def git(self, name, address):
        return copy.deepcopy(next(n for n in self.nodes if n['target']['name']==name and n['target']['identifier']==address))
    def registry(self, name, minimum, installed=None, exact=False):
        available=[n for n in self.nodes if n['target']['name']==name and (n['raw']['version']==minimum if exact else version(n['raw']['version'])>=version(minimum))]
        if installed:
            keep=next((n for n in available if n['raw']['version']==installed['version']),None)
            if keep:return copy.deepcopy(keep)
        return copy.deepcopy(min(available,key=lambda n:version(n['raw']['version'])))


def baseline(packages=None):
    return dict(fingerprint='a'*64,captured_at=timezone.now().isoformat(),error='',packages=packages or [],builtins=[],registries=[])


def installed(n, direct=True):
    return dict(n['target'],direct=direct,dependencies=[dict(name=k,version=v) for k,v in (n['raw']['dependencies']|n['raw']['dependenciesGit']).items()])


def mappings(nodes):
    return {'package_mappings':[dict(package_name=n['target']['name'],commit=n['target']['commit'],package_json_sha256=n['sha'],plugins=[]) for n in nodes if n['target']['source']=='git']}


class ResolverTests(SimpleTestCase):
    def run_graph(self, root, nodes, state=None, index=None, plugins=None):
        return resolve(FakeSource(root,nodes),{},state or baseline(),'2022.3.62',index or mappings(nodes),plugins or [])
    def test_diamond_uses_highest_minimum_and_orders_shared_dependency_once(self):
        root=node('root',deps={'com.test.left':'1.0.0','com.test.right':'1.0.0'})
        nodes=[root,node('left',deps={'com.test.shared':'1.0.0'}),node('right',deps={'com.test.shared':'2.0.0'}),node('shared'),node('shared','2.0.0')]
        rows=self.run_graph(root,nodes)['packages']
        self.assertEqual([r['name'] for r in rows],['com.test.shared','com.test.left','com.test.right','com.test.root'])
        self.assertEqual(rows[0]['version'],'2.0.0')
    def test_upgraded_dependency_drops_obsolete_edges(self):
        root=node('root',deps={'com.test.left':'1.0.0','com.test.right':'1.0.0'})
        nodes=[root,node('left',deps={'com.test.shared':'1.0.0'}),node('right',deps={'com.test.left':'2.0.0'}),node('left','2.0.0'),node('shared')]
        self.assertNotIn('com.test.shared',[r['name'] for r in self.run_graph(root,nodes)['packages']])
    def test_obsolete_git_pin_does_not_conflict_with_upgraded_graph(self):
        a=node('shared',commit='a'*40);b=node('shared',commit='b'*40)
        root=node('root',deps={'com.test.left':'1.0.0','com.test.right':'1.0.0'})
        left=node('left',git={'com.test.shared':a['target']['identifier']})
        right=node('right',deps={'com.test.left':'2.0.0'},git={'com.test.shared':b['target']['identifier']})
        rows=self.run_graph(root,[root,left,node('left','2.0.0'),right,a,b])['packages']
        self.assertEqual(next(p for p in rows if p['name']=='com.test.shared')['target']['commit'],'b'*40)
    def test_cycles_and_fixed_git_conflicts_block(self):
        root=node('root',deps={'com.test.child':'1.0.0'});child=node('child',deps={'com.test.root':'1.0.0'})
        with self.assertRaisesRegex(ValueError,'循环'):self.run_graph(root,[root,child])
        a=node('shared',commit='a'*40);b=node('shared',commit='b'*40)
        left=node('left',git={'com.test.shared':a['target']['identifier']});right=node('right',git={'com.test.shared':b['target']['identifier']})
        root=node('root',deps={'com.test.left':'1.0.0','com.test.right':'1.0.0'})
        with self.assertRaisesRegex(ValueError,'冲突'):self.run_graph(root,[root,left,right,a,b])
    def test_existing_other_root_constrains_shared_version_but_pruned_old_indirect_does_not(self):
        root=node('root',deps={'com.test.shared':'1.0.0'});one=node('shared');two=node('shared','2.0.0')
        other=node('other',deps={'com.test.shared':'2.0.0'})
        rows=self.run_graph(root,[root,one,two],baseline([installed(other)]))['packages']
        self.assertEqual(rows[0]['version'],'2.0.0')
        value=self.run_graph(root,[root,one,two],baseline([installed(other,False)]))
        self.assertEqual(value['packages'][0]['version'],'1.0.0');self.assertEqual(value['protected_packages'],[])
    def test_exact_git_mapping_and_metadata_hash_required(self):
        root=node('root',commit='a'*40)
        self.assertFalse(self.run_graph(root,[root])['errors'])
        self.assertIn('缺少插件映射',self.run_graph(root,[root],index={'package_mappings':[]})['errors'][0])
        wrong=mappings([root]);wrong['package_mappings'][0]['package_json_sha256']='b'*64
        self.assertIn('指纹',self.run_graph(root,[root],index=wrong)['errors'][0])
    def test_unity_requirement_and_local_overwrite_block(self):
        root=node('root');root['raw']['unity']='6000.0'
        with self.assertRaisesRegex(ValueError,'Unity'):self.run_graph(root,[root])
        root['raw'].pop('unity');old=installed(root);old['source']='local'
        self.assertIn('本地/嵌入包',self.run_graph(root,[root],baseline([old]))['errors'][0])
    def test_git_commit_is_reused_but_direct_tag_is_pinned(self):
        root=node('root',commit='a'*40);old=installed(root)
        self.assertEqual(self.run_graph(root,[root],baseline([old]))['packages'][0]['change'],'unchanged')
        old['identifier']=old['identifier'].rsplit('#',1)[0]+'#release'
        self.assertEqual(self.run_graph(root,[root],baseline([old]))['packages'][0]['change'],'change')
    def test_unknown_scoped_registry_is_not_contacted(self):
        state=baseline();state['registries']=[dict(url='https://untrusted.example',scopes=['com.test'])]
        with patch('dashboard.plan_resolver.build_opener') as network:
            with self.assertRaisesRegex(ValueError,'允许列表'):Source(state,'2022.3.62').registry('com.test.a','1.0.0',None)
            network.assert_not_called()

    def test_git_diamond_uses_higher_semver_and_its_plugin_mapping(self):
        low=node('shared','1.9.0',commit='a'*40);high=node('shared','1.10.0',commit='b'*40)
        high['requested_ref']='shared-1.10.0'
        left=node('left',git={'com.test.shared':low['target']['identifier']})
        right=node('right',git={'com.test.shared':high['target']['identifier']})
        root=node('root',deps={'com.test.left':'1.0.0','com.test.right':'1.0.0'})
        result=self.run_graph(root,[root,left,right,low,high],index=mappings([high]))
        self.assertFalse(result['errors'])
        self.assertEqual([p['name'] for p in result['packages']],['com.test.shared','com.test.left','com.test.right','com.test.root'])
        self.assertEqual(result['packages'][0]['target']['commit'],'b'*40)
        self.assertEqual(result['packages'][0]['requested_ref'],'shared-1.10.0')
        self.assertTrue(any('1.9.0 / 1.10.0 → 1.10.0' in s for s in result['warnings']))

    def test_git_upgrade_rebuilds_graph_and_drops_obsolete_edges_and_pins(self):
        a=node('leaf',commit='a'*40);b=node('leaf',commit='b'*40)
        old=node('left',commit='c'*40,git={'com.test.leaf':a['target']['identifier']})
        new=node('left','2.0.0',commit='d'*40)
        right=node('right',git={'com.test.left':new['target']['identifier'],'com.test.leaf':b['target']['identifier']})
        root=node('root',git={'com.test.left':old['target']['identifier']},deps={'com.test.right':'1.0.0'})
        rows=self.run_graph(root,[root,old,new,right,a,b])['packages']
        self.assertEqual(next(p for p in rows if p['name']=='com.test.leaf')['target']['commit'],'b'*40)
        self.assertEqual(next(p for p in rows if p['name']=='com.test.left')['dependencies'],[])
        self.assertLess([p['name'] for p in rows].index('com.test.left'),[p['name'] for p in rows].index('com.test.right'))

    def test_existing_other_root_git_constraint_upgrades_new_dependency(self):
        low=node('shared',commit='a'*40);high=node('shared','2.0.0',commit='b'*40)
        root=node('root',git={'com.test.shared':low['target']['identifier']})
        other=node('other',git={'com.test.shared':high['target']['identifier']})
        rows=self.run_graph(root,[root,low,high],baseline([installed(other)]))['packages']
        self.assertEqual(rows[0]['target'],high['target'])

    def test_newer_installed_git_dependency_is_kept_and_its_graph_is_expanded(self):
        low=node('shared',commit='a'*40);leaf=node('leaf')
        high=node('shared','2.0.0',commit='b'*40,deps={'com.test.leaf':'1.0.0'})
        root=node('root',git={'com.test.shared':low['target']['identifier']})
        rows=self.run_graph(root,[root,low,high,leaf],baseline([installed(high),installed(leaf)]))['packages']
        self.assertEqual([p['name'] for p in rows],['com.test.leaf','com.test.shared','com.test.root'])
        self.assertEqual(rows[1]['change'],'unchanged')
        self.assertEqual(rows[1]['target'],high['target'])

    def test_explicit_git_root_can_still_be_downgraded(self):
        root=node('root',commit='a'*40);old=node('root','2.0.0',commit='b'*40)
        row=self.run_graph(root,[root,old],baseline([installed(old)]))['packages'][0]
        self.assertEqual((row['version'],row['change']),('1.0.0','change'))

    def test_higher_version_does_not_merge_different_git_repositories(self):
        low=node('shared',commit='a'*40);high=node('shared','2.0.0',commit='b'*40)
        high['target']['identifier']=high['target']['identifier'].replace('/com.test.','/fork/com.test.')
        root=node('root',git={'com.test.shared':low['target']['identifier']})
        other=node('other',git={'com.test.shared':high['target']['identifier']})
        with self.assertRaisesRegex(ValueError,'不同仓库'):
            self.run_graph(root,[root,low,high],baseline([installed(other)]))


@override_settings(LOCAL_MODE=True)
class PlanQueueTests(TestCase):
    native=base_tests.InstallationTests.native
    def setUp(self):
        cache.clear();self.identity=dict(instance_id='plan-project',session_id='plan-session')
        self.native('register',dict(self.identity,project_name='Plan',project_path='/fixture',machine_name='fixture',unity_version='2022.3.62',process_id=1))
        self.project=UnityProject.objects.get();self.base='/api/projects/plan-project/installation-plans/';self.jobs='/api/projects/plan-project/installations/'
        self.root=node('root',deps={'com.test.dep':'1.0.0'});self.nodes=[self.root,node('dep')]
        self.snapshot=baseline();self.sync(snapshot=self.snapshot)
        source=patch('dashboard.installation_plans.Source',side_effect=lambda *a:FakeSource(self.root,self.nodes));source.start();self.addCleanup(source.stop)
        repo=patch('dashboard.installation_plans.snapshot',return_value=(mappings(self.nodes),[]));repo.start();self.addCleanup(repo.stop)
    def sync(self, report=None, snapshot=None, **extra):
        return self.native('upm/sync',dict(self.identity,protocol=1,executor_version=extra.pop('executor_version',7),report=report,has_package_state=snapshot is not None,package_state=snapshot,**extra))
    def preview(self):
        response=self.client.post(self.base,dict(root={'name':'com.test.root','identifier':'com.test.root@1.0.0'},request_key=str(uuid.uuid4())),content_type='application/json')
        self.assertEqual(response.status_code,201,response.content);return response.json()['plan']
    def confirm(self, preview=None):
        p=preview or self.preview();body=dict(digest=p['digest'],plugins=[],selection_digest=digest({}))
        response=self.client.post(self.base+p['id']+'/confirm/',body,content_type='application/json')
        self.assertEqual(response.status_code,202,response.content);return InstallJob.objects.get(pk=response.json()['job']['id'])
    def report(self, work, state='succeeded', sequence=1):
        t=work['target'];e=dict(t,direct=True,manifest_match=True,lock_match=True,project_fingerprint='b'*64)
        if t.get('operation')=='plan_verify':e.update(verified=True,plan_digest=t['plan_digest'])
        return dict(attempt_id=work['attempt_id'],sequence=sequence,state=state,evidence=e)
    def control(self,job,action):return self.client.post(self.jobs+str(job.pk)+'/control/',dict(action=action),content_type='application/json')
    def test_confirm_idempotence_and_state_change_rejected(self):
        p=self.preview();self.snapshot['fingerprint']='c'*64;self.snapshot['captured_at']=timezone.now().isoformat();self.sync(snapshot=self.snapshot)
        response=self.client.post(self.base+p['id']+'/confirm/',dict(digest=p['digest'],plugins=[],selection_digest=digest({})),content_type='application/json')
        self.assertEqual(response.status_code,400);self.assertFalse(InstallJob.objects.exists())
        p=self.preview();job=self.confirm(p)
        r=self.client.post(self.base+p['id']+'/confirm/',dict(digest=p['digest'],plugins=[],selection_digest=digest({})),content_type='application/json')
        self.assertEqual(r.status_code,200);self.assertEqual(r.json()['job']['id'],str(job.pk))
    def test_git_version_conflict_returns_preview_and_queues_selected_high_commit(self):
        low=node('shared',commit='a'*40);high=node('shared','2.0.0',commit='b'*40)
        left=node('left',git={'com.test.shared':low['target']['identifier']})
        right=node('right',git={'com.test.shared':high['target']['identifier']})
        self.root=node('root',deps={'com.test.left':'1.0.0','com.test.right':'1.0.0'})
        self.nodes=[self.root,left,right,low,high]
        with patch('dashboard.installation_plans.snapshot',return_value=(mappings(self.nodes),[])):
            preview=self.preview()
            self.assertEqual(preview['state'],'ready');self.assertEqual(preview['errors'],[])
            self.assertEqual(preview['packages'][0]['version'],'2.0.0')
            job=self.confirm(preview)
        self.assertEqual(job.steps.first().target['commit'],'b'*40)
        self.assertEqual(job.steps.last().target['packages'][0]['commit'],'b'*40)
    def test_success_ack_barrier_and_final_verification(self):
        job=self.confirm();self.assertEqual(job.steps.count(),3)
        for i in range(3):
            work=self.sync().json()['work'];self.assertFalse(work['recover_only'])
            self.assertEqual(work['expected_fingerprint'],('a' if i==0 else 'b')*64)
            report=self.report(work)
            if i==2:
                bad=copy.deepcopy(report);bad['evidence'].pop('verified');self.assertEqual(self.sync(bad).status_code,400)
            response=self.sync(report);self.assertEqual(response.status_code,200,response.content);self.assertIsNone(response.json()['work'])
            self.assertIsNone(self.sync(report).json()['work']) # Lost acknowledgement cannot dispatch another step.
            if i<2:self.assertIsNone(job.steps.get(position=i+1).attempt.dispatched_at)
        job.refresh_from_db();self.assertFalse(job.active);self.assertEqual(job.steps.filter(state='succeeded').count(),3)
    def test_pause_resume_cancel_do_not_interrupt_dispatched_work(self):
        job=self.confirm();first=self.sync().json()['work'];self.assertEqual(self.control(job,'pause').status_code,200)
        self.assertTrue(self.sync().json()['work']['recover_only'])
        self.sync(self.report(first));self.assertIsNone(self.sync().json()['work'])
        self.assertEqual(self.client.get(self.jobs).json()['jobs'][0]['display_state'],'paused')
        self.control(job,'resume');second=self.sync().json()['work'];self.control(job,'cancel')
        self.assertEqual(self.sync().json()['work']['attempt_id'],second['attempt_id'])
        self.sync(self.report(second));job.refresh_from_db();self.assertFalse(job.active)
        self.assertEqual(job.steps.get(position=2).state,'skipped')
    def test_package_failure_ends_plan_and_skips_undispatched_steps(self):
        job=self.confirm();work=self.sync().json()['work'];report=self.report(work,'failed');report['failure_kind']='preflight'
        self.assertEqual(self.sync(report).status_code,200)
        job.refresh_from_db();self.assertFalse(job.active);self.assertIsNone(self.sync().json()['work'])
        self.assertEqual(job.step.state,'failed')
        self.assertTrue(all(s.state=='skipped' and s.attempt.dispatched_at is None for s in job.steps.filter(position__gt=0)))
        response=self.client.get(self.jobs).json()['jobs'][0]
        self.assertEqual(response['display_state'],'failed');self.assertFalse(response['can_retry'])
        retry=self.client.post(self.jobs+str(job.pk)+'/retry/',dict(attempt_id=work['attempt_id'],request_key=str(uuid.uuid4()),reason='retry'),content_type='application/json')
        self.assertEqual(retry.status_code,409)
        self.assertIsNone(self.sync(report).json()['work'])

    def test_failed_plugin_is_skipped_after_ack_and_removed_from_final_goals(self):
        job=self.confirm();step=job.steps.first()
        plugin=dict(operation='plugin',name='Plugin',plugin_id='fixture',build_id='d'*64,identifier='fixture',source='plugin')
        step.target=plugin;step.save()
        final=job.steps.last();final.target['plugins']=[dict(plugin_id='fixture',build_id='d'*64,destination_root='')];final.save()
        work=self.sync(executor_version=10).json()['work']
        bad=self.report(work,'failed');bad.update(failure_kind='plugin_import')
        self.assertEqual(self.sync(bad,executor_version=10).status_code,400)
        bad['evidence']['operation_finished']=True
        response=self.sync(bad,executor_version=10);self.assertEqual(response.status_code,200,response.content);self.assertIsNone(response.json()['work'])
        step.refresh_from_db();job.refresh_from_db();final.refresh_from_db()
        self.assertEqual(step.state,'skipped');self.assertTrue(job.active);self.assertEqual(final.target['plugins'],[])
        self.assertEqual(job.expected_fingerprint,'b'*64)
        self.assertIsNone(self.sync(bad,executor_version=10).json()['work'])
        next_work=self.sync(executor_version=10).json()['work']
        self.assertNotEqual(next_work['attempt_id'],work['attempt_id'])
        self.assertEqual(next_work['expected_fingerprint'],'b'*64)

    def test_old_executor_never_receives_plan_and_timeout_never_releases_lock(self):
        job=self.confirm()
        r=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=6));self.assertIsNone(r.json()['work'])
        self.assertIsNone(job.step.attempt.dispatched_at)
        self.sync();InstallStep.objects.update(updated_at=timezone.now()-timedelta(hours=1))
        self.assertEqual(self.client.get(self.jobs).json()['jobs'][0]['display_state'],'needs_review')
        cache.clear();self.assertEqual(self.client.get(self.jobs).json()['jobs'][0]['display_state'],'waiting_connection')
        job.refresh_from_db();self.assertTrue(job.active)
    def test_stale_snapshot_and_stale_session_cannot_create_plan(self):
        old=baseline();old['captured_at']=(timezone.now()-timedelta(minutes=5)).isoformat();self.sync(snapshot=old)
        self.assertEqual(ProjectPackageState.objects.get().payload,self.snapshot) # delayed snapshot ignored
        ProjectPackageState.objects.update(payload=old)
        r=self.client.post(self.base,dict(root={'name':'com.test.root'},request_key=str(uuid.uuid4())),content_type='application/json');self.assertEqual(r.status_code,400)
    def test_success_without_fingerprint_is_rejected_atomically(self):
        job=self.confirm();work=self.sync().json()['work'];report=self.report(work);report['evidence'].pop('project_fingerprint')
        self.assertEqual(self.sync(report).status_code,400);job.refresh_from_db();self.assertEqual(job.step.state,'queued')
    def test_plugin_steps_precede_package_dedup_and_conflicts_are_permissive(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory);repo=path/'repository';repo.mkdir();items=[]
            for i in range(2):
                artifact=path/('v'+str(i)+'.zip')
                with zipfile.ZipFile(artifact,'w') as z:z.writestr('Plugin/data.txt',str(i))
                items.append(stage_artifact(repo,artifact,{'id':'zip','name':'ZIP'}))
            write_json(repo/'index.json',dict(schema_version=1,plugins=items,package_mappings=[]))
            plan=InstallationPlan.objects.get(pk=self.preview()['id']);choices={}
            for position,pkg in enumerate(plan.payload['packages']):
                item=items[position];pkg['plugins']=[dict(key=str(position),name='ZIP',version='',format='zip',available=True,plugin_id='zip',build_id=item['build_id'])]
                choices[str(position)]=dict(selected=True,destination_root='Assets/Chosen')
            with override_settings(PLUGIN_REPOSITORY_ROOT=repo,PLUGIN_DELIVERY_ROOT=path/'delivery'):
                steps,conflicts,_=assemble(plan,choices)
                self.assertEqual([t.get('operation','upm') for t,s in steps],['plugin','upm','plugin','upm','plan_verify']);self.assertTrue(conflicts)
                plan.payload['packages'][1]['plugins'][0]['build_id']=items[0]['build_id']
                steps,conflicts,_=assemble(plan,choices);self.assertEqual(steps[2][1],'skipped')
                choices['0']['selected']=False
                steps,_,_=assemble(plan,choices);self.assertEqual(steps[0][1],'skipped');self.assertEqual(steps[2][1],'queued')

    def test_independent_refresh_never_enqueues_mtp_work(self):
        with patch('dashboard.views.bridge.enqueue') as enqueue:
            response=self.client.post('/api/projects/plan-project/mtp/refresh/')
            self.assertEqual(response.status_code,200);self.assertIsNone(response.json()['task_id']);enqueue.assert_not_called()
        before=self.client.get('/api/projects/').json()['projects'][0]['snapshot_digest']
        self.snapshot['fingerprint']='f'*64;self.snapshot['captured_at']=timezone.now().isoformat();self.sync(snapshot=self.snapshot)
        after=self.client.get('/api/projects/').json()['projects'][0]['snapshot_digest']
        self.assertNotEqual(before,after)
    def test_selection_must_cover_all_offers_and_zip_roots_are_immutable(self):
        from dashboard.installation_plans import choices
        plan=InstallationPlan.objects.get(pk=self.preview()['id'])
        plan.payload['packages'][0]['plugins']=[dict(key='zip',format='zip',name='ZIP',available=True)]
        data=dict(digest=plan.payload['digest'],plugins=[])
        with self.assertRaisesRegex(ValueError,'每个插件'):choices(plan,data)
        data['plugins']=[dict(key='zip',selected=True,destination_root='Packages')]
        with self.assertRaises(ValueError):choices(plan,data)
        data['plugins'][0]['destination_root']='Assets/Chosen'
        self.assertEqual(choices(plan,data)['zip']['destination_root'],'Assets/Chosen')
