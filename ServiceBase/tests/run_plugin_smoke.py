"""Real single-plugin imports in a disposable Unity project and database."""
import argparse,io,json,os,shutil,subprocess,sys,tarfile,threading,time,uuid,zipfile
from pathlib import Path
from socketserver import ThreadingMixIn
from wsgiref.simple_server import WSGIServer,WSGIRequestHandler,make_server
parser=argparse.ArgumentParser();parser.add_argument('--unity',type=Path,default=Path('/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app'));parser.add_argument('--timeout',type=int,default=240)
parser.add_argument('--edm',action='store_true');parser.add_argument('--drop-callbacks',action='store_true')
parser.add_argument('--remove-partial',action='store_true');parser.add_argument('--remove-restart',action='store_true');parser.add_argument('--management',action='store_true');parser.add_argument('--management-web',action='store_true');parser.add_argument('--web-ui',action='store_true');parser.add_argument('--choice-restart',action='store_true');parser.add_argument('--partial',action='store_true');args=parser.parse_args();root=Path(__file__).resolve().parent.parent
run=root/'.runtime'/('plugin-smoke-'+uuid.uuid4().hex[:8]);project=run/'UnityProject'
(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
shutil.copytree(root/'unity/UpmExecutor',project/'Assets/Editor/UpmExecutor')
if args.drop_callbacks:
    importer=project/'Assets/Editor/UpmExecutor/PluginImporter.cs'
    code=importer.read_text().replace('if (!importing || current == null) return;', 'if (state != "test-never-delivered" || !importing || current == null) return;')
    # Also simulate a lost callback with no domain reload (ZIP keeps this static flag).
    code=code.replace('else journal.plugin.callback="completed";', 'else { importing=true; journal.plugin.callback=""; }')
    importer.write_text(code)
if args.remove_partial:
    # Fault injection only in the disposable copy: interrupt after the first owned file moves.
    remover=project/'Assets/Editor/UpmExecutor/PluginRemover.cs'
    code=remover.read_text().replace('File.Move(path+".meta",backup+".meta");','File.Move(path+".meta",backup+".meta"); if (!file.folder) throw new IOException("Injected partial removal");')
    remover.write_text(code)
(project/'Packages/manifest.json').write_text('{"dependencies":{}}');(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+args.unity.parent.name+'\n')
(project/'Assets/Chosen').mkdir();(project/'Assets/Chosen.meta').write_text('fileFormatVersion: 2\nguid: 99999999999999999999999999999999\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
(project/'Assets/Editor/ReloadProbe.cs').write_text('using System.IO; using UnityEditor; using UnityEngine; [InitializeOnLoad] public static class ReloadProbe { static ReloadProbe() { File.AppendAllText(Path.Combine(Application.dataPath,"../reloads"),"loaded\\n"); } }')
(run/'smoke_settings.py').write_text("from servicebase.settings import *\nDATABASES={'default':{'ENGINE':'django.db.backends.sqlite3','NAME':"+repr(str(run/'db.sqlite3'))+"}}\nPLUGIN_REPOSITORY_ROOT="+repr(str(run/'repository'))+"\nPLUGIN_DELIVERY_ROOT="+repr(str(run/'delivery'))+"\nALLOWED_HOSTS+=['testserver']\n")
sys.path[:0]=[str(run),str(root)];os.environ['DJANGO_SETTINGS_MODULE']='smoke_settings';os.environ['SERVICEBASE_MODE']='local'
import django;django.setup()
from django.core.management import call_command
from django.core.wsgi import get_wsgi_application
from django.test import Client
from dashboard.plugin_repository import stage_artifact,write_json
from dashboard.models import UnityProject,InstallJob,PluginInstallation
call_command('migrate',verbosity=0)
repo=run/'repository';repo.mkdir()
def meta(guid,folder=False,script=False):
    return 'fileFormatVersion: 2\nguid: '+guid+'\n'+('folderAsset: yes\n' if folder else '')+('MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n' if script else 'DefaultImporter:\n  externalObjects: {}\n')+'  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
source=run/'test.unitypackage'
with tarfile.open(source,'w:gz') as tar:
    for path,guid,content,script in [('Assets/Fixture','1'*32,None,False),('Assets/Fixture/Editor','2'*32,None,False),('Assets/Fixture/Editor/InstalledPlugin.cs','3'*32,b'using UnityEditor; [InitializeOnLoad] public static class InstalledPlugin { static InstalledPlugin() {} }',True)]:
        entries={'pathname':path.encode(),'asset.meta':meta(guid,content is None,script).encode()}
        if content is not None:entries['asset']=content
        for name,data in entries.items():
            entry=tarfile.TarInfo(guid+'/'+name);entry.size=len(data);tar.addfile(entry,io.BytesIO(data))
if args.edm:
    source=root/'external/magictavern/edm4u/unversioned/6db6b031d4fbd18a73e8b56455245b24355a9cfc558b7ae042326503bb01bc2a/artifact.unitypackage'
up=stage_artifact(repo,source,{'id':'edm4u' if args.edm else 'fixture','name':'EDM4U' if args.edm else 'Unity fixture'})
zsource=run/'test.zip'
with zipfile.ZipFile(zsource,'w') as z:
    z.writestr('ZipGenerated/data.bytes',b'generated meta fixture');z.writestr('ZipFixture.meta',meta('4'*32,True));z.writestr('ZipFixture/data.bytes',b'test zip contents');z.writestr('ZipFixture/data.bytes.meta',meta('5'*32))
    if args.management:
        z.writestr('ZipExisting/data.bytes',b'preexisting fixture')
        z.writestr('ZipOwned/data.bytes',b'owned fixture')
        z.writestr('ZipMissing/data.bytes',b'missing fixture')
zp=stage_artifact(repo,zsource,{'id':'zipfixture','name':'ZIP fixture'})
items=[up,zp]
if args.management:
    existing=project/'Assets/Chosen/ZipExisting';existing.mkdir()
    existing.with_suffix('.meta').write_text(meta('6'*32,True))
    (existing/'data.bytes').write_bytes(b'preexisting fixture');(existing/'data.bytes.meta').write_text(meta('7'*32))
    shared_source=run/'shared.zip'
    with zipfile.ZipFile(shared_source,'w') as z:
        z.writestr('ZipFixture.meta',meta('4'*32,True));z.writestr('ZipFixture/data.bytes',b'test zip contents');z.writestr('ZipFixture/data.bytes.meta',meta('5'*32))
    shared=stage_artifact(repo,shared_source,{'id':'zipshared','name':'Shared ZIP fixture'});items.append(shared)
write_json(repo/'index.json',{'schema_version':1,'plugins':items,'package_mappings':[]})
write_json(repo/'import-layouts.json',{'builds':{'zipfixture@'+zp['build_id']:{'mode':'assets','prefix':'','strip':''}}})
from django.contrib.staticfiles.handlers import StaticFilesHandler
application=StaticFilesHandler(get_wsgi_application());dropped=False


def app(env,start):
    global dropped
    if args.remove_restart and env['PATH_INFO']=='/api/bridge/plugins/removal-receipt/' and not (run/'removal-restarted').exists():
        captured=[];result=application(env,lambda status,headers,exc_info=None:captured.append((status,headers)));b''.join(result)
        if hasattr(result,'close'):result.close()
        if captured[0][0].startswith('200'):
            (run/'removal-receipt-saved').touch();start('503 Service Unavailable',[('Content-Type','application/json')]);return [b'{}']
        start(*captured[0]);return []
    if env['PATH_INFO']=='/api/bridge/plugins/download/' and (run/'hold-download').exists():
        start('503 Service Unavailable',[('Content-Type','application/json')]);return [b'{}']
    if env['PATH_INFO']=='/api/bridge/upm/sync/':
        raw=env['wsgi.input'].read(int(env.get('CONTENT_LENGTH') or 0));env['wsgi.input']=io.BytesIO(raw)
        body=json.loads(raw);report=body.get('report') or {}
        if args.partial and not partial_injected and report.get('state')=='succeeded' and (report.get('evidence') or {}).get('build_id')==zp['build_id']:
            start('503 Service Unavailable',[('Content-Type','application/json')]);return [b'{}']
        if not dropped and report.get('state')=='succeeded':
            captured=[];result=application(env,lambda status,headers,exc_info=None:captured.append((status,headers)));b''.join(result)
            if hasattr(result,'close'):result.close()
            if captured[0][0].startswith('200'):
                dropped=True;start('503 Service Unavailable',[('Content-Type','application/json')]);return [b'{}']
            start(*captured[0]);return []
    return application(env,start)
class Server(ThreadingMixIn,WSGIServer):daemon_threads=True
class Quiet(WSGIRequestHandler):
    def log_message(self,*args):pass
server=make_server('127.0.0.1',0,app,server_class=Server,handler_class=Quiet);threading.Thread(target=server.serve_forever,daemon=True).start()
url='http://127.0.0.1:'+str(server.server_port);env=dict(os.environ,SERVICEBASE_URL=url,SERVICEBASE_TOKEN='')
print('Web URL:',url,flush=True)
process=subprocess.Popen([str(args.unity/'Contents/MacOS/Unity')]+['-batchmode','-nographics']+['-projectPath',str(project),'-logFile',str(run/'unity.log')],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
client=Client(); results=[]; partial_injected=False; before_imports=0; choice_restarted=False
try:
    first_ownership = None
    edm_ownership = None
    directories_checked = False
    for item in ((up,zp,zp,shared) if args.management else (up,zp) if (args.partial or args.web_ui or args.choice_restart) else (up,up,zp,zp) if args.edm else (up,zp,zp)):
        if args.choice_restart and item==zp: (run/'hold-download').touch()
        deadline=time.time()+args.timeout;job=None
        while time.time()<deadline:
            if process.poll() is not None:raise RuntimeError('Unity exited '+str(run))
            current=UnityProject.objects.first()
            if current and not job:
                endpoint='/api/projects/'+current.pk+'/installations/'
                state=client.get(endpoint).json()
                if state['zip_executor_ready']:
                    if not directories_checked:
                        directory_url='/api/projects/'+current.pk+'/directories/'
                        request=client.post(directory_url,dict(path='Assets'),content_type='application/json').json()
                        for _ in range(30):
                            listing=client.get(directory_url,dict(request_id=request['request_id'])).json()
                            if listing['state']!='pending':break
                            time.sleep(1)
                        assert listing['state']=='completed' and 'Assets/Chosen' in listing['folders'], listing
                        directories_checked=True
                    if args.web_ui and item==zp:
                        job=InstallJob.objects.filter(target__plugin_id='zipfixture').first()
                        if not job:
                            time.sleep(1);continue
                        print('Web submitted ZIP',str(job.pk),flush=True)
                        continue
                    response=client.post(endpoint,dict(operation='plugin',plugin_id=item['plugin_id'],build_id=item['build_id'],destination_root='Assets/Chosen' if item.get('format')=='zip' or item!=up else '',request_key=str(uuid.uuid4())),content_type='application/json')
                    if response.status_code!=202:raise RuntimeError(response.content.decode())
                    job=InstallJob.objects.get(pk=response.json()['job']['id']);print('Submitted',item['plugin_id'],str(job.pk),flush=True)
            if job:
                job.refresh_from_db();step=job.step
                active=project/'.servicebase/upm/active.json'
                if args.choice_restart and item==zp and not choice_restarted and step.attempt.destination_root and active.exists():
                    saved=json.loads(active.read_text())
                    if saved.get('destination_root') and not saved['intent']:
                        process.kill();process.wait();(run/'hold-download').unlink()
                        process=subprocess.Popen([str(args.unity/'Contents/MacOS/Unity'),'-batchmode','-nographics','-projectPath',str(project),'-logFile',str(run/'choice-restart.log')],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                        choice_restarted=True

                if args.partial and item==zp and not partial_injected and active.exists():
                    saved=json.loads(active.read_text())
                    if saved['report']['state']=='succeeded':
                        process.kill();process.wait();before_imports=(run/'unity.log').read_text().count('[ServiceBase Plugin] Start Import ')
                        (project/'Assets/Chosen/ZipFixture/data.bytes').unlink()
                        from datetime import datetime,timedelta,timezone
                        saved['started_at']=(datetime.now(timezone.utc)-timedelta(minutes=11)).isoformat()
                        saved['report']['state']='verifying';saved['report']['sequence']+=1;saved['report']['message']='Fault fixture: interrupted file set'
                        active.write_text(json.dumps(saved));partial_injected=True
                        process=subprocess.Popen([str(args.unity/'Contents/MacOS/Unity')]+['-batchmode','-nographics']+['-projectPath',str(project),'-logFile',str(run/'restarted.log')],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                if args.partial and item==zp and partial_injected and step.state=='succeeded':
                    assert not job.active
                    assert '[ServiceBase Plugin] Start Import ' not in (run/'restarted.log').read_text(errors='replace')
                    results.append(dict(plugin=item['plugin_id'],state='succeeded',missing_archive_file_ignored=True,repeat_imports=0));break
                if step.state in ('failed','needs_review'):raise RuntimeError(json.dumps(step.attempt.report))
                if step.state=='succeeded' and (project/'.servicebase/upm/history'/str(step.attempt.pk)).with_suffix('.json').exists():
                    results.append(dict(plugin=item['plugin_id'],state=step.state,evidence=step.attempt.report['evidence']))
                    if args.edm and item==up:
                        ownership=PluginInstallation.objects.get(plugin_id='edm4u').receipt.files
                        scope=[{k:f[k] for k in ('path','folder','sha256','existed','meta_existed')} for f in ownership]
                        if edm_ownership is None:edm_ownership=scope
                        else:assert scope==edm_ownership,'EDM reimport lost original archive scope'
                        assert len(ownership)==step.command['file_count'], 'Archive list was pruned to runtime files'
                        saved=json.loads((project/'.servicebase/upm/history'/str(step.attempt.pk)).with_suffix('.json').read_text())
                        assert json.loads(saved['plugin']['record_json'])['omitted_files']==[]
                    if item==zp:
                        ownership=PluginInstallation.objects.get(plugin_id='zipfixture').receipt.files
                        archived=[{k:f[k] for k in ('path','folder','sha256','existed','meta_existed','before_sha256','before_meta_sha256')} for f in ownership]
                        if first_ownership is None: first_ownership=archived
                        else: assert first_ownership==archived, 'Reimport lost original archive scope or baseline'
                    break
            log=(run/'unity.log').read_text(errors='replace') if (run/'unity.log').exists() else ''
            if 'error CS' in log or '[ServiceBase UPM] 本地执行器已停止自动操作' in log:raise RuntimeError('Executor error '+str(run/'unity.log'))
            time.sleep(1)
        else:raise TimeoutError(str(run))
    if args.web_ui or args.choice_restart:
        if args.choice_restart:
            assert choice_restarted
        proof=dict(results=results,web_ui=args.web_ui,choice_restart=args.choice_restart,directories_checked=directories_checked,batchmode_without_interaction=True)
        (run/'result.json').write_text(json.dumps(proof,indent=2));print(json.dumps(proof),flush=True)
        raise SystemExit(0)
    if args.partial:
        proof=dict(results=results,partial_recovery=True,missing_file_ignored=True,repeat_imports=0)
        (run/'result.json').write_text(json.dumps(proof,indent=2));print(json.dumps(proof),flush=True)
        raise SystemExit(0)
    if args.management:
        def inventory_wait(predicate):
            client.post('/api/projects/'+current.pk+'/plugins/refresh/')
            deadline=time.time()+90
            while time.time()<deadline:
                state=client.get('/api/projects/'+current.pk+'/plugins/').json()
                if state['fresh'] and not state['scanning'] and predicate(state): return state
                time.sleep(1)
            raise AssertionError(state)
        def wait_removed(job):
            global process
            deadline=time.time()+120
            while time.time()<deadline:
                if args.remove_restart and (run/'removal-receipt-saved').exists() and not (run/'removal-restarted').exists():
                    process.kill();process.wait();(run/'removal-restarted').touch()
                    process=subprocess.Popen([str(args.unity/'Contents/MacOS/Unity'),'-batchmode','-nographics','-projectPath',str(project),'-logFile',str(run/'removal-restart.log')],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                job.refresh_from_db()
                if args.remove_partial and job.step.state=='needs_review':
                    assert job.active
                    attempt=job.step.attempt
                    assert not (project/'.servicebase/plugins/removed'/str(attempt.pk)).with_suffix('.json').exists()
                    assert (project/'.servicebase/plugins/records').is_dir()
                    # Recheck must not execute another removal or release the project lock.
                    before=(run/'unity.log').read_text().count('[ServiceBase Plugin] Start Remove ')
                    response=client.post(endpoint+str(job.pk)+'/recheck/',{},content_type='application/json')
                    assert response.status_code==200,response.content
                    time.sleep(12)
                    job.refresh_from_db();assert job.active and job.step.state=='needs_review'
                    assert (run/'unity.log').read_text().count('[ServiceBase Plugin] Start Remove ')==before==1
                    assert list((project/'.servicebase/plugins/trash').rglob('data.bytes'))
                    assert (project/'Assets/Chosen/ZipGenerated/data.bytes').read_bytes()==b'user modified'
                    proof=dict(partial_removal=True,lock_retained=True,recheck_without_repeat=True,backups_preserved=True,modified_preserved=True)
                    (run/'result.json').write_text(json.dumps(proof,indent=2));print(json.dumps(proof),flush=True)
                    raise SystemExit(0)
                if job.step.state in ('failed','needs_review'):raise AssertionError(job.step.attempt.report)
                if job.step.state=='succeeded' and (project/'.servicebase/upm/history'/str(job.step.attempt.pk)).with_suffix('.json').exists():return
                time.sleep(1)
            raise AssertionError(job.step.attempt.report)
        state=inventory_wait(lambda s:len(s['installations'])==3)
        assert all(r['status']=='recorded' for r in state['installations']),state
        # Generated output, modified files and missing assets do not invalidate an executed import.
        changed=project/'Assets/Chosen/ZipGenerated/data.bytes';changed.write_bytes(b'user modified')
        missing=project/'Assets/Chosen/ZipMissing/data.bytes';missing.unlink();missing.with_suffix('.bytes.meta').unlink()
        extra=project/'Assets/Chosen/ZipOwned/user.txt';extra.write_text('user extra file')
        state=inventory_wait(lambda s:all(r['status']=='recorded' for r in s['installations']))
        if args.management_web:
            print('Management UI ready:',url+'/#plugins',flush=True)
            print('Project:',current.pk,flush=True)
            deadline=time.time()+args.timeout
            while time.time()<deadline:
                first=InstallJob.objects.filter(target__operation='plugin_remove',target__plugin_id='zipfixture').first()
                if first:break
                time.sleep(1)
            else:raise TimeoutError('Waiting for browser removal')
            wait_removed(first)
        else:
            record=next(r for r in state['installations'] if r['plugin_id']=='zipfixture')
            response=client.post(endpoint,dict(operation='plugin_remove',record_key=record['record_key'],record_digest=record['record_digest'],request_key=str(uuid.uuid4())),content_type='application/json')
            assert response.status_code==202,response.content
            wait_removed(InstallJob.objects.get(pk=response.json()['job']['id']))
        assert not (project/'Assets/Chosen/ZipExisting').exists(), 'Unshared archive file or empty parent survived'
        if args.remove_restart:
            assert (run/'removal-restarted').exists()
            assert '[ServiceBase Plugin] Start Remove ' not in (run/'removal-restart.log').read_text(errors='replace'), 'Removal repeated after restart'
        assert not changed.parent.exists(), 'Modified archive file or empty generated parent survived'
        assert not missing.parent.exists(), 'Empty parent of missing archive file survived'
        assert extra.read_text()=='user extra file'
        assert (project/'Assets/Chosen/ZipFixture/data.bytes').is_file(), 'Shared file removed'
        assert not (project/'Assets/Chosen/ZipOwned/data.bytes').exists()
        assert list((project/'.servicebase/plugins/trash').rglob('data.bytes')), 'No removal backup'
        state=inventory_wait(lambda s:len(s['installations'])==2)
        assert next(r for r in state['installations'] if r['plugin_id']=='zipshared')['status']=='recorded'
        # Removing an editor script forces another assembly reload; the job must still finish once.
        record=next(r for r in state['installations'] if r['plugin_id']=='fixture')
        response=client.post(endpoint,dict(operation='plugin_remove',record_key=record['record_key'],record_digest=record['record_digest'],request_key=str(uuid.uuid4())),content_type='application/json')
        assert response.status_code==202,response.content
        wait_removed(InstallJob.objects.get(pk=response.json()['job']['id']))
        assert not (project/'Assets/Fixture').exists()
        state=inventory_wait(lambda s:len(s['installations'])==1)
        assert len(state['removals'])==2
        proof=dict(results=results,removal_restart=args.remove_restart,archive_files_removed=True,record_status_stable=True,shared_preserved=True,modified_removed=True,empty_parents_removed=True,extra_preserved=True,owned_removed=True,backups_preserved=True,delete_domain_reload=True,domain_loads=(project/'reloads').read_text().count('loaded'),web_ui=args.management_web)
        (run/'result.json').write_text(json.dumps(proof,indent=2));print(json.dumps(proof),flush=True)
        raise SystemExit(0)
    # Reimporting an owned build must not stall on a file changed since import.
    changed=project/'Assets/Chosen/ZipFixture/data.bytes'; changed.write_bytes(b'user modified')
    response=client.post(endpoint,dict(operation='plugin',plugin_id=zp['plugin_id'],build_id=zp['build_id'],destination_root='Assets/Chosen',request_key=str(uuid.uuid4())),content_type='application/json')
    assert response.status_code==202, response.content
    conflict=InstallJob.objects.get(pk=response.json()['job']['id']); deadline=time.time()+60
    while time.time()<deadline:
        conflict.refresh_from_db()
        if conflict.step.state in ('failed','succeeded'): break
        time.sleep(1)
    assert conflict.step.state=='succeeded', conflict.step.attempt.report
    assert changed.read_bytes()==b'user modified'
    assert PluginInstallation.objects.count()==2
    assert dropped
    assert (project/'reloads').read_text().count('loaded')>1
    proof=dict(results=results,edm=args.edm,callbacks_dropped=args.drop_callbacks,directories_checked=directories_checked,batchmode_without_interaction=True,modified_reimport_finished=True,ownership_preserved_on_reimport=True,lost_ack_recovered=dropped,domain_loads=(project/'reloads').read_text().count('loaded'),import_calls=(run/'unity.log').read_text().count('[ServiceBase Plugin] Start Import '))
    (run/'result.json').write_text(json.dumps(proof,indent=2));print(json.dumps(proof),flush=True)
finally:
    process.terminate()
    try:process.wait(20)
    except subprocess.TimeoutExpired:process.kill();process.wait()
    server.shutdown();server.server_close();print('Artifacts:',run,flush=True)
