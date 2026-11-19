import json
import threading
from datetime import timedelta
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse
from unittest.mock import patch

from django.test import Client, TestCase
from django.utils import timezone

from dashboard.models import UnityProject


class FakeUnity(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        if urlparse(self.path).path == '/mtp/':
            if self.headers.get('X-ServiceBase-Token') != 'test-token':
                self.send_error(403)
                return
            payload = json.dumps({'instance_id':'project-a', 'session_id':'session-a',
                                  'schema_version':1, 'state':'ready', 'packages':[
                                      {'name':'com.example.root', 'tag':'root-1.0', 'depth':0,
                                       'dependencies':[{'name':'com.example.shared', 'requested_tag':'shared-1.0'}]}
                                  ]}).encode()
            self.send_response(200)
            self.send_header('Content-Length', str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return
        self.respond(parse_qs(urlparse(self.path).query)["message"][0])

    def do_POST(self):
        self.respond(json.loads(self.rfile.read(int(self.headers["Content-Length"])))['message'])

    def respond(self, message):
        if self.headers.get("X-ServiceBase-Token") != "test-token":
            self.send_error(403)
            return
        payload = json.dumps({"instance_id": "project-a", "session_id": "session-a",
                              "method": self.command, "message": message}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


class BridgeTests(TestCase):
    def test_delete_survives_heartbeats_and_can_be_undone(self):
        self.register()
        self.register(instance_id='project-b')
        endpoint = '/api/projects/project-a/'
        self.assertEqual(self.client.delete(endpoint).status_code, 200)
        self.assertEqual(self.client.delete(endpoint).status_code, 200)
        self.register(port=19001)  # Same session, even after Bridge changes port.
        rows = Client().get('/api/projects/').json()['projects']
        self.assertEqual([row['instance_id'] for row in rows], ['project-b'])
        self.assertEqual(self.send().status_code, 404)
        self.assertEqual(self.client.get(endpoint + 'mtp/').status_code, 404)
        self.assertEqual(self.client.post(endpoint + 'restore/').status_code, 200)
        self.assertEqual(len(self.client.get('/api/projects/').json()['projects']), 2)

    def test_new_unity_session_rediscovered_after_deletion(self):
        self.register()
        self.client.delete('/api/projects/project-a/')
        self.register(session_id='new-session')
        self.assertEqual(len(self.client.get('/api/projects/').json()['projects']), 1)

    def test_delete_and_restore_require_csrf_and_correct_methods(self):
        self.register()
        endpoint = '/api/projects/project-a/'
        self.assertEqual(self.client.get(endpoint).status_code, 405)
        self.assertEqual(self.client.get(endpoint + 'restore/').status_code, 405)
        client = Client(enforce_csrf_checks=True)
        self.assertEqual(client.delete(endpoint).status_code, 403)
        self.assertEqual(client.post(endpoint + 'restore/').status_code, 403)
        client.get('/')
        token = client.cookies['csrftoken'].value
        self.assertEqual(client.delete(endpoint, HTTP_X_CSRFTOKEN=token).status_code, 200)
        self.assertEqual(client.post(endpoint + 'restore/', HTTP_X_CSRFTOKEN=token).status_code, 200)
        self.assertEqual(self.client.delete('/api/projects/missing/').status_code, 404)

    def register(self, **overrides):
        payload = dict(instance_id="project-a", session_id="session-a", project_name="Pack Test",
                       project_path="/projects/Pack Test", machine_name="dev-mac", unity_version="2022.3",
                       process_id=123, port=18080, token="test-token")
        payload.update(overrides)
        return self.client.post('/api/heartbeat/', payload, content_type='application/json',
                                HTTP_X_SERVICEBASE_CLIENT='unity-bridge-v1')

    def send(self, **overrides):
        payload = dict(method="POST", message="你好 Unity & ? # +")
        payload.update(overrides)
        return self.client.post('/api/projects/project-a/message/', payload, content_type='application/json')

    def test_registration_updates_one_instance_and_never_exposes_token(self):
        self.assertEqual(self.register().status_code, 200)
        self.assertEqual(self.register(port=18081).status_code, 200)
        self.assertEqual(UnityProject.objects.count(), 1)
        row = self.client.get('/api/projects/').json()['projects'][0]
        self.assertEqual(row['port'], 18081)
        self.assertTrue(row['online'])
        self.assertNotIn('token', row)

    def test_offline_and_multiple_projects(self):
        self.register()
        self.register(instance_id="project-b", project_path="/projects/B", port=18082)
        UnityProject.objects.filter(pk="project-a").update(last_seen=timezone.now() - timedelta(seconds=16))
        self.assertEqual(self.send().status_code, 409)
        rows = self.client.get('/api/projects/').json()['projects']
        self.assertEqual(sum(row['online'] for row in rows), 1)

    def test_invalid_inputs(self):
        self.assertEqual(self.register(port=80).status_code, 400)
        self.assertEqual(self.register(instance_id="../../evil").status_code, 400)
        self.assertEqual(self.register(port=True).status_code, 400)
        self.assertEqual(self.client.post('/api/heartbeat/', '{}', content_type='application/json').status_code, 403)
        self.register()
        for fields in ({"method": "DELETE"}, {"message": ""}, {"message": "x" * 2001}, {"message": 42}):
            self.assertEqual(self.send(**fields).status_code, 400)
        self.assertEqual(self.client.post('/api/projects/missing/message/', {'message':'Hi'}, content_type='application/json').status_code, 404)

    def test_csrf_for_browser_and_native_header_for_registration(self):
        client = Client(enforce_csrf_checks=True)
        self.assertEqual(client.post('/api/projects/project-a/message/', {}, content_type='application/json').status_code, 403)
        self.assertEqual(client.get('/').status_code, 200)
        self.assertIn('csrftoken', client.cookies)
        self.assertEqual(client.post('/api/heartbeat/', {}, content_type='application/json',
                                    HTTP_X_SERVICEBASE_CLIENT='unity-bridge-v1', HTTP_ORIGIN='https://other.example').status_code, 403)

    def test_real_loopback_get_post_forwarding_and_session_check(self):
        server = ThreadingHTTPServer(('127.0.0.1', 0), FakeUnity)
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            self.register(port=server.server_port)
            for method in ('GET', 'POST'):
                result = self.send(method=method)
                self.assertEqual(result.status_code, 200, result.content)
                self.assertEqual(result.json()['response']['method'], method)
                self.assertEqual(result.json()['response']['message'], '你好 Unity & ? # +')
            mtp = self.client.get('/api/projects/project-a/mtp/')
            self.assertEqual(mtp.status_code, 200, mtp.content)
            self.assertEqual(mtp.json()['response']['packages'][0]['dependencies'][0]['name'], 'com.example.shared')
            self.assertEqual(mtp['Cache-Control'], 'no-store')
            UnityProject.objects.filter(pk='project-a').update(session_id='stale-session')
            self.assertEqual(self.send().status_code, 502)
            self.assertEqual(self.client.get('/api/projects/project-a/mtp/').status_code, 502)
        finally:
            server.shutdown()
            server.server_close()
            worker.join()

    def test_connection_failure_is_reported(self):
        self.register()
        with patch('dashboard.views.build_opener') as opener:
            opener.return_value.open.side_effect = TimeoutError('timed out')
            self.assertEqual(self.send().status_code, 502)

    def test_mtp_offline_missing_and_read_only(self):
        self.assertEqual(self.client.get('/api/projects/unknown/mtp/').status_code, 404)
        self.register()
        self.assertEqual(self.client.post('/api/projects/project-a/mtp/').status_code, 405)
        UnityProject.objects.update(last_seen=timezone.now() - timedelta(seconds=16))
        self.assertEqual(self.client.get('/api/projects/project-a/mtp/').status_code, 409)

    def test_mtp_absent_is_not_transport_error_and_old_bridge_is_explicit(self):
        from urllib.error import HTTPError
        self.register()
        with patch('dashboard.views.build_opener') as opener:
            reply = dict(instance_id='project-a', session_id='session-a', schema_version=1,
                         state='absent', detected=False, packages=[])
            response = opener.return_value.open.return_value.__enter__.return_value
            response.status = 200
            response.read.return_value = json.dumps(reply).encode()
            result = self.client.get('/api/projects/project-a/mtp/')
            self.assertEqual(result.status_code, 200)
            self.assertEqual(result.json()['response']['state'], 'absent')
            reply['schema_version'] = 99
            response.read.return_value = json.dumps(reply).encode()
            self.assertEqual(self.client.get('/api/projects/project-a/mtp/').status_code, 502)
            opener.return_value.open.side_effect = HTTPError('http://127.0.0.1', 404, 'Not Found', {}, None)
            result = self.client.get('/api/projects/project-a/mtp/')
            self.assertEqual(result.status_code, 502)
            self.assertIn('更新 ServiceBaseBridge.cs', result.json()['error'])
