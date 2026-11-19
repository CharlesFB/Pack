"""Versioned, read-only repository DTOs; never expose filesystem paths or run installers."""
import json
import re
from functools import lru_cache
from pathlib import Path

from django.conf import settings
from django.http import JsonResponse
from django.views.decorators.http import require_GET


@lru_cache(maxsize=256)
def _read(path, modified, size):
    return json.loads(Path(path).read_text())


def read_file(path):
    stat = path.stat()
    return _read(str(path), stat.st_mtime_ns, stat.st_size)


def inside(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root):
        raise ValueError('Invalid repository path')
    return path


def snapshot():
    root = Path(settings.PLUGIN_REPOSITORY_ROOT).resolve()
    index = read_file(root / 'index.json')
    if index.get('schema_version') != 1:
        raise ValueError('Unsupported index')
    current = set(index.get('catalog_snapshot', {}).get('mapping_keys', []))
    active = {(r['plugin_id'], r['version'], r['build_id']) for m in index['package_mappings']
              if m['key'] in current for r in m['plugins']}
    source_records = {}
    for catalog in index.get('external_catalogs', {}).values():
        for row in catalog['versions']:
            identity = (row['plugin_id'], row['version'], row['build_id'])
            active.add(identity)
            source_records.setdefault(identity, []).append({'project_id': catalog['project_id'],
                'ref': catalog['ref'], 'commit': row['commit'], 'filename': row['filename'],
                'version_label': row['version']})
    packages = {}
    for mapping in index['package_mappings']:
        for ref in mapping['plugins']:
            packages.setdefault((ref['plugin_id'], ref['version'], ref['build_id']), []).append({
                'name': mapping['package_name'], 'version': mapping['package_version'],
                'commit': mapping['commit'], 'current': mapping['key'] in current,
                'filename': Path(ref['source_path']).name})
    plugins = {}
    for item in index['plugins']:
        identity = (item['plugin_id'], item['version'], item['build_id'])
        directory = inside(root, item['directory'])
        category = Path(item['directory']).parts[0]
        entry = plugins.setdefault(item['plugin_id'], {'id': item['plugin_id'], 'name': item['plugin_id'],
                  'group_id': category, 'group_name': 'MagicTavern' if category == 'magictavern' else '', 'builds': []})
        availability, reviews = 'available', []
        display, manifest = {}, {}
        try:
            display = read_file(directory / 'plugin.json')
            manifest = read_file(directory / 'manifest.json')
            if (not isinstance(display, dict) or not isinstance(manifest, dict)
                    or any(not isinstance(v, str) for v in display.values())):
                raise ValueError('Invalid metadata')
            if (manifest['schema_version'] != 1 or manifest['plugin_id'] != item['plugin_id']
                    or manifest['build_id'] != item['build_id'] or manifest['sha256'] != item['build_id']
                    or display['version'] != item['version']
                    or manifest['artifact'] != 'artifact.' + display['format']):
                raise ValueError('Mismatched metadata')
            artifact = inside(directory, manifest['artifact'])
            if not artifact.is_file():
                availability = 'missing'
            elif artifact.stat().st_size != manifest['size']:
                availability = 'size_mismatch'
            for key, label in [('non_assets_paths', '包含 Assets 以外的路径'), ('missing_meta_paths', '资源缺少 .meta'),
                               ('control_character_paths', '资源路径含控制字符')]:
                if manifest.get(key):
                    reviews.append({'code': key, 'label': label, 'count': len(manifest[key]), 'samples': manifest[key][:8]})
            if manifest.get('package_manager_manifest'):
                reviews.append({'code': 'upm_manifest', 'label': '携带 UPM manifest，导入可能改变依赖', 'count': 1, 'samples': []})
            if manifest.get('layout') == 'unconfirmed':
                reviews.append({'code': 'layout', 'label': 'ZIP 导入前在网页选择解压根目录', 'count': 1, 'samples': []})
        except (OSError, ValueError, KeyError, TypeError):
            availability = 'metadata_unavailable'
            display, manifest, reviews = {}, {}, []
        name = display.get('name') or entry['name']
        entry['name'] = name
        entry['group_name'] = entry['group_name'] or name
        import_reason = ''
        try:
            from .plugin_delivery import policy
            policy(root,item['plugin_id'],item['build_id'],display.get('format',''))
            reviews = [r for r in reviews if r['code'] != 'layout']
            if availability != 'available' or any(r['code'] in ('non_assets_paths','missing_meta_paths','control_character_paths','upm_manifest') for r in reviews):
                import_reason = '制品路径或元数据需先整理'
        except ValueError as error:
            import_reason = str(error)
        entry['builds'].append({'import_available': not import_reason, 'import_reason': import_reason,'id': item['build_id'], 'version': item['version'],
             'version_status': 'known' if item['version'] else 'unconfirmed',
             'catalog_status': 'current' if identity in active else 'historical',
             'availability': availability, 'format': display.get('format', ''),
             'description': display.get('description', ''), 'unity': display.get('unity', ''),
             'published_at': display.get('published_at', ''), 'size_bytes': manifest.get('size'),
             'asset_count': len(manifest.get('files', [])), 'sha256': item['build_id'], 'reviews': reviews,
             'metadata_status': item.get('metadata_status', 'needs_review'),
             'sources': source_records.get(identity, []), 'package_references': packages.get(identity, []),
             'project_installation_status': 'not_checked'})
    for entry in plugins.values():
        entry['builds'].sort(key=lambda b: (b['catalog_status'] == 'current', bool(b['version']),
            tuple(int(v) for v in re.findall(r'\d+', b['version'])), b['version'], b['id']), reverse=True)
    return index, sorted(plugins.values(), key=lambda p: (p['group_id'] != 'magictavern', p['name'].casefold()))


def reply(handler):
    def view(request, **kwargs):
        try:
            index, plugins = snapshot()
            return handler(request, index, plugins, **kwargs)
        except (OSError, ValueError, KeyError, TypeError):
            return JsonResponse({'api_version': 1, 'error': '插件仓库索引不可用，请检查服务器仓库配置。'}, status=503)
    return require_GET(view)


@reply
def groups(request, index, plugins):
    items = {}
    for plugin in plugins:
        group = items.setdefault(plugin['group_id'], {'id': plugin['group_id'], 'name': plugin['group_name'],
                                  'plugin_count': 0, 'build_count': 0})
        group['plugin_count'] += 1
        group['build_count'] += len(plugin['builds'])
    return JsonResponse({'api_version': 1, 'groups': list(items.values()), 'plugin_count': len(plugins),
                         'build_count': sum(len(p['builds']) for p in plugins)})


@reply
def listing(request, index, plugins):
    query, group = request.GET.get('q', '').casefold(), request.GET.get('group', '')
    items = [p for p in plugins if (not group or p['group_id'] == group) and
             (not query or query in (p['name'] + ' ' + p['id']).casefold())]
    try:
        page = max(1, int(request.GET.get('page', 1)))
        size = min(100, max(1, int(request.GET.get('page_size', 100))))
    except ValueError:
        return JsonResponse({'api_version': 1, 'error': '分页参数无效'}, status=400)
    summaries = [{k: v for k, v in p.items() if k != 'builds'} | {'build_count': len(p['builds']),
        'versions': list(dict.fromkeys(b['version'] for b in p['builds'])),
        'formats': sorted({b['format'] for b in p['builds']}),
        'review_count': sum(len(b['reviews']) for b in p['builds']),
        'available_count': sum(b['availability'] == 'available' for b in p['builds'])} for p in items]
    return JsonResponse({'api_version': 1, 'total': len(items), 'page': page, 'page_size': size,
                         'plugins': summaries[(page-1)*size:page*size]})


@reply
def detail(request, index, plugins, plugin_id):
    plugin = next((p for p in plugins if p['id'] == plugin_id), None)
    return JsonResponse({'api_version': 1, 'plugin': plugin} if plugin else
                        {'api_version': 1, 'error': '插件不存在'}, status=200 if plugin else 404)


@reply
def package_mapping(request, index, plugins):
    name, commit = request.GET.get('name', ''), request.GET.get('commit', '')
    if not name or not re.fullmatch('[0-9a-fA-F]{40}', commit):
        return JsonResponse({'api_version': 1, 'error': '请提供包名与完整的 40 位 commit'}, status=400)
    mapping = next((m for m in index['package_mappings'] if m['package_name'] == name and m['commit'] == commit.lower()), None)
    if not mapping:
        return JsonResponse({'api_version': 1, 'error': '该包版本没有已保存的插件映射'}, status=404)
    return JsonResponse({'api_version': 1, 'package': {'name': name, 'commit': mapping['commit'],
        'version': mapping['package_version']}, 'plugins': [{k: r[k] for k in
        ('plugin_id', 'version', 'build_id', 'required', 'phase', 'order')} for r in mapping['plugins']]})
