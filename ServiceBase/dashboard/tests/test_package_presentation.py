import copy
import uuid
from unittest.mock import patch

from django.test import TestCase, SimpleTestCase, override_settings
from django.utils import timezone

from dashboard.catalog import merge_snapshot, normalize
from dashboard.models import CatalogCache, InstallationPlan, InstallJob, InstallStep, ProjectPackageState, UnityProject
from dashboard.package_presentation import independent_snapshot, assign_depths
from dashboard.tests.test_installation_plans import node, installed, baseline, FakeSource, mappings
from dashboard.plan_resolver import resolve


@override_settings(LOCAL_MODE=True)
class PackagePresentationTests(TestCase):
    def setUp(self):
        self.project = UnityProject.objects.create(instance_id='display-project', session_id='session', transport='pull',
            project_name='Display', project_path='/fixture', machine_name='fixture', unity_version='6000.3.19',
            process_id=1, port=0, token='', last_seen=timezone.now())
        self.leaf = node('leaf', commit='a'*40)
        self.mid = node('mid', commit='b'*40, git={'com.test.leaf': self.leaf['target']['identifier']})
        self.root = node('root', commit='c'*40, git={'com.test.mid': self.mid['target']['identifier']})
        self.nodes = [self.leaf, self.mid, self.root]
        self.state = baseline([installed(n) for n in self.nodes])
        directory = {'Packages': [dict(PackageName=n['target']['name'].split('.')[-1], ProjectId=i,
            Tags=[n['target']['name'].split('.')[-1]+'-1.0.0']) for i,n in enumerate(self.nodes)]}
        info = {'PackageCache': {str(i): {n['target']['name'].split('.')[-1]+'-1.0.0':n['raw']} for i,n in enumerate(self.nodes)}}
        self.catalog = normalize(directory, info)
        CatalogCache.objects.create(key='test', packages=self.catalog, revision='catalog-commit')
        source = patch('dashboard.catalog.source', return_value=('test','https://git.example.org','secret','main'))
        source.start(); self.addCleanup(source.stop)
        self.mapping = {'package_mappings': [dict(package_name=n['target']['name'], commit=n['target']['commit'],
            remote_sources=[dict(project_id=i, tag=n['target']['name'].split('.')[-1]+'-1.0.0')]) for i,n in enumerate(self.nodes)]}
        read = patch('dashboard.plugin_api.read_file', side_effect=lambda path:copy.deepcopy(self.mapping))
        read.start(); self.addCleanup(read.stop)

    def history(self, root=None, nodes=None, original=(), verified=True, completed=None, refs=None):
        root = root or self.root
        nodes = self.nodes if nodes is None else nodes
        name = root['target']['name']
        job = InstallJob.objects.create(project=self.project, request_key=uuid.uuid4(), active=False,
            target=dict(operation='plan', name=name, original_direct=list(original)))
        plan = InstallationPlan.objects.create(project=self.project, job=job, request_key=job.request_key,
            session_id='session', state='ready', root=dict(tag=name.split('.')[-1]+'-1.0.0'),
            payload=dict(root_name=name, packages=[dict(name=n['target']['name'], target=n['target'],
                requested_ref=(refs or {}).get(n['target']['name'], '')) for n in nodes]))
        for i,n in enumerate(nodes):
            InstallStep.objects.create(job=job, position=i, target=n['target'],
                state='succeeded' if completed is None or n['target']['name'] in completed else 'failed')
        InstallStep.objects.create(job=job, position=len(nodes), target=dict(operation='plan_verify'), state='succeeded' if verified else 'skipped')
        return plan

    def display(self):
        return merge_snapshot(independent_snapshot(self.project, self.state))

    def test_existing_commit_installs_show_tags_and_real_depth_in_both_views(self):
        # An earlier cancelled plan installed the leaf; later plans reuse it.
        self.history(nodes=[self.leaf], verified=False)
        self.history(nodes=[self.mid,self.root], original=['com.test.leaf'])
        before = copy.deepcopy(self.state)
        ProjectPackageState.objects.create(project=self.project, session_id='session', payload=self.state)
        response = self.client.get('/api/projects/display-project/mtp/')
        self.assertEqual(response.status_code, 200, response.content)
        result = response.json()['response']
        for group in ('packages','project_packages'):
            rows = {p['name']:p for p in result[group]}
            for short,depth,commit in [('root',0,'c'),('mid',1,'b'),('leaf',2,'a')]:
                p = rows['com.test.'+short]
                self.assertEqual((p['tag'],p['depth'],p['revision']), (short+'-1.0.0',depth,commit*40))
            self.assertEqual(rows['com.test.root']['dependencies'][0]['name'],'com.test.mid')
            self.assertEqual(rows['com.test.mid']['dependencies'][0]['name'],'com.test.leaf')
        self.assertEqual(ProjectPackageState.objects.get().payload, before)
        self.assertEqual(self.state, before)

    def test_explicit_existing_root_stays_root_when_also_referenced(self):
        self.history(original=['com.test.leaf'])
        rows = {p['name']:p for p in self.display()['project_packages']}
        self.assertEqual(rows['com.test.leaf']['depth'],0)
        self.assertEqual(rows['com.test.mid']['depth'],1)

    def test_selecting_unchanged_dependency_as_root_promotes_it(self):
        self.history()
        self.history(root=self.mid,nodes=[],original=[p['name'] for p in self.state['packages']])
        rows = {p['name']:p for p in self.display()['project_packages']}
        self.assertEqual(rows['com.test.mid']['depth'],0)
        self.assertEqual(rows['com.test.leaf']['depth'],1)

    def test_new_dependency_refs_survive_without_plugin_index(self):
        self.mapping = {}
        self.history(refs={'com.test.leaf':'leaf-1.0.0','com.test.mid':'mid-1.0.0'})
        self.assertEqual({p['tag'] for p in self.display()['project_packages']}, {'leaf-1.0.0','mid-1.0.0','root-1.0.0'})

    def test_failed_steps_and_changed_commits_do_not_supply_old_tags(self):
        self.mapping = {}
        self.history(verified=False,completed=[])
        self.assertEqual({p['tag'] for p in self.display()['project_packages']},{c*40 for c in 'abc'})
        self.history()
        self.state['packages'][-1].update(commit='d'*40,identifier=self.root['target']['identifier'].rsplit('#',1)[0]+'#'+'d'*40)
        root = next(p for p in self.display()['project_packages'] if p['name']=='com.test.root')
        self.assertEqual(root['tag'],'d'*40)

    def test_ambiguous_release_mapping_is_not_guessed_by_version(self):
        self.catalog[0]['available_tags'].append('leaf-other')
        CatalogCache.objects.filter(key='test').update(packages=self.catalog)
        self.mapping['package_mappings'][0]['remote_sources'].append(dict(project_id=0, tag='leaf-other'))
        leaf = next(p for p in self.display()['project_packages'] if p['name']=='com.test.leaf')
        self.assertEqual(leaf['tag'],'a'*40)

    def test_repository_identity_prevents_same_name_catalog_stealing_match(self):
        other = dict(self.catalog[0], catalog_id='99:leaf')
        CatalogCache.objects.filter(key='test').update(packages=[other]+self.catalog)
        rows = {p['catalog_id']:p for p in self.display()['packages']}
        self.assertEqual(rows['99:leaf']['installation_status'],'not_installed')
        self.assertEqual(rows['0:leaf']['installation_status'],'installed')

    def test_partial_plan_dependency_keeps_dependency_role_without_missing_root(self):
        self.history(nodes=[self.leaf],verified=False)
        self.state['packages'] = self.state['packages'][:1]
        self.assertEqual(self.display()['project_packages'][0]['depth'],1)

    def test_deleting_history_preserves_dependency_roles_tags_and_cleanup_ownership(self):
        from dashboard.package_maintenance import managed_dependencies
        plan=self.history()
        before=self.display()['project_packages']
        owned=managed_dependencies(self.project,self.state)
        response=self.client.delete('/api/projects/display-project/installations/'+str(plan.job_id)+'/')
        self.assertEqual(response.status_code,200)
        self.assertEqual(self.display()['project_packages'],before)
        self.assertEqual(managed_dependencies(self.project,self.state),owned)
        self.assertEqual(owned,{'com.test.leaf','com.test.mid'})

    def test_only_mt_git_and_local_packages_are_displayed_without_changing_snapshot(self):
        unity=node('com.unity.ide.rider');builtin=node('com.unity.modules.audio');builtin['target']['source']='builtin'
        unrelated=node('com.vendor.tool',commit='e'*40)
        mt_missing=node('com.magictavern.orphan',commit='f'*40)
        mt_registry=node('com.magictavern.registry')
        self.state['packages'].extend(installed(n) for n in [unity,builtin,unrelated,mt_missing,mt_registry])
        self.state['packages'][-6]['dependencies'].extend(dict(name=n['target']['name'],version=n['raw']['version']) for n in [unity,builtin,mt_registry])
        before=copy.deepcopy(self.state)
        result=self.display()
        self.assertEqual({p['name'] for p in result['project_packages']},{n['target']['name'] for n in self.nodes}|{'com.magictavern.orphan'})
        for collection in ['packages','project_packages']:
            self.assertFalse(any(d['name'].startswith('com.unity.') or d['name']=='com.magictavern.registry' for p in result[collection] for d in p['dependencies']))
        self.assertEqual(self.state,before)

    def test_resolved_root_ref_takes_precedence_over_original_requested_tag(self):
        self.mapping={}
        self.history(refs={'com.test.root':'root-2.0.0'})
        root=next(p for p in self.display()['project_packages'] if p['name']=='com.test.root')
        self.assertEqual(root['tag'],'root-2.0.0')


class DisplayGraphTests(SimpleTestCase):
    def test_shared_dependencies_use_shortest_depth_and_cycles_terminate(self):
        rows = [dict(name=name,dependencies=[dict(name=dep) for dep in deps]) for name,deps in
                [('a',['b']),('b',['c']),('c',['b']),('other',['c']),('orphan',[])]]
        assign_depths(rows, {'a','other'})
        self.assertEqual({p['name']:p['depth'] for p in rows},dict(a=0,b=1,c=1,other=0,orphan=1))

    def test_resolver_retains_requested_refs_without_changing_pinned_targets(self):
        leaf=node('leaf',commit='a'*40); root=node('root',commit='b'*40,git={'com.test.leaf':leaf['target']['identifier']})
        root.update(requested_ref='root-1.0.0',catalog_id='123:root'); leaf['requested_ref']='leaf-1.0.0'
        result=resolve(FakeSource(root,[root,leaf]),{},baseline(),'6000.3.19',mappings([root,leaf]),[])
        for row in result['packages']:
            self.assertEqual(row['requested_ref'],row['name'].split('.')[-1]+'-1.0.0')
            self.assertTrue(row['target']['identifier'].endswith(row['target']['commit']))
            self.assertNotIn('requested_ref',row['target'])
        self.assertEqual(result['packages'][-1]['catalog_id'],'123:root')
