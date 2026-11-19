from django.core.cache import cache
from django.test import TestCase, override_settings
from dashboard.models import InstallJob, UnityProject
from dashboard.tests.test_installations import InstallationTests


@override_settings(LOCAL_MODE=True)
class ProjectDirectoryTests(TestCase):
    native = InstallationTests.native
    sync = InstallationTests.sync

    def setUp(self):
        InstallationTests.setUp(self)
        self.url='/api/projects/install-project/directories/'

    def v5(self, **values):
        return self.native('upm/sync',dict(self.identity,protocol=1,executor_version=5,**values))

    def query(self, path='Assets'):
        return self.client.post(self.url,{'path':path},content_type='application/json')

    def test_scoped_read_only_query_and_expiry(self):
        self.assertEqual(self.query().status_code,409)
        self.v5()
        query=self.query().json()
        self.assertEqual(self.v5().json()['directory_request'],{'request_id':query['request_id'],'path':'Assets'})
        result=dict(request_id=query['request_id'],path='Assets',folders=['Assets/Plugins'],error='',truncated=False)
        self.assertEqual(self.v5(directory_result=result).status_code,200)
        read=self.client.get(self.url,{'request_id':query['request_id']}).json()
        self.assertEqual(read['folders'],['Assets/Plugins'])
        self.assertEqual(read['state'],'completed')
        self.assertFalse(InstallJob.objects.exists())
        self.assertEqual(self.v5(directory_result=result).status_code,200)
        UnityProject.objects.filter(pk=self.identity['instance_id']).update(session_id='new-session')
        self.assertEqual(self.client.get(self.url,{'request_id':query['request_id']}).status_code,410)
        cache.clear()
        self.assertEqual(self.client.get(self.url,{'request_id':query['request_id']}).status_code,410)

    def test_invalid_paths_wrong_session_and_outside_children(self):
        self.v5()
        for path in ('Packages','Assets/../Secret','/Assets','Assets//A','Assets/A/'):
            self.assertEqual(self.query(path).status_code,400,path)
        query=self.query().json()
        body=dict(request_id=query['request_id'],path='Assets',folders=['Packages'],error='',truncated=False)
        self.assertEqual(self.v5(directory_result=body).status_code,400)
        body['folders']=['Assets/Plugins/Nested']
        self.assertEqual(self.v5(directory_result=body).status_code,400)
        body['folders']=['Assets/Plugins']
        self.assertEqual(self.native('upm/sync',dict(self.identity,session_id='wrong',protocol=1,executor_version=5,directory_result=body)).status_code,409)
        self.assertEqual(self.client.get(self.url,{'request_id':query['request_id']}).json()['state'],'pending')
        self.assertEqual(self.client.post('/api/projects/other/directories/',{'path':'Assets'},content_type='application/json').status_code,404)

    def test_unavailable_directory_returns_failure_and_does_not_block_jobs(self):
        self.v5()
        query=self.query('Assets/Deleted').json()
        result=dict(request_id=query['request_id'],path='Assets/Deleted',folders=[],error='目录不存在',truncated=False)
        self.assertEqual(self.v5(directory_result=result).status_code,200)
        self.assertEqual(self.client.get(self.url,{'request_id':query['request_id']}).json()['state'],'failed')
        self.assertIsNone(self.v5().json()['directory_request'])

    def test_unity_empty_serialized_object_is_not_a_directory_result(self):
        response=self.v5(has_directory_result=False,
                         directory_result=dict(request_id='',path='',folders=[],error='',truncated=False))
        self.assertEqual(response.status_code,200)
        self.assertIsNone(response.json()['directory_request'])
