import uuid
from datetime import timedelta
from django.core.cache import cache
from django.test import TestCase, override_settings, Client
from django.contrib.auth.models import User
from django.utils import timezone
from dashboard.models import InstallJob, InstallStep, InstallEvent, UnityProject


@override_settings(LOCAL_MODE=True)
class InstallationTests(TestCase):
    def setUp(self):
        cache.clear()
        self.identity = {'instance_id':'install-project', 'session_id':'session-1'}
        self.native('register', dict(self.identity, project_name='Test', project_path='/test', machine_name='test', unity_version='2022.3', process_id=1))
        self.url = '/api/projects/install-project/installations/'
        self.sync()

    def native(self, path, body):
        if path == 'upm/sync': body.setdefault('executor_version',2)
        return self.client.post('/api/bridge/' + path + '/', body, content_type='application/json', HTTP_X_SERVICEBASE_CLIENT='unity-bridge-v2')

    def sync(self, report=None):
        return self.native('upm/sync', dict(self.identity, protocol=1, report=report))

    def submit(self, key=None, **overrides):
        data = dict(name='com.test.example', identifier='com.test.example@1.2.3', request_key=key or str(uuid.uuid4()))
        data.update(overrides)
        return self.client.post(self.url, data, content_type='application/json')

    def start(self):
        response = self.submit()
        self.assertEqual(response.status_code, 202)
        return self.sync().json()['work']

    def report(self, work, state='succeeded', sequence=1):
        return dict(attempt_id=work['attempt_id'], sequence=sequence, state=state, evidence=dict(name='com.test.example', identifier='com.test.example@1.2.3', source='registry', version='1.2.3', direct=True, manifest_match=True, lock_match=True))

    def test_request_idempotency_project_mutex_and_fixed_target(self):
        key = str(uuid.uuid4())
        first = self.submit(key)
        self.assertEqual(first.status_code, 202)
        self.assertEqual(self.submit(key).json()['job']['id'], first.json()['job']['id'])
        self.assertEqual(self.submit(key, identifier='com.test.example@2.0.0').status_code, 409)
        self.assertEqual(self.submit().status_code, 409)
        self.assertEqual(InstallJob.objects.count(), 1)
        self.assertEqual(self.submit(identifier='com.test.example@latest').status_code, 400)
        self.assertEqual(self.submit(identifier='https://user:password@example.org/a.git#'+'a'*40).status_code, 400)

    def test_delivery_recovery_duplicate_receipts_and_terminal_immutable(self):
        work = self.start()
        self.assertFalse(work['recover_only'])
        self.assertTrue(self.sync().json()['work']['recover_only'])
        report = self.report(work)
        self.assertIsNone(self.sync(report).json()['work'])
        self.assertEqual(self.sync(report).json()['ack'], 1)
        self.assertEqual(InstallEvent.objects.count(), 1)
        self.assertEqual(self.sync(self.report(work, 'running', 2)).status_code, 400)
        self.assertFalse(InstallJob.objects.get().active)
        self.assertEqual(self.submit().status_code, 202)

    def test_unknown_timeout_retains_lock_and_late_success(self):
        work = self.start()
        self.sync(self.report(work,'running'))
        InstallStep.objects.update(updated_at=timezone.now()-timedelta(minutes=20))
        self.assertEqual(self.client.get(self.url).json()['jobs'][0]['display_state'], 'needs_review')
        cache.clear()
        self.assertEqual(self.client.get(self.url).json()['jobs'][0]['display_state'], 'waiting_connection')
        self.assertTrue(InstallJob.objects.get().active)
        self.assertEqual(self.sync(self.report(work, sequence=2)).status_code, 200)
        self.assertEqual(InstallJob.objects.get().step.state, 'succeeded')

    def test_success_requires_evidence_and_unknown_failure_rejected(self):
        work = self.start()
        report = self.report(work); report['evidence']['lock_match'] = False
        self.assertEqual(self.sync(report).status_code, 400)
        self.assertEqual(self.sync(self.report(work,'failed')).status_code, 400)
        report = self.report(work, 'failed'); report['failure_kind'] = 'preflight'
        self.assertEqual(self.sync(report).status_code, 200)

    def test_recheck_never_creates_another_attempt(self):
        work = self.start()
        self.sync(self.report(work, 'needs_review', 4))
        url = self.url + work['job_id'] + '/recheck/'
        self.assertEqual(self.client.post(url).status_code, 200)
        returned = self.sync().json()['work']
        self.assertEqual(returned['attempt_id'], work['attempt_id'])
        self.assertEqual(returned['sequence'], 4)
        self.assertEqual(returned['recheck'], 1)
        self.assertTrue(returned['recover_only'])
        self.assertEqual(self.sync(self.report(work, 'running', 5)).status_code, 400)

    def test_auth_session_and_csrf(self):
        self.start()
        with override_settings(LOCAL_MODE=False):
            self.assertEqual(self.client.get(self.url).status_code, 401)
            self.client.force_login(User.objects.create_user('outsider'))
            self.assertEqual(self.client.get(self.url).status_code, 404)
        self.identity['session_id'] = 'stale'
        self.assertEqual(self.sync().status_code, 409)
        client = Client(enforce_csrf_checks=True)
        self.assertEqual(client.post(self.url, {}, content_type='application/json').status_code, 403)

    def test_stale_or_conflicting_report_cannot_overwrite_progress(self):
        work = self.start()
        first = self.report(work, 'accepted')
        self.sync(first)
        self.sync(self.report(work, 'running', 2))
        self.assertEqual(self.sync(first).status_code, 200)
        first['message'] = 'changed'
        self.assertEqual(self.sync(first).status_code, 400)
        self.assertEqual(InstallJob.objects.get().step.state, 'running')

    def test_unity_empty_serialized_report_is_explicitly_absent(self):
        response = self.native('upm/sync', dict(self.identity, protocol=1, has_report=False,
                                               report={'attempt_id':'', 'state':'', 'sequence':0}))
        self.assertEqual(response.status_code, 200)

    def test_v10_can_replay_v9_journal_with_new_false_evidence_field(self):
        work=self.start()
        report=self.report(work,'needs_review',4)
        self.assertEqual(self.sync(report).status_code,200)
        report['evidence']['operation_finished']=False
        response=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=10,report=report))
        self.assertEqual(response.status_code,200,response.content)
        self.assertEqual(response.json()['ack'],4)
        self.assertTrue(response.json()['work']['recover_only'])
        self.assertNotIn('operation_finished',InstallEvent.objects.get(sequence=4).report['evidence'])
        report['evidence']['operation_finished']=True
        self.assertEqual(self.sync(report).status_code,400)
        report['evidence']['operation_finished']=False
        report['message']='changed'
        self.assertEqual(self.sync(report).status_code,400)

    def test_git_target_is_frozen_and_protected_package_rejected(self):
        self.assertEqual(self.submit(name='com.magictavern.mtpmanager', identifier='com.magictavern.mtpmanager@1.0.0').status_code, 400)
        identifier = 'https://git.example.org/packages/repo.git?path=/Unity/Package#' + 'a' * 40
        result = self.submit(identifier=identifier)
        self.assertEqual(result.status_code, 202)
        self.assertEqual(result.json()['job']['target']['commit'], 'a' * 40)
        work = self.sync().json()['work']
        report = self.report(work)
        report['evidence'].update(identifier=identifier,source='git',commit='b'*40)
        self.assertEqual(self.sync(report).status_code, 400)
        report['evidence']['commit'] = 'a'*40
        self.assertEqual(self.sync(report).status_code, 200)

    def test_manual_retry_is_audited_idempotent_and_old_receipts_cannot_overwrite(self):
        work = self.start()
        report = self.report(work, 'failed'); report['failure_kind'] = 'preflight'
        self.sync(report)
        url = self.url + work['job_id'] + '/retry/'
        payload = {'request_key':str(uuid.uuid4()),'attempt_id':work['attempt_id'],'reason':'Fixed preflight configuration'}
        response = self.client.post(url,payload,content_type='application/json')
        self.assertEqual(response.status_code,202)
        self.assertEqual(response.json()['job']['attempt_number'],2)
        self.assertEqual(self.client.post(url,payload,content_type='application/json').status_code,200)
        self.assertEqual(InstallJob.objects.get().step.attempts.count(),2)
        old_ack = self.sync(report)
        self.assertEqual(old_ack.status_code,200)
        self.assertNotEqual(old_ack.json()['work']['attempt_id'],work['attempt_id'])
        self.assertEqual(self.sync(self.report(work,'succeeded',2)).status_code,400)
        self.assertEqual(InstallJob.objects.get().step.state,'queued')
        attempt = InstallJob.objects.get().step.attempt
        self.assertEqual(attempt.retry_reason,payload['reason'])
        audit = InstallEvent.objects.get(attempt=attempt, sequence=0)
        self.assertEqual(audit.report['action'], 'manual_retry')
        self.assertEqual(audit.report['previous_attempt'], work['attempt_id'])
        self.assertEqual(audit.report['reason'], payload['reason'])
        self.assertIsNotNone(audit.created_at)
        self.assertEqual(self.client.post(url,dict(payload,reason='changed'),content_type='application/json').status_code,409)

    def test_unknown_state_cannot_be_manually_retried_and_old_failure_cannot_release_it(self):
        work = self.start()
        self.sync(self.report(work,'running'))
        report = self.report(work,'failed',2); report['failure_kind'] = 'preflight'
        self.assertEqual(self.sync(report).status_code,400)
        self.sync(self.report(work,'needs_review',2))
        response = self.client.post(self.url+work['job_id']+'/retry/', {'request_key':str(uuid.uuid4()),'attempt_id':work['attempt_id'],'reason':'Request retry'},content_type='application/json')
        self.assertEqual(response.status_code,409)
        self.assertTrue(InstallJob.objects.get().active)
        self.assertEqual(InstallJob.objects.get().step.attempts.count(),1)

    def test_blocked_executor_stays_online_without_being_ready(self):
        work = self.start()
        response = self.native('upm/sync',dict(self.identity,protocol=1,has_report=False,diagnostic='Journal corrupted'))
        self.assertEqual(response.status_code,200)
        status = self.client.get(self.url).json()
        self.assertTrue(status['executor_online'])
        self.assertFalse(status['executor_ready'])
        self.assertEqual(status['jobs'][0]['display_state'],'needs_review')
        self.assertEqual(status['jobs'][0]['diagnostic'],'Journal corrupted')
        self.assertEqual(self.submit().status_code,409)

    def test_old_executor_cannot_start_new_work_but_can_finish_existing_work(self):
        work=self.start()
        reply=self.native('upm/sync',dict(self.identity,protocol=1,executor_version=1,report=self.report(work)))
        self.assertEqual(reply.status_code,200)
        self.assertFalse(InstallJob.objects.get().active)
        self.assertFalse(self.client.get(self.url).json()['executor_ready'])
        self.assertEqual(self.submit().status_code,409)

    def test_queued_task_is_not_dispatched_to_blocked_or_old_executor(self):
        self.assertEqual(self.submit().status_code,202)
        for extra in ({'diagnostic':'MTP loaded'},{'executor_version':1}):
            response=self.native('upm/sync',dict(self.identity,protocol=1,has_report=False,**extra))
            self.assertIsNone(response.json()['work'])
            self.assertIsNone(InstallJob.objects.get().step.attempt.dispatched_at)
        self.assertFalse(self.sync().json()['work']['recover_only'])
