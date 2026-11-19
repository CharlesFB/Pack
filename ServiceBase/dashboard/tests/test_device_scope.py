import hashlib
from datetime import timedelta
from django.test import TestCase, Client, override_settings
from django.contrib.auth.models import User
from django.utils import timezone
from django.core.cache import cache
from dashboard.models import BridgeCredential, UnityProject, BridgeTask, PackageSnapshot


@override_settings(LOCAL_MODE=False)
class DeviceScopeTests(TestCase):
    def setUp(self):
        cache.clear()
        self.member=User.objects.create_user('member')
        self.other=User.objects.create_user('other')
        self.admin=User.objects.create_user('admin',is_staff=True)
        for name,owner in [('mac-a',self.member),('mac-b',self.member),('mac-c',self.other)]:
            cred=BridgeCredential.objects.create(owner=owner,name=name,digest=hashlib.sha256(name.encode()).hexdigest())
            p=UnityProject.objects.create(instance_id=name,session_id='session',project_name=name,project_path='/test',machine_name=name,unity_version='2022',process_id=1,port=0,token='',last_seen=timezone.now(),owner=owner,credential=cred,transport='pull')
            PackageSnapshot.objects.create(project=p,session_id='session',digest='x',payload={'schema_version':1,'state':'ready','packages':[]})
        self.client.force_login(self.member)

    def test_two_browsers_same_account_are_isolated_and_admin_sees_offline(self):
        self.assertEqual(self.client.get('/api/projects/').json()['total'],0)
        self.assertEqual(self.client.post('/accounts/device/',{'token':'mac-a'}).status_code,302)
        second=Client();second.force_login(self.member)
        second.post('/accounts/device/',{'token':'mac-b'})
        self.assertEqual([p['instance_id'] for p in self.client.get('/api/projects/').json()['projects']],['mac-a'])
        self.assertEqual([p['instance_id'] for p in second.get('/api/projects/').json()['projects']],['mac-b'])
        self.client.force_login(self.admin)
        result=self.client.get('/api/projects/').json()
        self.assertEqual(result['total'],3)
        self.assertTrue(all(not p['online'] for p in result['projects']))
        self.assertEqual(self.client.get('/api/projects/mac-c/mtp/').status_code,200)

    def test_cross_device_all_operations_are_denied(self):
        self.client.post('/accounts/device/',{'token':'mac-a'})
        self.assertEqual(self.client.get('/api/projects/mac-b/mtp/').status_code,404)
        for suffix in ['restore/','mtp/refresh/','message/']:
            response=self.client.post('/api/projects/mac-b/'+suffix,{'method':'GET','message':'test'},content_type='application/json')
            self.assertEqual(response.status_code,404)
        self.assertEqual(self.client.delete('/api/projects/mac-b/').status_code,404)
        task=BridgeTask.objects.create(project_id='mac-b',session_id='session',kind='message',expires_at=timezone.now()+timedelta(seconds=60))
        self.assertEqual(self.client.get('/api/tasks/'+str(task.pk)+'/').status_code,404)

    def test_binding_owner_revocation_unbind_and_csrf(self):
        self.assertEqual(self.client.post('/accounts/device/',{'token':'mac-c'}).status_code,200)
        self.assertNotIn('bridge_device_id',self.client.session)
        self.client.post('/accounts/device/',{'token':'mac-a'})
        BridgeCredential.objects.filter(name='mac-a').update(active=False)
        self.assertEqual(self.client.get('/api/projects/').json()['total'],0)
        self.client.post('/accounts/device/',{'token':'mac-b'})
        self.client.post('/accounts/device/',{'action':'unbind'})
        self.assertEqual(self.client.get('/api/projects/').json()['total'],0)
        strict=Client(enforce_csrf_checks=True);strict.force_login(self.member)
        self.assertEqual(strict.post('/accounts/device/',{'token':'mac-b'}).status_code,403)

    @override_settings(LOCAL_MODE=True)
    def test_local_login_does_not_bypass_device_scope(self):
        self.assertEqual(self.client.get('/api/projects/').json()['total'],0)
        self.client.logout()
        self.assertEqual(self.client.get('/api/projects/').json()['total'],0)
