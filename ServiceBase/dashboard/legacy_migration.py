"""Opt-in legacy record migration. Reads are requested explicitly; writes use the project job lock."""
import json
import re
import uuid
from datetime import timedelta
from django.core.cache import cache
from django.db import transaction
from django.http import JsonResponse
from django.utils import timezone
from django.views.decorators.http import require_http_methods, require_POST
from .auth import project_scope
from .models import UnityProject, LegacyMigration, InstallJob, InstallStep, InstallAttempt

SOURCES = {'Packages/upm-install.json', 'Packages/upm-local.json', 'Packages/ump-lock.json',
           'Library/upm-dependencies.json', 'Library/MTPCache/upm-dependencies.json', 'Packages/plugin-assets.json'}
KINDS = {'plugin', 'root', 'local', 'dependency'}
SHA = re.compile(r'[a-f0-9]{64}')


def dto(row):
    stale = row.session_id != row.project.session_id or timezone.now()-row.created_at > timedelta(minutes=30)
    return dict(id=str(row.pk), state=row.state, digest=row.digest, preview=row.payload,
                expired=stale, job_id=str(row.job_id) if row.job_id else None,
                created_at=row.created_at.isoformat(), waiting_too_long=row.state=='pending' and timezone.now()-row.created_at>timedelta(seconds=90))


@require_http_methods(['GET', 'POST'])
def previews(request, instance_id):
    project = project_scope(request, UnityProject.objects).filter(pk=instance_id).first()
    if not project:
        return JsonResponse({'error': '工程不存在或无访问权限'}, status=404)
    from .installations import executor_key
    if request.method == 'GET':
        return JsonResponse(dict(ready=bool(cache.get(executor_key(project)+':legacy-migration')),
            previews=[dto(r) for r in LegacyMigration.objects.filter(project=project).select_related('project').order_by('-created_at')[:10]]))
    try:
        key = uuid.UUID(json.loads(request.body)['request_key'])
        with transaction.atomic():
            project = UnityProject.objects.select_for_update().get(pk=project.pk)
            previous = LegacyMigration.objects.filter(project=project, request_key=key).first()
            if previous:
                return JsonResponse(dto(previous))
            if not cache.get(executor_key(project)+':legacy-migration'):
                return JsonResponse({'error': '请连接 v9 执行器后读取旧记录'}, status=409)
            if InstallJob.objects.filter(project=project, active=True).exists():
                return JsonResponse({'error': '请先完成当前工程任务'}, status=409)
            # A new explicit scan supersedes an unconfirmed read request only.
            LegacyMigration.objects.filter(project=project, state='pending').update(state='superseded')
            row = LegacyMigration.objects.create(project=project, session_id=project.session_id, request_key=key)
        return JsonResponse(dto(row), status=202)
    except (ValueError, KeyError, TypeError):
        return JsonResponse({'error': '请提供有效迁移请求标识'}, status=400)


def validate_preview(value):
    if not isinstance(value, dict) or len(json.dumps(value).encode()) > 1500000:
        raise ValueError('迁移预览超限')
    error = value.get('error', '')
    if not isinstance(error, str) or len(error) > 500:
        raise ValueError('迁移错误摘要无效')
    rows = value.get('entries')
    if not isinstance(rows, list) or len(rows)>500 or (not error and not SHA.fullmatch(value.get('preview_digest',''))):
        raise ValueError('迁移预览格式无效')
    ids = set()
    for item in rows:
        if not isinstance(item,dict) or item.get('kind') not in KINDS or item.get('source_file') not in SOURCES:
            raise ValueError('旧记录类型无效')
        for field in ('id','source_digest','entry_digest'):
            if not SHA.fullmatch(item.get(field,'')): raise ValueError('旧记录指纹无效')
        if item['id'] in ids: raise ValueError('重复迁移记录')
        ids.add(item['id'])
        for field in ('name','package_name','tag','revision','warning'):
            text=item.get(field,'')
            if not isinstance(text,str) or len(text)>500 or '://' in text or '@' in text or any(ord(c)<32 for c in text):
                raise ValueError('迁移展示字段无效')
        if type(item.get('migrated')) is not bool or type(item.get('is_local')) is not bool:
            raise ValueError('迁移标记无效')
        if any(type(item.get(k)) is not int or not 0<=item[k]<=10000 for k in ('file_count','missing')) or item['missing']>item['file_count']:
            raise ValueError('旧插件资源数量无效')
        if item.get('files') not in (None,[]): raise ValueError('预览只接收摘要，不接收原始旧记录')
        roots=item.get('roots')
        if not isinstance(roots,list) or len(roots)>8 or any(not isinstance(p,str) or not p.startswith('Assets/') or len(p)>512 or '..' in p.split('/') for p in roots):
            raise ValueError('旧插件根目录无效')
    # Store only the documented sanitized summary fields.
    fields=('id','kind','name','package_name','tag','revision','source_file','source_digest','entry_digest','warning','is_local','migrated','roots','file_count','missing')
    return dict(error=error, entries=[{k:r[k] for k in fields} for r in rows])


def exchange(project, result, enabled):
    if not enabled: return None
    if result:
        request_id = uuid.UUID(result.get('request_id',''))
        row = LegacyMigration.objects.filter(pk=request_id, project=project, session_id=project.session_id, state='pending').first()
        if row:
            row.payload = validate_preview(result)
            row.digest = result.get('preview_digest','') or ''
            row.state = 'failed' if row.payload['error'] else 'preview'
            row.save(update_fields=['payload','digest','state','updated_at'])
    if InstallJob.objects.filter(project=project,active=True).exists(): return None
    row = LegacyMigration.objects.filter(project=project, session_id=project.session_id, state='pending',created_at__gt=timezone.now()-timedelta(minutes=30)).order_by('-created_at').first()
    return dict(request_id=str(row.pk)) if row else None


@require_POST
def confirm(request, instance_id, migration_id):
    from .installations import executor_key, dto as job_dto
    try:
        data=json.loads(request.body); selected=data['entries']
        if not isinstance(selected,list) or not selected or len(selected)>500 or any(not isinstance(s,str) or not SHA.fullmatch(s) for s in selected) or len(set(selected))!=len(selected):
            raise ValueError('请逐项选择需要迁移的记录')
        selected=sorted(selected)
        with transaction.atomic():
            project=project_scope(request,UnityProject.objects).select_for_update().filter(pk=instance_id).first()
            if not project: return JsonResponse({'error':'工程不存在或无访问权限'},status=404)
            row=LegacyMigration.objects.select_related('project','job').filter(pk=migration_id,project=project).first()
            if not row:return JsonResponse({'error':'迁移预览不存在'},status=404)
            if data.get('digest')!=row.digest:raise ValueError('迁移预览指纹已变化')
            if row.job:
                if row.job.target['migration_entries']!=selected:raise ValueError('已确认的迁移选择不可修改')
                return JsonResponse({'job':job_dto(row.job)})
            if row.state!='preview' or dto(row)['expired']:raise ValueError('预览过期或尚未完成，请重新读取')
            allowed={e['id'] for e in row.payload['entries'] if not e['migrated']}
            if not set(selected)<=allowed:raise ValueError('选择包含已迁移或不在预览中的记录')
            if not cache.get(executor_key(project)+':legacy-migration') or InstallJob.objects.filter(project=project,active=True).exists():
                return JsonResponse({'error':'执行器未就绪或工程有未结束任务'},status=409)
            target=dict(operation='legacy_migrate',source='legacy',name='旧记录迁移',identifier='仅迁移所选记录，不修改工程资源',
                migration_id=str(row.pk),migration_digest=row.digest,migration_entries=selected)
            row.job=InstallJob.objects.create(project=project,request_key=row.pk,target=target)
            InstallAttempt.objects.create(step=InstallStep.objects.create(job=row.job))
            row.state='confirmed';row.save(update_fields=['job','state','updated_at'])
        return JsonResponse({'job':job_dto(row.job)},status=202)
    except (ValueError, KeyError, TypeError) as error:
        return JsonResponse({'error':str(error) if isinstance(error,ValueError) else '迁移参数无效'},status=400)


def success(attempt, evidence):
    target=attempt.step.command
    if (not isinstance(evidence,dict) or evidence.get('verified') is not True
            or any(evidence.get(k)!=target[k] for k in ('migration_id','migration_digest'))
            or not SHA.fullmatch(evidence.get('record_digest',''))):
        raise ValueError('缺少迁移落盘核验证据')
    row=LegacyMigration.objects.get(job=attempt.step.job)
    row.state='completed';row.save(update_fields=['state','updated_at'])
