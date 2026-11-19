"""Diagnose one Git Add in an isolated project, without ServiceBase or MTP code.

The input target must be a credential-free HTTPS URL pinned to a commit. Git
credentials stay with the existing local helper. Git tracing is local-only.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import time
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--unity', type=Path, required=True)
parser.add_argument('--project', type=Path, required=True, help='Read-only manifest baseline')
parser.add_argument('--target', required=True)
parser.add_argument('--name', required=True)
parser.add_argument('--no-auto-gc', action='store_true')
parser.add_argument('--cache-only-policy', action='store_true')
parser.add_argument('--timeout', type=int, default=240)
args = parser.parse_args()
from urllib.parse import urlsplit
uri = urlsplit(args.target)
assert uri.scheme == 'https' and not uri.username and not uri.password and re.fullmatch('[a-f0-9]{40}', uri.fragment)
root = Path(__file__).resolve().parent.parent
run = root / '.runtime' / ('direct-git-probe-' + uuid.uuid4().hex[:8])
project = run / 'UnityProject'
(project/'Assets/Editor').mkdir(parents=True)
(project/'Packages').mkdir()
(project/'ProjectSettings').mkdir()
manifest = json.loads((args.project/'Packages/manifest.json').read_text())
assert args.name not in manifest['dependencies'], 'Choose an uninstalled target'
(project/'Packages/manifest.json').write_text(json.dumps(manifest, indent=2))
(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+args.unity.parent.name+'\n')
(run/'input.json').write_text(json.dumps(dict(identifier=args.target, name=args.name, commit=uri.fragment)))
(project/'Assets/Editor/DirectGitProbe.cs').write_text(r'''using System; using System.IO; using System.Linq;
using UnityEditor; using UnityEditor.PackageManager; using UnityEditor.PackageManager.Requests; using UnityEngine;
[InitializeOnLoad] public static class DirectGitProbe {
    [Serializable] class Input { public string identifier,name,commit; }
    [Serializable] class Result { public string state,message,commit; public int code; public bool present; }
    static AddRequest request; static double started;
    static string Folder { get { return Path.GetFullPath(Path.Combine(Application.dataPath,"../..")); } }
    static DirectGitProbe() { started=EditorApplication.timeSinceStartup; EditorApplication.update+=Tick; }
    public static void Begin() {
        if(File.Exists(Path.Combine(Folder,"intent")))return;
        var input=JsonUtility.FromJson<Input>(File.ReadAllText(Path.Combine(Folder,"input.json")));
        File.WriteAllText(Path.Combine(Folder,"intent"),DateTime.UtcNow.ToString("o"));
        File.AppendAllText(Path.Combine(Folder,"calls"),"Client.Add\n");
        request=Client.Add(input.identifier);
    }
    static void Finish(Result result) { File.WriteAllText(Path.Combine(Folder,"result.json"),JsonUtility.ToJson(result,true)); EditorApplication.Exit(0); }
    static void Tick() {
        if(!File.Exists(Path.Combine(Folder,"intent")) || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        var input=JsonUtility.FromJson<Input>(File.ReadAllText(Path.Combine(Folder,"input.json")));
        var info=UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p=>p.name==input.name);
        if(request!=null && request.IsCompleted && request.Status==StatusCode.Failure) {
            Finish(new Result { state="failed",code=request.Error==null?-1:(int)request.Error.errorCode,message=request.Error==null?"missing Error":request.Error.message,present=info!=null }); return;
        }
        if(info!=null && info.source==PackageSource.Git && info.git!=null && info.git.hash==input.commit) {
            Finish(new Result {state="succeeded",commit=info.git.hash,present=true});return;
        }
        if(EditorApplication.timeSinceStartup-started>200)Finish(new Result {state="unknown",message="No result after 200 seconds",present=info!=null});
    }
}
''')
trace=run/'git-trace.jsonl';trace.touch(mode=0o600)
env=dict(os.environ, GIT_TRACE2_EVENT=str(trace))
if args.cache_only_policy:
    # An isolated global config includes the user's existing credential configuration;
    # the new maintenance override matches only this disposable project's UPM clones.
    import sys
    sys.path.insert(0,str(root))
    from tools.configure_upm_git_cache import configure
    config=run/'gitconfig';config.touch(mode=0o600)
    originals=[Path(os.environ.get('XDG_CONFIG_HOME',str(Path.home()/'.config')))/'git/config',Path.home()/'.gitconfig']
    for original in originals:
        if original.is_file():subprocess.run(['git','config','--file',str(config),'--add','include.path',str(original)],check=True)
    configure(project,config,'apply')
    env['GIT_CONFIG_GLOBAL']=str(config)
if args.no_auto_gc:
    env.update(GIT_CONFIG_COUNT='2',GIT_CONFIG_KEY_0='gc.auto',GIT_CONFIG_VALUE_0='0',
               GIT_CONFIG_KEY_1='maintenance.auto',GIT_CONFIG_VALUE_1='false')
print('Probe:',run, 'auto_gc_disabled:',args.no_auto_gc,flush=True)
process=subprocess.Popen([str(args.unity/'Contents/MacOS/Unity'),'-batchmode','-nographics',
    '-projectPath',str(project),'-logFile',str(run/'unity.log'),'-executeMethod','DirectGitProbe.Begin'],env=env,
    stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
try:
    process.wait(args.timeout)
finally:
    if process.poll() is None:
        process.terminate()
        try:process.wait(15)
        except subprocess.TimeoutExpired:process.kill();process.wait()
f=run/'result.json'
if not f.exists():raise RuntimeError('Unity probe did not produce a result; inspect '+str(run))
result=json.loads(f.read_text())
message=result.get('message','')
message=re.sub(r'(?i)(https?://)[^/\s@]+@',r'\1[redacted]@',message)
message=re.sub(r'(?i)(token|password|authorization)([\"\s:=]+)[^\s,;]+',r'\1\2[redacted]',message)
result['message']=message
result['calls']=(run/'calls').read_text().count('Client.Add')
result['auto_gc_disabled']=args.no_auto_gc
result['cache_only_policy']=args.cache_only_policy
(run/'summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps(result,ensure_ascii=False),flush=True)
