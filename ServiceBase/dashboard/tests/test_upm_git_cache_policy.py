import os
from pathlib import Path
import subprocess
import tempfile
from django.test import SimpleTestCase
from tools.configure_upm_git_cache import configure


class GitCachePolicyTests(SimpleTestCase):
    def test_only_selected_projects_upm_temporary_clone_is_affected_and_removal_is_precise(self):
        with tempfile.TemporaryDirectory(prefix='git-policy test ') as d:
            root=Path(d);project=root/'Unity';(project/'Packages').mkdir(parents=True)
            (project/'Packages/manifest.json').write_text('{"dependencies":{}}')
            config=root/'global';config.write_text('[user]\n name = fixture\n[maintenance]\n auto = true\n[gc]\n auto = 6700\n')
            repositories=[project/'Library/PackageCache/.tmp-123-abc/clone',project/'ordinary',root/'Other/Library/PackageCache/.tmp-123-abc/clone']
            env=dict(os.environ,GIT_CONFIG_GLOBAL=str(config),GIT_CONFIG_NOSYSTEM='1')
            for repo in repositories:
                repo.mkdir(parents=True);subprocess.run(['git','init','--quiet',str(repo)],env=env,check=True,capture_output=True)
            def value(repo,key):return subprocess.check_output(['git','-C',str(repo),'config','--get',key],env=env,text=True).strip()
            configure(project,config,'apply');first=config.read_text();configure(project,config,'apply')
            self.assertEqual(first,config.read_text())
            self.assertEqual(value(repositories[0],'maintenance.auto'),'false')
            self.assertEqual(value(repositories[0],'gc.auto'),'0')
            for repo in repositories[1:]:
                self.assertEqual(value(repo,'maintenance.auto'),'true');self.assertEqual(value(repo,'gc.auto'),'6700')
            configure(project,config,'remove')
            self.assertEqual(value(repositories[0],'maintenance.auto'),'true')
            self.assertEqual(value(repositories[0],'user.name'),'fixture')
