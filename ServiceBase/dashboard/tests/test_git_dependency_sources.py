"""Exercise the real metadata transport adapter, not only the fake graph source."""
import json
from unittest.mock import Mock, patch

from django.test import SimpleTestCase

from dashboard.plan_resolver import Source, resolve
from dashboard.tests.test_installation_plans import baseline, node, installed, mappings, FakeSource


class GitDependencySourcesTests(SimpleTestCase):
    def setUp(self):
        self.name='com.test.shared'
        self.project=Mock(ssh_url_to_repo='git@git.example.org:group/repo.git')
        self.project.commits.get.return_value.id='a'*40
        self.client=Mock()
        self.client.projects.get.return_value=self.project
        self.raw=json.dumps(dict(name=self.name,version='1.0.0')).encode()
        for p in (
            patch('dashboard.plan_resolver.catalog.source',return_value=('key','https://git.example.org/','server-token','main')),
            patch('dashboard.plan_resolver.gitlab.Gitlab',return_value=self.client),
        ):
            p.start();self.addCleanup(p.stop)
        p=patch('dashboard.plan_resolver.get_bytes',return_value=self.raw)
        self.read=p.start();self.addCleanup(p.stop)
        self.source=Source(baseline(),'2022.3.62')
        self.addCleanup(self.source.close)

    def test_https_ssh_and_scp_pin_the_same_credential_free_target(self):
        targets=[]
        for address in ('https://git.example.org/group/repo.git#release-1.0.0',
                        'ssh://git@git.example.org/group/repo.git#release-1.0.0',
                        'git@git.example.org:group/repo.git#release-1.0.0'):
            targets.append(self.source.git(self.name,address)['target'])
        self.assertEqual(targets,[targets[0]]*3)
        self.assertEqual(targets[0]['identifier'],'https://git.example.org/group/repo.git#'+'a'*40)
        self.assertNotIn('server-token',json.dumps(targets))
        self.project.commits.get.assert_called_with('release-1.0.0')
        self.assertEqual(self.source.git(self.name,'https://git.example.org/group/repo.git#release-1.0.0')['requested_ref'],'release-1.0.0')

    def test_ssh_custom_port_is_checked_against_gitlab_not_https_port(self):
        self.project.ssh_url_to_repo='ssh://git@git.example.org:2222/group/repo.git'
        result=self.source.git(self.name,'ssh://git@git.example.org:2222/group/repo.git?path=/Packages/Shared#v1')
        self.assertEqual(result['target']['identifier'],'https://git.example.org/group/repo.git?path=/Packages/Shared#'+'a'*40)
        self.read.assert_called_with(self.project,'Packages/Shared/package.json','a'*40,limit=1024*1024)
        with self.assertRaisesRegex(ValueError,'公布的仓库地址'):
            self.source.git(self.name,'ssh://git@git.example.org:22/group/repo.git#v1')

    def test_relative_gitlab_service_path_applies_to_https_not_ssh_repository(self):
        self.source.url='https://git.example.org/gitlab'
        self.source.gl=self.client
        result=self.source.git(self.name,'ssh://git@git.example.org/group/repo.git#v1')
        self.assertEqual(result['target']['identifier'],'https://git.example.org/gitlab/group/repo.git#'+'a'*40)
        self.assertEqual(result['target'],self.source.git(self.name,'https://git.example.org/gitlab/group/repo.git#v1')['target'])

    def test_untrusted_links_are_rejected_before_repository_requests_without_echoing_secrets(self):
        for address in (
            'ssh://git@other.example/group/repo.git#v1',
            'ssh://git:secret-token@git.example.org/group/repo.git#v1',
            'ssh://secret-token@git.example.org/group/repo.git#v1',
            'https://secret-token@git.example.org/group/repo.git#v1',
            'http://git.example.org/group/repo.git#v1',
            'https://git.example.org:444/group/repo.git#v1',
            'ssh://git@git.example.org/group/repo.git',
            'ssh://git@git.example.org/group/repo.git?token=secret-token#v1',
            'ssh://git@git.example.org/group/repo.git?path=/../Hidden#v1',
            'ssh://git@git.example.org/group/%2e%2e/repo.git#v1',
            'ssh://git@git.example.org/group/%252e%252e/repo.git#v1',
            'ssh://git@git.example.org:invalid/group/repo.git#v1',
            'ssh://git@git.example.org/group/repo.git#v\n1',
            None,
        ):
            with self.subTest(address=address):
                with self.assertRaises(ValueError) as error:self.source.git(self.name,address)
                self.assertIn(self.name,str(error.exception))
                self.assertNotIn('secret-token',str(error.exception))
                self.assertNotIn('://',str(error.exception))
        self.client.projects.get.assert_not_called()
        self.read.assert_not_called()

    def test_mismatched_advertised_ssh_endpoint_does_not_read_commit_or_metadata(self):
        self.project.ssh_url_to_repo='git@git.example.org:another/repo.git'
        with self.assertRaisesRegex(ValueError,'公布的仓库地址'):
            self.source.git(self.name,'ssh://git@git.example.org/group/repo.git#v1')
        self.project.commits.get.assert_not_called();self.read.assert_not_called()

    def test_existing_ssh_dependency_constraint_is_not_ignored(self):
        shared=node('shared',commit='a'*40)
        other_shared=node('shared',commit='b'*40)
        other_shared['target']['identifier']='ssh://git@git.example.org/group/repo.git#v2'
        root=node('root',git={shared['target']['name']:shared['target']['identifier']})
        other=node('other',git={self.name:other_shared['target']['identifier']})
        with self.assertRaisesRegex(ValueError,'Git 依赖版本冲突'):
            resolve(FakeSource(root,[root,shared,other_shared]),{},baseline([installed(other)]),
                    '2022.3.62',mappings([root,shared]),[])
