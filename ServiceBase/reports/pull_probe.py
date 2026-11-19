"""Isolated v2 poll burst probe; no production database or network traffic."""
import os,sys,tempfile,time,json
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from collections import Counter
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
os.environ['DJANGO_SETTINGS_MODULE']='servicebase.settings'
from django.conf import settings
with tempfile.TemporaryDirectory(prefix='servicebase-v2-probe-') as tmp:
    settings.DATABASES['default']['NAME']=str(Path(tmp)/'probe.sqlite3')
    import django
    django.setup()
    from django.core.management import call_command
    from django.core.cache import cache
    from django.db import connections
    from django.test import RequestFactory
    from dashboard.bridge import register,poll
    call_command('migrate',verbosity=0)
    def req(data):
        return RequestFactory().post('/api/bridge/poll/',json.dumps(data),content_type='application/json',
                 REMOTE_ADDR='127.0.0.1',HTTP_X_SERVICEBASE_CLIENT='unity-bridge-v2')
    for i in range(1000):
        response=register(req(dict(instance_id=f'probe-{i}',session_id='test',project_name=f'Project {i}',
            project_path=f'/sample/{i}',machine_name='synthetic',unity_version='2022',process_id=i+1)))
        assert response.status_code==200
    connections.close_all()
    for workers in (1,8,32):
        def invoke(i):
            start=time.perf_counter()
            try: status=str(poll(req(dict(instance_id=f'probe-{i}',session_id='test'))).status_code)
            except Exception as error: status=type(error).__name__+': '+str(error)
            finally: connections.close_all()
            return status,(time.perf_counter()-start)*1000
        started=time.perf_counter()
        with ThreadPoolExecutor(max_workers=workers) as pool: results=list(pool.map(invoke,range(1000)))
        elapsed=time.perf_counter()-started
        print(json.dumps(dict(workers=workers,requests=1000,seconds=round(elapsed,3),
             outcomes=dict(Counter(r[0] for r in results)),p95_ms=round(sorted(r[1] for r in results)[949],2))))
    connections.close_all()
