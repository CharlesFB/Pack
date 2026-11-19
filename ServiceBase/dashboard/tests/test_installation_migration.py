import uuid
from django.db import connection
from django.db.migrations.executor import MigrationExecutor
from django.test import TransactionTestCase
from django.utils import timezone


class InstallationMigrationTests(TransactionTestCase):
    def test_existing_attempt_and_report_survive_one_to_many_migration(self):
        old_target=('dashboard','0006_installjob_installstep_installattempt_installevent_and_more')
        new_target=('dashboard','0007_installattempt_number_installattempt_requested_by_and_more')
        executor=MigrationExecutor(connection)
        executor.migrate([old_target])
        try:
            apps=executor.loader.project_state([old_target]).apps
            project=apps.get_model('dashboard','UnityProject').objects.create(instance_id='migration',session_id='session',
                project_name='Test',project_path='/fixture',machine_name='fixture',unity_version='2022.3',process_id=1,port=0,token='',last_seen=timezone.now())
            job=apps.get_model('dashboard','InstallJob').objects.create(project=project,request_key=uuid.uuid4(),target={'name':'com.test'})
            step=apps.get_model('dashboard','InstallStep').objects.create(job=job,state='running')
            attempt=apps.get_model('dashboard','InstallAttempt').objects.create(step=step,sequence=2,report={'state':'running'})
            executor=MigrationExecutor(connection);executor.migrate([new_target])
            apps=executor.loader.project_state([new_target]).apps
            Attempt=apps.get_model('dashboard','InstallAttempt')
            restored=Attempt.objects.get(pk=attempt.pk)
            self.assertEqual(restored.number,1)
            self.assertEqual(restored.report,{'state':'running'})
            self.assertEqual(restored.step_id,step.pk)
            Attempt.objects.create(step_id=step.pk,number=2,retry_key=uuid.uuid4())
            self.assertEqual(Attempt.objects.filter(step_id=step.pk).count(),2)
            self.assertTrue(apps.get_model('dashboard','InstallJob').objects.get(pk=job.pk).active)
        finally:
            final_executor=MigrationExecutor(connection)
            final_executor.migrate(final_executor.loader.graph.leaf_nodes())

    def test_existing_active_job_survives_step_foreign_key_migration(self):
        old=('dashboard','0010_plugininventory_pluginremovalreceipt')
        new=('dashboard','0011_installationplan_projectpackagestate_and_more')
        executor=MigrationExecutor(connection);executor.migrate([old])
        try:
            apps=executor.loader.project_state([old]).apps
            project=apps.get_model('dashboard','UnityProject').objects.create(instance_id='step-migration',session_id='session',project_name='Test',project_path='/fixture',machine_name='fixture',unity_version='2022.3',process_id=1,port=0,token='',last_seen=timezone.now())
            target={'name':'com.test.old','identifier':'com.test.old@1.0.0'}
            job=apps.get_model('dashboard','InstallJob').objects.create(project=project,request_key=uuid.uuid4(),target=target)
            step=apps.get_model('dashboard','InstallStep').objects.create(job=job,state='verifying')
            attempt=apps.get_model('dashboard','InstallAttempt').objects.create(step=step,sequence=3,report={'state':'verifying'},dispatched_at=timezone.now())
            executor=MigrationExecutor(connection);executor.migrate([new])
            migrated=executor.loader.project_state([new]).apps
            self.assertEqual(migrated.get_model('dashboard','InstallStep').objects.get(pk=step.pk).job_id,job.pk)
            self.assertEqual(migrated.get_model('dashboard','InstallAttempt').objects.get(pk=attempt.pk).report,{'state':'verifying'})
            # Runtime model properties must run against the current schema.
            executor=MigrationExecutor(connection)
            executor.migrate(executor.loader.graph.leaf_nodes())
            from dashboard.models import InstallJob,InstallStep,InstallAttempt
            restored=InstallJob.objects.get(pk=job.pk)
            self.assertEqual(restored.step.pk,step.pk);self.assertEqual(restored.step.command,target)
            self.assertEqual(restored.step.attempt.pk,attempt.pk);self.assertTrue(restored.active)
            following=InstallStep.objects.create(job=restored,position=1,target={'name':'com.test.next'})
            InstallAttempt.objects.create(step=following)
            self.assertEqual(restored.steps.count(),2);self.assertEqual(restored.step.state,'verifying')
        finally:
            executor=MigrationExecutor(connection);executor.migrate(executor.loader.graph.leaf_nodes())
