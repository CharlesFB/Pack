"""Ingest all requested versioned external plugins from GitLab 2636 without MTP runtime."""
import hashlib
import json
import re
from pathlib import Path

import gitlab

from .plugin_catalog_sync import download, get_bytes
from .plugin_repository import (digest, read_json, repository_lock, safe_path, stage_artifact,
                                verify_index, write_json, write_group_index)


def external_plugin_directory(name):
    aliases = {'A PathFinding Project Pro': 'astar-pathfinding-pro', 'Adjust SDK': 'adjust',
               'Applovin MAX SDK': 'applovin-max', 'DOTween Pro': 'dotween-pro', 'EDMU': 'edm4u',
               'Odin Inspector and Serializer': 'odin-inspector', 'Spine': 'spine',
               'Corgi Engine - 2D + 2.5D Platformer': 'corgi-engine',
               'Behavior Designer Behavior Trees for Everyone': 'behavior-designer',
               'Anti Cheat ToolKit': 'anti-cheat-toolkit', 'Task Invoker - Mobile Background Tasks': 'task-invoker',
               'Text Animator for Unity': 'text-animator', 'NiceVibrations': 'nice-vibrations'}
    return aliases.get(name, re.sub('[^a-z0-9]+', '-', name.lower()).strip('-')[:100])


def requested_versions(document):
    rows, seen = [], set()
    for package in document['Packages']:
        name = package['PackageName']
        if '/' in name or '\\' in name or name in ('.', '..'):
            raise ValueError('Invalid external plugin name')
        versions, tags, files = package['Versions'], package['Tags'], package['PluginFileNames']
        if package.get('ProjectId') != 2636 or len(versions) != len(files) or tags != versions:
            raise ValueError('External version/file index mismatch: ' + name)
        slug = re.sub('[^a-z0-9]+', '-', name.lower()).strip('-')[:75]
        plugin_id = 'external2636-' + slug + '-' + hashlib.sha256(name.encode()).hexdigest()[:8]
        for order, (version, filename) in enumerate(zip(versions, files)):
            if '/' in version or '\\' in version or '/' in filename or '\\' in filename:
                raise ValueError('Invalid external version or filename')
            path = safe_path('plugins/' + name + '/' + version + '/' + filename)
            if Path(filename).suffix.lower() not in ('.unitypackage', '.zip') or path in seen:
                raise ValueError('Unsupported or duplicate external artifact')
            seen.add(path)
            rows.append({'package_name': name, 'plugin_id': plugin_id, 'version': version,
                         'source_path': path, 'filename': filename, 'source_order': order})
    if not rows:
        raise ValueError('External list is empty')
    return rows


def verify_cached_blob(path, blob):
    if not path.exists():
        return False
    sha = hashlib.sha1(('blob ' + str(path.stat().st_size) + '\0').encode())
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024**2), b''):
            sha.update(chunk)
    return sha.hexdigest() == blob


def sync_external(root, cache, document, url, token, log=print):
    root, cache = Path(root).resolve(), Path(cache).resolve()
    rows = requested_versions(document)
    cache.mkdir(parents=True, exist_ok=True)
    root.mkdir(parents=True, exist_ok=True)
    with gitlab.Gitlab(url, private_token=token, timeout=90, retry_transient_errors=True) as gl:
        project = gl.projects.get(2636, lazy=True)
        commit = project.commits.get('main').id
        tree = {f['path']: f for f in project.repository_tree(
            ref=commit, recursive=True, iterator=True, per_page=100) if f['type'] == 'blob'}
        missing = [r['source_path'] for r in rows if r['source_path'] not in tree]
        if missing:
            raise ValueError('Requested external files missing at main: ' + ', '.join(missing))
        requested = {r['source_path'] for r in rows}
        extra = sorted(p for p in tree if p.startswith('plugins/') and Path(p).suffix.lower() in
                       ('.unitypackage', '.zip') and p not in requested)
        metadata_cache = {}
        for count, row in enumerate(rows, 1):
            blob = tree[row['source_path']]['id']
            target = cache / (blob + Path(row['filename']).suffix.lower())
            if not verify_cached_blob(target, blob):
                log('[{}/{}] Download {} / {}'.format(count, len(rows), row['package_name'], row['version']))
                partial = target.with_suffix(target.suffix + '.part')
                download(project, {'path': row['source_path'], 'blob_id': blob}, commit, partial)
                partial.replace(target)
            else:
                log('[{}/{}] Cached {} / {}'.format(count, len(rows), row['package_name'], row['version']))
            row.update(commit=commit, blob_id=blob, local_file=str(target))
            parent = str(Path(row['source_path']).parent)
            fallback = str(Path(parent).parent)
            # Same precedence as MTP: a version's package.json, otherwise the plugin-level one.
            metadata_path = next((p + '/package.json' for p in (parent, fallback) if p + '/package.json' in tree), None)
            metadata = {}
            if metadata_path:
                if metadata_path not in metadata_cache:
                    raw = get_bytes(project, metadata_path, commit)
                    metadata_cache[metadata_path] = (json.loads(raw), hashlib.sha256(raw).hexdigest())
                metadata, metadata_hash = metadata_cache[metadata_path]
                row.update(metadata_path=metadata_path, metadata_sha256=metadata_hash,
                           metadata_scope='version' if metadata_path.startswith(parent + '/') else 'plugin')
            # A plugin-level version cannot override the requested folder's version label.
            row['display'] = {'id': row['plugin_id'], 'name': row['package_name'], 'version': row['version'],
                              'directory_name': external_plugin_directory(row['package_name']),
                              'description': metadata.get('description', '') if isinstance(metadata.get('description', ''), str) else '',
                              'unity': metadata.get('unity', '') if isinstance(metadata.get('unity', ''), str) else '',
                              'published_at': metadata.get('published_at', '') if isinstance(metadata.get('published_at', ''), str) else ''}
    with repository_lock(root):
        index = read_json(root / 'index.json')
        published = []
        for count, row in enumerate(rows, 1):
            log('[{}/{}] Verify archive {} / {}'.format(count, len(rows), row['package_name'], row['version']))
            artifact = stage_artifact(root, Path(row['local_file']), row['display'], namespace=None)
            if artifact not in index['plugins']:
                index['plugins'].append(artifact)
            entry = {k: v for k, v in row.items() if k not in ('local_file', 'display')}
            entry.update(build_id=artifact['build_id'], directory=artifact['directory'], execution_ready=False)
            published.append(entry)
        catalog = {'schema_version': 1, 'project_id': 2636, 'ref': 'main', 'commit': commit,
                   'selection': 'all versions in provided MTP external list; preserve version variants and source order',
                   'source_refresh_time': document.get('RefreshTime', ''),
                   'versions': published, 'additional_remote_archives': extra}
        index.setdefault('external_catalogs', {})['2636'] = catalog
        index['plugins'].sort(key=lambda p: (p['plugin_id'], p['version'], p['build_id']))
        result = verify_index(root, index)
        write_json(root / 'index.json', index)
        # Convenient derived view; index.json remains the authoritative atomic publication.
        write_json(root / 'external-2636-index.json', catalog)
        write_group_index(root, index)
        return dict(result, external_packages=len({r['package_name'] for r in rows}), external_versions=len(rows))
