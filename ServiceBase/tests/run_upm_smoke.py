"""Isolated real-Unity UPM smoke: local registry, own SQLite, no MTP or business project edits.
Run with .venv/bin/python tests/run_upm_smoke.py --unity /path/Unity.app
All fixtures and logs stay in .runtime/upm-smoke-<timestamp>; server/editor stop on exit.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import threading
import time
from socketserver import ThreadingMixIn
from urllib.parse import unquote
from wsgiref.simple_server import WSGIServer, WSGIRequestHandler, make_server

parser = argparse.ArgumentParser()
parser.add_argument('--unity', type=Path, required=True)
parser.add_argument('--timeout', type=int, default=240)
parser.add_argument('--failure', action='store_true', help='Return a registry 404 and verify the original UPM error is reported')
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
run = root / '.runtime' / ('upm-smoke-' + str(int(time.time())))
project = run / 'UnityProject'
(project / 'Assets/Editor').mkdir(parents=True)
(project / 'Packages').mkdir()
(project / 'ProjectSettings').mkdir()
shutil.copytree(root / 'unity/UpmExecutor', project / 'Assets/Editor/UpmExecutor')
(project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+args.unity.parent.name+'\n')
(project / 'Assets/Editor/ReloadProbe.cs').write_text('''using System.IO;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad] public static class ReloadProbe {
    static ReloadProbe() { File.AppendAllText(Path.Combine(Application.dataPath, "../reload-probe.txt"), "loaded\\n"); }
}
''')
(run / 'smoke_settings.py').write_text("from servicebase.settings import *\nDATABASES = {'default': {'ENGINE':'django.db.backends.sqlite3','NAME':"+repr(str(run / 'db.sqlite3'))+"}}\nALLOWED_HOSTS += ['testserver']\n")
sys.path[:0] = [str(run),str(root)]
os.environ['DJANGO_SETTINGS_MODULE'] = 'smoke_settings'
os.environ['SERVICEBASE_MODE'] = 'local'
import django
django.setup()
from django.core.management import call_command
from django.core.wsgi import get_wsgi_application
from django.test import Client
from dashboard.models import UnityProject, InstallJob, InstallEvent
call_command('migrate', verbosity=0)
application = get_wsgi_application()
blob = io.BytesIO()
name = 'com.servicebase.smoke.install'
version = '1.0.' + str(int(time.time()))
with tarfile.open(fileobj=blob, mode='w:gz') as archive:
    for path, value in {'package/package.json':json.dumps({'name':name,'version':version,'displayName':'ServiceBase isolated smoke'}),
                        'package/Editor/InstalledProbe.cs':'using UnityEditor; [InitializeOnLoad] public static class InstalledProbe { static InstalledProbe() {} }',
                        'package/Editor.meta':'fileFormatVersion: 2\nguid: 11111111111111111111111111111111\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n',
                        'package/Editor/InstalledProbe.cs.meta':'fileFormatVersion: 2\nguid: 22222222222222222222222222222222\nMonoImporter:\n  externalObjects: {}\n',
                        'package/package.json.meta':'fileFormatVersion: 2\nguid: 33333333333333333333333333333333\nPackageManifestImporter:\n  externalObjects: {}\n'}.items():
        data = value.encode(); entry = tarfile.TarInfo(path); entry.size = len(data); archive.addfile(entry,io.BytesIO(data))
artifact = blob.getvalue()
dropped = False

def app(environ, start):
    global dropped
    path = unquote(environ['PATH_INFO'])
    if path.startswith('/registry/'):
        if args.failure:
            start('404 Not Found',[('Content-Type','application/json')])
            return [b'{"error":"fixture package deliberately unavailable"}']
        if path.endswith('.tgz'):
            content, mime = artifact, 'application/octet-stream'
        else:
            content = json.dumps({'name':name, 'dist-tags':{'latest':version}, 'versions':{version:{'name':name,'version':version,'dist':{'tarball':url+'/registry/package.tgz','shasum':hashlib.sha1(artifact).hexdigest()}}}}).encode()
            mime = 'application/json'
        start('200 OK',[('Content-Type',mime),('Content-Length',str(len(content)))])
        return [content]
    # Simulate losing a success response AFTER the DB commits; the next sync must deduplicate.
    if path == '/api/bridge/upm/sync/':
        body = environ['wsgi.input'].read(int(environ.get('CONTENT_LENGTH') or 0))
        environ['wsgi.input'] = io.BytesIO(body)
        data = json.loads(body)
        if not dropped and (data.get('report') or {}).get('state') == 'succeeded':
            captured = []
            result = application(environ, lambda status, headers, exc_info=None: captured.append((status,headers)))
            b''.join(result)
            if hasattr(result,'close'): result.close()
            if captured[0][0].startswith('200'):
                dropped = True
                start('503 Service Unavailable',[('Content-Type','application/json')])
                return [b'{"error":"injected lost acknowledgement"}']
            start(*captured[0]); return []
    return application(environ,start)

class Server(ThreadingMixIn,WSGIServer): daemon_threads = True
class Quiet(WSGIRequestHandler):
    def log_message(self, *args): pass
server = make_server('127.0.0.1',0,app,server_class=Server,handler_class=Quiet)
url = 'http://127.0.0.1:' + str(server.server_port)
(project / 'Packages/manifest.json').write_text(json.dumps({'dependencies':{},'scopedRegistries':[{'name':'Local smoke fixture','url':url+'/registry','scopes':['com.servicebase.smoke']}]}))
threading.Thread(target=server.serve_forever,daemon=True).start()
env = dict(os.environ, SERVICEBASE_URL=url, SERVICEBASE_TOKEN='')
command = [str(args.unity / 'Contents/MacOS/Unity'),'-batchmode','-nographics','-projectPath',str(project),'-logFile',str(run/'unity.log')]
process = subprocess.Popen(command,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
client = Client()
try:
    deadline = time.time()+args.timeout
    job_id = None; before = 0
    while time.time() < deadline:
        if process.poll() is not None: raise RuntimeError('Unity exited early; inspect '+str(run/'unity.log'))
        if (run/'unity.log').exists() and '[ServiceBase UPM] 本地执行器已停止自动操作' in (run/'unity.log').read_text(errors='replace'):
            raise RuntimeError('Executor stopped; inspect '+str(run/'unity.log'))
        current = UnityProject.objects.first()
        if current:
            endpoint = '/api/projects/' + current.pk + '/installations/'
            if not job_id and client.get(endpoint).json()['executor_online']:
                before = (project/'reload-probe.txt').read_text().count('loaded')
                response = client.post(endpoint, {'name':name,'identifier':name+'@'+version,'request_key':'11111111-1111-4111-8111-111111111111'},content_type='application/json')
                if response.status_code != 202: raise RuntimeError(response.content.decode())
                job_id = response.json()['job']['id']
                print('Submitted isolated UPM job',job_id,flush=True)
            if job_id:
                job = InstallJob.objects.get(pk=job_id)
                if args.failure and job.step.state=='failed':
                    history=project/'.servicebase/upm/history'/ (str(job.step.attempt.pk)+'.json')
                    if not history.exists():
                        time.sleep(1);continue
                    saved=json.loads(history.read_text());message=job.step.attempt.report['message']
                    assert job.step.attempt.report['failure_kind']=='add_completed'
                    assert saved['upm_error_message'] and saved['upm_error_code']
                    assert 'UPM 错误' in message and name in message and '与安装前一致' in message,message
                    assert not job.active
                    proof=dict(state='failed',raw_error_saved_locally=True,web_error=message,lock_released=True,
                               add_calls=(run/'unity.log').read_text().count('[ServiceBase UPM] Start Add '))
                    assert proof['add_calls']==1
                    (run/'result.json').write_text(json.dumps(proof,ensure_ascii=False,indent=2));print(json.dumps(proof,ensure_ascii=False),flush=True)
                    break
                if job.step.state in ('failed','needs_review'): raise RuntimeError(json.dumps(job.step.attempt.report))
                history = project/'.servicebase/upm/history'
                if job.step.state == 'succeeded' and history.exists() and list(history.glob('*.json')):
                    after = (project/'reload-probe.txt').read_text().count('loaded')
                    assert after > before, 'Install did not trigger a domain reload'
                    assert dropped, 'Lost acknowledgement was not exercised'
                    assert InstallEvent.objects.filter(attempt=job.step.attempt, report__state='succeeded').count() == 1
                    proof = {'job_id':job_id,'state':job.step.state,'evidence':job.step.attempt.report['evidence'], 'domain_loads_before':before,'domain_loads_after':after,'lost_ack_recovered':dropped,'mtp_present':False}
                    (run/'result.json').write_text(json.dumps(proof,indent=2))
                    print(json.dumps(proof),flush=True)
                    print('Artifacts:',run,flush=True)
                    break
        time.sleep(1)
    else: raise TimeoutError('Smoke timeout; inspect '+str(run/'unity.log'))
finally:
    process.terminate()
    try: process.wait(timeout=20)
    except subprocess.TimeoutExpired: process.kill(); process.wait()
    server.shutdown(); server.server_close()
