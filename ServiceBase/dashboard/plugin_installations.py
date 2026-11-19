"""Plugin commands and device-scoped delivery/ownership evidence."""
import hashlib
import json
import re
from django.db import transaction
from django.http import FileResponse, JsonResponse
from django.views.decorators.http import require_GET
from .auth import project_scope
from .bridge import native, checked_session
from .models import UnityProject, InstallJob, InstallAttempt, PluginReceipt, PluginInstallation
from .plugin_delivery import delivery_root, placed_files, destination_path


def scoped_attempt(data, identity, operation=("plugin","plugin_update")):
    project_id=checked_session(data,identity)
    if project_id is None: raise ValueError('工程会话已失效')
    attempt=InstallAttempt.objects.select_related('step__job__project').get(pk=data['attempt_id'],step__job__project_id=project_id)
    project=attempt.step.job.project
    if project.session_id!=data['session_id'] or any(getattr(project,k)!=identity[k] for k in ('owner_id','credential_id')): raise ValueError('工程会话已失效')
    if attempt.step.command.get('operation') not in ((operation,) if isinstance(operation,str) else operation) or attempt.dispatched_at is None: raise ValueError('未派发的插件任务')
    return attempt

@native
def destination(request,data,identity):
    chosen=destination_path(data.get('destination_root'))
    try: attempt=scoped_attempt(data,identity)
    except InstallAttempt.DoesNotExist: return JsonResponse({'error':'插件任务不存在'},status=404)
    with transaction.atomic():
        UnityProject.objects.select_for_update().get(pk=attempt.step.job.project_id)
        job=InstallJob.objects.select_for_update().get(pk=attempt.step.job_id)
        attempt.refresh_from_db()
        if not attempt.step.command.get('requires_destination') or not job.active or job.step.attempt.pk!=attempt.pk:
            raise ValueError('该任务不能选择 ZIP 导入目录')
        if attempt.destination_root:
            if attempt.destination_root!=chosen: raise ValueError('已确认的导入目录不可更改')
        else:
            if job.step.state not in ('accepted','waiting_input'): raise ValueError('任务已进入执行或恢复阶段')
            files=placed_files(attempt.step.command,chosen)
            if sum(len(f['path'].encode('utf-8'))*3+500 for f in files)>2200000: raise ValueError('所选目录过长，文件归属记录将超出限制')
            attempt.destination_root=chosen
            attempt.save(update_fields=['destination_root'])
    return JsonResponse({'ok':True,'destination_root':chosen})

@native
def download(request,data,identity):
    try: attempt=scoped_attempt(data,identity)
    except InstallAttempt.DoesNotExist: return JsonResponse({'error':'插件任务不存在'},status=404)
    target=attempt.step.command
    path=delivery_root()/target['bundle_sha256']/'bundle.zip'
    if not path.is_file(): return JsonResponse({'error':'导入制品缓存缺失，请恢复服务器缓存'},status=409)
    return FileResponse(path.open('rb'),content_type='application/zip',as_attachment=True,filename='plugin.zip')

@native
def receipt(request,data,identity):
    try: attempt=scoped_attempt(data,identity)
    except InstallAttempt.DoesNotExist: return JsonResponse({'error':'插件任务不存在'},status=404)
    target=attempt.step.command
    expected=placed_files(target,attempt.destination_root)
    # Preserve the exact client JSON digest; Unity hashes this UTF-8 JSON string too.
    raw=data.get('record_json','')
    if not isinstance(raw,str) or len(raw)>2400000: raise ValueError('归属记录超限')
    record=json.loads(raw); files=record.get('files')
    from .plugin_import_verification import imported_snapshot, archive_execution
    execution=archive_execution(target,record,expected)
    post_import,expected=(False,expected) if execution else imported_snapshot(target,record,expected)
    if target.get('requires_destination') and record.get('destination_root')!=attempt.destination_root: raise ValueError('归属记录目标目录不符')
    if record.get('plugin_id')!=target['plugin_id'] or record.get('build_id')!=target['build_id'] or record.get('attempt_id')!=str(attempt.pk): raise ValueError('归属记录身份不符')
    if not isinstance(files,list) or len(files)!=len(expected): raise ValueError('文件核验数量不符')
    if target.get('operation')=='plugin_update':
        from .plugin_updates import validate_record
        validate_record(attempt.step.job.project,target,files)
    elif not execution:
        for actual,wanted in zip(files,expected):
            if not isinstance(actual,dict) or not re.fullmatch('[a-f0-9]{32}',actual.get('guid','')) or not re.fullmatch('[a-f0-9]{64}',actual.get('meta_sha256','')): raise ValueError('无效文件证据')
            if (actual.get('path')!=wanted['path'] or actual.get('folder') is not wanted['folder']
                    or actual.get('sha256')!=wanted['sha256']): raise ValueError('文件内容证据不符')
            before=actual.get('before_sha256'); before_meta=actual.get('before_meta_sha256')
            if before not in ('',wanted['sha256']) or not isinstance(before_meta,str) or (before_meta and not re.fullmatch('[a-f0-9]{64}',before_meta)): raise ValueError('文件覆盖基线不符')
            preserved=wanted['generated'] and bool(before_meta)
            source_meta=before_meta if preserved else wanted['meta_sha256']
            if post_import:
                if actual.get('source_meta_sha256')!=source_meta: raise ValueError('原制品 meta 证据不符')
            elif actual.get('meta_sha256')!=source_meta: raise ValueError('meta 证据不符')
            if not preserved and actual.get('guid')!=wanted['guid']: raise ValueError('GUID 证据不符')
            if not isinstance(actual.get('existed'),bool) or not isinstance(actual.get('meta_existed'),bool): raise ValueError('缺少原文件归属基线')
            if actual['existed']!=actual['meta_existed'] or actual['meta_existed']!=bool(before_meta): raise ValueError('meta 基线不一致')
            if not wanted['folder'] and actual['existed']!=bool(before): raise ValueError('文件基线不一致')
    sha=hashlib.sha256(raw.encode()).hexdigest()
    with transaction.atomic():
        # Follow the same project -> job lock order as the command protocol.
        UnityProject.objects.select_for_update().get(pk=attempt.step.job.project_id)
        attempt.step.job.refresh_from_db()
        previous=PluginReceipt.objects.filter(attempt=attempt).first()
        if previous:
            if previous.digest!=sha: raise ValueError('已保存的导入归属记录不可修改')
        else:
            if not attempt.step.job.active or attempt.step.attempt.pk!=attempt.pk: raise ValueError('任务已结束')
            PluginReceipt.objects.create(attempt=attempt,digest=sha,files=files)
    return JsonResponse({'ok':True,'digest':sha})


def success(attempt,evidence):
    target=attempt.step.command
    if not isinstance(evidence,dict): raise ValueError('无效插件证据')
    saved=PluginReceipt.objects.filter(attempt=attempt,digest=evidence.get('record_digest','')).first()
    if not saved or evidence.get('recipe_id')!=target['recipe_id'] or evidence.get('build_id')!=target['build_id']:
        raise ValueError('缺少持久化的插件归档路径记录')
    PluginInstallation.objects.update_or_create(project=attempt.step.job.project,plugin_id=target['plugin_id'],build_id=target['build_id'],defaults={'receipt':saved})

@require_GET
def records(request, instance_id):
    from .plugin_inventory import current
    from .models import PluginRemovalReceipt
    project = project_scope(request, UnityProject.objects).filter(pk=instance_id).first()
    if not project:
        return JsonResponse({'error': '工程不存在或无访问权限'}, status=404)
    snapshot = current(project)
    # Read large historical receipts only for older clients without a current inventory.
    if not snapshot.get('scanned_at'):
        rows = PluginInstallation.objects.filter(project=project).select_related('receipt__attempt')
        snapshot['installations'] = [dict(
            plugin_id=r.plugin_id, build_id=r.build_id, verified_at=r.verified_at.isoformat(),
            destination_root=r.receipt.attempt.destination_root, file_count=len(r.receipt.files),
            roots=sorted({'Assets/' + f['path'].split('/')[1] for f in r.receipt.files})[:8],
            status='verified_at_import') for r in rows]
    removals = PluginRemovalReceipt.objects.filter(
        attempt__step__job__project=project, attempt__step__state='succeeded').order_by('-received_at')[:30]
    snapshot['removals'] = [dict(
        plugin_id=r.payload['plugin_id'], build_id=r.payload['build_id'],
        removed_at=r.received_at.isoformat(),
        removed=sum(f['outcome']=='removed' for f in r.payload['files']),
        retained=sum(f['outcome']=='retained' for f in r.payload['files']),
        retained_paths=[f['path'] for f in r.payload['files'] if f['outcome']=='retained'][:8]) for r in removals]
    from .models import PluginUpdateReceipt
    updates=PluginUpdateReceipt.objects.filter(attempt__step__job__project=project,attempt__step__state='succeeded').order_by('-received_at')[:30]
    snapshot['updates']=[dict(plugin_id=r.payload['plugin_id'],build_id=r.payload['build_id'],previous_build_id=r.payload['previous_build_id'],updated_at=r.received_at.isoformat(),
        removed=sum(f['outcome']=='removed' for f in r.payload['files']),retained=sum(f['outcome']=='retained' for f in r.payload['files']),
        retained_paths=[f['path'] for f in r.payload['files'] if f['outcome']=='retained'][:8]) for r in updates]
    return JsonResponse(snapshot)


@native
def removal_receipt(request, data, identity):
    from .models import PluginRemovalReceipt
    try:
        attempt = scoped_attempt(data, identity, 'plugin_remove')
    except InstallAttempt.DoesNotExist:
        return JsonResponse({'error': '插件删除任务不存在'}, status=404)
    target = attempt.step.command
    raw = data.get('record_json', '')
    if not isinstance(raw, str) or len(raw) > 2400000:
        raise ValueError('删除记录超限')
    result = json.loads(raw)
    if not isinstance(result, dict) or any(result.get(k) != target[k] for k in ('plugin_id','build_id','record_key','record_digest')) or result.get('attempt_id') != str(attempt.pk):
        raise ValueError('删除记录身份不符')
    original = PluginReceipt.objects.filter(digest=target['record_digest'], attempt__step__job__project=attempt.step.job.project).first()
    files = result.get('files')
    if not original or not isinstance(files, list) or len(files) != len(original.files):
        raise ValueError('删除证据数量不符')
    expected = {f['path']: f for f in original.files}
    for item in files:
        if not isinstance(item, dict): raise ValueError('无效删除证据')
        wanted = expected.pop(item.get('path'), None)
        if not wanted or item.get('outcome') not in ('removed','retained','missing') or not isinstance(item.get('reason'), str) or len(item['reason']) > 120:
            raise ValueError('删除证据与原始文件清单不一致')
        if item['outcome'] == 'removed' and result.get('policy')!='archive-list-v1' and (wanted['existed'] or wanted['meta_existed']):
            raise ValueError('不能删除导入前已存在的资源')
    digest = hashlib.sha256(raw.encode()).hexdigest()
    with transaction.atomic():
        project = UnityProject.objects.select_for_update().get(pk=attempt.step.job.project_id)
        if project.session_id != data['session_id'] or any(getattr(project,k)!=identity[k] for k in ('owner_id','credential_id')):
            raise ValueError('工程会话已失效')
        attempt.step.job.refresh_from_db()
        saved = PluginRemovalReceipt.objects.filter(attempt=attempt).first()
        if saved:
            if saved.digest != digest: raise ValueError('删除回执不可更改')
        else:
            if not attempt.step.job.active or attempt.step.attempt.pk != attempt.pk: raise ValueError('任务已结束')
            PluginRemovalReceipt.objects.create(attempt=attempt, digest=digest, payload=result)
    return JsonResponse({'ok': True, 'digest': digest})


@native
def update_receipt(request,data,identity):
    from .models import PluginUpdateReceipt
    from .plugin_updates import receipt_for
    try:attempt=scoped_attempt(data,identity,'plugin_update')
    except InstallAttempt.DoesNotExist:return JsonResponse({'error':'更新任务不存在'},status=404)
    target=attempt.step.command;raw=data.get('record_json','')
    if not isinstance(raw,str) or len(raw.encode())>2400000:raise ValueError('更新结果超限')
    result=json.loads(raw)
    if not isinstance(result,dict) or any(result.get(k)!=target[k] for k in ('plugin_id','build_id','previous_build_id','record_key','record_digest')) or result.get('attempt_id')!=str(attempt.pk):raise ValueError('更新回执身份不符')
    new_receipt=PluginReceipt.objects.filter(attempt=attempt,digest=result.get('new_record_digest')).first()
    old=receipt_for(attempt.step.job.project,target['record_digest'])
    files=result.get('files')
    if not new_receipt or not isinstance(files,list) or len(files)!=len(old.files):raise ValueError('更新回执缺少完整归属证据')
    expected={f['path']:f for f in old.files};next_files={f['path']:f for f in new_receipt.files}
    for item in files:
        if not isinstance(item,dict):raise ValueError('更新文件结果无效')
        prior=expected.pop(item.get('path'),None);outcome=item.get('outcome')
        if not prior or outcome not in ('removed','retained','missing','replaced','reused') or not isinstance(item.get('reason'),str) or len(item['reason'])>120:raise ValueError('更新结果不匹配')
        if item['path'] in next_files:
            next_file=next_files[item['path']]
            changed=any(next_file[k]!=prior[k] for k in ('sha256','meta_sha256','folder'))
            if outcome!=('replaced' if changed else 'reused'):raise ValueError('旧文件替换结果不一致')
        elif outcome not in ('removed','retained','missing'):raise ValueError('旧文件清理结果不一致')
        if outcome in ('removed','replaced') and (prior['existed'] or prior['meta_existed']):raise ValueError('不能修改原有资源')
    sha=hashlib.sha256(raw.encode()).hexdigest()
    with transaction.atomic():
        project=UnityProject.objects.select_for_update().get(pk=attempt.step.job.project_id)
        if project.session_id!=data['session_id'] or any(getattr(project,k)!=identity[k] for k in ('owner_id','credential_id')):raise ValueError('会话已失效')
        attempt.step.job.refresh_from_db();saved=PluginUpdateReceipt.objects.filter(attempt=attempt).first()
        if saved:
            if saved.digest!=sha:raise ValueError('更新回执不可修改')
        else:
            if not attempt.step.job.active or attempt.step.attempt.pk!=attempt.pk:raise ValueError('任务已结束')
            PluginUpdateReceipt.objects.create(attempt=attempt,digest=sha,payload=result)
    return JsonResponse({'ok':True,'digest':sha})
