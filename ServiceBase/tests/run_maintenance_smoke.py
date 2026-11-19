"""Real v8 maintenance: authenticated HTTPS Git, built-in dependency, plugins, reload/restart.
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
parser=argparse.ArgumentParser();parser.add_argument('--web-ui',action='store_true');parser.add_argument('--partial-update',action='store_true');parser.add_argument('--keep-ui',action='store_true');parser.add_argument('--timeout',type=int,default=300);args=parser.parse_args()
root=Path(__file__).resolve().parent.parent;run=root/'.runtime'/('maintenance-smoke-'+uuid.uuid4().hex[:8]);project=run/'UnityProject'
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

if args.partial_update:
    updater=project/'Assets/Editor/UpmExecutor/PluginUpdater.cs'
    updater.write_text(updater.read_text().replace('else CopyChecked(source,destination,f.sha256);','else { CopyChecked(source,destination,f.sha256); if (File.Exists(Path.Combine(root,"inject-update-failure"))) throw new IOException("Injected partial update"); }'))
def unity_plugin(version):
    source=run/('plugin-'+str(version)+'.unitypackage')
    files=[('Assets/Fixture','1'*32,None,False),('Assets/Fixture/Editor','2'*32,None,False),('Assets/Fixture/Editor/InstalledPlugin.cs','3'*32,('using UnityEditor; [InitializeOnLoad] public static class InstalledPlugin { public const int Version = '+str(version)+'; static InstalledPlugin() {} }').encode(),True)]
    files.append(('Assets/Fixture/'+('obsolete' if version==1 else 'new')+'.bytes','4'*32,b'contents',False))
    with tarfile.open(source,'w:gz') as tar:
        for path,guid,content,script in files:
            entries={'pathname':path.encode(),'asset.meta':meta(guid,content is None,script).encode()}
            if content is not None:entries['asset']=content
            for name,data in entries.items():
                entry=tarfile.TarInfo(guid+'/'+name);entry.size=len(data);tar.addfile(entry,io.BytesIO(data))
    return stage_artifact(repo,source,dict(id='fixture',name='Maintenance Plugin',version=str(version)+'.0.0'))
def zip_plugin(version,shared=False):
    source=run/('zip-'+str(version)+('-shared' if shared else '')+'.zip')
    contents={'Data/shared.bytes':b'shared'} if shared else {'Data/data.bytes':str(version).encode(),'Data/existing.bytes':b'preexisting'}
    if not shared:contents.update({'Data/obsolete.bytes':b'old','Data/modified.bytes':b'original','Data/shared.bytes':b'shared'} if version==1 else {'Data/new.bytes':b'new'})
    with zipfile.ZipFile(source,'w') as z:
        for path,data in contents.items():z.writestr(path,data)
    return stage_artifact(repo,source,dict(id='shared' if shared else 'zipfixture',name='Shared' if shared else 'ZIP Maintenance',version=str(version)+'.0.0'))
up1,up2=unity_plugin(1),unity_plugin(2);zp1,zp2,shared=zip_plugin(1),zip_plugin(2),zip_plugin(1,True)
existing=project/'Assets/Chosen/Data';existing.mkdir();(existing/'existing.bytes').write_bytes(b'preexisting')
gitserver,dep_url,gitenv,stats=create_git_fixture(run,'com.servicebase.plan.dep','1.0.0')
work=run/'git-fixture/source';nodes={};gitbase=dep_url.split('?')[0]
def publish(folder,name,version='1.0.0',deps=None,unitydeps=None,script=False):
    directory=work/'Unity'/folder;directory.mkdir(exist_ok=True)
    raw=dict(name=name,version=version,displayName=name,dependenciesGit=deps or {},dependencies=unitydeps or {})
    (directory/'package.json').write_text(json.dumps(raw))
    if script:
        (directory/'Editor').mkdir(exist_ok=True);(directory/'Editor/DependsOnImportedPlugin.cs').write_text('public static class DependsOnImportedPlugin { public static int Version = InstalledPlugin.Version; }')
    for path in (work/'Unity').rglob('*'):
        if path.name.endswith('.meta'):continue
        sidecar=Path(str(path)+'.meta')
        if not sidecar.exists():sidecar.write_text(meta(hashlib.md5(str(path.relative_to(work)).encode()).hexdigest(),path.is_dir(),path.suffix=='.cs'))
    subprocess.run(['git','-C',str(work),'add','.'],check=True);subprocess.run(['git','-C',str(work),'-c','user.name=Smoke','-c','user.email=smoke@example.invalid','commit','-qm',name+' '+version],check=True)
    sha=subprocess.check_output(['git','-C',str(work),'rev-parse','HEAD'],text=True).strip();subprocess.run(['git','-C',str(work),'push','-q',str(run/'git-fixture/fixture.git'),'HEAD:refs/heads/main'],check=True)
    return gitbase+'?path=/Unity/'+folder+'#'+sha
stale=publish('Stale','com.servicebase.plan.stale');fresh=publish('New','com.servicebase.plan.new')
other=publish('Other','com.servicebase.plan.other',deps={'com.servicebase.plan.dep':dep_url})
root1=publish('Root','com.servicebase.plan.root',deps={'com.servicebase.plan.dep':dep_url,'com.servicebase.plan.stale':stale},unitydeps={'com.unity.modules.audio':'1.0.0'},script=True)
root2=publish('Root','com.servicebase.plan.root','2.0.0',deps={'com.servicebase.plan.new':fresh},unitydeps={'com.unity.modules.audio':'1.0.0'},script=True)
for url in (dep_url,stale,fresh,other,root1,root2):
    prefix,commit=url.rsplit('#',1);path=prefix.split('?path=')[1].strip('/')+'/package.json'
    raw=subprocess.check_output(['git','-C',str(work),'show',commit+':'+path]);value=json.loads(raw)
    target=target_from(dict(name=value['name'],identifier=url));target['version']=value['version'];nodes[url]=dict(target=target,raw=value,sha=hashlib.sha256(raw).hexdigest())
rows=[]
for url,n in nodes.items():
    item=up1 if url==root1 else up2 if url==root2 else None
    rows.append(dict(key=n['target']['name']+'@'+n['target']['commit'],package_name=n['target']['name'],package_version=n['target']['version'],commit=n['target']['commit'],package_json_sha256=n['sha'],plugins=[dict(plugin_id=item['plugin_id'],version=item['version'],build_id=item['build_id'],source_path='fixture.unitypackage',order=0)] if item else []))
write_json(repo/'index.json',dict(schema_version=1,plugins=[up1,up2,zp1,zp2,shared],package_mappings=rows))
class FixtureSource(Source):
    def root(self,data):return nodes[data.get('identifier',root2 if data.get('tag')=='2.0.0' else root1)]
    def git(self,name,address):
        assert address in nodes and nodes[address]['target']['name']==name
        return nodes[address]
patches=[patch('dashboard.installation_plans.Source',FixtureSource),patch('dashboard.catalog.source',return_value=('maintenance-fixture','https://unused.example','fixture-only','')),patch('dashboard.catalog.blacklist_settings',return_value=(set(),False))]
for p in patches:p.start()
CatalogCache.objects.create(key='maintenance-fixture',packages=normalize({'Packages':[dict(PackageName='root',ProjectId=1,Tags=['1.0.0','2.0.0'])]},{'PackageCache':{'1':{'1.0.0':nodes[root1]['raw'],'2.0.0':nodes[root2]['raw']}}}),revision='fixture')
application=StaticFilesHandler(get_wsgi_application())
def app(env,start):
    if env['PATH_INFO']=='/api/bridge/plugins/update-receipt/' and (run/'drop-update-ack').exists():
        captured=[];result=application(env,lambda status,headers,exc_info=None:captured.append((status,headers)));body=b''.join(result)
        if hasattr(result,'close'):result.close()
        if captured[0][0].startswith('200'):
            (run/'ack-saved').touch();start('503 Service Unavailable',[('Content-Type','application/json')]);return [b'{}']
        start(*captured[0]);return [body]
    return application(env,start)
class Server(ThreadingMixIn,WSGIServer):daemon_threads=True
class Quiet(WSGIRequestHandler):
    def log_message(self,*args):pass
server=make_server('127.0.0.1',0,app,server_class=Server,handler_class=Quiet);threading.Thread(target=server.serve_forever,daemon=True).start()
url='http://127.0.0.1:'+str(server.server_port);env=dict(os.environ,SERVICEBASE_URL=url,SERVICEBASE_TOKEN='',**gitenv);process=None;client=Client();proof={};completed=[]
def start_unity(log):
    return subprocess.Popen(['/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity','-batchmode','-nographics','-projectPath',str(project),'-logFile',str(run/log)],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
def wait(check,seconds=None):
    deadline=time.time()+(seconds or args.timeout)
    while time.time()<deadline:
        value=check()
        if value:return value
        if process.poll() is not None:raise AssertionError('Unity exited: '+str(run))
        time.sleep(.5)
    raise TimeoutError(str(run))
def post(path,body,status=200):
    response=client.post(path,body,content_type='application/json');assert response.status_code==status,response.content;return response.json()
def finish(job):
    def done():
        job.refresh_from_db()
        if job.step.state in ('failed','needs_review'):raise AssertionError(job.step.attempt.report)
        return not job.active
    wait(done);completed.append(dict(id=str(job.pk),steps=[dict(name=s.command['name'],operation=s.command.get('operation','upm'),state=s.state) for s in job.steps.order_by('position')]))
    fresh_state()
    print('Finished',job.target.get('plan_action',job.target.get('operation')),job.target['name'],flush=True)
def fresh_state():
    def ready():
        p=UnityProject.objects.get(pk=current.pk)
        from dashboard.package_state import current as package_current
        from dashboard.plugin_inventory import current as plugin_current
        try:
            state=package_current(p);inventory=plugin_current(p)
            active=project/'.servicebase/upm/active.json'
            return not active.exists() and inventory['fresh'] and not inventory['scanning'] and state
        except ValueError:return False
    wait(ready)
def plan(root,updates=False,cleanup=False):
    fresh_state();p=post(base,dict(root=root,request_key=str(uuid.uuid4())),201)['plan'];assert not p['errors'],p
    choices=[]
    for row in [r for pkg in p['packages'] for r in pkg['plugins']]+p.get('cleanup_plugins',[]):
        choice=dict(key=row['key'],selected=cleanup if row.get('action')=='remove' else True,destination_root=row.get('destination_root',''))
        if updates and row.get('update_options'):choice['replace_record']=row['update_options'][0]['record_key'];choice['destination_root']=row['update_options'][0]['destination_root']
        choices.append(choice)
    body=dict(digest=p['digest'],plugins=choices);checked=post(base+p['id']+'/selection/',body);body['selection_digest']=checked['selection_digest']
    job=InstallJob.objects.get(pk=post(base+p['id']+'/confirm/',body,202)['job']['id']);return job,p
try:
    process=start_unity('unity.log');print('Web URL:',url,'Artifacts:',run,flush=True)
    wait(lambda:ProjectPackageState.objects.filter(payload__error='').exists());current=UnityProject.objects.first();base='/api/projects/'+current.pk+'/installation-plans/';jobs='/api/projects/'+current.pk+'/installations/'
    job,_=plan(dict(identifier=root1));finish(job)
    job,_=plan(dict(identifier=other));finish(job)
    job,preview=plan(dict(identifier=root2),updates=True);finish(job)
    assert [r['name'] for r in preview['removals']]==['com.servicebase.plan.stale'],preview
    assert any(r['name']=='com.servicebase.plan.dep' for r in preview['retained_dependencies'])
    assert b'Version = 2' in (project/'Assets/Fixture/Editor/InstalledPlugin.cs').read_bytes()
    assert not (project/'Assets/Fixture/obsolete.bytes').exists() and (project/'Assets/Fixture/new.bytes').exists()
    proof.update(unitypackage_update=True,guid_move_preserved=True,stale_dependency_removed=True,shared_dependency_preserved=True)
    job,_=plan(dict(operation='uninstall',name='com.servicebase.plan.other'));finish(job)
    job,_=plan(dict(operation='uninstall',name='com.servicebase.plan.root'),cleanup=True);finish(job)
    assert json.loads((project/'Packages/manifest.json').read_text())['dependencies']=={}
    assert not (project/'Assets/Fixture').exists();proof.update(upm_uninstall=True,optional_old_plugin_cleanup=True)
    for item in (zp1,shared):
        fresh_state();job=InstallJob.objects.get(pk=post(jobs,dict(operation='plugin',plugin_id=item['plugin_id'],build_id=item['build_id'],destination_root='Assets/Chosen',request_key=str(uuid.uuid4())),202)['job']['id']);finish(job)
    data=project/'Assets/Chosen/Data';before_meta=(data/'data.bytes.meta').read_bytes();(data/'modified.bytes').write_bytes(b'user changes');(data/'extra.bytes').write_bytes(b'user extra')
    from dashboard.models import PluginInventory
    post('/api/projects/'+current.pk+'/plugins/refresh/',{},202);fresh_state()
    old=next(r for r in client.get('/api/projects/'+current.pk+'/plugins/').json()['installations'] if r['plugin_id']=='zipfixture')
    if args.partial_update:(project/'inject-update-failure').touch()
    else:(run/'drop-update-ack').touch()
    if args.web_ui:
        print('Browser: choose ZIP Maintenance 2.0.0 and preview/update old record.',flush=True)
        (run/'browser-ready').touch();job=wait(lambda:InstallJob.objects.filter(target__plan_action='plugin_update').first(),600)
    else:job,_=plan(dict(operation='plugin_update',plugin_id='zipfixture',build_id=zp2['build_id'],record_key=old['record_key'],record_digest=old['record_digest']))
    if args.partial_update:
        def stopped():job.refresh_from_db();return job.step.state=='needs_review'
        wait(stopped);assert job.active
        sequence=job.step.attempt.sequence
        post(jobs+str(job.pk)+'/recheck/',{},200)
        def rechecked():job.refresh_from_db();return job.step.state=='needs_review' and job.step.attempt.sequence>sequence+1
        wait(rechecked);assert job.active
        assert list((project/'.servicebase/plugins/updates').rglob('backup/Assets/Chosen/Data/data.bytes'))
        assert (data/'modified.bytes').read_bytes()==b'user changes'
        assert (run/'unity.log').read_text().count('[ServiceBase Plugin] Start Update ')==2
        proof.update(partial_update_keeps_lock=True,read_only_recheck=True,backup_available=True)
    else:
        wait(lambda:(run/'ack-saved').exists());process.kill();process.wait();cache.clear();(run/'drop-update-ack').unlink();process=start_unity('restart.log');finish(job)
        assert (data/'data.bytes').read_bytes()==b'2' and (data/'data.bytes.meta').read_bytes()==before_meta
        assert not (data/'obsolete.bytes').exists() and (data/'new.bytes').exists()
        assert (data/'modified.bytes').read_bytes()==b'user changes' and (data/'shared.bytes').read_bytes()==b'shared'
        assert (data/'existing.bytes').read_bytes()==b'preexisting' and (data/'extra.bytes').read_bytes()==b'user extra'
        logs=(run/'unity.log').read_text()+(run/'restart.log').read_text();assert logs.count('[ServiceBase Plugin] Start Update ')==2
        assert list((project/'.servicebase/plugins/updates').rglob('backup/Assets/Chosen/Data/data.bytes'))
        proof.update(zip_update=True,generated_meta_preserved=True,modified_shared_preexisting_extra_preserved=True,old_files_cleaned=True,backup_available=True,lost_update_receipt_ack_restart=True,update_calls=2)
    proof['completed']=completed;proof['domain_loads']=(project/'reloads').read_text().count('loaded');proof['web_ui']=args.web_ui
    (run/'result.json').write_text(json.dumps(proof,indent=2));print(json.dumps(proof),flush=True)
    if args.keep_ui:
        print('Create '+str(run/'close-ui')+' to close.',flush=True)
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
