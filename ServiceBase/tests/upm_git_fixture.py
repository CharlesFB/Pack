"""HTTPS Git smart-HTTP fixture with process-scoped trust and credentials.
No global Git configuration, trust store, external Git account or TLS bypass.
"""
import base64
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import ssl
import subprocess
import threading
from urllib.parse import urlsplit


def create_git_fixture(run, package_name, version):
    base = run / 'git-fixture'; base.mkdir()
    work = base / 'source'; work.mkdir()
    package = work / 'Unity/Fixture'; package.mkdir(parents=True)
    (package/'package.json').write_text(json.dumps({'name':package_name,'version':version}))
    subprocess.run(['git','init','-q',str(work)],check=True)
    subprocess.run(['git','-C',str(work),'add','.'],check=True)
    subprocess.run(['git','-C',str(work),'-c','user.name=Smoke','-c','user.email=smoke@example.invalid','commit','-qm','Isolated fixture'],check=True)
    commit = subprocess.check_output(['git','-C',str(work),'rev-parse','HEAD'],text=True).strip()
    subprocess.run(['git','clone','--bare','-q',str(work),str(base/'fixture.git')],check=True)
    config = base/'tls.cnf'
    config.write_text('[req]\ndistinguished_name=dn\nx509_extensions=v3\nprompt=no\n[dn]\nCN=localhost\n[v3]\nsubjectAltName=DNS:localhost,IP:127.0.0.1\nbasicConstraints=critical,CA:TRUE\nkeyUsage=critical,digitalSignature,keyEncipherment,keyCertSign\n')
    cert, key = base/'cert.pem', base/'key.pem'
    subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-nodes','-days','1','-config',str(config),'-keyout',str(key),'-out',str(cert)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    stats = {'challenges':0,'authorized':0,'upload_pack':0}
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args): pass
        def do_GET(self): self.handle_git()
        def do_POST(self): self.handle_git()
        def handle_git(self):
            expected = 'Basic '+base64.b64encode(b'smoke:fixture-only-password').decode()
            if self.headers.get('Authorization') != expected:
                stats['challenges'] += 1
                self.send_response(401); self.send_header('WWW-Authenticate','Basic realm="isolated-smoke"'); self.end_headers(); return
            stats['authorized'] += 1
            uri = urlsplit(self.path)
            if uri.path.endswith('/git-upload-pack'): stats['upload_pack'] += 1
            body = self.rfile.read(int(self.headers.get('Content-Length','0')))
            env = dict(os.environ,GIT_PROJECT_ROOT=str(base),GIT_HTTP_EXPORT_ALL='1',PATH_INFO=uri.path,
                       REQUEST_METHOD=self.command,QUERY_STRING=uri.query,CONTENT_TYPE=self.headers.get('Content-Type',''),
                       CONTENT_LENGTH=str(len(body)),REMOTE_USER='smoke',REMOTE_ADDR='127.0.0.1',HTTP_GIT_PROTOCOL=self.headers.get('Git-Protocol',''))
            result = subprocess.run(['git','http-backend'],input=body,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env,check=True)
            header, content = result.stdout.split(b'\r\n\r\n',1)
            headers=[line.decode().split(':',1) for line in header.split(b'\r\n')]
            status=next((int(v.strip().split()[0]) for k,v in headers if k.lower()=='status'),200)
            self.send_response(status)
            for k,v in headers:
                if k.lower()!='status': self.send_header(k,v.strip())
            self.send_header('Content-Length',str(len(content))); self.end_headers(); self.wfile.write(content)
    server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
    context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); context.load_cert_chain(cert,key)
    server.socket=context.wrap_socket(server.socket,server_side=True)
    threading.Thread(target=server.serve_forever,daemon=True).start()
    helper=base/'credential-helper'
    helper.write_text('#!/bin/sh\nif [ "$1" = get ]; then\n  echo username=smoke\n  echo password=fixture-only-password\nfi\n'); helper.chmod(0o700)
    env={'GIT_SSL_CAINFO':str(cert),'GIT_CONFIG_COUNT':'2','GIT_CONFIG_KEY_0':'credential.helper','GIT_CONFIG_VALUE_0':'',
         'GIT_CONFIG_KEY_1':'credential.helper','GIT_CONFIG_VALUE_1':str(helper),'GIT_TERMINAL_PROMPT':'0'}
    target='https://127.0.0.1:'+str(server.server_port)+'/fixture.git?path=/Unity/Fixture#'+commit
    return server,target,env,stats
