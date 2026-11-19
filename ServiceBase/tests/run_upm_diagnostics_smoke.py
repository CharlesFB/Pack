"""Compile diagnostic capture and prove local-only secrets survive safely in journals."""
import argparse,json,os,shutil,subprocess,uuid
from pathlib import Path
parser=argparse.ArgumentParser();parser.add_argument('--unity',type=Path,default=Path('/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app'));args=parser.parse_args()
root=Path(__file__).resolve().parent.parent
run=root/'.runtime'/('upm-diagnostics-smoke-'+uuid.uuid4().hex[:8]);project=run/'UnityProject'
scripts=project/'Assets/Editor/UpmExecutor';shutil.copytree(root/'unity/UpmExecutor',scripts)
(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
(project/'Packages/manifest.json').write_text('{"dependencies":{}}')
(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+args.unity.parent.name+'\n')
(scripts/'DiagnosticsSmoke.cs').write_text(r'''using System; using System.IO; using UnityEditor; using UnityEngine;
namespace ServiceBase.Upm {public static class DiagnosticsSmoke {
    static void Require(bool ok,string message) { if(!ok)throw new Exception(message); }
    public static void Run() {
        SessionState.SetBool("ServiceBase.Bridge.Enabled",false);
        var root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        var id=Guid.NewGuid().ToString();
        var j=new Journal {work=new Work {attempt_id=id,target=new Target {name="com.test"}},report=new Report {attempt_id=id,state="failed"},manifest_before=File.ReadAllText(Path.Combine(root,"Packages/manifest.json")),lock_before=File.Exists(Path.Combine(root,"Packages/packages-lock.json"))?File.ReadAllText(Path.Combine(root,"Packages/packages-lock.json")):""};
        string raw="ENOENT scandir /Library/PackageCache/clone/.git/objects/ab https://account:secret-one@host/repo?token=secret-two ssh://git:secret-three@host/repo git@host:secret-four.git\nAuthorization: Bearer secret-five\npassword=secret-six private_token='secret-seven' glpat-secret-eight";
        UpmDiagnostics.Remember(j,"Unknown",raw);
        string message=UpmDiagnostics.Failure(j,root);
        foreach(var secret in new[]{"secret-one","secret-two","secret-three","secret-four","secret-five","secret-six","secret-seven","secret-eight"})Require(!message.Contains(secret),"Credential escaped");
        Require(message.Contains("ENOENT")&&message.Contains(".git/objects/ab")&&message.Contains("与安装前一致"),"Diagnostic lost");
        string path=Path.Combine(root,"diagnostic-journal.json");InstallJournal.Save(path,j);
        var restored=InstallJournal.Load(path);Require(restored.upm_error_message==raw,"Original local diagnostic lost");
        File.AppendAllText(Path.Combine(root,"Packages/manifest.json")," ");
        Require(UpmDiagnostics.Failure(restored,root).Contains("已有变化"),"Changed manifest hidden");
        Require(UpmDiagnostics.PublicMessage(new string('x',20000)).Length<=1201,"Unbounded public message");
        Require(UpmDiagnostics.Detail(new Journal()).Contains("旧执行器"),"Legacy journal unsupported");
        File.WriteAllText(Path.Combine(root,"../result.json"),"{\"compiled\":true,\"original_error_persisted\":true,\"credentials_redacted\":true,\"baseline_difference_reported\":true,\"legacy_journal_supported\":true}");
    }
}}
''')
print('Fixture:',run,flush=True)
result=subprocess.run([str(args.unity/'Contents/MacOS/Unity'),'-batchmode','-nographics','-quit','-projectPath',str(project),'-logFile',str(run/'unity.log'),'-executeMethod','ServiceBase.Upm.DiagnosticsSmoke.Run'],
    env=dict(os.environ,SERVICEBASE_URL='http://127.0.0.1:1',SERVICEBASE_TOKEN=''),timeout=240,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
assert result.returncode==0 and (run/'result.json').exists(),'Unity diagnostics test failed: '+str(run)
print((run/'result.json').read_text(),flush=True)
