"""Disposable Django process for real restart/network-failure tests."""
import json
from pathlib import Path
from django.http import JsonResponse

class FaultMiddleware:
    def __init__(self,get_response): self.get_response=get_response
    def __call__(self,request):
        from django.conf import settings
        run=Path(settings.SMOKE_RUN)
        if (run/'offline.flag').exists(): return JsonResponse({'error':'Injected outage'},status=503)
        response=self.get_response(request)
        if request.path=='/api/bridge/upm/sync/' and response.status_code==200:
            body=json.loads(request.body)
            if (run/'preflight-failure.flag').exists() and json.loads(response.content).get('work'):
                (run/'preflight-failure.flag').unlink()
                (run/'UnityProject/Library/mtp-install-recovery.json').write_text('{"fixture":true}')
            if (run/'drop-ack.flag').exists() and (body.get('report') or {}).get('state')=='succeeded':
                (run/'drop-ack.flag').unlink(); (run/'ack-dropped').touch()
                return JsonResponse({'error':'Injected lost acknowledgement'},status=503)
            if (run/'drop-delivery.flag').exists() and json.loads(response.content).get('work'):
                (run/'drop-delivery.flag').unlink(); (run/'delivery-dropped').touch()
                return JsonResponse({'error':'Injected lost delivery'},status=503)
        return response

if __name__=='__main__':
    import os, sys
    from socketserver import ThreadingMixIn
    from wsgiref.simple_server import WSGIServer,WSGIRequestHandler,make_server
    run=Path(sys.argv[1]).resolve()
    sys.path[:0]=[str(run),str(Path(__file__).resolve().parent.parent),str(Path(__file__).resolve().parent)]
    os.environ['DJANGO_SETTINGS_MODULE']='smoke_settings'; os.environ['SERVICEBASE_MODE']='local'
    import django
    django.setup()
    from django.core.management import call_command
    from django.core.wsgi import get_wsgi_application
    from django.contrib.staticfiles.handlers import StaticFilesHandler
    call_command('migrate',verbosity=0)
    class Server(ThreadingMixIn,WSGIServer): daemon_threads=True; allow_reuse_address=True
    class Quiet(WSGIRequestHandler):
        def log_message(self,*args): pass
    server=make_server('127.0.0.1',int(sys.argv[2]),StaticFilesHandler(get_wsgi_application()),server_class=Server,handler_class=Quiet)
    (run/'server-ready').write_text(str(server.server_port))
    server.serve_forever()
