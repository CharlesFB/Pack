"""Bounded dependency resolver. Only trusted servers receive repository credentials."""
import hashlib
import json
import re
import time
from urllib.parse import urlsplit, unquote, quote
from urllib.request import Request, build_opener, HTTPRedirectHandler
import gitlab
from django.conf import settings
from . import catalog
from .models import CatalogCache
from .plugin_catalog_sync import get_bytes

VERSION = re.compile(r'(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$')


def version(value):
    match = VERSION.fullmatch(value or '')
    if not match: raise ValueError('依赖必须使用明确的三段版本号')
    pre = match[4]
    return tuple(map(int, match.group(1,2,3))) + (pre is None, tuple((0,int(p)) if p.isdigit() else (1,p) for p in (pre or '').split('.')))


def compatible(requirement, unity):
    if not requirement: return True
    wanted = re.match(r'^(\d+)\.(\d+)(?:\.(\d+))?', requirement)
    actual = re.match(r'^(\d+)\.(\d+)\.(\d+)', unity)
    if not wanted or not actual: return False
    return tuple(int(v or 0) for v in actual.groups()) >= tuple(int(v or 0) for v in wanted.groups())


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs): return None


def git_uri(address):
    """Parse metadata links, including GitLab's scp-style SSH clone address."""
    if not isinstance(address, str) or len(address)>2048 or any(c.isspace() or ord(c)<32 for c in address) or '\\' in address:
        raise ValueError('仓库地址格式无效')
    scp = re.fullmatch(r'git@([A-Za-z0-9.-]+):(.+)', address)
    if scp: address='ssh://git@'+scp[1]+'/'+scp[2]
    try:
        uri=urlsplit(address)
        uri.port  # Validate a malformed or out-of-range port before any API request.
        return uri
    except ValueError:
        raise ValueError('仓库地址或端口格式无效') from None


def repository_path(path):
    if not path.endswith('.git'): raise ValueError('仓库路径必须以 .git 结尾')
    repo=unquote(path[:-4])
    if (not repo or any(p in ('.','..','') for p in repo.split('/'))
            or any(c in repo for c in '\\?#@%') or any(c.isspace() or ord(c)<32 for c in repo)):
        raise ValueError('仓库路径无效')
    return repo


class Source:
    def __init__(self, state, unity):
        self.state, self.unity = state, unity
        self.gl = None
        self.cache = {}
        self.started = time.monotonic()

    def check(self):
        if time.monotonic()-self.started > 80: raise ValueError('依赖解析超过 80 秒，请重试或缩小目标范围')

    def gitlab(self):
        self.check()
        if self.gl is None:
            self.key, self.url, token, _ = catalog.source()
            self.gl = gitlab.Gitlab(self.url, private_token=token, timeout=12, retry_transient_errors=False)
        return self.gl

    def close(self):
        if self.gl: self.gl.session.close()

    def root(self, data):
        if 'catalog_id' not in data:
            from .installations import target_from
            target = target_from(data)
            if target['source'] == 'registry': return self.registry(target['name'], target['version'], None, exact=True)
            return self.git(target['name'], target['identifier'])
        gl = self.gitlab()
        row = CatalogCache.objects.filter(key=self.key).first()
        item = next((p for p in row.packages if p['catalog_id']==data['catalog_id']), None) if row else None
        if not item or data.get('tag') not in item['available_tags']: raise ValueError('所选包或 Tag 不在当前服务目录中')
        blacklist, debug = catalog.blacklist_settings()
        pid = item['catalog_id'].split(':',1)[0]
        if not debug and pid in blacklist: raise ValueError('所选包已被目录配置隐藏')
        project = gl.projects.get(int(pid))
        return dict(self.git(item['name'], project.http_url_to_repo+'#'+data['tag']), catalog_id=item['catalog_id'])

    def git(self, name, address):
        if not isinstance(name,str) or not re.fullmatch(r'[a-z0-9][a-z0-9._-]{2,159}',name):
            raise ValueError('Git 依赖包名无效')
        try:
            return self._git(name,address)
        except ValueError as error:
            # Never include the original URL: malformed links may contain credentials.
            if isinstance(error,json.JSONDecodeError):
                raise ValueError('Git 依赖 '+name+'：package.json 无法解析') from None
            raise ValueError('Git 依赖 '+name+'：'+str(error)) from None

    def _git(self, name, address):
        gl = self.gitlab()
        uri, base = git_uri(address), urlsplit(self.url)
        ssh=uri.scheme=='ssh'
        if uri.scheme not in ('https','ssh'):raise ValueError('仅支持配置 GitLab 的 HTTPS 或 SSH 仓库地址')
        if uri.hostname!=base.hostname:raise ValueError('仓库主机与配置的 GitLab 不一致')
        if ssh:
            if uri.username!='git' or uri.password is not None:raise ValueError('SSH 地址只允许 git 用户，不能包含密码')
        elif uri.username is not None or uri.password is not None:
            raise ValueError('HTTPS 仓库地址不能包含凭据')
        elif (uri.port or 443)!=(base.port or 443):raise ValueError('HTTPS 仓库端口与配置的 GitLab 不一致')
        if not uri.fragment or len(uri.fragment)>200:raise ValueError('仓库地址必须带明确的 Tag 或 commit')
        if (uri.query and not re.fullmatch(r'path=/[A-Za-z0-9_./-]+',uri.query)) or any(p in ('.','..') for p in uri.query.split('/')):
            raise ValueError('包子目录只支持 ?path=/目录，不能包含越界路径')
        prefix = '/' if ssh else base.path.rstrip('/')+'/'
        if not uri.path.startswith(prefix): raise ValueError('Git 仓库不属于已配置的服务路径')
        repo = repository_path(uri.path[len(prefix):])
        canonical=self.url.rstrip('/')+'/'+quote(repo,safe='/._-')+'.git'+('?' + uri.query if uri.query else '')
        key = address
        if key not in self.cache:
            project = gl.projects.get(repo, lazy=not ssh)
            if ssh:
                advertised=git_uri(project.ssh_url_to_repo)
                if (advertised.scheme!='ssh' or advertised.username!='git' or advertised.password is not None
                        or advertised.hostname!=uri.hostname or (advertised.port or 22)!=(uri.port or 22)
                        or not advertised.path.startswith('/') or repository_path(advertised.path[1:])!=repo):
                    raise ValueError('SSH 地址与 GitLab 公布的仓库地址不一致')
            commit = project.commits.get(uri.fragment).id
            if not re.fullmatch('[a-f0-9]{40}',commit): raise ValueError('Git commit 无效')
            sub = uri.query[5:].strip('/') if uri.query else ''
            raw = get_bytes(project, (sub+'/' if sub else '')+'package.json', commit, limit=1024*1024)
            metadata = json.loads(raw)
            if metadata.get('name') != name: raise ValueError('Git 包元数据名称与依赖名称不一致：'+name)
            version(metadata.get('version',''))
            from .installations import target_from
            target = target_from({'name':name, 'identifier':canonical+'#'+commit})
            target['version'] = metadata.get('version','')
            self.cache[key] = {'target':target,'raw':metadata,'sha':hashlib.sha256(raw).hexdigest(), 'requested_ref':uri.fragment}
        result = self.cache[key]
        if result['target']['name'] != name: raise ValueError('同一仓库引用对应不同包名')
        return result

    def registry(self, name, minimum, installed, exact=False):
        self.check(); version(minimum)
        builtin = next((p for p in self.state.get('builtins',[]) if p['name']==name), None)
        if builtin:
            if version(builtin['version']) < version(minimum): raise ValueError('编辑器内置包版本不满足要求：'+name)
            raw = {'name':name,'version':builtin['version'],'dependencies':{d['name']:d['version'] for d in builtin.get('dependencies',[])}}
            return {'target':dict(name=name,identifier=name+'@'+builtin['version'],source='builtin',version=builtin['version'],commit=''),'raw':raw,'sha':''}
        registry = 'https://packages.unity.com'
        matched = [(len(scope),r['url'].rstrip('/')) for r in self.state.get('registries',[]) for scope in r['scopes'] if name==scope or name.startswith(scope+'.')]
        if matched: registry = max(matched)[1]
        allowed = getattr(settings,'SERVICEBASE_ALLOWED_REGISTRIES',['https://packages.unity.com'])
        if registry not in allowed: raise ValueError('该包的 Registry 尚未在服务端允许列表配置：'+name)
        key = registry+'/'+name
        if key not in self.cache:
            with build_opener(NoRedirect()).open(Request(registry+'/'+quote(name,safe=''),headers={'Accept':'application/json'}),timeout=12) as response:
                raw = response.read(32*1024*1024+1)
                if len(raw)>32*1024*1024: raise ValueError('Registry 元数据超限')
                self.cache[key] = json.loads(raw)['versions']
        versions = self.cache[key]
        candidates = []
        for value, metadata in versions.items():
            try:
                if (value==minimum if exact else version(value)>=version(minimum)) and compatible(metadata.get('unity',''), self.unity): candidates.append(value)
            except ValueError: pass
        if not candidates: raise ValueError('没有满足版本与 Unity 要求的 Registry 包：'+name)
        selected = min(candidates,key=version)
        if not exact and installed and installed['source']=='registry' and installed['version'] in candidates: selected=installed['version']
        if not isinstance(versions[selected],dict) or versions[selected].get('name')!=name or versions[selected].get('version')!=selected:
            raise ValueError('Registry 元数据名称或版本不一致：'+name)
        from .installations import target_from
        return {'target':target_from({'name':name,'identifier':name+'@'+selected}), 'raw':versions[selected], 'sha':''}


def resolve(source, root, state, unity, index, plugins, managed=None):
    root_node = source.root(root)
    root_name = root_node['target']['name']
    installed = {p['name']:p for p in state['packages']}
    local_assets=state.get('assets_packages',[])
    assets_names={p['name'] for p in local_assets}
    from .package_maintenance import reachable as walk, edges
    detached=(set(managed or []) & walk([root_name],edges(state['packages'])))-{root_name}
    hints = {root_name:root_node}
    registry_cache = {}
    warnings = ['逐条安装的新增/变更依赖会写入 manifest，成为直接依赖；服务会记录原有根包和本次添加项。',
                '插件文件冲突仅作提示，允许提交计划；实际导入仍保护不同内容的现有文件，冲突步骤会停止等待处理。']
    def registry(name, minimum):
        key=(name,minimum)
        if key not in registry_cache: registry_cache[key]=source.registry(name,minimum,installed.get(name))
        return registry_cache[key]
    # Rebuild constraints on every pass. An upgraded dependency can drop old edges;
    # neither its old minimums nor its old Git pins may constrain the final graph.
    for iteration in range(200):
        nodes, order, visiting, problems = {}, [], set(), []
        fixed, minimums = {root_name:root_node}, {}
        git_choices = {root_name:[root_node]} if root_node['target']['source']=='git' else {}
        def constrain(name, minimum=None, git_node=None):
            if git_node:
                candidates=git_choices.setdefault(name,[])
                if all(n['target']['identifier']!=git_node['target']['identifier'] for n in candidates):
                    candidates.append(git_node)
                # Keep a newer installed dependency; only the requested root may
                # intentionally be downgraded. Read its exact commit, never a moving tag.
                old=installed.get(name)
                if (name!=root_name and old and old['source']=='git'
                        and version(old['version'])>max(version(n['raw']['version']) for n in candidates)
                        and old['identifier'].rsplit('#',1)[0]==git_node['target']['identifier'].rsplit('#',1)[0]):
                    current=source.git(name,old['identifier'].rsplit('#',1)[0]+'#'+old['commit'])
                    if current['raw']['version']!=old['version']:raise ValueError('已安装 Git 包版本与 commit 元数据不一致：'+name)
                    candidates.append(current)
                fixed[name]=max(candidates,key=lambda n:version(n['raw']['version']))
            if minimum:
                version(minimum)
                if name not in minimums or version(minimum)>version(minimums[name]): minimums[name]=minimum
        def visit(name, minimum=None, git_node=None):
            source.check();constrain(name,minimum,git_node)
            if name in visiting:
                problems.append('检测到循环依赖：'+name);return
            if name in nodes: return
            if len(nodes)>=100 or len(visiting)>=64: raise ValueError('依赖图超过 100 个包或 64 层')
            # Traverse last pass's selected graph, while collecting fresh constraints.
            # Otherwise the first lower Git edge would win again on every pass.
            node=hints.get(name) or fixed.get(name) or registry(name,minimums.get(name))
            raw=node['raw']
            if not compatible(raw.get('unity',''),unity): problems.append('当前 Unity 版本不满足要求：'+name)
            nodes[name]=dict(node,dependencies=[]);visiting.add(name)
            deps, git_deps = raw.get('dependencies') or {}, raw.get('dependenciesGit') or {}
            if not isinstance(deps,dict) or not isinstance(git_deps,dict) or len(deps)+len(git_deps)>300: raise ValueError('包依赖元数据无效：'+name)
            resolved_git={n:source.git(n,v) for n,v in sorted(git_deps.items())}
            for dep,value in resolved_git.items():
                visit(dep,git_node=value);nodes[name]['dependencies'].append(dep)
            for dep,minimum in sorted(deps.items()):
                if not isinstance(minimum,str): raise ValueError('依赖版本必须为字符串')
                visit(dep,minimum=minimum);nodes[name]['dependencies'].append(dep)
            nodes[name]['dependencies']=list(dict.fromkeys(nodes[name]['dependencies']))
            visiting.remove(name);order.append(name)
        visit(root_name)
        reachable=set()
        def keep(name):
            if name in reachable: return
            reachable.add(name)
            deps=nodes[name]['dependencies'] if name in nodes else [d['name'] for d in installed.get(name,{}).get('dependencies',[])]
            for dep in deps: keep(dep)
        for name in list(nodes)+[p['name'] for p in state['packages'] if p.get('direct') and p['name'] not in detached]: keep(name)
        for local in local_assets:
            for dependency in local.get('dependencies',[]):keep(dependency['name'])
        for old in state['packages']+local_assets:
            if old in state['packages'] and (old['name'] in nodes or old['name'] not in reachable): continue
            for dep in old.get('dependencies',[]):
                if dep['name'] not in nodes: continue
                if VERSION.fullmatch(dep['version']): constrain(dep['name'],minimum=dep['version'])
                elif dep['version'].startswith(('https://','ssh://','git@')): constrain(dep['name'],git_node=source.git(dep['name'],dep['version']))
                else: raise ValueError('工程存在无法确认的依赖约束：'+dep['name'])
        desired={}
        for name in nodes:
            desired[name]=fixed.get(name) or registry(name,minimums[name])
            candidates=git_choices.get(name,[])
            if candidates:
                repositories={n['target']['identifier'].rsplit('#',1)[0] for n in candidates}
                selected_version=version(desired[name]['raw']['version'])
                commits={n['target']['commit'] for n in candidates if version(n['raw']['version'])==selected_version}
                if len(repositories)>1:problems.append('Git 依赖版本冲突：'+name+'，同名包来自不同仓库或子目录，不能自动合并')
                elif len(commits)>1:problems.append('Git 依赖版本冲突：'+name+'，最高版本号相同但 commit 不同，无法判断较高版本')
            if name in minimums and version(desired[name]['raw']['version'])<version(minimums[name]):
                problems.append('固定包版本不满足依赖最低要求：'+name)
        if all(nodes[n]['target']==desired[n]['target'] for n in nodes):
            if problems: raise ValueError(problems[0])
            break
        hints=desired
    else: raise ValueError('依赖约束未能稳定收敛')
    for name,candidates in git_choices.items():
        versions=sorted({n['raw']['version'] for n in candidates},key=version)
        if len(versions)>1:
            selected=nodes[name]
            label=selected.get('requested_ref') or selected['raw']['version']
            warnings.append('Git 依赖已自动统一：'+name+'（'+ ' / '.join(versions)+' → '+selected['raw']['version']+'；'+label+'），按 MT 包向下兼容处理。')
    plugin_lookup = {p['id']:p for p in plugins}
    errors, packages = [], []
    for name in order:
        node, old = nodes[name], installed.get(name)
        target, raw = node['target'], node['raw']
        target['version'] = raw.get('version', target['version'])
        same = bool(old and old['source']==target['source'] and old['version']==target['version'] and (target['source']!='git' or old['commit']==target['commit']))
        if target['source']=='git' and old and old.get('direct') and old['identifier']!=target['identifier']: same=False
        if old and old['source'] in ('local','embedded'): errors.append('本地/嵌入包不能由计划覆盖：'+name)
        if name in assets_names:errors.append('Assets 中存在同名本地包，不能再安装 UPM 包：'+name)
        package = dict(name=name,display_name=raw.get('displayName',name),version=target['version'],unity=raw.get('unity',''),target=target,
                       requested_ref=node.get('requested_ref',''),catalog_id=node.get('catalog_id',''),
                       dependencies=node['dependencies'],change='unchanged' if same else 'change' if old else 'add',
                       before=old['version'] if old else '',direct=not same or bool(old and old.get('direct')),plugins=[])
        if target['source']=='git':
            mapping=next((m for m in index['package_mappings'] if m['package_name']==name and m['commit']==target['commit']),None)
            if not mapping: errors.append('此 commit 缺少插件映射，不能假设没有附带插件：'+name)
            elif node['sha'] not in [mapping.get('package_json_sha256')]+[r.get('package_json_sha256') for r in mapping.get('remote_sources',[])]: errors.append('插件映射的 package.json 指纹不匹配：'+name)
            else:
                for position, ref in enumerate(sorted(mapping['plugins'],key=lambda p:p.get('order',0))):
                    plugin=plugin_lookup.get(ref['plugin_id'])
                    build=next((b for b in plugin['builds'] if b['id']==ref['build_id'] and b['version']==ref['version']),None) if plugin else None
                    key=name+':'+str(position)
                    package['plugins'].append(dict(key=key,plugin_id=ref['plugin_id'],build_id=ref['build_id'],version=ref['version'],
                        name=plugin['name'] if plugin else ref['plugin_id'],format=build['format'] if build else '',
                        available=bool(build and build['import_available']),reason=build['import_reason'] if build else '仓库构建不存在',
                        required=ref.get('required') is True,filename=ref.get('source_path','').rsplit('/',1)[-1]))
        packages.append(package)
    protected=[p for p in state['packages'] if p['name'] not in nodes and p['name'] in reachable]
    prunable=[p['name'] for p in state['packages'] if p['name'] not in reachable]
    if prunable: warnings.append('UPM 可能清理不再被引用的旧间接依赖：'+', '.join(prunable))
    return dict(root_name=root_name,packages=packages,baseline=state['fingerprint'],protected_packages=protected,
                original_direct=[p['name'] for p in state['packages'] if p.get('direct')],warnings=warnings,errors=errors)
