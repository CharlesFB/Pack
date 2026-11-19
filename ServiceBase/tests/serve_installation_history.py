"""Disposable UI fixture: four history states, no Unity executor or project writes."""
import json
import os
from pathlib import Path
import sys
import uuid
from datetime import timedelta
from wsgiref.simple_server import make_server

root = Path(__file__).resolve().parent.parent
run = root / '.runtime' / ('installation-history-ui-' + uuid.uuid4().hex[:8])
run.mkdir(parents=True)
(run / 'fixture_settings.py').write_text(
    'from servicebase.settings import *\n'
    + "DATABASES={'default':{'ENGINE':'django.db.backends.sqlite3','NAME':" + repr(str(run/'db.sqlite3')) + '}}\n'
)
sys.path[:0] = [str(run), str(root)]
os.environ['DJANGO_SETTINGS_MODULE'] = 'fixture_settings'
os.environ['SERVICEBASE_MODE'] = 'local'
import django
django.setup()
from django.core.management import call_command
from django.core.cache import cache
from django.core.wsgi import get_wsgi_application
from django.contrib.staticfiles.handlers import StaticFilesHandler
from django.utils import timezone
from dashboard.models import UnityProject, ProjectPackageState, InstallJob, InstallStep, InstallAttempt
from dashboard.bridge import presence_key
from dashboard.installations import executor_key

call_command('migrate', verbosity=0)
project = UnityProject.objects.create(instance_id='ui-fixture',session_id='fixture',transport='pull',project_name='TestService · 界面测试',
    project_path='/isolated-ui-fixture',machine_name='UI fixture',unity_version='6000.3.19',process_id=1,port=0,token='',last_seen=timezone.now())
ProjectPackageState.objects.create(project=project,session_id='fixture',payload=dict(fingerprint='a'*64,captured_at=timezone.now().isoformat(),packages=[],assets_packages=[],error=''))
cache.set(presence_key(project.pk,project.session_id),timezone.now().isoformat(),None)
cache.set(executor_key(project),True,None)
cache.set(executor_key(project)+':plans',True,None)
definitions = [
    ('com.magictavern.sdk.adjustsdk',False,True,['succeeded','failed','skipped','skipped','skipped','skipped','skipped']),
    ('com.magictavern.nativeutility',False,False,['failed']),
    ('com.magictavern.devicemanager',False,False,['succeeded','skipped','succeeded','succeeded']),
    ('com.magictavern.sdk.adjustsdk',True,False,['succeeded','skipped','succeeded','verifying','queued','queued','queued','queued']),
]
for offset,(name,active,cancelled,states) in enumerate(definitions):
    target = dict(operation='plan',plan_action='install',name=name,identifier='plan:'+str(uuid.uuid4())) if len(states)>1 else dict(name=name,identifier=name+'@1.3.6',version='1.3.6',source='registry')
    job = InstallJob.objects.create(project=project,request_key=uuid.uuid4(),target=target,active=active,cancel_requested=cancelled,
        finished_at=None if active else timezone.now(),created_at=timezone.now()-timedelta(minutes=offset))
    for position,state in enumerate(states):
        operation = 'plugin' if position==3 else 'plan_verify' if position==len(states)-1 and len(states)>1 else 'upm'
        label = 'External Dependency Manager for Unity' if operation=='plugin' else '最终核对全部包与插件' if operation=='plan_verify' else ['com.magictavern.frontengine.base','External Dependency Manager for Unity','com.magictavern.nativeutility','EDM4U','com.magictavern.devicemanager','Adjust'][position%6]
        step=InstallStep.objects.create(job=job,position=position,state=state,target=dict(operation=operation,name=label,identifier='fixture',reason='未选择导入' if state=='skipped' else ''))
        message = {'succeeded':'目标状态核对通过。','verifying':'资源已导入，正在核对文件和 Unity 注册状态。','failed':'UPM 请求失败：请检查 Git 连接后重试。'}.get(state,'')
        InstallAttempt.objects.create(step=step,sequence=1 if state!='queued' else 0,
            dispatched_at=None if state in ('queued','skipped') else timezone.now(),
            report=dict(state=state,message=message,failure_kind='add_completed' if state=='failed' else ''))
    if active:
        (run/'active-job.json').write_text(json.dumps(dict(job_id=str(job.pk),step_id=job.step.pk)))

server = make_server('127.0.0.1',0,StaticFilesHandler(get_wsgi_application()))
print(json.dumps(dict(url='http://127.0.0.1:'+str(server.server_port),directory=str(run))),flush=True)
server.serve_forever()
