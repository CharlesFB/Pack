"""Single-operation executor and durable multi-step orchestration. Delivery/timeout never proves execution failure."""
import json
import re
import uuid
from urllib.parse import urlsplit
from django.core.cache import cache
from django.db import IntegrityError, OperationalError, transaction
from django.http import JsonResponse
from django.utils import timezone
from django.views.decorators.http import require_http_methods, require_POST
from .auth import project_scope
from .bridge import native, checked_session, presence_key
from .models import UnityProject, InstallJob, InstallStep, InstallAttempt, InstallEvent

STATES = {'waiting_input', 'cancelled', 'accepted', 'running', 'verifying', 'needs_review', 'succeeded', 'failed'}
TERMINAL = {'succeeded', 'failed', 'cancelled'}


def target_from(data):
    name, identifier = data.get('name', ''), data.get('identifier', '')
    if not isinstance(name, str) or not re.fullmatch(r'[a-z0-9][a-z0-9._-]{2,159}', name):
        raise ValueError('请填写完整 UPM 包名')
    if name == 'com.magictavern.mtpmanager':
        raise ValueError('MTP 自身的更新需要单独验证共存与清理行为')
    if not isinstance(identifier, str) or len(identifier) > 2048 or any(c.isspace() for c in identifier):
        raise ValueError('安装标识无效')
    if identifier.startswith(name + '@'):
        version = identifier[len(name)+1:]
        if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?', version):
            raise ValueError('Registry 包必须使用明确版本号')
        return dict(name=name, identifier=identifier, source='registry', version=version, commit='')
    uri = urlsplit(identifier)
    if (uri.scheme != 'https' or not uri.hostname or uri.username or uri.password
            or not uri.path.endswith('.git') or not re.fullmatch('[a-f0-9]{40}', uri.fragment)
            or (uri.query and not re.fullmatch(r'path=/[A-Za-z0-9_./-]+', uri.query))
            or '..' in uri.query.split('/')):
        raise ValueError('Git 使用无凭据的 HTTPS .git 地址和完整小写 commit，可带 ?path=/子目录')
    return dict(name=name, identifier=identifier, source='git', version='', commit=uri.fragment)


def executor_key(project):
    return 'upm-executor:v2:' + project.pk + ':' + project.session_id


def executor_status(project):
    value = cache.get(executor_key(project))
    return {'online': value is not None, 'ready': value is True,
            'diagnostic': value if isinstance(value, str) else ''}


@require_POST
def retry(request, instance_id, job_id):
    """Explicitly retry only a known terminal failure; unknown operations retain their lock."""
    try:
        data = json.loads(request.body)
        key, expected = uuid.UUID(data['request_key']), uuid.UUID(data['attempt_id'])
        reason = data['reason'].strip()
        if not 1 <= len(reason) <= 500:
            raise ValueError('请填写重试原因（最多 500 字）')
        with transaction.atomic():
            project = project_scope(request, UnityProject.objects).select_for_update().filter(pk=instance_id).first()
            if not project:
                return JsonResponse({'error': '工程不存在或无访问权限'}, status=404)
            job = InstallJob.objects.select_for_update().filter(pk=job_id, project=project, history_deleted_at__isnull=True).first()
            if not job:
                return JsonResponse({'error': '任务不存在'}, status=404)
            step = job.step
            prior = step.attempts.filter(retry_key=key).first()
            if prior:
                predecessor = step.attempts.get(number=prior.number-1)
                if prior.retry_reason != reason or predecessor.id != expected:
                    return JsonResponse({'error': '重复请求的恢复参数不一致'}, status=409)
                return JsonResponse({'job': dto(job)})
            previous = step.attempt
            if (previous.pk != expected or (job.active and job.target.get('operation') != 'plan') or job.cancel_requested or step.state != 'failed'
                    or (job.target.get('operation')=='plan' and not job.active)
                    or previous.report.get('failure_kind') not in ('preflight', 'add_completed', 'remove_completed')
                    or not executor_status(project)['ready']
                    or (job.target.get('operation') == 'plan' and not cache.get(executor_key(project)+(':maintenance' if job.target.get('required_executor',7)>=8 else ':plans')))
                    or (step.command.get('operation') == 'plugin' and not cache.get(executor_key(project)+':plugins'))
                    or step.command.get('operation') == 'plugin_remove'
                    or (step.command.get('requires_destination') and (not previous.destination_root or not cache.get(executor_key(project)+':zip-destination')))
                    or InstallJob.objects.filter(project=project, active=True).exclude(pk=job.pk).exists()):
                return JsonResponse({'error': '只有执行器就绪、没有其他任务且旧尝试明确失败时才可重试；未知结果请继续核对'}, status=409)
            attempt = InstallAttempt.objects.create(step=step, number=previous.number+1, retry_key=key, retry_reason=reason,
                                                    requested_by=request.user if request.user.is_authenticated else None,
                                                    destination_root=previous.destination_root)
            InstallEvent.objects.create(attempt=attempt, sequence=0, report={'action':'manual_retry',
                'previous_attempt':str(previous.id), 'reason':reason, 'requested_by':attempt.requested_by_id})
            step.state = 'queued'
            step.save(update_fields=['state','updated_at'])
            job.active, job.finished_at = True, None
            job.save(update_fields=['active','finished_at'])
        return JsonResponse({'job': dto(job)}, status=202)
    except (ValueError, TypeError, KeyError, AttributeError):
        return JsonResponse({'error': '请提供当前尝试 ID、请求 UUID 和重试原因'}, status=400)
    except (IntegrityError, OperationalError):
        return JsonResponse({'error': '任务状态正在变化，请使用同一请求标识重试'}, status=409)


def can_retry(job, step):
    command, attempt = step.command, step.attempt
    return (not job.history_deleted_at and not job.cancel_requested and command.get('operation') != 'plugin_remove'
            and (job.target.get('operation')!='plan' or job.active)
            and step.state == 'failed' and attempt.report.get('failure_kind') in ('preflight', 'add_completed', 'remove_completed')
            and (not job.active or job.target.get('operation') == 'plan') and executor_status(job.project)['ready']
            and (job.target.get('operation') != 'plan' or bool(cache.get(executor_key(job.project)+(':maintenance' if job.target.get('required_executor',7)>=8 else ':plans'))))
            and (command.get('operation') != 'plugin' or bool(cache.get(executor_key(job.project)+':plugins')))
            and (not command.get('requires_destination') or bool(attempt.destination_root and cache.get(executor_key(job.project)+':zip-destination'))))


def step_dto(step):
    attempt = step.attempt
    return dict(id=step.pk, position=step.position, target=step.command, state=step.state,
                report=attempt.report, destination_root=attempt.destination_root,
                attempt_id=str(attempt.pk), attempt_number=attempt.number,
                updated_at=step.updated_at.isoformat())


def dto(job):
    step, attempt = job.step, job.step.attempt
    live = executor_status(job.project)
    stale = (timezone.now() - step.updated_at).total_seconds() > 600
    status = step.state
    if not job.active and job.cancel_requested: status = 'cancelled'
    elif job.active and step.state == 'waiting_input' and not live['diagnostic']: status = 'waiting_input'
    elif job.active and job.cancel_requested: status = 'stopping'
    elif job.active and job.paused and attempt.dispatched_at is None: status = 'paused'
    elif job.active and not live['online']: status = 'waiting_connection'
    elif job.active and (stale or (live['diagnostic'] and job.target.get('operation')!='legacy_migrate')): status = 'needs_review'
    rows = sorted(job.steps.all(), key=lambda s:s.position)
    return dict(id=str(job.id), target={k:v for k,v in job.target.items() if k!='baseline_packages'}, state=step.state, display_state=status,
                active=job.active, sequence=attempt.sequence, report=attempt.report,
                created_at=job.created_at.isoformat(), updated_at=step.updated_at.isoformat(),
                executor_online=live['online'], diagnostic='' if job.target.get('operation')=='legacy_migrate' else live['diagnostic'], recheck=attempt.recheck,
                attempt_id=str(attempt.id), attempt_number=attempt.number, destination_root=attempt.destination_root,
                can_retry=can_retry(job, step), paused=job.paused, cancel_requested=job.cancel_requested,
                can_delete=can_delete_history(job),
                steps=[step_dto(s) for s in rows], completed=sum(s.state=='succeeded' for s in rows),
                skipped=sum(s.state=='skipped' for s in rows), total=len(rows),
                attempts=[{'id':str(item.id), 'number':item.number, 'state':item.report.get('state','queued'),
                           'retry_reason':item.retry_reason} for item in sorted(step.attempts.all(),key=lambda a:a.number)[-10:]])


def can_delete_history(job):
    return not job.active and all(s.state in TERMINAL | {'skipped'} for s in job.steps.all())


@require_http_methods(['DELETE'])
def delete_history(request, instance_id, job_id):
    """Remove a finished card without erasing receipts or dependency ownership."""
    with transaction.atomic():
        project = project_scope(request, UnityProject.objects).select_for_update().filter(pk=instance_id).first()
        if not project:
            return JsonResponse({'error': '工程不存在或无访问权限'}, status=404)
        job = InstallJob.objects.select_for_update().filter(pk=job_id, project=project).first()
        if not job:
            return JsonResponse({'error': '安装记录不存在'}, status=404)
        if not can_delete_history(job):
            return JsonResponse({'error': '任务仍在进行或等待核对，请先结束任务再删除记录'}, status=409)
        if job.history_deleted_at is None:
            job.history_deleted_at = timezone.now()
            job.save(update_fields=['history_deleted_at'])
    return JsonResponse({'ok': True, 'id': str(job.pk)})


def settle(job):
    """A definite package failure ends the queue; an import failure is skipped."""
    rows = list(job.steps.all())
    failed=any(s.state=='failed' and s.command.get('operation','upm') in ('upm','plan_verify') for s in rows)
    if job.cancel_requested or failed:
        for step in rows:
            if step.state == 'queued' and step.attempt.dispatched_at is None:
                step.state = 'skipped'; step.save(update_fields=['state','updated_at'])
        finished = all(s.state in TERMINAL | {'skipped'} for s in rows)
    else:
        finished = all(s.state in ('succeeded','skipped') for s in rows)
    if finished:
        job.active, job.finished_at = False, timezone.now()
        job.save(update_fields=['active','finished_at'])


def skip_failed_import(job, step):
    """Adjust only undispatched final verification; keep the preview and failure event."""
    step.state='skipped'
    target=step.command
    identity=lambda t:(t.get('plugin_id'),t.get('build_id'),t.get('destination_root') or '')
    for final in job.steps.filter(target__operation='plan_verify'):
        if final.attempt.dispatched_at is not None:raise ValueError('最终核对已派发，不能更改插件执行结果')
        command=dict(final.target)
        command['plugins']=[p for p in command.get('plugins',[]) if identity(p)!=identity(dict(target,destination_root=step.attempt.destination_root))]
        final.target=command;final.save(update_fields=['target','updated_at'])


def same_report(previous, current):
    """Unity adds a false v10 field when deserializing/replaying a v9 journal."""
    def normalize(report):
        result=dict(report)
        if isinstance(result.get('evidence'),dict):
            evidence=dict(result['evidence'])
            if evidence.get('operation_finished') is False:evidence.pop('operation_finished')
            result['evidence']=evidence
        return result
    return normalize(previous)==normalize(current)


@require_POST
def control(request, instance_id, job_id):
    try:
        action = json.loads(request.body)['action']
        if action not in ('pause','resume','cancel'): raise ValueError('无效队列操作')
        with transaction.atomic():
            project = project_scope(request, UnityProject.objects).select_for_update().filter(pk=instance_id).first()
            if not project: return JsonResponse({'error':'工程不存在或无访问权限'}, status=404)
            job = InstallJob.objects.select_for_update().filter(pk=job_id,project=project).first()
            if not job or job.target.get('operation') != 'plan': return JsonResponse({'error':'安装计划不存在'}, status=404)
            if not job.active: return JsonResponse({'job':dto(job)})
            if job.cancel_requested and action != 'cancel': raise ValueError('已取消的后续步骤不能恢复')
            if action == 'cancel': job.cancel_requested = True
            else: job.paused = action == 'pause'
            job.save(update_fields=['paused','cancel_requested'])
            settle(job)
        return JsonResponse({'job':dto(job)})
    except (ValueError,KeyError,TypeError): return JsonResponse({'error':'请提供 pause、resume 或 cancel 操作'}, status=400)


@require_http_methods(['GET', 'POST'])
def jobs(request, instance_id):
    project = project_scope(request, UnityProject.objects).filter(pk=instance_id).first()
    if project is None:
        return JsonResponse({'error': '工程不存在或无访问权限'}, status=404)
    if request.method == 'GET':
        return JsonResponse({'jobs': [dto(j) for j in InstallJob.objects.filter(project=project, history_deleted_at__isnull=True).select_related('project').prefetch_related('steps__attempts').order_by('-created_at')[:10]],
                             'executor_online': executor_status(project)['online'], 'executor_ready': executor_status(project)['ready'],
                             'executor_diagnostic': executor_status(project)['diagnostic'],
                             'plugin_executor_ready': bool(cache.get(executor_key(project)+':plugins')),
                             'plugin_inventory_ready': bool(cache.get(executor_key(project)+':plugin-inventory')),
                             'plugin_management_ready': bool(cache.get(executor_key(project)+':plugin-management')),
                             'plan_executor_ready': bool(cache.get(executor_key(project)+':plans')),
                             'maintenance_executor_ready': bool(cache.get(executor_key(project)+':maintenance')),
                             'zip_executor_ready': bool(cache.get(executor_key(project)+':zip-destination'))})
    try:
        data = json.loads(request.body)
        key = uuid.UUID(data['request_key'])
        expected_fingerprint=''
        if data.get('operation') == 'plugin':
            from .plugin_delivery import prepare, destination_path, placed_files
            target = prepare(data['plugin_id'], data['build_id'])
            if target.get('requires_destination'):
                target['destination_root'] = destination_path(data.get('destination_root'))
                files = placed_files(target, target['destination_root'])
                if sum(len(f['path'].encode('utf-8'))*3+500 for f in files) > 2200000:
                    raise ValueError('所选目录过长，文件归属记录将超出限制')
        elif data.get('operation') == 'plugin_remove':
            target = None  # Resolve under the project lock, after idempotency lookup.
        else:
            target = target_from(data)
        with transaction.atomic():
            project = UnityProject.objects.select_for_update().get(pk=project.pk)
            prior = InstallJob.objects.filter(project=project, request_key=key).first()
            if prior:
                if data.get('operation') == 'plugin_remove' and prior.target.get('operation') == 'plugin_remove' and all(prior.target.get(k) == data.get(k) for k in ('record_key','record_digest')):
                    return JsonResponse({'job': dto(prior)})
                if prior.target != target:
                    return JsonResponse({'error': '同一请求标识不能更改安装目标'}, status=409)
                return JsonResponse({'job': dto(prior)})
            if target is None:
                if not cache.get(executor_key(project)+':plugin-management'):
                    return JsonResponse({'error':'插件删除需要版本 6 的独立执行器'}, status=409)
                from .plugin_inventory import removal_target
                target = removal_target(project, data)
                if cache.get(executor_key(project)+':maintenance'):
                    from .plugin_updates import check_package_references
                    check_package_references(project,target)
                    from .package_state import current as package_state
                    expected_fingerprint=package_state(project)['fingerprint']
            if target.get('operation') == 'plugin' and not cache.get(executor_key(project)+':plugins'):
                return JsonResponse({'error':'请升级完整执行器到版本 3 后导入插件'}, status=409)
            if target.get('requires_destination') and not cache.get(executor_key(project)+':zip-destination'):
                return JsonResponse({'error':'ZIP 网页选择目录需要版本 5 的独立执行器'}, status=409)
            if project.transport != 'pull' or not executor_status(project)['ready']:
                return JsonResponse({'error': '请连接新版独立 UPM 执行器'}, status=409)
            if InstallJob.objects.filter(project=project, active=True).exists():
                return JsonResponse({'error': '该工程有未结束的安装任务，请先核对其结果'}, status=409)
            job = InstallJob.objects.create(project=project, target=target, request_key=key,expected_fingerprint=expected_fingerprint)
            InstallAttempt.objects.create(step=InstallStep.objects.create(job=job), destination_root=target.get('destination_root', ''))
        return JsonResponse({'job': dto(job)}, status=202)
    except OSError:
        return JsonResponse({'error':'仓库制品或导入缓存不可用'}, status=503)
    except ValueError as error:
        return JsonResponse({'error': str(error) if not isinstance(error, json.JSONDecodeError) else '需要有效 JSON 请求'}, status=400)
    except (KeyError, TypeError, AttributeError):
        return JsonResponse({'error': '请提交有效包名、固定安装标识和 request_key'}, status=400)
    except (IntegrityError, OperationalError):
        return JsonResponse({'error': '工程任务正在变更，请使用同一请求标识重试'}, status=409)


@require_POST
def recheck(request, instance_id, job_id):
    with transaction.atomic():
        project = project_scope(request, UnityProject.objects).select_for_update().filter(pk=instance_id).first()
        if not project:
            return JsonResponse({'error': '工程不存在或无访问权限'}, status=404)
        job = InstallJob.objects.select_for_update().filter(pk=job_id, project=project).first()
        if not job:
            return JsonResponse({'error': '任务不存在'}, status=404)
        if not job.active:
            return JsonResponse({'error': '任务已经结束'}, status=409)
        if job.step.state == 'waiting_input':
            return JsonResponse({'error':'旧 ZIP 任务尚未选择目录；升级完整执行器后结束旧任务，再从网页重新导入'}, status=409)
        attempt = job.step.attempt
        attempt.recheck += 1
        attempt.save(update_fields=['recheck'])
    return JsonResponse({'job': dto(job)})


@native
def sync(request, data, identity):
    project_id = checked_session(data, identity)
    if project_id is None:
        return JsonResponse({'error': '请重新注册工程'}, status=409)
    if data.get('protocol') != 1:
        raise ValueError('不支持的执行器协议')
    with transaction.atomic():
        # Recheck DB identity under the same project lock used by registration.
        project = UnityProject.objects.select_for_update().get(pk=project_id)
        if project.session_id != data['session_id'] or any(getattr(project, k) != identity[k] for k in ('owner_id','credential_id')):
            return JsonResponse({'error': '会话已失效'}, status=409)
        diagnostic = data.get('diagnostic', '')
        if not isinstance(diagnostic, str) or len(diagnostic) > 500:
            raise ValueError('无效诊断信息')
        if data.get('executor_version') not in (2, 3, 4, 5, 6, 7, 8, 9,10):
            diagnostic = '请更新完整 UpmExecutor 目录；旧执行器不再接收新任务，已派发任务保留恢复。'
        from .models import ProjectPackageState, LegacyMigration
        if data.get('executor_version') not in (9,10) and (ProjectPackageState.objects.filter(project=project,payload__has_key='assets_packages').exists() or LegacyMigration.objects.filter(project=project,state='completed').exists()):
            diagnostic='此工程已有独立 Assets/迁移记录，请保持使用 v9 或以上执行器。'
        cache.set(executor_key(project)+':assets-packages',data.get('executor_version') in (9,10),45)
        cache.set(executor_key(project), diagnostic or True, 45)
        cache.set(executor_key(project)+':legacy-migration',data.get('executor_version') in (9,10) and data.get('migration_capable') is True,45)
        cache.set(executor_key(project)+':plugins', data.get('executor_version') in (3,4,5,6,7,8,9,10) and not diagnostic, 45)
        cache.set(executor_key(project)+':zip-destination', data.get('executor_version') in (5,6,7,8,9,10) and not diagnostic, 45)
        cache.set(executor_key(project)+':directories', data.get('executor_version') in (5,6,7,8,9,10), 45)
        cache.set(executor_key(project)+':plugin-management', data.get('executor_version') in (6,7,8,9,10) and not diagnostic, 45)
        cache.set(executor_key(project)+':plugin-inventory', data.get('executor_version') in (6,7,8,9,10), 45)
        cache.set(executor_key(project)+':plans', data.get('executor_version') in (7,8,9,10) and not diagnostic, 45)
        cache.set(executor_key(project)+':maintenance',data.get('executor_version') in (8,9,10) and not diagnostic,45)
        if data.get('executor_version') in (7,8,9,10) and data.get('has_package_state'):
            from .package_state import accept
            accept(project, data.get('package_state'))
        inventory_request = ''
        if data.get('executor_version') in (6,7,8,9,10):
            from .plugin_inventory import exchange as inventory_exchange
            inventory_request = inventory_exchange(project, data.get('plugin_inventory') if data.get('has_plugin_inventory') else None)
        from .project_directories import exchange
        directory_result = data.get('directory_result') if data.get('has_directory_result', bool(data.get('directory_result'))) else None
        directory_request = exchange(project, directory_result) if data.get('executor_version') in (5,6,7,8,9,10) else None
        from .legacy_migration import exchange as migration_exchange
        legacy_request=migration_exchange(project,data.get('legacy_preview') if data.get('has_legacy_preview') else None,data.get('executor_version') in (9,10))
        cache.set(presence_key(project.pk, project.session_id), timezone.now().isoformat(), 45)
        report = data.get('report') if data.get('has_report', bool(data.get('report'))) else None
        ack = 0
        if report:
            if not isinstance(report, dict):
                raise ValueError('安装回执必须是 JSON 对象')
            attempt = InstallAttempt.objects.select_for_update().filter(pk=report.get('attempt_id'), step__job__project=project).select_related('step__job').first()
            if not attempt:
                raise ValueError('未知安装记录；保留本地日志并检查服务器')
            sequence, state = report.get('sequence'), report.get('state')
            if type(sequence) is not int or sequence < 1 or state not in STATES or len(json.dumps(report)) > 12000:
                raise ValueError('无效安装回执')
            previous = InstallEvent.objects.filter(attempt=attempt, sequence=sequence).first()
            if previous and not same_report(previous.report, report):
                raise ValueError('相同序号的回执内容发生变化')
            if sequence > attempt.sequence:
                job, step = attempt.step.job, attempt.step
                command = step.command
                if not job.active or attempt.dispatched_at is None or step.attempt.pk != attempt.pk or job.step.pk != step.pk:
                    raise ValueError('安装任务已结束或未派发')
                if (state == 'accepted' and step.state not in ('queued', 'accepted')) or (state == 'running' and step.state not in ('queued', 'accepted', 'waiting_input', 'running')):
                    raise ValueError('禁止退回安装前状态或重新启动安装')
                if state in ('waiting_input','cancelled'):
                    if command.get('destination_root') or not command.get('requires_destination') or step.state not in ('queued','accepted','waiting_input'):
                        raise ValueError('只允许尚未写入的 ZIP 等待选择或取消')
                    if state == 'cancelled' and (attempt.destination_root or report.get('failure_kind')!='user_cancel'):
                        raise ValueError('确认目录后不能取消为未写入状态')
                if state == 'running' and command.get('requires_destination') and not attempt.destination_root:
                    raise ValueError('ZIP 尚未确认导入目录')
                if state == 'succeeded' and command.get('operation')=='legacy_migrate':
                    from .legacy_migration import success
                    success(attempt,report.get('evidence',{}))
                elif state == 'succeeded' and command.get('operation')=='plugin_update':
                    from .plugin_updates import success
                    success(attempt,report.get('evidence',{}))
                elif state == 'succeeded' and command.get('operation')=='upm_remove':
                    evidence=report.get('evidence',{})
                    if not isinstance(evidence,dict) or evidence.get('name')!=command['name'] or evidence.get('identifier')!=command['identifier'] or evidence.get('removed') is not True or evidence.get('manifest_match') is not True or evidence.get('lock_match') is not True:
                        raise ValueError('缺少包卸载的完整核验证据')
                elif state == 'succeeded' and step.command.get('operation') == 'plugin':
                    from .plugin_installations import success
                    success(attempt, report.get('evidence', {}))
                elif state == 'succeeded' and command.get('operation') == 'plugin_remove':
                    from .plugin_inventory import removal_success
                    removal_success(attempt, report.get('evidence', {}))
                elif state == 'succeeded' and command.get('operation') == 'plan_verify':
                    evidence = report.get('evidence', {})
                    if not isinstance(evidence,dict) or evidence.get('plan_digest') != command['plan_digest'] or evidence.get('verified') is not True:
                        raise ValueError('缺少完整计划的最终核验证据')
                elif state == 'succeeded':
                    evidence = report.get('evidence', {})
                    if not isinstance(evidence, dict):
                        raise ValueError('无效安装核验证据')
                    target = command
                    if (evidence.get('name') != target['name'] or evidence.get('identifier') != target['identifier']
                            or evidence.get('source') != target['source'] or evidence.get('direct') is not True
                            or evidence.get('manifest_match') is not True or evidence.get('lock_match') is not True
                            or (target['source'] in ('registry','builtin') and evidence.get('version') != target['version'])
                            or (target['source'] == 'git' and evidence.get('commit') != target['commit'])):
                        raise ValueError('缺少匹配的安装核验证据')
                if state=='failed' and report.get('failure_kind')=='add_completed' and command.get('operation','upm')!='upm':
                    raise ValueError('安装失败类型与步骤不符')
                if state=='failed' and report.get('failure_kind')=='remove_completed' and command.get('operation')!='upm_remove':
                    raise ValueError('卸载失败类型与步骤不符')
                failure=report.get('failure_kind')
                if state=='failed' and failure in ('plugin_import','package_verify','plan_verify'):
                    operation=command.get('operation','upm')
                    if data.get('executor_version')!=10 or operation!={'plugin_import':'plugin','package_verify':'upm','plan_verify':'plan_verify'}[failure]:
                        raise ValueError('失败类型与执行器或步骤不符')
                    if failure=='plugin_import' and report.get('evidence',{}).get('operation_finished') is not True:
                        raise ValueError('插件执行尚未结束，不能继续后续步骤')
                if state == 'failed' and (failure not in ('preflight', 'add_completed', 'remove_completed','plugin_import','package_verify','plan_verify')
                        or (report.get('failure_kind') == 'preflight' and step.state not in ('queued', 'accepted', 'waiting_input'))):
                    raise ValueError('未知操作状态不能判定失败')
                InstallEvent.objects.create(attempt=attempt, sequence=sequence, report=report)
                attempt.sequence, attempt.report = sequence, report
                attempt.save(update_fields=['sequence','report'])
                step.state = state
                skipped_import=state=='failed' and command.get('operation')=='plugin' and job.target.get('operation')=='plan'
                if skipped_import:skip_failed_import(job,step)
                step.save(update_fields=['state','updated_at'])
                if (state == 'succeeded' or (skipped_import and failure=='plugin_import')) and job.target.get('operation') == 'plan':
                    fingerprint = report.get('evidence',{}).get('project_fingerprint','')
                    if not re.fullmatch('[a-f0-9]{64}',fingerprint): raise ValueError('缺少步骤完成后的工程指纹')
                    job.expected_fingerprint = fingerprint; job.save(update_fields=['expected_fingerprint'])
                if state in TERMINAL:
                    if job.target.get('operation') == 'plan': settle(job)
                    else:
                        job.active, job.finished_at = False, timezone.now()
                        job.save(update_fields=['active','finished_at'])
            ack = sequence
        job = InstallJob.objects.filter(project=project, active=True).first()
        work = None
        if job:
            step = job.step
            command = step.command
            planned = job.target.get('operation') == 'plan'
            attempt = step.attempt
            eligible = True
            if planned:
                eligible = (data.get('executor_version') in (7,8,9,10) and data['executor_version']>=job.target.get('required_executor',7) and step.state not in TERMINAL | {'skipped'}
                            and not (report and report.get('state') in TERMINAL)
                            and not (attempt.dispatched_at is None and (job.paused or job.cancel_requested)))
            if command.get('operation') in ('plugin_update','upm_remove') and data.get('executor_version') not in (8,9,10): eligible=False
            if command.get('operation') == 'plugin_remove' and data.get('executor_version') not in (6,7,8,9,10): eligible = False
            if attempt.dispatched_at is None and (not executor_status(project)['ready'] or (command.get('operation') == 'plugin' and data.get('executor_version') not in (3,4,5,6,7,8,9,10))): eligible = False
            if command.get('requires_destination') and (data.get('executor_version') not in (4,5,6,7,8,9,10) or (command.get('destination_root') and data.get('executor_version') not in (5,6,7,8,9,10))): eligible = False
            if command.get('operation')=='legacy_migrate':
                eligible=data.get('executor_version') in (9,10) and data.get('migration_capable') is True
            if eligible:
                recovery = attempt.dispatched_at is not None
                if not recovery:
                    attempt.dispatched_at = timezone.now()
                    attempt.save(update_fields=['dispatched_at'])
                work = dict(job_id=str(job.id), attempt_id=str(attempt.id), target=command,
                            expected_fingerprint=job.expected_fingerprint if planned else '',
                            destination_root=attempt.destination_root, recover_only=recovery,
                            recheck=attempt.recheck, sequence=attempt.sequence)
        return JsonResponse({'ok': True, 'ack': ack, 'work': work, 'directory_request': directory_request, 'inventory_request': inventory_request, 'legacy_request': legacy_request})
