"""Immutable previews and project-scoped confirmation into a serial execution queue."""
import hashlib
import json
import uuid
from datetime import timedelta
from django.db import transaction, IntegrityError
from django.http import JsonResponse
from django.utils import timezone
from django.views.decorators.http import require_http_methods, require_POST
from django.core.cache import cache
from .auth import project_scope
from .models import UnityProject, InstallationPlan, InstallJob, InstallStep, InstallAttempt
from .package_state import current
from .installations import executor_key, dto
from .plugin_api import snapshot
from .plan_resolver import Source, resolve
from .plugin_delivery import prepare, placed_files, destination_path


def digest(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest()


def preview_dto(plan):
    if plan.state=='resolving' and timezone.now()-plan.created_at>timedelta(minutes=2):
        return dict(id=str(plan.pk),state='failed',error='预览解析已中断或超时，请重新生成。',job_id=None)
    payload = {k:v for k,v in plan.payload.items() if k not in ('protected_packages','original_direct','baseline_packages')}
    return dict(id=str(plan.pk),state=plan.state,error=plan.error,created_at=plan.created_at.isoformat(),
                **payload,job_id=str(plan.job_id) if plan.job_id else None)


@require_http_methods(['GET','POST'])
def previews(request, instance_id):
    project=project_scope(request,UnityProject.objects).filter(pk=instance_id).first()
    if not project: return JsonResponse({'error':'工程不存在或无访问权限'},status=404)
    if request.method=='GET':
        return JsonResponse({'plans':[preview_dto(p) for p in InstallationPlan.objects.filter(project=project).order_by('-created_at')[:10]]})
    plan=None
    try:
        data=json.loads(request.body); key=uuid.UUID(data['request_key']); root=data['root']
        if not isinstance(root,dict): raise ValueError('请选择安装目标')
        root={k:root[k] for k in ('catalog_id','tag','name','identifier','operation','plugin_id','build_id','record_key','record_digest') if k in root}
        if not root or any(not isinstance(v,str) or len(v)>2048 for v in root.values()): raise ValueError('安装目标无效')
        with transaction.atomic():
            project=UnityProject.objects.select_for_update().get(pk=project.pk)
            prior=InstallationPlan.objects.filter(project=project,request_key=key).first()
            if prior:
                if prior.root!=root: return JsonResponse({'error':'同一请求标识不能更改预览目标'},status=409)
                return JsonResponse({'plan':preview_dto(prior)})
            state=current(project)
            if InstallJob.objects.filter(project=project,active=True).exists(): raise ValueError('工程有未结束任务，请先处理当前任务')
            plan=InstallationPlan.objects.create(project=project,request_key=key,root=root,session_id=project.session_id)
        from .package_maintenance import prepare_cleanup, uninstall_preview, managed_dependencies
        from .plugin_updates import standalone_preview, add_update_options, cleanup_options
        index,plugins=snapshot()
        if root.get('operation')=='uninstall':
            payload=uninstall_preview(project,state,root)
        elif root.get('operation')=='plugin_update':
            payload=standalone_preview(project,state,root)
        else:
            source=Source(state,project.unity_version)
            try:payload=resolve(source,root,state,project.unity_version,index,plugins,managed_dependencies(project,state))
            finally:source.close()
            prepare_cleanup(project,state,payload)
            add_update_options(project,payload)
        if root.get('operation')!='plugin_update':cleanup_options(project,state,payload,index)
        payload['requires_assets_scan']='assets_packages' in state
        payload['digest']=digest(payload)
        plan.payload,plan.state=payload,'ready';plan.save(update_fields=['payload','state'])
        return JsonResponse({'plan':preview_dto(plan)},status=201)
    except (ValueError,TypeError,KeyError) as error:
        message=str(error) if isinstance(error,ValueError) and not isinstance(error,json.JSONDecodeError) else '预览参数或元数据无效'
        if plan: plan.state,plan.error='failed',message[:500];plan.save(update_fields=['state','error'])
        return JsonResponse({'error':message},status=400)
    except Exception:
        # GitLab/network errors can embed credentials; never return exception text.
        if plan: plan.state,plan.error='failed','远端包元数据或插件索引读取失败，请检查服务配置后重新预览';plan.save(update_fields=['state','error'])
        return JsonResponse({'error':'远端包元数据或插件索引读取失败，请检查服务配置后重新预览'},status=502)


def choices(plan, data):
    if data.get('digest')!=plan.payload['digest']: raise ValueError('预览已变化，请重新读取')
    selected=data.get('plugins')
    if not isinstance(selected,list) or len(selected)>500: raise ValueError('插件选择清单无效')
    offered={r['key']:r for r in all_offers(plan)}
    result={}
    for row in selected:
        if not isinstance(row,dict) or row.get('key') not in offered or row['key'] in result or type(row.get('selected')) is not bool: raise ValueError('插件选择与预览不一致')
        item=offered[row['key']]
        destination=destination_path(row.get('destination_root')) if row['selected'] and item['format']=='zip' and item.get('action')!='remove' else item.get('destination_root','')
        if row['selected'] and not item['available']: raise ValueError('所选插件制品不可导入：'+item['name'])
        result[row['key']]=dict(selected=row['selected'],destination_root=destination)
        if row.get('replace_record'):
            option=next((r for r in item.get('update_options',[]) if r['record_key']==row['replace_record']),None)
            if not option:raise ValueError('旧版本选择不在预览中')
            if row['selected'] and destination!=option['destination_root']:raise ValueError('更新须沿用原导入根目录')
            result[row['key']].update(replace_record=option['record_key'],replace_digest=option['record_digest'])
    if set(result)!=set(offered): raise ValueError('请明确每个插件的导入选择')
    return result


def all_offers(plan):
    return [r for p in plan.payload['packages'] for r in p['plugins']]+plan.payload.get('cleanup_plugins',[])


def assemble(plan, selected):
    """Prepare exact artifacts before holding the project lock; no Unity writes here."""
    steps, goals, plugin_goals, seen, path_owners, families, guid_owners = [], [], [], {}, {}, {}, {}
    warnings=list(plan.payload['warnings'])
    conflicts=[];removed_records=[]
    from .plugin_updates import replacement, file_diff, check_package_references
    if plan.payload.get('plugin_update'):
        target=plan.payload['plugin_update']
        target=replacement(plan.project,target['plugin_id'],target['build_id'],target['record_key'],target['record_digest'])
        check_package_references(plan.project,target)
        if file_diff(plan.project,target)['blocked']:raise ValueError('更新涉及原有或共享文件，需先处理')
        steps.append((target,'queued'));plugin_goals.append(dict(plugin_id=target['plugin_id'],build_id=target['build_id'],destination_root=target['destination_root']))
        removed_records.append(target['record_key'])
    for package in plan.payload['packages']:
        for item in package['plugins']:
            choice=selected[item['key']]
            if not choice['selected']:
                steps.append((dict(operation='skip',name=item['name'],identifier=item['plugin_id']+'@'+item['build_id'],reason='未选择导入'), 'skipped'));continue
            identity=(item['plugin_id'],item['build_id'],choice['destination_root'])
            if identity in seen:
                steps.append((dict(operation='skip',name=item['name'],identifier=item['plugin_id']+'@'+item['build_id'],reason='复用前序相同构建及目录'), 'skipped'));continue
            if choice.get('replace_record'):
                target=replacement(plan.project,item['plugin_id'],item['build_id'],choice['replace_record'],choice['replace_digest'],choice['destination_root'])
                check_package_references(plan.project,target,plan.payload)
                diff=file_diff(plan.project,target)
                if diff['blocked']:raise ValueError('不能覆盖原有或共享文件：'+', '.join(diff['blocked'][:8]))
                if target['record_key'] in removed_records:raise ValueError('同一旧插件记录不能替换两次')
                removed_records.append(target['record_key'])
                warnings.append(item['name']+'：新增 '+str(len(diff['added']))+'，替换 '+str(len(diff['changed']))+'，待清理旧文件 '+str(len(diff['removed'])))
                if diff['guid_changes']:conflicts.append(item['name']+' 的 GUID 发生变化，请检查资源引用')
            else: target=prepare(item['plugin_id'],item['build_id'])
            if target.get('requires_destination'): target['destination_root']=choice['destination_root']
            files=placed_files(target,choice['destination_root'])
            if sum(len(f['path'].encode())*3+500 for f in files)>2200000: raise ValueError('所选目录使插件记录超过大小限制')
            identity_text=item['name']+' '+(item['version'] or 'unversioned')+' @ '+(choice['destination_root'] or '原始目录')
            if item['plugin_id'] in families and families[item['plugin_id']]!=identity:
                conflicts.append('同插件选择了不同构建或目录：'+item['name'])
            families[item['plugin_id']]=identity
            for file in files:
                guid=file.get('guid','')
                if guid and guid in guid_owners and guid_owners[guid]!=file['path'].casefold() and len(conflicts)<100:
                    conflicts.append('GUID 重复：'+file['path']+' / '+guid_owners[guid])
                if guid: guid_owners[guid]=file['path'].casefold()
                if file['folder']: continue
                path=file['path'].casefold()
                if path in path_owners and len(conflicts)<100:
                    conflicts.append(file['path']+'：'+path_owners[path]+' / '+identity_text)
                path_owners[path]=identity_text
            seen[identity]=len(steps)
            steps.append((target,'queued'))
            plugin_goals.append(dict(plugin_id=item['plugin_id'],build_id=item['build_id'],destination_root=choice['destination_root']))
        target=package['target']
        goals.append(dict(target,direct=package['direct']))
        if package['change']!='unchanged': steps.append((target,'queued'))
    for row in plan.payload.get('removals',[]):steps.append((row['target'],'queued'))
    for item in plan.payload.get('cleanup_plugins',[]):
        if selected[item['key']]['selected']:
            target=item['target']
            if target['record_key'] in removed_records:raise ValueError('更新已处理此旧插件，请取消重复删除')
            from .plugin_inventory import removal_target
            check_package_references(plan.project,target,plan.payload)
            removal_target(plan.project,target) # Fresh record binding; never trust historical receipts alone.
            steps.append((target,'queued'));removed_records.append(target['record_key'])
        else:steps.append((dict(operation='skip',name=item['name'],identifier=item['plugin_id'],reason='已由更新步骤处理旧记录' if item['target']['record_key'] in removed_records else '保留旧插件'),'skipped'))
    final=dict(operation='plan_verify',name='最终核对全部包与插件',identifier='plan:'+str(plan.pk),plan_digest=plan.payload['digest'],
               packages=goals,plugins=plugin_goals,protected_packages=plan.payload['protected_packages'],
               expected_direct=plan.payload.get('expected_direct',sorted(set(plan.payload['original_direct']) | {p['name'] for p in plan.payload['packages'] if p['direct']})),
               absent_packages=plan.payload.get('absent_packages',[]),removed_records=removed_records)
    steps.append((final,'queued'))
    if len(steps)>500: raise ValueError('安装计划超过 500 步')
    return steps,list(dict.fromkeys(conflicts)),warnings


@require_POST
def selection(request, instance_id, plan_id):
    project=project_scope(request,UnityProject.objects).filter(pk=instance_id).first()
    plan=InstallationPlan.objects.filter(pk=plan_id,project=project,state='ready').first() if project else None
    if not plan: return JsonResponse({'error':'安装预览不存在或无访问权限'},status=404)
    try:
        data=json.loads(request.body);selected=choices(plan,data)
        if plan.payload['errors']: raise ValueError('预览存在阻止项，请先修复并重新预览')
        steps,conflicts,warnings=assemble(plan,selected)
        return JsonResponse({'steps':[dict(target=t,state=s) for t,s in steps], 'conflicts':conflicts,'warnings':warnings,'selection_digest':digest(selected)})
    except (ValueError,TypeError,KeyError) as error: return JsonResponse({'error':str(error) if isinstance(error,ValueError) else '选择参数无效'},status=400)
    except OSError: return JsonResponse({'error':'插件仓库制品不可用'},status=503)


@require_POST
def confirm(request, instance_id, plan_id):
    project=project_scope(request,UnityProject.objects).filter(pk=instance_id).first()
    if not project: return JsonResponse({'error':'工程不存在或无访问权限'},status=404)
    plan=InstallationPlan.objects.filter(pk=plan_id,project=project,state='ready').first()
    if not plan: return JsonResponse({'error':'安装预览不存在'},status=404)
    try:
        data=json.loads(request.body);selected=choices(plan,data)
        if plan.job_id:
            if plan.selection!=selected: return JsonResponse({'error':'已确认计划的插件选择不可更改'},status=409)
            return JsonResponse({'job':dto(plan.job)})
        if plan.payload['errors']: raise ValueError('预览存在阻止项，请先修复并重新预览')
        if data.get('selection_digest')!=digest(selected): raise ValueError('请先检查当前选择及冲突提示')
        steps,conflicts,warnings=assemble(plan,selected)
        required=9 if plan.payload.get('requires_assets_scan') else 8 if any(t.get('operation') in ('upm_remove','plugin_update','plugin_remove') for t,s in steps) else 7
        with transaction.atomic():
            project=UnityProject.objects.select_for_update().get(pk=project.pk)
            plan=InstallationPlan.objects.select_for_update().get(pk=plan.pk)
            if plan.job_id:
                if plan.selection!=selected: raise ValueError('已确认计划的插件选择不可更改')
                return JsonResponse({'job':dto(plan.job)})
            if not cache.get(executor_key(project)+':plans'): raise ValueError('请连接 v7 或以上独立执行器；当前执行器不可执行计划')
            if required>=8 and not cache.get(executor_key(project)+':maintenance'):raise ValueError('卸载和更新需要 v8 独立执行器')
            if required==9 and not cache.get(executor_key(project)+':assets-packages'):raise ValueError('此计划需要 v9 的 Assets 本地包核对')
            if plan.payload.get('inventory_signature'):
                from .plugin_updates import inventory_signature
                if inventory_signature(project)!=plan.payload['inventory_signature']:raise ValueError('插件记录已变化，请重新预览')
            state=current(project)
            if project.session_id!=plan.session_id or state['fingerprint']!=plan.payload['baseline']: raise ValueError('工程会话或包状态已变化，请重新预览')
            if timezone.now()-plan.created_at>timedelta(hours=1): raise ValueError('预览已超过一小时，请重新生成')
            if InstallJob.objects.filter(project=project,active=True).exists(): raise ValueError('工程已有未结束任务')
            target=dict(operation='plan',name=plan.payload['root_name'],identifier='plan:'+str(plan.pk),plan_id=str(plan.pk),
                        warnings=warnings,conflicts=conflicts,original_direct=plan.payload['original_direct'],required_executor=required,baseline_packages=state['packages'],plan_action=plan.payload.get('plan_action','install'))
            job=InstallJob.objects.create(project=project,request_key=plan.request_key,target=target,expected_fingerprint=plan.payload['baseline'])
            for position,(target,state) in enumerate(steps):
                step=InstallStep.objects.create(job=job,position=position,target=target,state=state)
                InstallAttempt.objects.create(step=step,destination_root=target.get('destination_root',''))
            plan.job,plan.selection=job,selected;plan.save(update_fields=['job','selection'])
        return JsonResponse({'job':dto(job)},status=202)
    except (ValueError,TypeError,KeyError) as error: return JsonResponse({'error':str(error) if isinstance(error,ValueError) else '确认参数无效'},status=400)
    except IntegrityError: return JsonResponse({'error':'工程正在创建任务，请重试同一预览'},status=409)
    except OSError: return JsonResponse({'error':'插件制品不可用'},status=503)
