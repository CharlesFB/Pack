#!/usr/bin/env python3
"""临时 HTTPS 服务的初始化、启停与成员凭据管理。"""
import argparse
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[2]  # 工程根目录，不依赖当前 shell 工作目录。
ENV = ROOT / '.runtime/quick.env'  # 独立生产密钥及本次临时域名，不提交 Git。
DOCKER = shutil.which('docker') or '/Applications/Docker.app/Contents/Resources/bin/docker'
os.environ['PATH'] = str(Path(DOCKER).parent) + os.pathsep + os.environ.get('PATH','')


def private_write(
    path,  # 私有输出文件路径
    text,  # 待保存的配置或凭据，不写入控制台
):
    """以仅当前用户可读写的权限保存配置或凭据。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    os.fchmod(fd, 0o600)
    with os.fdopen(fd, 'w') as stream:
        stream.write(text)


def compose(
    *args,  # Docker Compose 子命令和参数
    capture=False,  # 是否捕获输出，避免凭据进入终端
    input=None,  # 通过 stdin 传递的私有数据
    stdout=None,  # 大型备份直接写文件，不进入控制台
):
    """在独立临时部署栈中执行 Compose 命令，失败立即停止当前操作。"""
    return subprocess.run([DOCKER,'compose','--env-file',str(ENV),'-f',str(ROOT/'deploy/quick/compose.yaml'),*args],
                          check=True, text=True, capture_output=capture, input=input, stdout=stdout, cwd=ROOT)

def check_docker():
    """检查 Docker 是否可用，避免后续命令失败。"""
    try:
        subprocess.run([DOCKER,'info'], check=True, text=True, capture_output=True)
    except Exception as e:
        raise RuntimeError('Docker 不可用，请先安装并启动 Docker Desktop。') from e

def provision(
    username,  # 新建或已存在的账号名
    device=None,  # 成员本机设备名称；为空时仅初始化管理员
):
    """首次生成账号及设备凭据，只在本机私有文件保存原文，不重置已有密码。"""
    payload = {'username':username,'device':device,'password':secrets.token_urlsafe(24),'token':secrets.token_urlsafe(32)}
    code = '''import sys,json,hashlib
from django.contrib.auth import get_user_model
from dashboard.models import BridgeCredential
from django.db import transaction
p=json.load(sys.stdin)
with transaction.atomic():
    U=get_user_model()
    if p['device'] and BridgeCredential.objects.filter(owner__username=p['username'],name=p['device']).exists():
        raise ValueError('Device name already exists; use a new device name.')
    user=U.objects.filter(username=p['username']).first()
    created=user is None
    if created:
        user=U.objects.create_user(p['username'],password=p['password'],is_staff=p['device'] is None,is_superuser=p['device'] is None)
    if not user.is_active or (not p['device'] and not user.is_staff):
        raise ValueError('Existing account is not an active administrator.')
    if p['device']:
        if user.is_staff: raise ValueError('Use a regular member account for device isolation.')
        BridgeCredential.objects.create(owner=user,name=p['device'],digest=hashlib.sha256(p['token'].encode()).hexdigest())
    print(json.dumps({'username':user.username,'password':p['password'] if created else '(已有账号，密码未修改)','device':p['device'],'device_token':p['token'] if p['device'] else None}))
'''
    result = compose('run','--rm','-T','web','python','manage.py','shell','-c',code,capture=True,input=json.dumps(payload))
    values = json.loads(result.stdout.strip().splitlines()[-1])
    if not device and values['password'].startswith('('):
        print('管理员已存在，保留原密码。')
        return
    filename = 'admin' if device is None else 'member-' + secrets.token_hex(4)
    path = ROOT / '.runtime/quick-credentials' / (filename + '.json')
    private_write(path,json.dumps(values,ensure_ascii=False,indent=2)+'\n')
    print('凭据已保存：' + str(path))


def initialize():
    """准备独立生产数据卷、数据库结构、静态文件和首次管理员账号。"""
    if not ENV.exists():
        private_write(ENV,'DOMAIN=pending.invalid\nSECRET_KEY='+secrets.token_urlsafe(64)+'\nPOSTGRES_PASSWORD='+secrets.token_urlsafe(32)+'\n')
    compose('build','web','maintenance')
    compose('up','-d','db','redis')
    compose('run','--rm','web','python','manage.py','migrate')
    compose('run','--rm','web','python','manage.py','collectstatic','--noinput')
    provision('admin')


def start():
    """创建临时隧道，提取唯一域名后配置生产主机白名单，再启动网页服务。"""
    compose('up','-d','db','redis')
    compose('up','-d','--no-deps','--force-recreate','tunnel')
    sync_tunnel()


def update():
    """更新现有部署，保留启停状态和隧道；迁移失败时不启动网页。"""
    if not ENV.exists():
        raise RuntimeError('尚未初始化，请先使用 init。')
    if not (ROOT/'external/index.json').is_file():
        raise RuntimeError('缺少 external/index.json，请先准备插件仓库。')
    check_docker()
    running = set(compose('ps','--status','running','--services',capture=True).stdout.split())
    compose('build','web','maintenance')
    applications = ('web','proxy','maintenance')
    resume = [name for name in applications if name in running]
    temporary_services = [name for name in ('redis','db') if name not in running]
    # Stop writers before the backup and schema migration. Unity retains its local journal.
    if resume:
        compose('stop',*resume)
    try:
        compose('up','-d','--wait','db','redis')
        backup_database()
        compose('run','--rm','--no-deps','web','python','manage.py','migrate','--noinput')
        compose('run','--rm','--no-deps','web','python','manage.py','collectstatic','--noinput')
        compose('run','--rm','--no-deps','web','python','manage.py','check')
        compose('run','--rm','--no-deps','web','python','manage.py','check_installation_deployment')
        # Replace stopped containers too, so a later Docker Desktop start uses the new image.
        compose('up','--no-start','--no-deps','--no-build',*applications)
        if resume:
            compose('up','-d','--no-deps','--no-build',*resume)
    finally:
        if temporary_services:
            compose('stop',*temporary_services)
    print('部署已更新，保留更新前的启停状态；隧道未改动。需要启动时执行 start。')


def backup_database():
    """在停止应用写入后保存升级前的 PostgreSQL 备份。"""
    directory = ROOT/'.runtime/backups'
    directory.mkdir(parents=True,exist_ok=True)
    backup = directory/('quick-'+time.strftime('%Y%m%d-%H%M%S')+'-'+secrets.token_hex(3)+'.dump')
    temporary = backup.with_suffix('.tmp')
    try:
        fd = os.open(temporary,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
        with os.fdopen(fd,'wb') as stream:
            compose('exec','-T','db','pg_dump','-U','servicebase','-d','servicebase','-Fc',stdout=stream)
        temporary.replace(backup)
    finally:
        temporary.unlink(missing_ok=True)
    print('升级前备份：'+str(backup),flush=True)


def sync_tunnel():
    """读取当前运行隧道的域名并同步白名单，不重建隧道或读取旧启动日志。"""
    container = compose('ps','--status','running','-q','tunnel',capture=True).stdout.strip()
    if not container:
        raise RuntimeError('隧道未运行，请使用 start 启动。')
    started = subprocess.run([DOCKER,'inspect','--format','{{.State.StartedAt}}',container],
                             check=True,text=True,capture_output=True).stdout.strip()
    deadline = time.monotonic() + 60
    hostname = None
    while time.monotonic() < deadline:
        logs = compose('logs','--no-color','--since',started,'tunnel',capture=True)
        matches = re.findall(r'https://([a-z0-9-]+\.trycloudflare\.com)',logs.stdout + logs.stderr)
        if matches:
            hostname = matches[-1]
            break
        time.sleep(1)
    if not hostname:
        raise RuntimeError('临时隧道没有返回地址，请检查网络和 tunnel 日志。')
    values = [line for line in ENV.read_text().splitlines() if not line.startswith('DOMAIN=')]
    private_write(ENV,'DOMAIN='+hostname+'\n'+'\n'.join(values)+'\n')
    compose('up','-d','web','proxy','maintenance')
    private_write(ROOT/'.runtime/quick-url.txt','https://'+hostname+'\n')
    print('临时访问地址：https://'+hostname)


def main():
    """解析运维动作；停止服务保留数据卷，成员凭据单独保存以便分发。"""
    parser=argparse.ArgumentParser()
    parser.add_argument('action',choices=['init','update','start','sync','stop','status','member'])
    parser.add_argument('username',nargs='?')
    parser.add_argument('device',nargs='?')
    args=parser.parse_args()
    if args.action=='init': initialize()
    elif args.action=='update': update()
    elif args.action=='start': start()
    elif args.action=='sync': sync_tunnel()
    elif args.action=='stop': compose('stop')
    elif args.action=='status': compose('ps')
    elif not args.username or not args.device: parser.error('member 需要账号名和设备名称')
    else: provision(args.username,args.device)


if __name__=='__main__':
    main()
