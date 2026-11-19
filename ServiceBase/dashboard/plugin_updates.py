"""Replacement previews and evidence validation for independently owned plugins."""
from .models import PluginReceipt, PluginUpdateReceipt, PluginInstallation
from .plugin_delivery import prepare, placed_files
from .plugin_inventory import current, removal_target


def receipt_for(project, digest):
    saved=PluginReceipt.objects.filter(digest=digest,attempt__step__job__project=project).select_related('attempt__step__job').first()
    if not saved:raise ValueError('服务端缺少原始插件归属证据')
    return saved


def replacement(project, plugin_id, build_id, key, digest, destination=None):
    old=removal_target(project,dict(record_key=key,record_digest=digest))
    if old['plugin_id']!=plugin_id or old['build_id']==build_id:raise ValueError('请选择同一插件的另一构建')
    target=prepare(plugin_id,build_id)
    if target.get('requires_destination'):
        if not old['destination_root']:raise ValueError('跨格式更新无法沿用旧 ZIP 根目录，请单独导入')
        if destination and destination!=old['destination_root']:raise ValueError('更新必须沿用原导入根目录')
        target['destination_root']=old['destination_root']
    elif old['destination_root']:raise ValueError('跨格式更新无法改变旧导入根目录，请单独导入')
    target.update(operation='plugin_update',record_key=key,record_digest=digest,previous_build_id=old['build_id'],
                  previous_file_count=old['file_count'],destination_root=old['destination_root'])
    return target


def file_diff(project, target):
    old={f['path']:f for f in receipt_for(project,target['record_digest']).files}
    new={f['path']:f for f in placed_files(target,target['destination_root'])}
    result=dict(added=[],changed=[],removed=[],unchanged=[],blocked=[],guid_changes=[])
    inventory=current(project)
    shared={}
    for r in inventory['installations']:
        if r['record_key']==target['record_key']:continue
        for f in receipt_for(project,r['record_digest']).files:shared.setdefault(f['path'],[]).append(r['plugin_id'])
    for path,f in new.items():
        before=old.get(path)
        if not before:result['added'].append(path);continue
        change=(f['sha256']!=before['sha256'] or f['folder']!=before['folder'] or (not f['generated'] and f['meta_sha256']!=before['meta_sha256']))
        result['changed' if change else 'unchanged'].append(path)
        if not f['generated'] and f['guid']!=before['guid']:result['guid_changes'].append(path)
        if change and (before['existed'] or path in shared):result['blocked'].append(path+'：原有文件或其他插件仍引用')
    for path in old.keys()-new.keys():
        if old[path]['existed'] or path in shared:result['unchanged'].append(path)
        else:result['removed'].append(path)
    return result


def inventory_signature(project):
    from .installation_plans import digest
    state=current(project)
    if not state['fresh'] or state['scanning']:raise ValueError('请等待 Unity 完成插件状态核对')
    return digest(sorted((r['record_key'],r['record_digest']) for r in state['installations']))


def add_update_options(project, payload):
    state=current(project)
    if not state['fresh'] or state['scanning']:return
    for package in payload['packages']:
        for offered in package['plugins']:
            offered['update_options']=[dict(record_key=r['record_key'],record_digest=r['record_digest'],build_id=r['build_id'],destination_root=r['destination_root'],status=r['status'])
                for r in state['installations'] if r['plugin_id']==offered['plugin_id'] and r['build_id']!=offered['build_id']]


def standalone_preview(project, state, data):
    target=replacement(project,data['plugin_id'],data['build_id'],data['record_key'],data['record_digest'])
    check_package_references(project,target)
    diff=file_diff(project,target)
    return dict(plan_action='plugin_update',root_name=target['name'],packages=[],baseline=state['fingerprint'],
                original_direct=[p['name'] for p in state['packages'] if p['direct']],protected_packages=state['packages'],
                plugin_update=target,file_diff=diff,inventory_signature=inventory_signature(project),
                warnings=['更新沿用原导入根目录；旧文件先备份，额外文件和用户修改不会静默覆盖。'],errors=diff['blocked'])


def expected_record_files(project, target):
    old={f['path']:f for f in receipt_for(project,target['record_digest']).files}
    wanted=placed_files(target,target['destination_root'])
    for f in wanted:
        prior=old.get(f['path'])
        if prior and f['generated']:
            f['guid'],f['meta_sha256']=prior['guid'],prior['meta_sha256']
    return wanted,old


def validate_record(project, target, files):
    import re
    expected,old=expected_record_files(project,target)
    if not isinstance(files,list) or len(files)!=len(expected):raise ValueError('更新文件数量不一致')
    for f,w in zip(files,expected):
        if not isinstance(f,dict) or (f.get('folder') is not w['folder'] or any(f.get(k)!=w[k] for k in ('path','sha256'))):raise ValueError('更新文件内容不匹配')
        prior=old.get(w['path'])
        if prior:
            if any(f.get(k)!=prior[k] for k in ('existed','meta_existed','before_sha256','before_meta_sha256')):raise ValueError('更新不能改变原文件归属')
            if f.get('guid')!=w['guid'] or f.get('meta_sha256')!=w['meta_sha256']:raise ValueError('更新 meta 证据不符')
        else:
            existed=f.get('existed');meta=f.get('before_meta_sha256')
            if type(existed) is not bool or existed!=f.get('meta_existed') or bool(meta)!=existed:raise ValueError('新增文件归属不完整')
            if f.get('before_sha256')!=('' if w['folder'] or not existed else w['sha256']):raise ValueError('新增文件覆盖基线不符')
            preserved=w['generated'] and existed
            if f.get('meta_sha256')!=(meta if preserved else w['meta_sha256']) or (not preserved and f.get('guid')!=w['guid']):raise ValueError('新增 meta 证据不符')
        if not isinstance(f.get('guid'),str) or not isinstance(f.get('meta_sha256'),str) or not re.fullmatch('[a-f0-9]{32}',f.get('guid','')) or not re.fullmatch('[a-f0-9]{64}',f.get('meta_sha256','')):raise ValueError('更新哈希格式无效')


def success(attempt,evidence):
    if not isinstance(evidence,dict):raise ValueError('无效插件更新证据')
    target=attempt.step.command
    receipt=PluginReceipt.objects.filter(attempt=attempt,digest=evidence.get('record_digest')).first()
    update=PluginUpdateReceipt.objects.filter(attempt=attempt,digest=evidence.get('update_digest')).first()
    if not receipt or not update or update.payload['new_record_digest']!=receipt.digest or evidence.get('build_id')!=target['build_id'] or evidence.get('recipe_id')!=target['recipe_id']:
        raise ValueError('缺少插件更新与旧文件处理证据')
    PluginInstallation.objects.filter(project=attempt.step.job.project,receipt__digest=target['record_digest']).delete()
    PluginInstallation.objects.update_or_create(project=attempt.step.job.project,plugin_id=target['plugin_id'],build_id=target['build_id'],defaults={'receipt':receipt})


def cleanup_options(project, state, payload, index):
    mappings={(m['package_name'],m['commit']):m for m in index.get('package_mappings',[])}
    old={p['name']:p for p in state['packages']}
    planned={p['name']:p['target'] for p in payload['packages']}
    removed=set(payload.get('absent_packages',[]))
    changed={p['name'] for p in payload['packages'] if p['change']=='change'}|removed
    def refs(packages):
        return {(r['plugin_id'],r['build_id']) for p in packages if p.get('source')=='git'
                for r in mappings.get((p['name'],p.get('commit')),{}).get('plugins',[])}
    candidates=refs([p for n,p in old.items() if n in changed])
    preserved=refs([p for n,p in old.items() if n not in changed]+list(planned.values()))
    inventory=current(project)
    if not inventory['fresh'] or inventory['scanning']:
        if candidates:payload['warnings'].append('插件状态尚未更新，本预览不安排旧插件清理；可刷新状态后重新预览。')
        return
    offered=[]
    for record in inventory['installations']:
        key=(record['plugin_id'],record['build_id'])
        if key not in candidates-preserved:continue
        try:
            target=removal_target(project,record);original=receipt_for(project,record['record_digest']).attempt.step.command
        except ValueError:continue
        offered.append(dict(key='cleanup:'+record['record_key'],action='remove',plugin_id=record['plugin_id'],build_id=record['build_id'],version=original.get('version',''),
                            name=target['name'],format=original.get('format',''),available=True,reason='',default_selected=False,
                            destination_root=target['destination_root'],target=target))
    payload['cleanup_plugins']=offered


def check_package_references(project, target, payload=None):
    """Only exact server mappings prove usage; never consult MTP business state."""
    from .package_state import current as package_state
    from .plugin_api import snapshot
    state=package_state(project)
    packages={p['name']:p for p in state['packages']}
    if payload:
        for name in payload.get('absent_packages',[]):packages.pop(name,None)
        packages.update({p['name']:p['target'] for p in payload['packages']})
    index,_=snapshot()
    mappings={(m['package_name'],m['commit']):m for m in index.get('package_mappings',[])}
    build=target.get('previous_build_id',target['build_id'])
    users=sorted(p['name'] for p in packages.values() if any(r['plugin_id']==target['plugin_id'] and r['build_id']==build
        for r in mappings.get((p['name'],p.get('commit')),{}).get('plugins',[])))
    if users:raise ValueError('旧插件仍被工程包映射引用，请随关联包一起处理：'+', '.join(users))
