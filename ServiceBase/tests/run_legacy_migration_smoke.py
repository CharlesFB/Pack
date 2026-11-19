"""Opt-in migration, Assets discovery, no MTP reads, and lost-ack restart in an isolated Unity project."""
import io
import json
import os
import shutil
import subprocess
import sys
import threading
import time
import uuid
from pathlib import Path
from socketserver import ThreadingMixIn
from wsgiref.simple_server import WSGIServer, WSGIRequestHandler, make_server

root=Path(__file__).resolve().parent.parent
run=root/'.runtime'/('legacy-migration-smoke-'+uuid.uuid4().hex[:8]);project=run/'UnityProject'
(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
shutil.copytree(root/'unity',project/'Assets/Editor/ServiceBase',ignore=shutil.ignore_patterns('README.md'))
(project/'Packages/manifest.json').write_text('{"dependencies":{}}')
(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2022.3.62f2\n')
(project/'Assets/Local').mkdir();(project/'Assets/Local/package.json').write_text(json.dumps(dict(name='com.example.local',version='1.0.0',displayName='Local fixture')))
(project/'Assets/Existing').mkdir();(project/'Assets/Existing/data.bytes').write_bytes(b'original resource')
def meta(guid,folder=False):return 'fileFormatVersion: 2\nguid: '+guid+'\n'+('folderAsset: yes\n' if folder else '')+'DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
(project/'Assets/Existing.meta').write_text(meta('a'*32,True));(project/'Assets/Existing/data.bytes.meta').write_text(meta('b'*32))
(project/'Assets/FakeMtp').mkdir()
(project/'Assets/FakeMtp/Fake.asmdef').write_text(json.dumps(dict(name='Magictavern.FrontEngine.MTPManager',includePlatforms=['Editor'])))
(project/'Assets/FakeMtp/Trap.cs').write_text('''using System; using System.IO; using UnityEngine;
namespace MagicTavern.FrontEngine.MTPManager {
public interface IMTPackageInfoService { object ExportToMTPackageInfoList(); object GetPackageJson(string name); }
public static class ServiceLocator { public static bool TryGet<T>(out T result) { File.WriteAllText(Path.Combine(Application.dataPath,"../mtp-called"),"called"); throw new Exception("MTP must not be read"); } }
}''')
legacy=dict(Dependencies={'com.example.local':dict(PackageName='com.example.local',Tag='local-1.0.0',Revision=None,isLocal=True,Url='https://never-upload-this-secret@example.invalid/repo.git',Depth=0,Dependencies={},Plugins={})})
(project/'Packages/upm-install.json').write_text(json.dumps(legacy))
(project/'Packages/plugin-assets.json').write_text(json.dumps(dict(Content={'com.example.local#Example.unitypackage':['Assets/Existing','Assets/Existing/data.bytes']})))
(run/'smoke_settings.py').write_text("from servicebase.settings import *\nDATABASES={'default':{'ENGINE':'django.db.backends.sqlite3','NAME':"+repr(str(run/'db.sqlite3'))+",'OPTIONS':{'timeout':30,'transaction_mode':'IMMEDIATE'}}}\nALLOWED_HOSTS+=['testserver']\n")
sys.path[:0]=[str(run),str(root)];os.environ['DJANGO_SETTINGS_MODULE']='smoke_settings';os.environ['SERVICEBASE_MODE']='local'
import django;django.setup()
from django.core.management import call_command
from django.core.wsgi import get_wsgi_application
from django.test import Client
from dashboard.models import UnityProject, ProjectPackageState, PluginInventory, InstallJob
call_command('migrate',verbosity=0)
from django.contrib.staticfiles.handlers import StaticFilesHandler
application=StaticFilesHandler(get_wsgi_application())
lost=threading.Event()
def app(env,start):
    if env['PATH_INFO']=='/api/bridge/upm/sync/':
        raw=env['wsgi.input'].read(int(env.get('CONTENT_LENGTH') or 0));env['wsgi.input']=io.BytesIO(raw)
        data=json.loads(raw);report=data.get('report') or {}
        if report.get('state')=='succeeded' and not (run/'restarted').exists():
            if '--lost-journal' in sys.argv:
                lost.set();start('503 Service Unavailable',[('Content-Type','application/json')]);return [b'{}']
            captured=[];result=application(env,lambda *args:captured.append(args));b''.join(result)
            if hasattr(result,'close'):result.close()
            if captured[0][0].startswith('200'):
                lost.set();start('503 Service Unavailable',[('Content-Type','application/json')]);return [b'{}']
            start(*captured[0]);return []
    return application(env,start)
class Server(ThreadingMixIn,WSGIServer):daemon_threads=True
class Quiet(WSGIRequestHandler):
    def log_message(self,*args):pass
server=make_server('127.0.0.1',0,app,server_class=Server,handler_class=Quiet);threading.Thread(target=server.serve_forever,daemon=True).start()
url='http://127.0.0.1:'+str(server.server_port);env=dict(os.environ,SERVICEBASE_URL=url,SERVICEBASE_TOKEN='')
unity='/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity'
def start(log):return subprocess.Popen([unity,'-batchmode','-nographics','-projectPath',str(project),'-logFile',str(run/log)],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
process=start('unity.log');client=Client()
def wait(check, timeout=240):
    deadline=time.time()+timeout
    while time.time()<deadline:
        if process.poll() is not None:raise RuntimeError('Unity exited: '+str(run))
        value=check()
        if value:return value
        time.sleep(1)
    raise RuntimeError('Timed out: '+str(run))
def get(path):
    r=client.get(path);assert r.status_code==200,r.content;return r.json()
def post(path,data,status=202):
    r=client.post(path,data,content_type='application/json');assert r.status_code==status,r.content;return r.json()
try:
    print('Fixture:',run,'Web URL:',url,flush=True)
    state=wait(lambda:ProjectPackageState.objects.first());p=UnityProject.objects.get();base='/api/projects/'+p.pk+'/'
    assert state.payload['assets_packages'][0]['name']=='com.example.local',state.payload
    assert not (project/'.servicebase/migrations').exists()
    assert not (project/'mtp-called').exists()
    time.sleep(32)  # Exceeds the retired Bridge snapshot collection interval.
    assert not (project/'mtp-called').exists()
    wait(lambda:get(base+'legacy-migrations/')['ready'])
    scan=post(base+'legacy-migrations/',dict(request_key=str(uuid.uuid4())))
    def preview():
        rows=get(base+'legacy-migrations/')['previews'];row=next(r for r in rows if r['id']==scan['id'])
        if row['state']=='failed':raise RuntimeError(str(row))
        return row if row['state']=='preview' else None
    result=wait(preview);assert len(result['preview']['entries'])==2,result
    assert 'never-upload-this-secret' not in json.dumps(result)
    # First confirmation is stale; source mutation must fail before adding a migration record.
    f=project/'Packages/upm-install.json';f.write_text(f.read_text()+'\n')
    entries=[e['id'] for e in result['preview']['entries']]
    job=post(base+'legacy-migrations/'+scan['id']+'/confirm/',dict(digest=result['digest'],entries=entries))['job']
    wait(lambda:InstallJob.objects.get(pk=job['id']).step.state=='failed')
    assert not (project/'.servicebase/migrations/records').exists()
    wait(lambda:not (project/'.servicebase/upm/active.json').exists())
    scan=post(base+'legacy-migrations/',dict(request_key=str(uuid.uuid4())))
    result=wait(preview);chosen=[e['id'] for e in result['preview']['entries'] if e['kind']=='plugin']
    before={str(f.relative_to(project)):f.read_bytes() for f in [project/'Packages/upm-install.json',project/'Packages/plugin-assets.json',project/'Assets/Existing/data.bytes',project/'Assets/Existing/data.bytes.meta']}
    body=dict(digest=result['digest'],entries=chosen)
    job=post(base+'legacy-migrations/'+scan['id']+'/confirm/',body)['job']
    replay=post(base+'legacy-migrations/'+scan['id']+'/confirm/',body,status=200);assert replay['job']['id']==job['id']
    wait(lost.is_set)
    process.kill();process.wait()
    if '--lost-journal' in sys.argv:(project/'.servicebase/upm/active.json').unlink()
    (run/'restarted').touch();process=start('restarted.log')
    wait(lambda:not (project/'.servicebase/upm/active.json').exists())
    wait(lambda:(PluginInventory.objects.first() and PluginInventory.objects.first().payload.get('legacy_installations')))
    archives=list((project/'.servicebase/migrations/records').glob('*.json'));assert len(archives)==1
    archive=json.loads(archives[0].read_text());assert len(archive['entries'])==1;assert archive['entries'][0]['kind']=='plugin'
    assert 'never-upload-this-secret' not in archives[0].read_text()
    for rel,raw in before.items():assert (project/rel).read_bytes()==raw,rel
    assert not (project/'mtp-called').exists()
    snapshot=get(base+'plugins/');assert snapshot['legacy_installations'][0]['status']=='baseline_matches',snapshot
    scan=post(base+'legacy-migrations/',dict(request_key=str(uuid.uuid4())))
    result=wait(preview);assert next(e for e in result['preview']['entries'] if e['kind']=='plugin')['migrated']
    # A subsequent user edit is visible as modified against the opted-in baseline.
    (project/'Assets/Existing/data.bytes').write_bytes(b'user changed resource')
    post(base+'plugins/refresh/',{},status=202)
    wait(lambda:any(e['status']=='modified' for e in get(base+'plugins/').get('legacy_installations',[])))
    summary=dict(assets_detected=True,no_automatic_scan=True,no_mtp_calls=True,stale_confirmation_rejected=True,
        selected_only=True,originals_preserved=True,restart_ack_recovered=True,lost_active_journal='--lost-journal' in sys.argv,archives=len(archives),baseline_changes_detected=True)
    (run/'result.json').write_text(json.dumps(summary,indent=2));print(json.dumps(summary),flush=True)
    if '--web-ui' in sys.argv:
        (run/'web-ready').touch(); print('Waiting for browser review; remove web-ready to finish.',flush=True)
        deadline=time.monotonic()+300
        while (run/'web-ready').exists() and time.monotonic()<deadline:
            if process.poll() is not None:raise RuntimeError('Unity exited during browser review')
            time.sleep(1)
        (run/'web-ready').unlink(missing_ok=True)
finally:
    if process.poll() is None:process.terminate()
    try:process.wait(timeout=20)
    except subprocess.TimeoutExpired:process.kill();process.wait()
    server.shutdown()
