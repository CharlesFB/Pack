"""Conservative package removal: immutable service history proves dependency ownership."""
from .models import InstallJob


def same_package(current, target):
    return (current['source']==target.get('source') and current['version']==target.get('version')
            and (current['source']!='git' or (current.get('commit')==target.get('commit')
                 and current.get('identifier')==target.get('identifier'))))


def managed_dependencies(project, state):
    ledger={}
    for job in InstallJob.objects.filter(project=project).order_by('created_at').prefetch_related('steps__attempts'):
        planned=job.target.get('operation')=='plan'
        if 'baseline_packages' in job.target:
            baseline={p['name']:p for p in job.target['baseline_packages']}
            for name,entry in ledger.items():
                if name not in baseline or not same_package(baseline[name],entry['target']):entry['owned']=False
        for step in sorted(job.steps.all(),key=lambda s:s.position):
            if step.state!='succeeded': continue
            target=step.command;name=target.get('name');operation=target.get('operation','upm')
            if operation=='upm_remove': ledger.pop(name,None)
            elif operation=='upm' and target.get('source') in ('registry','git','builtin'):
                old=ledger.get(name)
                owned=old['owned'] if old else planned and name not in job.target.get('original_direct',[name])
                explicit=not planned or name==job.target['name'] or bool(old and old['root'])
                ledger[name]=dict(owned=owned,root=explicit,target=target)
            elif operation=='plan_verify' and job.target.get('plan_action','install') in ('install','update'):
                root=job.target['name']
                if root in ledger: ledger[root]['root']=True
    return {p['name'] for p in state['packages'] if p.get('direct') and p['name'] in ledger
            and ledger[p['name']]['owned'] and not ledger[p['name']]['root']
            and same_package(p,ledger[p['name']]['target'])}


def edges(packages):
    return {p['name']:[d['name'] if isinstance(d,dict) else d for d in p.get('dependencies',[])] for p in packages}


def reachable(roots, graph):
    seen=set();pending=list(roots)
    while pending:
        name=pending.pop()
        if name not in seen: seen.add(name);pending.extend(graph.get(name,[]))
    return seen


def parent_first(names, graph):
    ordered=[];visiting=set();seen=set()
    def visit(name):
        if name in visiting: raise ValueError('待清理依赖存在循环，不能安全排序')
        if name in seen:return
        visiting.add(name)
        for dep in graph.get(name,[]):
            visit(dep)
        visiting.remove(name);seen.add(name)
        if name in names:ordered.append(name)
    for name in sorted(names):visit(name)
    return list(reversed(ordered))


def removal_target(package):
    return dict(operation='upm_remove',name=package['name'],identifier='remove:'+package['name'],
                source=package['source'],version=package['version'],commit=package.get('commit',''),
                previous={k:package.get(k,'') for k in ('name','source','version','identifier','commit')})


def prepare_cleanup(project, state, payload, uninstall=False):
    current={p['name']:p for p in state['packages']};root=payload['root_name']
    if root not in current:
        if uninstall:raise ValueError('工程尚未安装此包')
        payload['plan_action']='install';return
    if uninstall and (not current[root]['direct'] or current[root]['source'] in ('local','embedded') or root=='com.magictavern.mtpmanager'):
        raise ValueError('只能卸载工程直接依赖；本地、嵌入包和 MTP 自身不由此入口删除')
    before=edges(state['packages']);after=dict(before)
    planned={p['name']:p for p in payload['packages']}
    after.update(edges(payload['packages']))
    if uninstall:after.pop(root,None)
    old_tree=reachable([root],before)
    owned=managed_dependencies(project,state)
    candidates=(old_tree & owned)-set(planned)-({root} if uninstall else set())
    direct={p['name'] for p in current.values() if p['direct']}
    next_roots=(direct-candidates-({root} if uninstall else set())) | {p['name'] for p in planned.values() if p['direct']}
    keep=reachable(next_roots,after)
    # Assets packages remain outside the UPM graph, but their UPM dependencies are protected.
    keep.update(reachable([d['name'] for p in state.get('assets_packages',[]) for d in p.get('dependencies',[])],after))
    if uninstall and root in keep:
        users=sorted(n for n in keep if root in after.get(n,[]))
        raise ValueError('此包仍被其他包引用，不能卸载：'+', '.join(users))
    remove=(candidates-keep) | ({root} if uninstall else set())
    # Unproven direct dependencies remain roots, including user-created and repinned packages.
    absent=old_tree-keep
    absent={n for n in absent if n in current}
    payload.update(plan_action='uninstall' if uninstall else 'update',
        removals=[dict(name=n,version=current[n]['version'],target=removal_target(current[n]),
                      reason='所选根包' if n==root and uninstall else '服务添加且已无引用的旧依赖') for n in parent_first(remove,before)],
        absent_packages=sorted(absent),expected_direct=sorted((direct | {p['name'] for p in planned.values() if p['direct']})-remove),
        protected_packages=[p for p in state['packages'] if p['name'] in keep and p['name'] not in planned],
        retained_dependencies=[dict(name=n,reason='仍有其他根包或新版本依赖引用' if n in owned else '原有依赖或来源不明确，保留')
                               for n in sorted(old_tree-set(planned)-absent-{root}) if n in current],
        baseline_packages=state['packages'])
    if remove:payload['warnings'].append('卸载只调用 UPM 移除清单项；共享依赖和缺少服务创建证据的依赖保留。')


def uninstall_preview(project, state, root):
    name=root.get('name')
    if not isinstance(name,str):raise ValueError('请选择要卸载的包')
    payload=dict(root_name=name,packages=[],baseline=state['fingerprint'],original_direct=[p['name'] for p in state['packages'] if p['direct']],
                 protected_packages=[],warnings=['已完成的卸载不会自动回滚；每次 Remove 前保留本地 manifest 和 lock 备份。'],errors=[])
    prepare_cleanup(project,state,payload,uninstall=True)
    return payload
