"""Isolated view/SQLite contention probe; no HTTP server or real project records."""
import os, sys, tempfile, time, json, math
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from collections import Counter
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
os.environ['DJANGO_SETTINGS_MODULE'] = 'servicebase.settings'
from django.conf import settings
with tempfile.TemporaryDirectory(prefix='servicebase-probe-') as tmp:
    settings.DATABASES['default']['NAME'] = str(Path(tmp)/'probe.sqlite3')
    import django
    django.setup()
    from django.db import connection, connections
    from django.test import RequestFactory
    from dashboard.models import UnityProject
    from dashboard.views import heartbeat, projects
    from django.core.management import call_command
    call_command('migrate', verbosity=0)
    def request(i):
        data=dict(instance_id=f'probe-{i}', session_id='test-session', project_name=f'Project {i}',
                  project_path=f'/sample/Project{i}', machine_name='synthetic-machine', unity_version='2022.3',
                  token='synthetic-token', port=20000+i, process_id=i+1)
        return RequestFactory().post('/api/heartbeat/', data=json.dumps(data), content_type='application/json',
              REMOTE_ADDR='127.0.0.1', HTTP_X_SERVICEBASE_CLIENT='unity-bridge-v1')
    for i in range(1000): heartbeat(request(i))
    connection.close()
    for workers in (1,8,32):
        def invoke(i):
            start=time.perf_counter()
            try:
                status=str(heartbeat(request(i)).status_code)
            except Exception as error:
                status=type(error).__name__+': '+str(error)
            finally:
                connections.close_all()
            return status, (time.perf_counter()-start)*1000
        start=time.perf_counter()
        with ThreadPoolExecutor(max_workers=workers) as pool:
            results=list(pool.map(invoke, range(1000)))
        elapsed=time.perf_counter()-start
        durations=sorted(x[1] for x in results)
        print(json.dumps(dict(workers=workers, requests=1000, seconds=round(elapsed,3),
             outcomes=dict(Counter(x[0] for x in results)), p95_ms=round(durations[949],2)),ensure_ascii=False),flush=True)
    response=projects(RequestFactory().get('/api/projects/'))
    print(json.dumps(dict(synthetic_list_rows=1000,json_bytes=len(response.content))))
    connections.close_all()
