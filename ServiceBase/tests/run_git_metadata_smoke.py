"""Compile the independent executor and verify SSH metadata in a disposable Unity project."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid

root=Path(__file__).resolve().parent.parent
run=root/'.runtime'/('git-metadata-smoke-'+uuid.uuid4().hex[:8])
project=run/'UnityProject'
scripts=project/'Assets/Editor/UpmExecutor'
shutil.copytree(root/'unity/UpmExecutor',scripts)
(project/'Packages').mkdir();(project/'ProjectSettings').mkdir();(project/'Assets/Local').mkdir()
(project/'Packages/manifest.json').write_text('{"dependencies":{}}')
(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2022.3.62f2\n')
links={
    'com.test.https':'https://git.example.org/group/repo.git#v1',
    'com.test.ssh':'ssh://git@git.example.org/group/repo.git#v1',
    'com.test.scp':'git@git.example.org:group/repo.git#v1',
    'com.test.secret':'ssh://git:must-not-upload@git.example.org/group/repo.git#v1',
    'com.test.user':'ssh://must-not-upload@git.example.org/group/repo.git#v1',
}
(project/'Assets/Local/package.json').write_text(json.dumps(dict(name='com.test.local',version='1.0.0',dependenciesGit=links)))
(scripts/'GitMetadataSmoke.cs').write_text('''using System; using System.IO; using System.Linq; using UnityEditor; using UnityEngine;
namespace ServiceBase.Upm {
public static class GitMetadataSmoke {
    public static void Run() {
        SessionState.SetBool("ServiceBase.Bridge.Enabled",false);
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        var snapshot=ProjectPackageState.Read(root);
        var deps=snapshot.assets_packages.Single(p=>p.name=="com.test.local").dependencies.ToDictionary(d=>d.name,d=>d.version);
        if(deps["com.test.ssh"]!="ssh://git@git.example.org/group/repo.git#v1" || deps["com.test.scp"]!="git@git.example.org:group/repo.git#v1" || deps["com.test.https"]!="https://git.example.org/group/repo.git#v1")throw new Exception("Valid Git metadata was lost");
        if(deps["com.test.secret"]!="" || deps["com.test.user"]!="" || JsonUtility.ToJson(snapshot).Contains("must-not-upload"))throw new Exception("Credential metadata escaped");
        File.WriteAllText(Path.Combine(root,"../result.json"),"{\\"compiled\\":true,\\"ssh_preserved\\":true,\\"scp_preserved\\":true,\\"credential_links_removed\\":true}");
    }
}}
''')
print('Fixture:',run,flush=True)
env=dict(os.environ,SERVICEBASE_URL='http://127.0.0.1:1',SERVICEBASE_TOKEN='')
result=subprocess.run(['/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity',
    '-batchmode','-nographics','-quit','-projectPath',str(project),'-logFile',str(run/'unity.log'),
    '-executeMethod','ServiceBase.Upm.GitMetadataSmoke.Run'],env=env,timeout=240,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
assert result.returncode==0 and (run/'result.json').is_file(),'Unity verification failed: '+str(run)
print((run/'result.json').read_text(),flush=True)
