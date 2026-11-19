"""Browse a build's recorded resources without opening or extracting its archive."""
import re
from functools import lru_cache
from pathlib import Path

from django.conf import settings
from django.http import JsonResponse
from django.views.decorators.http import require_GET

from .plugin_api import inside, read_file
from .plugin_repository import MAX_ENTRIES, safe_path

PAGE_SIZE = 100


@lru_cache(maxsize=32)
def resource_tree(manifest_path, modified, size, fmt):
    manifest = read_file(Path(manifest_path))
    files = manifest['files']
    if not isinstance(files, list) or len(files) > MAX_ENTRIES:
        raise ValueError('Invalid file index')
    nodes = {}

    def add(path, folder, size=None):
        if not isinstance(path, str) or safe_path(path) != path or len(path) > 16384:
            raise ValueError('Invalid resource path')
        if size is not None and (type(size) is not int or size < 0):
            raise ValueError('Invalid resource size')
        previous = nodes.get(path)
        if previous and previous['folder'] != folder:
            raise ValueError('Conflicting resource paths')
        nodes[path] = dict(name=path.rsplit('/', 1)[-1], path=path, folder=folder, size_bytes=size,
                           meta=not folder and path.endswith('.meta'))
        parent = path.rpartition('/')[0]
        while parent:
            if parent in nodes and not nodes[parent]['folder']:
                raise ValueError('Conflicting resource paths')
            nodes.setdefault(parent, dict(name=parent.rsplit('/', 1)[-1], path=parent,
                                           folder=True, size_bytes=None, meta=False))
            parent = parent.rpartition('/')[0]

    for row in files:
        if not isinstance(row, dict):
            raise ValueError('Invalid resource record')
        if fmt == 'unitypackage':
            add(row['path'], 'asset' not in row, row.get('asset', {}).get('size'))
            if 'asset.meta' in row:
                add(row['path'] + '.meta', False, row['asset.meta']['size'])
        else:
            add(row['path'], False, row['size'])
    children = {'': []}
    for row in nodes.values():
        if row['folder']:
            children.setdefault(row['path'], [])
        children.setdefault(row['path'].rpartition('/')[0], []).append(row)
    for rows in children.values():
        rows.sort(key=lambda row: (not row['folder'], row['name'].casefold(), row['name']))
    return children, sum(not row['folder'] for row in nodes.values()), sum(row['meta'] for row in nodes.values())


@require_GET
def contents(request, plugin_id, build_id):
    path = request.GET.get('path', '')
    try:
        page = int(request.GET.get('page', '1'))
        if page < 1 or request.GET.get('meta', '0') not in ('0', '1'):
            raise ValueError()
        if path and (len(path) > 16384 or safe_path(path) != path):
            raise ValueError()
    except (ValueError, TypeError):
        return JsonResponse({'api_version': 1, 'error': '目录或分页参数无效'}, status=400)
    if not re.fullmatch('[a-f0-9]{64}', build_id):
        return JsonResponse({'api_version': 1, 'error': '插件构建不存在'}, status=404)
    try:
        root = Path(settings.PLUGIN_REPOSITORY_ROOT).resolve()
        index = read_file(root / 'index.json')
        if index['schema_version'] != 1:
            raise ValueError('Invalid repository index')
        item = next((row for row in index['plugins'] if row['plugin_id'] == plugin_id
                     and row['build_id'] == build_id and row['version'] == request.GET.get('version', '')), None)
        if not item:
            return JsonResponse({'api_version': 1, 'error': '插件版本或构建不存在'}, status=404)
        directory = inside(root, item['directory'])
        manifest_path = inside(root, str((directory / 'manifest.json').relative_to(root)))
        display = read_file(inside(root, str((directory / 'plugin.json').relative_to(root))))
        manifest = read_file(manifest_path)
        if (manifest['schema_version'] != 1 or manifest['plugin_id'] != plugin_id or manifest['build_id'] != build_id
                or manifest['sha256'] != build_id or display['version'] != item['version']
                or display['format'] not in ('zip', 'unitypackage') or manifest['artifact'] != 'artifact.' + display['format']):
            raise ValueError('Mismatched metadata')
        stat = manifest_path.stat()
        children, file_count, meta_count = resource_tree(str(manifest_path), stat.st_mtime_ns, stat.st_size, display['format'])
        if path not in children:
            return JsonResponse({'api_version': 1, 'error': '资源目录不存在'}, status=404)
        show_meta = request.GET.get('meta', '0') == '1'
        rows = [row for row in children[path] if show_meta or not row['meta']]
        return JsonResponse(dict(api_version=1, plugin_id=plugin_id, build_id=build_id, version=item['version'],
                                 format=display['format'], path=path, page=page, page_size=PAGE_SIZE, total=len(rows),
                                 file_count=file_count if show_meta else file_count-meta_count,
                                 folder_count=len(children)-1, meta_count=meta_count,
                                 entries=rows[(page-1)*PAGE_SIZE:page*PAGE_SIZE]))
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        return JsonResponse({'api_version': 1, 'error': '插件内容索引不可用，请检查仓库元数据。'}, status=503)
