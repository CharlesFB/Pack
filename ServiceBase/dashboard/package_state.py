"""Independent executor package baseline; no MTP snapshots or credentials."""
import json
import re
from datetime import timedelta
from django.utils import timezone
from django.utils.dateparse import parse_datetime
from .models import ProjectPackageState

SHA = re.compile('[a-f0-9]{64}')
NAME = re.compile(r'[a-zA-Z0-9][a-zA-Z0-9._-]{2,159}')


def accept(project, payload):
    if not isinstance(payload, dict) or len(json.dumps(payload, ensure_ascii=False).encode()) > 1500000:
        raise ValueError('工程包状态超限')
    if not SHA.fullmatch(payload.get('fingerprint', '')):
        raise ValueError('工程指纹无效')
    if not isinstance(payload.get('error', ''), str) or len(payload.get('error', '')) > 500:
        raise ValueError('工程状态错误摘要无效')
    scanned = parse_datetime(payload.get('captured_at', ''))
    if not scanned or not timezone.is_aware(scanned) or scanned > timezone.now()+timedelta(seconds=30):
        raise ValueError('工程状态时间无效')
    for group in ('packages', 'builtins'):
        rows = payload.get(group)
        if not isinstance(rows, list) or len(rows) > 500:
            raise ValueError('工程包清单无效')
        names = set()
        for row in rows:
            if not isinstance(row, dict) or not NAME.fullmatch(row.get('name', '')) or row['name'] in names:
                raise ValueError('工程包身份无效')
            names.add(row['name'])
            for key in ('version', 'source', 'identifier', 'commit'):
                if not isinstance(row.get(key, ''), str) or len(row.get(key, '')) > 2048:
                    raise ValueError('工程包字段无效')
            if type(row.get('direct', False)) is not bool:
                raise ValueError('工程依赖类型无效')
            dependencies = row.get('dependencies', [])
            if not isinstance(dependencies, list) or len(dependencies) > 300:
                raise ValueError('工程依赖数量超限')
            for dep in dependencies:
                if not isinstance(dep, dict) or not NAME.fullmatch(dep.get('name', '')) or not isinstance(dep.get('version'), str) or len(dep['version']) > 2048:
                    raise ValueError('工程依赖字段无效')
    assets = payload.get('assets_packages', [])
    if not isinstance(assets,list) or len(assets)>500:
        raise ValueError('Assets 本地包清单超限')
    roots=set()
    from .plugin_delivery import asset_path
    for row in assets:
        if not isinstance(row,dict) or not NAME.fullmatch(row.get('name','')) or not SHA.fullmatch(row.get('manifest_sha256','')):
            raise ValueError('Assets 本地包身份无效')
        root=asset_path(row.get('root',''))
        if root in roots:raise ValueError('重复的 Assets 包目录')
        roots.add(root)
        for key,limit in (('version',256),('display_name',256),('description',2000),('warning',500)):
            if not isinstance(row.get(key,''),str) or len(row.get(key,''))>limit:raise ValueError('Assets 包展示字段超限')
        deps=row.get('dependencies',[])
        if not isinstance(deps,list) or len(deps)>300 or any(not isinstance(d,dict) or not NAME.fullmatch(d.get('name','')) or not isinstance(d.get('version'),str) or len(d['version'])>2048 for d in deps):
            raise ValueError('Assets 包依赖字段无效')
    registries = payload.get('registries', [])
    if not isinstance(registries, list) or len(registries) > 32:
        raise ValueError('Registry 列表无效')
    from urllib.parse import urlsplit
    for registry in registries:
        if not isinstance(registry, dict) or not isinstance(registry.get('url'), str) or len(registry['url']) > 2048:
            raise ValueError('Registry 地址无效')
        uri = urlsplit(registry['url'])
        if uri.scheme != 'https' or uri.username or uri.password or uri.query or uri.fragment or not uri.hostname:
            raise ValueError('Registry 地址必须为无凭据 HTTPS')
        if not isinstance(registry.get('scopes'), list) or len(registry['scopes']) > 100 or any(not isinstance(s,str) or not re.fullmatch(r'[a-zA-Z0-9._-]{1,160}',s) for s in registry['scopes']):
            raise ValueError('Registry scope 无效')
    previous = ProjectPackageState.objects.filter(project=project).first()
    if previous and previous.session_id == project.session_id:
        if payload == previous.payload or scanned < parse_datetime(previous.payload['captured_at']):
            return
    ProjectPackageState.objects.update_or_create(project=project, defaults={'session_id':project.session_id, 'payload':payload})


def current(project):
    row = ProjectPackageState.objects.filter(project=project, session_id=project.session_id).first()
    if not row or row.payload.get('error'):
        raise ValueError('请连接 v7 或以上执行器并等待独立工程包状态上报')
    captured = parse_datetime(row.payload['captured_at'])
    if timezone.now()-captured > timedelta(seconds=120):
        raise ValueError('工程包状态已过期，请等待 Unity 刷新后重新预览')
    return row.payload
