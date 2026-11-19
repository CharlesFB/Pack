import uuid

from django.contrib.auth.models import User
from django.test import TestCase, Client, override_settings

from dashboard.models import InstallJob, InstallStep, InstallAttempt, InstallEvent
from dashboard.tests import test_installations as base_tests


@override_settings(LOCAL_MODE=True)
class InstallationHistoryTests(TestCase):
    setUp = base_tests.InstallationTests.setUp
    native = base_tests.InstallationTests.native
    sync = base_tests.InstallationTests.sync
    submit = base_tests.InstallationTests.submit
    start = base_tests.InstallationTests.start
    report = base_tests.InstallationTests.report

    def test_delete_finished_history_is_idempotent_and_keeps_receipts(self):
        work = self.start(); self.sync(self.report(work))
        url = self.url+work['job_id']+'/'
        self.assertTrue(self.client.get(self.url).json()['jobs'][0]['can_delete'])
        self.assertEqual(self.client.delete(url).status_code,200)
        deleted = InstallJob.objects.get().history_deleted_at
        self.assertIsNotNone(deleted)
        self.assertEqual(self.client.get(self.url).json()['jobs'],[])
        self.assertEqual(self.client.delete(url).status_code,200)
        self.assertEqual(InstallJob.objects.get().history_deleted_at,deleted)
        self.assertEqual((InstallStep.objects.count(),InstallAttempt.objects.count(),InstallEvent.objects.count()),(1,1,1))
        # Lost acknowledgements still resolve against the original immutable receipt.
        self.assertEqual(self.sync(self.report(work)).json()['ack'],1)

    def test_running_and_uncertain_jobs_cannot_be_deleted(self):
        work = self.start()
        url = self.url+work['job_id']+'/'
        for i,state in enumerate(('accepted','running','needs_review'),1):
            self.sync(self.report(work,state,i))
            self.assertFalse(self.client.get(self.url).json()['jobs'][0]['can_delete'])
            self.assertEqual(self.client.delete(url).status_code,409)
            self.assertIsNone(InstallJob.objects.get().history_deleted_at)

    def test_failed_deleted_job_cannot_be_retried_from_stale_page(self):
        work = self.start(); report = self.report(work,'failed'); report['failure_kind']='preflight'
        self.sync(report)
        self.assertEqual(self.client.delete(self.url+work['job_id']+'/').status_code,200)
        payload = dict(request_key=str(uuid.uuid4()),attempt_id=work['attempt_id'],reason='old page')
        self.assertEqual(self.client.post(self.url+work['job_id']+'/retry/',payload,content_type='application/json').status_code,404)
        self.assertFalse(InstallJob.objects.get().active)

    def test_cancelled_finished_job_can_be_deleted_but_stopping_job_cannot(self):
        work = self.start(); job=InstallJob.objects.get()
        step=job.step; step.target=dict(job.target); step.save()
        job.target.update(operation='plan'); job.cancel_requested=True; job.save()
        self.assertEqual(self.client.delete(self.url+work['job_id']+'/').status_code,409)
        report=self.report(work);report['evidence']['project_fingerprint']='a'*64
        self.assertEqual(self.sync(report).status_code,200)
        self.assertEqual(self.client.get(self.url).json()['jobs'][0]['display_state'],'cancelled')
        self.assertEqual(self.client.delete(self.url+work['job_id']+'/').status_code,200)

    def test_delete_scoped_to_project_owner_and_requires_csrf_and_delete_method(self):
        work=self.start();self.sync(self.report(work));url=self.url+work['job_id']+'/'
        self.assertEqual(self.client.get(url).status_code,405)
        self.assertEqual(self.client.delete('/api/projects/missing/installations/'+work['job_id']+'/').status_code,404)
        self.assertEqual(Client(enforce_csrf_checks=True).delete(url).status_code,403)
        with override_settings(LOCAL_MODE=False):
            self.assertEqual(self.client.delete(url).status_code,401)
            self.client.force_login(User.objects.create_user('history-outsider'))
            self.assertEqual(self.client.delete(url).status_code,404)
        self.assertIsNone(InstallJob.objects.get().history_deleted_at)
