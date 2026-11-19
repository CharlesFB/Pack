"""Real Unity acceptance matrix; every scenario uses isolated files, DB and service.
Example: .venv/bin/python tests/run_upm_recovery_smoke.py --scenario restart
No production processes, project files or global Git/SSL settings are modified.
"""
from datetime import datetime, timedelta, timezone
import argparse, hashlib, io, json, os, shutil, signal, socket, sqlite3, subprocess, sys, tarfile, threading, time, uuid
from pathlib import Path
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from http.cookiejar import CookieJar
from urllib.request import build_opener,HTTPCookieProcessor,Request
from urllib.error import HTTPError,URLError
from upm_git_fixture import create_git_fixture

parser=argparse.ArgumentParser()
parser.add_argument('--scenario',choices=['rollback-journal','retry','git','restart','crash','outage','corrupt','write-failure','lost-delivery','mtp-record','mtp-loaded'],required=True)
parser.add_argument('--unity',type=Path,default=Path('/Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app'))
parser.add_argument('--timeout',type=int,default=210)
args=parser.parse_args()
root=Path(__file__).resolve().parent.parent
run=root/'.runtime'/('upm-recovery-'+args.scenario+'-'+uuid.uuid4().hex[:8]); run.mkdir(parents=True)
project=run/'UnityProject'
(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
shutil.copytree(root/'unity/UpmExecutor',project/'Assets/Editor/UpmExecutor')
(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2022.3.62f2\n')
name='com.servicebase.smoke.recovery';version='1.0.'+str(int(time.time()))
payload=io.BytesIO()
with tarfile.open(fileobj=payload,mode='w:gz') as archive:
    entries={'package/package.json':json.dumps({'name':name,'version':version}),
             'package/package.json.meta':'fileFormatVersion: 2\nguid: 12345678123456781234567812345678\n'}
    for path,content in entries.items():
        data=content.encode();entry=tarfile.TarInfo(path);entry.size=len(data);archive.addfile(entry,io.BytesIO(data))
artifact=payload.getvalue();downloads=[]
class Registry(BaseHTTPRequestHandler):
    def log_message(self,*args):pass
    def do_GET(self):
        if self.path.endswith('.tgz'):
            downloads.append(time.time())
            while (run/'hold-download.flag').exists(): time.sleep(.1)
            body=artifact;mime='application/octet-stream'
        else:
            body=json.dumps({'name':name,'dist-tags':{'latest':version},'versions':{version:{'name':name,'version':version,'dist':{'tarball':registry_url+'/package.tgz','shasum':hashlib.sha1(artifact).hexdigest()}}}}).encode();mime='application/json'
        try:
            self.send_response(200);self.send_header('Content-Type',mime);self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
        except (BrokenPipeError,ConnectionResetError): pass
registry=ThreadingHTTPServer(('127.0.0.1',0),Registry);registry_url='http://127.0.0.1:'+str(registry.server_port)
threading.Thread(target=registry.serve_forever,daemon=True).start()
manifest={'dependencies':{},'scopedRegistries':[{'name':'Isolated smoke','url':registry_url,'scopes':['com.servicebase.smoke']} ]}
if args.scenario=='mtp-record':
    (project/'Library').mkdir();(project/'Library/mtp-install-recovery.json').write_text('{"fixture":true}')
if args.scenario=='mtp-loaded':
    # Identity probe for the real assembly-name gate; does NOT pretend to execute the real MTP implementation.
    folder=project/'Assets/Editor/MtpIdentityProbe';folder.mkdir()
    (folder/'Probe.asmdef').write_text(json.dumps({'name':'Magictavern.FrontEngine.MTPManager','includePlatforms':['Editor']}))
    (folder/'Probe.cs').write_text('public class MtpIdentityProbe {}')
(project/'Packages/manifest.json').write_text(json.dumps(manifest))
(run/'smoke_settings.py').write_text("from servicebase.settings import *\nDATABASES={'default':{'ENGINE':'django.db.backends.sqlite3','NAME':"+repr(str(run/'db.sqlite3'))+"}}\nSMOKE_RUN="+repr(str(run))+"\nMIDDLEWARE.insert(0,'upm_service_fixture.FaultMiddleware')\n")
portsocket=socket.socket();portsocket.bind(('127.0.0.1',0));port=portsocket.getsockname()[1];portsocket.close()
url='http://127.0.0.1:'+str(port)
env=dict(os.environ,SERVICEBASE_URL=url,SERVICEBASE_TOKEN='')
gitserver=None;gitstats={};identifier=name+'@'+version
if args.scenario=='git': gitserver,identifier,gitenv,gitstats=create_git_fixture(run,name,version);env.update(gitenv)
jar=CookieJar();opener=build_opener(HTTPCookieProcessor(jar));service=None;unity=None;logs=[]
def api(path,data=None):
    headers={}
    if data is not None:
        headers={'Content-Type':'application/json','X-CSRFToken':next((c.value for c in jar if c.name=='csrftoken'),'')}
    try:
        response=opener.open(Request(url+path,data=None if data is None else json.dumps(data).encode(),headers=headers),timeout=10)
    except HTTPError as error:response=error
    body=response.read()
    return response.status,json.loads(body) if path.startswith('/api/') else body

def start_service():
    global service
    ready=run/'server-ready';ready.unlink(missing_ok=True)
    log=(run/'service.log').open('ab');logs.append(log)
    service=subprocess.Popen([sys.executable,str(root/'tests/upm_service_fixture.py'),str(run),str(port)],stdout=log,stderr=log)
    wait(lambda:ready.exists(),30)
    api('/')
def start_unity():
    global unity
    log=run/('unity-'+str(len(list(run.glob('unity-*.log'))))+'.log')
    unity=subprocess.Popen([str(args.unity/'Contents/MacOS/Unity'),'-batchmode','-nographics','-projectPath',str(project),'-logFile',str(log)],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
def stop(process,hard=False):
    if process is None or process.poll() is not None:return
    if hard:process.kill()
    else:process.terminate()
    try:process.wait(timeout=15)
    except subprocess.TimeoutExpired:process.kill();process.wait()
def wait(check,timeout=None):
    until=time.time()+(timeout or args.timeout)
    while time.time()<until:
        result=check()
        if result:return result
        time.sleep(.25)
    raise TimeoutError('Check timed out; evidence in '+str(run))
def status():
    code,data=api('/api/projects/')
    return data['projects'][0] if data.get('projects') else None
def journal():
    path=project/'.servicebase/upm/active.json'
    try:return json.loads(path.read_text())
    except (FileNotFoundError,json.JSONDecodeError):return None
def dbstate():
    with sqlite3.connect(run/'db.sqlite3') as conn:
        row=conn.execute('select j.active,s.state,a.report from dashboard_installjob j join dashboard_installstep s on s.job_id=j.id join dashboard_installattempt a on a.step_id=s.id order by a.number desc limit 1').fetchone()
        return None if row is None else {'active':bool(row[0]),'state':row[1],'report':json.loads(row[2])}
def add_count():
    return sum(p.read_text(errors='replace').count('[ServiceBase UPM] Start Add ') for p in run.glob('unity-*.log'))
try:
    start_service();start_unity()
    p=wait(status);endpoint='/api/projects/'+p['instance_id']+'/installations/'
    wait(lambda:api(endpoint)[1]['executor_online'])
    if args.scenario in ('mtp-record','mtp-loaded'):
        view=api(endpoint)[1];assert not view['executor_ready'];assert 'MTP' in view['executor_diagnostic']
        code,_=api(endpoint,{'name':name,'identifier':identifier,'request_key':str(uuid.uuid4())});assert code==409
        assert add_count()==0
        proof={'scenario':args.scenario,'blocked':True,'diagnostic':view['executor_diagnostic'],'add_calls':0,'mtp_fixture_only':True}
    else:
        if args.scenario in ('restart','crash','outage','corrupt','rollback-journal'):(run/'hold-download.flag').touch()
        if args.scenario=='lost-delivery':(run/'drop-delivery.flag').touch()
        if args.scenario=='retry':(run/'preflight-failure.flag').touch()
        if args.scenario=='write-failure':
            (project/'.servicebase').mkdir(exist_ok=True);(project/'.servicebase/upm').write_text('blocked-directory-fixture')
        code,data=api(endpoint,{'name':name,'identifier':identifier,'request_key':str(uuid.uuid4())});assert code==202,(code,data)
        job=data['job'];print(args.scenario,'job',job['id'],flush=True)
        if args.scenario=='retry':
            wait(lambda:dbstate() and dbstate()['state']=='failed')
            assert add_count()==0
            failed=api(endpoint)[1]['jobs'][0]
            (project/'Library/mtp-install-recovery.json').unlink()
            wait(lambda:api(endpoint)[1]['executor_ready'])
            retry_body={'request_key':str(uuid.uuid4()),'attempt_id':failed['attempt_id'],'reason':'Removed isolated preflight failure fixture'}
            code,response=api(endpoint+job['id']+'/retry/',retry_body);assert code==202,(code,response)
            assert response['job']['attempt_number']==2
            assert api(endpoint+job['id']+'/retry/',retry_body)[0]==200
        if args.scenario in ('restart','crash','outage','corrupt','rollback-journal'):
            wait(lambda:journal() and journal()['intent'] and downloads)
            if args.scenario=='restart':
                stop(service,hard=True);(run/'hold-download.flag').unlink();time.sleep(3);start_service()
            elif args.scenario=='outage':
                (run/'offline.flag').touch();(run/'hold-download.flag').unlink()
                wait(lambda:journal() and journal()['report']['state']=='succeeded')
                assert dbstate()['active'];(run/'offline.flag').unlink()
            else:
                stop(unity,hard=True)
                if args.scenario=='corrupt': (project/'.servicebase/upm/active.json').write_text('{corrupted fixture')
                else:
                    file=project/'.servicebase/upm/active.json';saved=json.loads(file.read_text())
                    saved['started_at']=(datetime.now(timezone.utc)-timedelta(minutes=11)).isoformat()
                    if args.scenario=='rollback-journal':
                        with sqlite3.connect(run/'db.sqlite3') as conn:
                            prior=conn.execute('select report from dashboard_installevent where sequence=1').fetchone()
                        assert prior and json.loads(prior[0])['state']=='accepted'
                        saved['report']=json.loads(prior[0]);saved['intent']=False;saved['ack']=0
                    file.write_text(json.dumps(saved)) # Advance timeout; optionally restore an older accepted record.
                (run/'hold-download.flag').unlink();start_unity()
        if args.scenario in ('corrupt','write-failure'):
            wait(lambda:api(endpoint)[1]['executor_diagnostic'])
            view=api(endpoint)[1];assert not view['executor_ready']
            assert dbstate()['active']
            assert add_count()==(1 if args.scenario=='corrupt' else 0)
            proof={'scenario':args.scenario,'blocked':True,'lock_retained':True,'diagnostic':view['executor_diagnostic'],'add_calls':add_count()}
        elif args.scenario=='lost-delivery':
            wait(lambda:(run/'delivery-dropped').exists() and journal())
            assert journal()['intent'] and add_count()==0 and dbstate()['active']
            stop(unity)
            file=project/'.servicebase/upm/active.json';saved=json.loads(file.read_text())
            saved['started_at']=(datetime.now(timezone.utc)-timedelta(minutes=11)).isoformat();file.write_text(json.dumps(saved))
            start_unity()
            wait(lambda:dbstate()['state']=='needs_review')
            assert dbstate()['active'] and add_count()==0
            code,_=api(endpoint+job['id']+'/retry/',{'request_key':str(uuid.uuid4()),'attempt_id':journal()['work']['attempt_id'],'reason':'Unknown outcome must remain blocked'})
            assert code==409
            proof={'scenario':args.scenario,'recover_only':True,'add_calls':0,'unsafe_retry_rejected':True,'timeout_advanced':True,'state':dbstate()['state']}
        else:
            wait(lambda:dbstate() and dbstate()['state'] in ('succeeded','failed','needs_review'))
            result=dbstate()
            if args.scenario in ('crash','rollback-journal') and result['state']=='needs_review':
                assert result['active']
                code,_=api(endpoint+job['id']+'/retry/',{'request_key':str(uuid.uuid4()),'attempt_id':journal()['work']['attempt_id'],'reason':'Unknown crash must not be retried'})
                assert code==409
            else:
                assert result['state']=='succeeded',result
                wait(lambda:not (project/'.servicebase/upm/active.json').exists())
            assert add_count()==1,add_count()
            if args.scenario=='git':assert gitstats['authorized'] and gitstats['upload_pack'] and gitstats['challenges']
            proof={'scenario':args.scenario,'state':result['state'],'evidence':result['report']['evidence'],'add_calls':add_count(),'git_http':gitstats,'timeout_advanced':args.scenario in ('crash','rollback-journal'),'lock_retained':result['active']}
    (run/'result.json').write_text(json.dumps(proof,indent=2,ensure_ascii=False))
    print(json.dumps(proof,ensure_ascii=False),flush=True);print('Evidence:',run,flush=True)
finally:
    (run/'hold-download.flag').unlink(missing_ok=True)
    stop(unity);stop(service)
    registry.shutdown();registry.server_close()
    if gitserver:gitserver.shutdown();gitserver.server_close()
    for log in logs:log.close()
