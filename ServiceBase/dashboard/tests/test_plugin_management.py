import copy
import hashlib
import json
import uuid
from datetime import timedelta
from django.utils import timezone
from django.test import TestCase, override_settings
from dashboard.tests import test_plugin_installations as plugin_tests
from dashboard.models import PluginInventory, PluginReceipt, InstallJob, UnityProject
from dashboard.plugin_delivery import recipe_for


@override_settings(LOCAL_MODE=True)
class PluginManagementTests(TestCase):
    native = plugin_tests.PluginInstallationTests.native
    sync = plugin_tests.PluginInstallationTests.sync
    submit = plugin_tests.PluginInstallationTests.submit
    report = plugin_tests.PluginInstallationTests.report
    plugin = plugin_tests.PluginInstallationTests.plugin
    setUp = plugin_tests.PluginInstallationTests.setUp

    def v6(self, **data):
        return self.native('upm/sync', dict(self.identity, protocol=1, executor_version=6, **data))

    def imported(self, preexisting=False):
        self.plugin()
        work = self.v6().json()['work']
        target = work['target']
        files = recipe_for(target)['files']
        for f in files:
            f.update(existed=preexisting,meta_existed=preexisting,before_sha256=f['sha256'] if preexisting and not f['folder'] else '',before_meta_sha256=f['meta_sha256'] if preexisting else '')
        raw = json.dumps(dict(plugin_id='sample',build_id=target['build_id'],attempt_id=work['attempt_id'],files=files))
        response = self.native('plugins/receipt',dict(self.identity,attempt_id=work['attempt_id'],record_json=raw))
        self.assertEqual(response.status_code,200,response.content)
        digest = hashlib.sha256(raw.encode()).hexdigest()
        report = self.report(work)
        report['evidence'] = dict(build_id=target['build_id'],recipe_id=target['recipe_id'],record_digest=digest)
        self.assertEqual(self.v6(report=report).status_code,200)
        item = dict(record_key='sample-'+target['build_id'],record_digest=digest,plugin_id='sample',build_id=target['build_id'],destination_root='',status='installed',file_count=len(files),matched=len(files),modified=0,missing=0,roots=['Assets/Test'],samples=[])
        snapshot = dict(scanned_at=timezone.now().isoformat(),request_key='initial',error='',installations=[item])
        self.assertEqual(self.v6(has_plugin_inventory=True,plugin_inventory=snapshot).status_code,200)
        return item,snapshot,files

    def remove(self,item,key=None):
        return self.client.post(self.url,dict(operation='plugin_remove',record_key=item['record_key'],record_digest=item['record_digest'],request_key=key or str(uuid.uuid4())),content_type='application/json')

    def test_inventory_refresh_freshness_session_and_validation(self):
        item,snapshot,_ = self.imported()
        endpoint='/api/projects/install-project/plugins/'
        self.assertTrue(self.client.get(endpoint).json()['fresh'])
        self.assertEqual(self.client.get(endpoint).json()['installations'][0]['status'],'installed')
        self.assertEqual(self.client.post(endpoint+'refresh/').status_code,202)
        self.assertTrue(self.client.get(endpoint).json()['scanning'])
        self.assertEqual(self.remove(item).status_code,400)
        request=self.v6().json()['inventory_request'];snapshot['request_key']=request
        snapshot['scanned_at']=timezone.now().isoformat()
        self.v6(has_plugin_inventory=True,plugin_inventory=snapshot)
        self.assertFalse(self.client.get(endpoint).json()['scanning'])
        bad=copy.deepcopy(snapshot);bad['installations'][0]['matched']+=1
        self.assertEqual(self.v6(has_plugin_inventory=True,plugin_inventory=bad).status_code,400)
        snapshot['scanned_at']=(timezone.now()-timedelta(minutes=5)).isoformat()
        PluginInventory.objects.update(payload=snapshot)
        self.assertFalse(self.client.get(endpoint).json()['fresh'])
        self.assertEqual(self.remove(item).status_code,400)
        self.native('register',dict(self.identity,session_id='new-session',project_name='Test',project_path='/test',machine_name='test',unity_version='2022.3',process_id=2))
        self.assertEqual(self.v6(has_plugin_inventory=True,plugin_inventory=snapshot).status_code,409)
        self.assertFalse(self.client.get(endpoint).json()['fresh'])

    def test_delete_is_idempotent_version_gated_and_requires_receipt(self):
        item,snapshot,files=self.imported()
        self.native('upm/sync',dict(self.identity,protocol=1,executor_version=5))
        self.assertEqual(self.remove(item).status_code,409)
        self.v6(); key=str(uuid.uuid4())
        response=self.remove(item,key);self.assertEqual(response.status_code,202,response.content)
        self.assertIsNone(self.native('upm/sync',dict(self.identity,protocol=1,executor_version=5)).json()['work'])
        work=self.v6().json()['work']
        self.assertEqual(work['target']['operation'],'plugin_remove')
        self.assertEqual(self.remove(item,key).status_code,200)
        self.assertEqual(self.remove(item).status_code,409)
        report=self.report(work); report['evidence']={}
        self.assertEqual(self.v6(report=report).status_code,400)
        result={k:item[k] for k in ('plugin_id','build_id','record_key','record_digest')}
        result.update(attempt_id=work['attempt_id'],files=[dict(path=f['path'],outcome='removed',reason='verified') for f in files])
        raw=json.dumps(result); data=dict(self.identity,attempt_id=work['attempt_id'],record_json=raw)
        self.assertEqual(self.native('plugins/removal-receipt',dict(data,session_id='old')).status_code,400)
        self.assertEqual(self.native('plugins/removal-receipt',data).status_code,200)
        self.assertEqual(self.native('plugins/removal-receipt',data).status_code,200)
        self.assertEqual(self.native('plugins/removal-receipt',dict(data,record_json=raw+' ')).status_code,400)
        report['evidence']=dict(build_id=item['build_id'],record_digest=hashlib.sha256(raw.encode()).hexdigest())
        self.assertEqual(self.v6(report=report).status_code,200)
        self.assertEqual(self.v6(report=report).status_code,200)
        self.assertFalse(InstallJob.objects.get(pk=work['job_id']).active)
        # A network retry still resolves after the local record disappears from the inventory.
        snapshot.update(installations=[],scanned_at=timezone.now().isoformat())
        self.v6(has_plugin_inventory=True,plugin_inventory=snapshot)
        self.assertEqual(self.remove(item,key).status_code,200)
        data=self.client.get('/api/projects/install-project/plugins/').json()
        self.assertEqual(data['installations'],[])
        self.assertEqual(data['removals'][0]['removed'],len(files))

    def test_delete_cannot_claim_preexisting_or_unrecorded_files(self):
        item,_,files=self.imported(preexisting=True)
        self.remove(item);work=self.v6().json()['work']
        result={k:item[k] for k in ('plugin_id','build_id','record_key','record_digest')}
        result.update(attempt_id=work['attempt_id'],files=[dict(path=f['path'],outcome='removed',reason='unsafe') for f in files])
        def send(): return self.native('plugins/removal-receipt',dict(self.identity,attempt_id=work['attempt_id'],record_json=json.dumps(result)))
        self.assertEqual(send().status_code,400)
        for f in result['files']:f['outcome']='retained'
        result['files'][0]['path']='Assets/untracked'
        self.assertEqual(send().status_code,400)
        result['files'][0]['path']=files[0]['path']
        self.assertEqual(send().status_code,200)

    def test_live_inventory_keeps_multiple_roots_and_enforces_project_scope(self):
        item,snapshot,_=self.imported()
        second=dict(item,record_key=item['record_key']+'-second',destination_root='Assets/Other')
        snapshot['installations'].append(second);snapshot['scanned_at']=timezone.now().isoformat()
        self.assertEqual(self.v6(has_plugin_inventory=True,plugin_inventory=snapshot).status_code,200)
        self.assertEqual(len(self.client.get('/api/projects/install-project/plugins/').json()['installations']),2)
        self.assertEqual(self.client.post('/api/projects/unknown/plugins/refresh/').status_code,404)
        self.assertEqual(self.client.get('/api/projects/unknown/plugins/').status_code,404)
        # Snapshots alone do not grant delete ownership without an import receipt.
        second['record_digest']='f'*64;snapshot['scanned_at']=timezone.now().isoformat()
        self.v6(has_plugin_inventory=True,plugin_inventory=snapshot)
        self.assertEqual(self.remove(second).status_code,400)
