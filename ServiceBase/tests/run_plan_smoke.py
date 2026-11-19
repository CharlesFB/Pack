"""Real v7 plan: authenticated HTTPS Git, built-in dependency, plugins, reload/restart.
Uses real package.json files in disposable Git repositories. Only GitLab metadata
transport is replaced with git-show; dependency resolver and all execution are real.
--web-ui waits for the browser to create/confirm the plan against the fixture catalog.
"""
import argparse,hashlib,io,json,os,shutil,subprocess,sys,tarfile,threading,time,uuid,zipfile
from pathlib import Path
from socketserver import ThreadingMixIn
from wsgiref.simple_server import WSGIServer,WSGIRequestHandler,make_server
from unittest.mock import patch
from upm_git_fixture import create_git_fixture
parser=argparse.ArgumentParser();parser.add_argument('--plugin-compile-error',action='store_true');parser.add_argument('--web-ui',action='store_true');parser.add_argument('--keep-ui',action='store_true');parser.add_argument('--timeout',type=int,default=300);args=parser.parse_args()
root=Path(__file__).resolve().parent.parent;run=root/'.runtime'/('plan-smoke-'+uuid.uuid4().hex[:8]);project=run/'UnityProject'
(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
shutil.copytree(root/'unity/UpmExecutor',project/'Assets/Editor/UpmExecutor')
(project/'Packages/manifest.json').write_text('{"dependencies":{}}');(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2022.3.62f2\n')
(project/'Assets/Chosen').mkdir();(project/'Assets/Editor/ReloadProbe.cs').write_text('using System.IO; using UnityEditor; using UnityEngine; [InitializeOnLoad] public static class ReloadProbe { static ReloadProbe() { File.AppendAllText(Path.Combine(Application.dataPath,"../reloads"),"loaded\\n"); } }')
(run/'smoke_settings.py').write_text("from servicebase.settings import *\nDATABASES={'default':{'ENGINE':'django.db.backends.sqlite3','NAME':"+repr(str(run/'db.sqlite3'))+"}}\nPLUGIN_REPOSITORY_ROOT="+repr(str(run/'repository'))+"\nPLUGIN_DELIVERY_ROOT="+repr(str(run/'delivery'))+"\nALLOWED_HOSTS+=['testserver']\n")
sys.path[:0]=[str(run),str(root)];os.environ['DJANGO_SETTINGS_MODULE']='smoke_settings';os.environ['SERVICEBASE_MODE']='local'
import django;django.setup()
from django.core.management import call_command
from django.core.wsgi import get_wsgi_application
from django.contrib.staticfiles.handlers import StaticFilesHandler
from django.core.cache import cache
from django.test import Client
from dashboard.models import UnityProject,ProjectPackageState,InstallJob,CatalogCache
from dashboard.plugin_repository import stage_artifact,write_json
from dashboard.installations import target_from
from dashboard.plan_resolver import Source
from dashboard.catalog import normalize
call_command('migrate',verbosity=0)
repo=run/'repository';repo.mkdir()
def meta(guid,folder=False,script=False):
    return 'fileFormatVersion: 2\nguid: '+guid+'\n'+('folderAsset: yes\n' if folder else '')+('MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n' if script else 'DefaultImporter:\n  externalObjects: {}\n')+'  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
source=run/'test.unitypackage'
plugin_code=b'''using System.IO; using UnityEditor; using UnityEngine;
[InitializeOnLoad] public static class InstalledPlugin { static InstalledPlugin() {
 string root=Application.dataPath+"/Fixture/";
 if(File.Exists(root+"generated.bin"))return;
 File.WriteAllText(root+"generated.bin","extra compiler output");
 File.WriteAllText(root+"data.bytes","rewritten by plugin");
 string meta=root+"data.bytes.meta";
 File.WriteAllText(meta,File.ReadAllText(meta).Replace("userData: ","userData: generated"));
} }'''
if args.plugin_compile_error:plugin_code=b'public class InstalledPlugin { broken syntax here; }'
with tarfile.open(source,'w:gz') as tar:
    for path,guid,content,script in [('Assets/Fixture','1'*32,None,False),('Assets/Fixture/Editor','2'*32,None,False),('Assets/Fixture/data.bytes','4'*32,b'original archived contents',False),('Assets/Fixture/Editor/InstalledPlugin.cs','3'*32,plugin_code,True)]:
        entries={'pathname':path.encode(),'asset.meta':meta(guid,content is None,script).encode()}
        if content is not None:entries['asset']=content
        for name,data in entries.items():
            entry=tarfile.TarInfo(guid+'/'+name);entry.size=len(data);tar.addfile(entry,io.BytesIO(data))
up=stage_artifact(repo,source,dict(id='fixture',name='Reload Plugin'))
zsource=run/'test.zip'
with zipfile.ZipFile(zsource,'w') as z:z.writestr('PlanData/data.bytes',b'plan contents')
zp=stage_artifact(repo,zsource,dict(id='zipfixture',name='ZIP Plan Data'))
gitserver,dep_url,gitenv,stats=create_git_fixture(run,'com.servicebase.plan.dep','1.0.0')
work=run/'git-fixture/source';rootdir=work/'Unity/Root';rootdir.mkdir()
rawroot=dict(name='com.servicebase.plan.root',version='1.0.0',displayName='Plan Root Fixture',dependencies={'com.unity.modules.audio':'1.0.0'},dependenciesGit={'com.servicebase.plan.dep':dep_url})
(rootdir/'package.json').write_text(json.dumps(rawroot));(rootdir/'Editor').mkdir()
(rootdir/'Editor/DependsOnImportedPlugin.cs').write_text('public static class DependsOnImportedPlugin { public static System.Type PluginType = typeof(InstalledPlugin); }')
subprocess.run(['git','-C',str(work),'add','.'],check=True);subprocess.run(['git','-C',str(work),'-c','user.name=Smoke','-c','user.email=smoke@example.invalid','commit','-qm','Root requiring plugin and dependency'],check=True)
sha=subprocess.check_output(['git','-C',str(work),'rev-parse','HEAD'],text=True).strip();subprocess.run(['git','-C',str(work),'push','-q',str(run/'git-fixture/fixture.git'),'HEAD:refs/heads/main'],check=True)
root_url=dep_url.split('?')[0]+'?path=/Unity/Root#'+sha
nodes={}
for url in (dep_url,root_url):
    prefix,commit=url.rsplit('#',1);path=prefix.split('?path=')[1].strip('/')+'/package.json'
    raw=subprocess.check_output(['git','-C',str(work),'show',commit+':'+path]);value=json.loads(raw)
    target=target_from(dict(name=value['name'],identifier=url));target['version']=value['version'];nodes[url]=dict(target=target,raw=value,sha=hashlib.sha256(raw).hexdigest())
rows=[]
for url,item in ((dep_url,up),(root_url,zp)):
    n=nodes[url];rows.append(dict(key=n['target']['name']+'@'+n['target']['commit'],package_name=n['target']['name'],package_version='1.0.0',commit=n['target']['commit'],package_json_sha256=n['sha'],plugins=[dict(plugin_id=item['plugin_id'],version=item['version'],build_id=item['build_id'],source_path='fixture.'+('unitypackage' if item==up else 'zip'),order=0)]))
write_json(repo/'index.json',dict(schema_version=1,plugins=[up,zp],package_mappings=rows))
class FixtureSource(Source):
    def root(self,data):return self.git('com.servicebase.plan.root',root_url)
    def git(self,name,address):
        assert address in nodes and nodes[address]['target']['name']==name
        return nodes[address]
patches=[patch('dashboard.installation_plans.Source',FixtureSource),patch('dashboard.catalog.source',return_value=('plan-fixture','https://unused.example','fixture-only','')),patch('dashboard.catalog.blacklist_settings',return_value=(set(),False))]
for p in patches:p.start()
CatalogCache.objects.create(key='plan-fixture',packages=normalize({'Packages':[dict(PackageName='root',ProjectId=1,Tags=['root-1.0.0'])]},{'PackageCache':{'1':{'root-1.0.0':rawroot}}}),revision=sha)
application=StaticFilesHandler(get_wsgi_application());dropped=False

def app(env,start):
    global dropped
    if env['PATH_INFO']=='/api/bridge/upm/sync/':
        raw=env['wsgi.input'].read(int(env.get('CONTENT_LENGTH') or 0));env['wsgi.input']=io.BytesIO(raw);data=json.loads(raw);report=data.get('report') or {}
        if report.get('state') in ('succeeded','failed') and not (run/'restarted').exists():
            captured=[];result=application(env,lambda status,headers,exc_info=None:captured.append((status,headers)));b''.join(result)
            if hasattr(result,'close'):result.close()
            if captured[0][0].startswith('200'):
                dropped=True;(run/'ack-dropped').touch();start('503 Service Unavailable',[('Content-Type','application/json')]);return [b'{}']
            start(*captured[0]);return []
    return application(env,start)
class Server(ThreadingMixIn,WSGIServer):daemon_threads=True
class Quiet(WSGIRequestHandler):
    def log_message(self,*args):pass
server=make_server('127.0.0.1',0,app,server_class=Server,handler_class=Quiet);threading.Thread(target=server.serve_forever,daemon=True).start()
url='http://127.0.0.1:'+str(server.server_port);env=dict(os.environ,SERVICEBASE_URL=url,SERVICEBASE_TOKEN='',**gitenv);process=None
client=Client()
def start_unity(log):
    return subprocess.Popen(['/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity','-batchmode','-nographics','-projectPath',str(project),'-logFile',str(run/log)],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
def wait(check,seconds=None):
    deadline=time.time()+(seconds or args.timeout)
    while time.time()<deadline:
        result=check()
        if result:return result
        if process.poll() is not None:raise AssertionError('Unity exited: '+str(run))
        time.sleep(.5)
    raise TimeoutError(str(run))
def post(path,body,status):
    r=client.post(path,body,content_type='application/json');assert r.status_code==status,r.content;return r.json()
try:
    process=start_unity('unity.log');print('Web URL:',url,flush=True)
    wait(lambda:ProjectPackageState.objects.filter(payload__error='').exists())
    current=UnityProject.objects.first();base='/api/projects/'+current.pk+'/installation-plans/';jobs='/api/projects/'+current.pk+'/installations/'
    if args.web_ui:
        print('Browser ready: preview Plan Root Fixture and choose Assets/Chosen for ZIP.',flush=True)
        job=wait(lambda:InstallJob.objects.filter(target__operation='plan').first(),600)
    else:
        p=post(base,dict(root={'catalog_id':'1:root','tag':'root-1.0.0'},request_key=str(uuid.uuid4())),201)['plan'];assert not p['errors'],p
        selection=[dict(key=r['key'],selected=True,destination_root='Assets/Chosen' if r['format']=='zip' else '') for package in p['packages'] for r in package['plugins']]
        body=dict(digest=p['digest'],plugins=selection);checked=post(base+p['id']+'/selection/',body,200);body['selection_digest']=checked['selection_digest']
        job=InstallJob.objects.get(pk=post(base+p['id']+'/confirm/',body,202)['job']['id'])
    print('Plan:',str(job.pk),flush=True)
    wait(lambda:(run/'ack-dropped').exists());job.refresh_from_db()
    assert job.steps.get(position=0).state==('skipped' if args.plugin_compile_error else 'succeeded')
    assert all(s.attempt.dispatched_at is None for s in job.steps.filter(position__gt=0))
    post(jobs+str(job.pk)+'/control/',{'action':'pause'},200)
    if args.plugin_compile_error:
        # Keep the already loaded executor alive during compiler errors. A fresh
        # Unity process cannot load source assemblies while the project is broken.
        (run/'restarted').touch()
    else:
        process.kill();process.wait();cache.clear();(run/'restarted').touch();process=start_unity('restart.log')
    first=job.steps.get(position=0);wait(lambda:(project/'.servicebase/upm/history'/str(first.attempt.pk)).with_suffix('.json').exists())
    time.sleep(4);assert all(s.attempt.dispatched_at is None for s in job.steps.filter(position__gt=0))
    post(jobs+str(job.pk)+'/control/',{'action':'resume'},200)
    def done():
        job.refresh_from_db()
        if job.step.state in ('failed','needs_review'):raise AssertionError(job.step.attempt.report)
        return not job.active
    wait(done)
    steps=list(job.steps.order_by('position'));assert all(s.state=='succeeded' or (args.plugin_compile_error and s.command.get('operation')=='plugin' and s.state=='skipped') for s in steps)
    assert steps[-1].command['operation']=='plan_verify'
    if not args.plugin_compile_error:assert (project/'Assets/Chosen/PlanData/data.bytes').read_bytes()==b'plan contents'
    assert (project/'Assets/Fixture/Editor/InstalledPlugin.cs').exists()
    logs=''.join(p.read_text(errors='replace') for p in (run/'unity.log',run/'restart.log') if p.exists())
    assert logs.count('[ServiceBase UPM] Start Add ')==3
    assert logs.count('[ServiceBase Plugin] Start Import ')==(1 if args.plugin_compile_error else 2)
    if not args.plugin_compile_error:
        assert (run/'restart.log').read_text().count('[ServiceBase Plugin] Start Import ')==1
        assert (project/'Assets/Fixture/data.bytes').read_text()=='rewritten by plugin'
        records=[json.loads(p.read_text()) for p in (project/'.servicebase/plugins/records').glob('*.json')]
        record=next(r for r in records if r['plugin_id']=='fixture')
        assert record['verification']=='archive-execution-v1'
        assert not any(f['path'].endswith('generated.bin') for f in record['files'])
    proof=dict(steps=[dict(name=s.command['name'],operation=s.command.get('operation','upm'),state=s.state) for s in steps],lost_ack_recovered=True,lost_ack_then_editor_restart=not args.plugin_compile_error,compile_errors_skipped=args.plugin_compile_error,generated_output_ignored=not args.plugin_compile_error,paused_no_new_dispatch=True,git_authenticated=stats['authorized']>0,add_calls=3,final_verified=steps[-1].attempt.report['evidence']['verified'],domain_loads=(project/'reloads').read_text().count('loaded'),web_ui=args.web_ui)
    (run/'result.json').write_text(json.dumps(proof,indent=2));print(json.dumps(proof),flush=True)
    if args.web_ui or args.keep_ui:
        print('Review completed UI; create '+str(run/'close-ui')+' to close.',flush=True)
        deadline=time.time()+300
        while time.time()<deadline and not (run/'close-ui').exists():time.sleep(1)
finally:
    if process and process.poll() is None:
        process.terminate()
        try:process.wait(20)
        except subprocess.TimeoutExpired:process.kill();process.wait()
    server.shutdown();server.server_close();gitserver.shutdown();gitserver.server_close()
    for p in patches:p.stop()
    print('Artifacts:',run,flush=True)
