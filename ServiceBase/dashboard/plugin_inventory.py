"""Current Unity-owned plugin inventory; historical import receipts are not live status."""
import json
import re
import uuid
from datetime import timedelta
from django.http import JsonResponse
from django.utils import timezone
from django.utils.dateparse import parse_datetime
from django.views.decorators.http import require_POST
from .auth import project_scope
from .models import UnityProject, PluginInventory, PluginReceipt


def current(project):
    row = PluginInventory.objects.filter(project=project).first()
    if not row:
        return {'fresh': False, 'installations': [], 'error': '', 'scanning': True}
    scanned = parse_datetime(row.payload.get('scanned_at', ''))
    from .installations import executor_status
    fresh = bool(row.session_id == project.session_id and scanned and timezone.is_aware(scanned)
                 and timedelta(seconds=-30) <= timezone.now() - scanned < timedelta(seconds=120)
                 and executor_status(project)['online'] and not row.payload.get('error'))
    return dict({'installations': [], 'error': '', **row.payload}, fresh=fresh, received_at=row.received_at.isoformat(),
                scanning=row.request_key != row.payload.get('request_key'))


def exchange(project, payload):
    row, _ = PluginInventory.objects.get_or_create(project=project)
    if payload is not None:
        if not isinstance(payload, dict) or len(json.dumps(payload,ensure_ascii=False).encode('utf-8')) > 1800000:
            raise ValueError('插件状态快照超限')
        scanned = parse_datetime(payload.get('scanned_at', ''))
        if not scanned or not timezone.is_aware(scanned) or scanned > timezone.now() + timedelta(seconds=30):
            raise ValueError('插件扫描时间无效')
        entries = payload.get('installations')
        if not isinstance(entries, list) or len(entries) > 2000 or not isinstance(payload.get('error', ''), str) or len(payload.get('error', '')) > 500:
            raise ValueError('无效插件状态清单')
        keys = set()
        for item in entries:
            if not isinstance(item, dict) or item.get('status') not in ('installed', 'modified', 'missing', 'partial','recorded'):
                raise ValueError('无效插件状态')
            if item.get('import_result','') not in ('','completed','reloaded','failed','cancelled','persistence_failed','compile_error','unknown'):
                raise ValueError('无效插件导入结果')
            key = item.get('record_key', '')
            if not re.fullmatch(r'[a-zA-Z0-9_.-]{1,256}', key) or key in keys or not re.fullmatch('[a-f0-9]{64}', item.get('record_digest', '')):
                raise ValueError('无效插件记录身份')
            keys.add(key)
            if not isinstance(item.get('plugin_id'), str) or len(item['plugin_id']) > 160 or not re.fullmatch('[a-f0-9]{64}', item.get('build_id', '')):
                raise ValueError('无效插件构建')
            if any(type(item.get(k)) is not int or not 0 <= item[k] <= 10000 for k in ('file_count', 'matched', 'modified', 'missing')) or item['file_count'] != sum(item[k] for k in ('matched', 'modified', 'missing')):
                raise ValueError('插件状态计数不一致')
            if not isinstance(item.get('destination_root', ''), str) or len(item.get('destination_root', '')) > 512:
                raise ValueError('无效插件根目录')
            if any(not isinstance(item.get(k, []), list) or len(item.get(k, [])) > 8 or any(not isinstance(p, str) or len(p) > 2048 for p in item.get(k, [])) for k in ('roots', 'samples')):
                raise ValueError('插件路径摘要超限')
        legacy = payload.get('legacy_installations', [])
        if not isinstance(legacy,list) or len(legacy)>500:raise ValueError('迁移记录状态超限')
        from .legacy_migration import SOURCES, KINDS, SHA
        ids=set()
        for item in legacy:
            if not isinstance(item,dict) or not SHA.fullmatch(item.get('id','')) or item['id'] in ids or item.get('kind') not in KINDS or item.get('source_file') not in SOURCES or not SHA.fullmatch(item.get('source_digest','')):
                raise ValueError('迁移记录身份无效')
            ids.add(item['id'])
            if item.get('status') not in ('recorded','baseline_matches','modified','partial'):raise ValueError('迁移状态无效')
            for field in ('name','package_name'):
                if not isinstance(item.get(field),str) or len(item[field])>256:raise ValueError('迁移展示字段无效')
            if any(type(item.get(k)) is not int or not 0<=item[k]<=10000 for k in ('file_count','matched','modified','missing')) or item['file_count']!=sum(item[k] for k in ('matched','modified','missing')):raise ValueError('迁移核对数量不一致')
            if not isinstance(item.get('roots'),list) or len(item['roots'])>8 or any(not isinstance(p,str) or not p.startswith('Assets/') or len(p)>512 for p in item['roots']):raise ValueError('迁移路径摘要无效')
        # Ignore delayed scans from the same editor session.
        if row.session_id != project.session_id or (payload != row.payload and payload['scanned_at'] >= row.payload.get('scanned_at', '')):
            row.session_id, row.payload = project.session_id, payload
            row.save(update_fields=['session_id', 'payload', 'received_at'])
    return row.request_key


@require_POST
def refresh(request, instance_id):
    from django.db import transaction
    with transaction.atomic():
        project = project_scope(request, UnityProject.objects).select_for_update().filter(pk=instance_id).first()
        if not project:
            return JsonResponse({'error': '工程不存在或无访问权限'}, status=404)
        row, _ = PluginInventory.objects.get_or_create(project=project)
        row.request_key = str(uuid.uuid4())
        row.save(update_fields=['request_key'])
    return JsonResponse({'ok': True}, status=202)


def removal_target(project, data):
    key, digest = data.get('record_key', ''), data.get('record_digest', '')
    snapshot = current(project)
    if not snapshot['fresh'] or snapshot['scanning']:
        raise ValueError('请等待 Unity 完成本轮插件状态核对')
    item = next((r for r in snapshot['installations'] if r['record_key'] == key and r['record_digest'] == digest), None)
    if not item:
        raise ValueError('插件归属记录已变化，请刷新后重试')
    saved = PluginReceipt.objects.filter(digest=digest, attempt__step__job__project=project).select_related('attempt__step__job').first()
    if not saved:
        raise ValueError('服务端缺少此插件的原始归属证据，不能删除')
    original = saved.attempt.step.command
    if original['plugin_id'] != item['plugin_id'] or original['build_id'] != item['build_id']:
        raise ValueError('插件记录与导入证据不一致')
    return dict(operation='plugin_remove', source='plugin', name=original['name'],
                identifier=original['identifier'], plugin_id=item['plugin_id'], build_id=item['build_id'],
                record_key=key, record_digest=digest, file_count=len(saved.files),
                destination_root=saved.attempt.destination_root)


def removal_success(attempt, evidence):
    from .models import PluginRemovalReceipt
    target = attempt.step.command
    if not isinstance(evidence, dict): raise ValueError('无效删除核验证据')
    saved = PluginRemovalReceipt.objects.filter(attempt=attempt, digest=evidence.get('record_digest', '')).first()
    if not saved or evidence.get('build_id') != target['build_id']:
        raise ValueError('缺少插件删除核验证据')
