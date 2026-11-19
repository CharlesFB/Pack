"""Project display metadata, separate from Unity's physical manifest dependencies."""
from collections import deque
import re

from django.conf import settings

from .models import InstallationPlan
from .package_maintenance import managed_dependencies, same_package


def named_ref(value):
    """A commit remains revision evidence, not a human-facing release label."""
    return value if isinstance(value, str) and value and not re.fullmatch(r'[a-fA-F0-9]{7,64}', value) else ''


def installed_refs(project, state):
    actual = {p['name']: p for p in state['packages']}
    result = {}
    plans = InstallationPlan.objects.filter(project=project, job__isnull=False).order_by('created_at').select_related('job').prefetch_related('job__steps')
    for plan in plans:
        steps = [s for s in plan.job.steps.all() if s.state == 'succeeded']
        verified = any(s.command.get('operation') == 'plan_verify' for s in steps)
        completed = {s.command.get('name') for s in steps if s.command.get('operation', 'upm') == 'upm'}
        for package in plan.payload.get('packages', []):
            name, target = package['name'], package['target']
            if name not in actual or not same_package(actual[name], target) or not (verified or name in completed):
                continue
            ref = package.get('requested_ref', '')
            catalog_id = package.get('catalog_id', '')
            if name == plan.payload.get('root_name'):
                ref = ref or plan.root.get('tag', '')
                catalog_id = plan.root.get('catalog_id') or catalog_id
            if named_ref(ref):
                result[name] = dict(tag=ref, **({'catalog_id': catalog_id} if catalog_id else {}))
    return result


def assign_depths(packages, roots):
    """Shortest distance from explicitly installed roots, including shared dependencies."""
    index = {p['name']: p for p in packages}
    depths = {name: 0 for name in roots if name in index}
    pending = deque(depths)
    while pending:
        name = pending.popleft()
        for edge in index[name]['dependencies']:
            child = edge['name']
            if child in index and child not in depths:
                depths[child] = depths[name] + 1
                pending.append(child)
    for package in packages:
        # A dependency left by a partial/cancelled plan is still a dependency.
        # The graph renderer keeps disconnected components visible separately.
        package['depth'] = depths.get(package['name'], 1)


def visible_project_packages(snapshot, items):
    """Filter display copies only; installation/cleanup still use the complete snapshot."""
    names={p['name'] for p in items} | {p.get('short_name','') for p in items}
    names.update(d['name'] for p in items for metadata in p.get('versions',{}).values()
                 for d in metadata.get('dependencies',[]))
    def managed(name):
        return name.startswith('com.magictavern.') or name in names
    records=[p for p in snapshot.get('packages',[]) if managed(p['name'])
             and p.get('source') not in ('registry','builtin')]
    hidden={p['name'] for p in snapshot.get('packages',[]) if p not in records}
    for row in records:
        row['dependencies']=[d for d in row.get('dependencies',[]) if managed(d['name']) and d['name'] not in hidden]
    snapshot['assets_packages']=[p for p in snapshot.get('assets_packages',[]) if managed(p['name'])]
    snapshot['detail']='MT 工程包状态（Unity 内置包与 Registry 包不展示）'
    return records


def independent_snapshot(project, value):
    refs = installed_refs(project, value)
    managed = managed_dependencies(project, value)
    roots = {p['name'] for p in value['packages'] if p['direct'] and p['name'] not in managed}
    packages = []
    for package in value['packages']:
        identifier = package['identifier']
        row = dict(name=package['name'], version=package['version'], installed_version=package['version'],
                   source=package['source'],
                   tag=identifier.rsplit('#', 1)[-1] if package['source'] == 'git' and '#' in identifier else '',
                   revision=package['commit'], is_local=package['source'] in ('local', 'embedded'),
                   installation_status='installed', installation_source='独立执行器实际注册信息',
                   dependencies=[dict(name=d['name'], requested_tag=d['version'].rsplit('#', 1)[-1]) for d in package.get('dependencies', [])])
        row.update(refs.get(package['name'], {}))
        packages.append(row)
    assets = value.get('assets_packages', [])
    registered = {p['name'] for p in packages}
    for local in assets:
        if local['name'] in registered:
            continue
        siblings = [p for p in assets if p['name'] == local['name']]
        registered.add(local['name'])
        roots.add(local['name'])
        packages.append(dict(name=local['name'], version=local['version'], installed_version=local['version'],
            display_name=local['display_name'], description=local['description'], tag='', revision='', is_local=True,
            assets_roots=[p['root'] for p in siblings], installation_status='installed',
            installation_source='Assets 中的 package.json（未逐项校验包文件）',
            dependencies=[dict(name=d['name'], requested_tag=d['version'].rsplit('#', 1)[-1]) for d in local.get('dependencies', [])]))
    assign_depths(packages, roots)
    return dict(schema_version=1, state='ready', captured_at=value['captured_at'], adapter='servicebase',
                packages=packages, assets_packages=assets, detail='独立执行器工程包状态')


def restore_catalog_tags(records, items):
    """Recover older installs using exact name/commit mappings, never version guessing."""
    from pathlib import Path
    from .plugin_api import read_file
    try:
        index = read_file(Path(settings.PLUGIN_REPOSITORY_ROOT) / 'index.json')
    except (OSError, ValueError):
        return
    catalog = {p['catalog_id']: p for p in items}
    releases = {}
    for mapping in index.get('package_mappings', []):
        key = (mapping['package_name'], mapping['commit'])
        for source in mapping.get('remote_sources', []):
            for item in items:
                tag = source.get('tag', '')
                if (item['name'] == key[0] and item['catalog_id'].split(':', 1)[0] == str(source.get('project_id'))
                        and tag in item['available_tags'] and named_ref(tag)):
                    releases.setdefault(key, set()).add((item['catalog_id'], tag))
    for record in records:
        options = releases.get((record['name'], record.get('revision')), set())
        if record.get('catalog_id'):
            options = {option for option in options if option[0] == record['catalog_id']}
        if named_ref(record.get('tag')):
            options = {option for option in options if option[1] == record['tag']}
        if len(options) == 1:
            catalog_id, tag = next(iter(options))
            if catalog[catalog_id]['name'] == record['name']:
                record.update(catalog_id=catalog_id, tag=tag)
