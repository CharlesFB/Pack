"""Temporary loopback fixture for browser QA, explicitly labelled as a snapshot.

Usage: .venv/bin/python tests/mtp_ui_smoke.py .runtime/mtp-api-snapshot.json
The JSON is produced by MtpApiSmoke.cs using the real MTP API, not hand-authored.
"""
import json
import os
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, ProxyHandler, build_opener

snapshot = json.load(open(sys.argv[1]))
snapshot.update(instance_id='mtp-api-ui-smoke', session_id='mtp-api-ui-smoke')
snapshot['detail'] = '浏览器验证：Pack Test 快照经真实 MTP API 导出；非运行中的 Pack Test'


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        if self.path != '/mtp/' or self.headers.get('X-ServiceBase-Token') != 'smoke-only':
            self.send_error(404)
            return
        data = json.dumps(snapshot).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)


server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
registration = dict(instance_id='mtp-api-ui-smoke', session_id='mtp-api-ui-smoke',
                    project_name='MTP API 验证（Pack Test 快照）', project_path='测试快照 / 非实时工程',
                    machine_name='本地测试', unity_version='2022.3 API', process_id=os.getpid(),
                    port=server.server_port, token='smoke-only')
try:
    while True:
        req = Request('http://127.0.0.1:8000/api/heartbeat/', data=json.dumps(registration).encode(),
                      headers={'Content-Type':'application/json', 'X-ServiceBase-Client':'unity-bridge-v1'})
        with build_opener(ProxyHandler({})).open(req, timeout=3) as response:
            response.read()
        time.sleep(3)
except KeyboardInterrupt:
    pass
finally:
    server.shutdown()
    server.server_close()
