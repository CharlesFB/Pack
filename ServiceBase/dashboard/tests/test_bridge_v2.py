import hashlib
from datetime import timedelta
from django.contrib.auth import get_user_model
from django.core.cache import cache
from django.db import connection
from django.test import Client, TestCase, override_settings
from django.test.utils import CaptureQueriesContext
from django.utils import timezone
from dashboard.models import UnityProject, BridgeCredential, BridgeTask, PackageSnapshot
from dashboard.bridge import presence_key, session_key

class PullBridgeTests(TestCase):
    def setUp(self):
        cache.clear()
        self.identity = dict(instance_id='v2-project', session_id='session-a')

    def native(self, route, body=None, **headers):
        return self.client.post('/api/bridge/' + route + '/', body or self.identity,
                                content_type='application/json', HTTP_X_SERVICEBASE_CLIENT='unity-bridge-v2', **headers)

    def register(self, **overrides):
        data = dict(self.identity, project_name='Test', project_path='/sample', machine_name='sample', unity_version='2022.3', process_id=1)
        data.update(overrides)
        return self.native('register', data)

    def enqueue(self, kind='message'):
        path = '/api/projects/v2-project/' + ('message/' if kind == 'message' else 'mtp/refresh/')
        return self.client.post(path, {'method':'GET','message':'hello'}, content_type='application/json')

    def snapshot(self):
        return dict(self.identity, schema_version=1, state='ready', packages=[], captured_at='2026-09-14T00:00:00Z')

    def test_idle_poll_has_no_sql_writes_and_cache_loss_recovers(self):
        self.assertEqual(self.register().status_code, 200)
        with CaptureQueriesContext(connection) as queries:
            for _ in range(3): self.assertEqual(self.native('poll').status_code, 200)
        self.assertFalse(any(q['sql'].lstrip().split()[0].upper() in ('INSERT','UPDATE','DELETE') for q in queries))
        cache.clear()
        self.assertEqual(self.native('poll').status_code, 200)
        self.assertTrue(self.client.get('/api/projects/').json()['projects'][0]['online'])
        cache.delete(presence_key('v2-project','session-a'))
        self.assertEqual(self.enqueue().status_code, 409)

    def test_task_delivery_retry_ack_and_get_mock_result(self):
        self.register()
        response = self.enqueue()
        self.assertEqual(response.status_code, 202)
        task_id = response.json()['task_id']
        for _ in range(2):
            task = self.native('poll').json()['tasks'][0]
            self.assertEqual(task['id'], task_id)
            self.assertEqual(task['method'], 'GET')
        body = dict(self.identity, task_id=task_id, result={'method':'GET','message':'hello'})
        self.assertEqual(self.native('result', body).status_code, 200)
        body['result'] = {'message':'duplicate must not overwrite'}
        self.assertEqual(self.native('result', body).status_code, 200)
        self.assertEqual(self.native('poll').json()['tasks'], [])
        status = self.client.get('/api/tasks/' + task_id + '/').json()
        self.assertEqual(status['state'], 'done')
        self.assertEqual(status['response']['message'], 'hello')

    def test_expiry_and_queue_bounds_and_refresh_dedup(self):
        self.register()
        a, b = self.enqueue('snapshot'), self.enqueue('snapshot')
        self.assertEqual(a.json()['task_id'], b.json()['task_id'])
        for _ in range(31): self.assertEqual(self.enqueue().status_code, 202)
        self.assertEqual(self.enqueue().status_code, 429)
        self.assertEqual(len(self.native('poll').json()['tasks']), 4)
        BridgeTask.objects.update(expires_at=timezone.now()-timedelta(seconds=1))
        self.assertEqual(self.native('poll').json()['tasks'], [])
        self.assertEqual(self.client.get('/api/tasks/'+a.json()['task_id']+'/').json()['state'], 'expired')
        self.assertEqual(self.native('result', dict(self.identity, task_id=a.json()['task_id'], result={})).status_code, 410)

    def test_snapshots_persist_offline_and_skip_unchanged_writes(self):
        self.register()
        snapshot = self.snapshot()
        self.assertEqual(self.native('snapshot', dict(self.identity, snapshot=snapshot)).status_code, 200)
        snapshot['captured_at'] = '2026-09-14T01:00:00Z'
        with CaptureQueriesContext(connection) as queries:
            self.assertEqual(self.native('snapshot', dict(self.identity, snapshot=snapshot)).status_code, 200)
        self.assertFalse(any(q['sql'].startswith(('UPDATE','INSERT')) for q in queries))
        cache.clear()
        response = self.client.get('/api/projects/v2-project/mtp/').json()
        self.assertTrue(response['stale'])
        self.assertEqual(response['response']['packages'], [])
        self.assertEqual(PackageSnapshot.objects.count(), 1)

    def test_session_replacement_rejects_old_upload_and_tasks(self):
        self.register()
        task = self.enqueue().json()['task_id']
        self.register(session_id='session-b')
        self.assertEqual(self.native('poll').status_code, 409)
        self.assertEqual(self.native('snapshot', dict(self.identity, snapshot=self.snapshot())).status_code, 409)
        self.assertEqual(self.client.get('/api/tasks/'+task+'/').json()['state'], 'expired')
        self.assertEqual(self.native('poll', dict(self.identity,session_id='session-b')).json()['tasks'], [])

    def test_pagination_and_search_are_bounded(self):
        self.register()
        base = UnityProject.objects.get()
        rows=[]
        for n in range(125):
            rows.append(UnityProject(instance_id='extra-'+str(n), session_id='x', project_name='Extra', project_path='/'+str(n),
                                      machine_name='x', unity_version='x', process_id=1, port=0, token='',last_seen=timezone.now()))
        UnityProject.objects.bulk_create(rows)
        listing=self.client.get('/api/projects/?page_size=500').json()
        self.assertEqual(len(listing['projects']),100)
        self.assertEqual(listing['total'],126)
        self.assertEqual(len(self.client.get('/api/projects/?q=Test').json()['projects']),1)
        self.assertEqual(self.client.get('/api/projects/?page=bad').status_code,400)

    def test_hidden_record_stays_hidden_until_new_session(self):
        self.register()
        self.client.delete('/api/projects/v2-project/')
        self.native('poll')
        self.assertEqual(self.client.get('/api/projects/').json()['total'],0)
        self.register(session_id='session-b')
        self.assertEqual(self.client.get('/api/projects/').json()['total'],1)

    def test_invalid_upload_and_origin_and_csrf(self):
        self.assertEqual(self.native('poll', HTTP_ORIGIN='https://evil.test').status_code,403)
        self.register()
        self.assertEqual(self.native('snapshot',dict(self.identity,snapshot={'packages':[]})).status_code,400)
        self.assertEqual(self.native('result',dict(self.identity,task_id='invalid',result={})).status_code,400)
        c=Client(enforce_csrf_checks=True)
        self.assertEqual(c.post('/api/projects/v2-project/mtp/refresh/').status_code,403)

    @override_settings(LOCAL_MODE=False)
    def test_remote_authentication_ownership_and_revocation(self):
        user=get_user_model().objects.create_user('alice',password='test')
        other=get_user_model().objects.create_user('bob',password='test')
        credential=BridgeCredential.objects.create(owner=user,name='test',digest=hashlib.sha256(b'secret').hexdigest())
        data=dict(self.identity,project_name='Remote',project_path='/remote',machine_name='remote',unity_version='2022',process_id=1)
        headers={'HTTP_AUTHORIZATION':'Bearer secret','REMOTE_ADDR':'192.0.2.1'}
        self.assertEqual(self.native('register',data,**headers).status_code,200)
        self.assertEqual(self.client.get('/api/projects/').status_code,401)
        self.client.force_login(other)
        self.assertEqual(self.client.get('/api/projects/').json()['total'],0)
        self.assertEqual(self.enqueue().status_code,404)
        self.client.force_login(user)
        self.assertEqual(self.client.get('/api/projects/').json()['total'],0)
        self.client.post('/accounts/device/', {'token':'secret'})
        self.assertEqual(self.client.get('/api/projects/').json()['total'],1)
        self.assertEqual(self.enqueue().status_code,202)
        self.assertEqual(self.native('poll',**headers).status_code,200)
        credential.active=False;credential.save();cache.clear()
        self.assertEqual(self.native('poll',**headers).status_code,403)

    @override_settings(LOCAL_MODE=False)
    def test_another_credential_cannot_take_over_project(self):
        user=get_user_model().objects.create_user('alice')
        for token in ('one','two'):
            BridgeCredential.objects.create(owner=user,name=token,digest=hashlib.sha256(token.encode()).hexdigest())
        data=dict(self.identity,project_name='Remote',project_path='/remote',machine_name='remote',unity_version='2022',process_id=1)
        self.assertEqual(self.native('register',data,HTTP_AUTHORIZATION='Bearer one').status_code,200)
        self.assertEqual(self.native('register',data,HTTP_AUTHORIZATION='Bearer two').status_code,403)
        self.assertEqual(self.native('poll',HTTP_AUTHORIZATION='Bearer two').status_code,409)
