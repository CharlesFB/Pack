"""Offline, service-owned plugin repository. Never imports or modifies Unity/MTP."""
import hashlib
import gzip
import json
import os
import re
import shutil
import tarfile
import tempfile
import zipfile
from contextlib import contextmanager
from pathlib import Path, PurePosixPath

SCHEMA = 1
MAX_ENTRIES = 100000
MAX_EXPANDED = 4 * 1024**3
DISPLAY_FIELDS = {'name', 'format', 'version', 'published_at', 'unity', 'description'}


def digest_stream(stream):
    digest = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b''):
        digest.update(chunk)
    return digest.hexdigest()


def digest(path):
    with Path(path).open('rb') as stream:
        return digest_stream(stream)


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def write_json(path, value):
    path = Path(path)
    with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


def safe_path(value):
    if not value or '\\' in value or '\x00' in value or ':' in value:
        raise ValueError('Invalid archive path')
    path = PurePosixPath(value)
    if path.is_absolute() or '..' in path.parts or not path.parts:
        raise ValueError('Unsafe archive path')
    return str(path)


def segment(value):
    if not isinstance(value, str) or not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]{0,150}', value):
        raise ValueError('Invalid repository identifier')
    return value


def inspect_archive(path):
    """Read archives without extraction; retain exact asset hashes and GUID evidence."""
    records, total, count = [], 0, 0
    if path.suffix.lower() == '.zip':
        with zipfile.ZipFile(path) as archive:
            seen = set()
            for item in archive.infolist():
                count += 1
                total += item.file_size
                if count > MAX_ENTRIES or total > MAX_EXPANDED:
                    raise ValueError('Archive exceeds inspection limits')
                name = safe_path(item.filename)
                if name in seen or (item.external_attr >> 16) & 0o170000 == 0o120000:
                    raise ValueError('Duplicate path or symbolic link in ZIP')
                seen.add(name)
                if not item.is_dir():
                    with archive.open(item) as stream:
                        records.append({'path': name, 'sha256': digest_stream(stream), 'size': item.file_size})
        return {'layout': 'unconfirmed', 'files': sorted(records, key=lambda r: r['path'])}
    groups, seen, ignored, auxiliary = {}, set(), 0, []
    # gzip.GzipFile handles the extended gzip headers used by Asset Store exports;
    # tarfile's direct r|gz reader in Python 3.12 mishandles the FEXTRA field.
    with gzip.open(path, 'rb') as compressed, tarfile.open(fileobj=compressed, mode='r|') as archive:
        for item in archive:
            count += 1
            total += item.size
            if count > MAX_ENTRIES or total > MAX_EXPANDED:
                raise ValueError('Archive exceeds inspection limits')
            if item.isdir() and item.name in ('.', './'):
                continue
            name = safe_path(item.name)
            if item.isdir():
                continue
            if not item.isfile() or name in seen:
                raise ValueError('Unsupported or duplicate unitypackage member')
            seen.add(name)
            parts = PurePosixPath(name).parts
            if parts[-1].startswith('._'):
                ignored += 1  # macOS AppleDouble sidecars are not Unity assets.
                continue
            if name == '.icon.png':
                with archive.extractfile(item) as stream:
                    auxiliary.append({'path': name, 'sha256': digest_stream(stream), 'size': item.size})
                continue
            if len(parts) != 2 or not (re.fullmatch('[0-9a-fA-F]{32}', parts[0]) or parts[0] == 'packagemanagermanifest'):
                raise ValueError('Invalid unitypackage member layout')
            guid, kind = parts
            group = groups.setdefault(guid, {'guid': guid})
            with archive.extractfile(item) as stream:
                if kind == 'pathname':
                    if item.size > 16384:
                        raise ValueError('Oversized unitypackage pathname')
                    asset_path = safe_path(stream.read().decode('utf-8-sig').rstrip('\r\n\x00'))
                    group['path'] = asset_path
                elif kind in ('asset', 'asset.meta'):
                    group[kind] = {'sha256': digest_stream(stream), 'size': item.size}
        # Reach the gzip trailer to verify its CRC, even when tar has already ended.
        for chunk in iter(lambda: compressed.read(1024**2), b''):
            total += len(chunk)
            if total > MAX_EXPANDED:
                raise ValueError('Archive exceeds inspection limits')
    paths, package_manager_manifest = set(), None
    for group_id, group in groups.items():
        if 'path' not in group or group['path'] in paths:
            raise ValueError('Incomplete or duplicate unitypackage asset record')
        paths.add(group['path'])
        if group_id == 'packagemanagermanifest':
            package_manager_manifest = {k: v for k, v in group.items() if k != 'guid'}
            continue
        # Directories can lack an asset member; do not infer file type from extension.
        records.append(group)
    if not records:
        raise ValueError('Empty unitypackage')
    result = {'layout': 'embedded-assets-paths', 'files': sorted(records, key=lambda r: r['path']),
            'ignored_appledouble_entries': ignored,
            'missing_meta_paths': sorted(r['path'] for r in records if 'asset.meta' not in r),
            'non_assets_paths': sorted(p for p in paths if not p.startswith('Assets/'))}
    if auxiliary:
        result['auxiliary_members'] = auxiliary
    if package_manager_manifest:
        result['package_manager_manifest'] = package_manager_manifest
    control_paths = sorted(p for p in paths if any(ord(c) < 32 for c in p))
    if control_paths:
        result['control_character_paths'] = control_paths
    return result


@contextmanager
def repository_lock(root):
    import fcntl
    root.mkdir(parents=True, exist_ok=True)
    with (root / '.repository.lock').open('a') as stream:
        fcntl.flock(stream, fcntl.LOCK_EX)
        yield


def version_directory(version):
    if not version:
        return 'unversioned'
    try:
        return segment(version)
    except ValueError:
        slug = re.sub(r'[^a-zA-Z0-9._-]+', '-', version).strip('.-')[:100] or 'version'
        return slug + '-' + hashlib.sha256(version.encode()).hexdigest()[:12]


def stage_artifact(root, source, definition, namespace='magictavern'):
    plugin_id = segment(definition['id'])
    display = {key: definition.get(key, '') for key in DISPLAY_FIELDS}
    display['format'] = source.suffix.lower().lstrip('.')
    display['name'] = display['name'] or source.stem
    if any(not isinstance(value, str) for value in display.values()):
        raise ValueError('Plugin metadata fields must be strings')
    sha = digest(source)
    base = Path(segment(namespace)) / plugin_id if namespace else Path(segment(definition['directory_name']))
    relative = base / version_directory(display['version']) / sha
    destination = root / relative
    if destination.exists():
        manifest = read_json(destination / 'manifest.json')
        if digest(destination / manifest['artifact']) != sha:
            raise ValueError('Existing immutable artifact is corrupted')
        if read_json(destination / 'plugin.json') != display:
            raise ValueError('Metadata differs for an existing immutable build')
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='.staging-', dir=root) as temporary:
            stage = Path(temporary)
            artifact = stage / ('artifact.' + display['format'])
            shutil.copyfile(source, artifact)
            if digest(artifact) != sha:
                raise ValueError('Source changed while copying')
            inspection = inspect_archive(artifact)
            manifest = dict(schema_version=SCHEMA, plugin_id=plugin_id, build_id=sha,
                            artifact=artifact.name, sha256=sha, size=artifact.stat().st_size,
                            metadata_status='needs_review', **inspection)
            write_json(stage / 'plugin.json', display)
            write_json(stage / 'manifest.json', manifest)
            stage.rename(destination)
    return {'plugin_id': plugin_id, 'version': display['version'], 'build_id': sha,
            'directory': relative.as_posix(), 'metadata_status': manifest['metadata_status']}


def write_group_index(root, index):
    """Derived management index: one category per actual top-level directory."""
    groups = {}
    for plugin in index['plugins']:
        category = Path(plugin['directory']).parts[0]
        display = read_json(root / plugin['directory'] / 'plugin.json')
        group = groups.setdefault(category, {'id': category, 'directory': category,
                    'name': 'MagicTavern' if category == 'magictavern' else display['name'],
                    'plugin_ids': [], 'builds': []})
        if plugin['plugin_id'] not in group['plugin_ids']:
            group['plugin_ids'].append(plugin['plugin_id'])
        group['builds'].append({k: plugin[k] for k in ('plugin_id', 'version', 'build_id', 'directory')})
    write_json(root / 'groups.json', {'schema_version': SCHEMA, 'layout_version': 2,
               'note': 'Physical storage categories; provenance and stable plugin IDs remain in index.json.',
               'groups': sorted(groups.values(), key=lambda g: (g['id'] != 'magictavern', g['id']))})


def import_project(project, root, definitions):
    """Only index locked Git package caches matching their commit; never select stale caches."""
    project, root = Path(project).resolve(), Path(root).resolve()
    if root.is_relative_to(project):
        raise ValueError('Repository must be outside the source Unity project')
    lock_path = project / 'Packages/packages-lock.json'
    before = digest(lock_path)
    dependencies = read_json(lock_path)['dependencies']
    selected, missing = [], []
    for name, dependency in sorted(dependencies.items()):
        if not name.startswith('com.magictavern.') or dependency.get('source') != 'git':
            continue
        commit = dependency.get('hash', '')
        if not re.fullmatch('[0-9a-f]{40}', commit):
            raise ValueError('Missing full Git commit for ' + name)
        candidates = [p for p in (project / 'Library/PackageCache').glob(name + '@*')
                      if len(p.name.split('@')[-1]) >= 7 and commit.startswith(p.name.split('@')[-1])]
        if len(candidates) != 1:
            missing.append(name)
            continue
        folder = candidates[0]
        metadata = read_json(folder / 'package.json')
        if metadata.get('name') != name:
            raise ValueError('Package cache name mismatch')
        selected.append((name, commit, folder, metadata))
    if missing:
        raise ValueError('Missing or ambiguous locked package caches: ' + ', '.join(missing))
    if not selected:
        raise ValueError('No locked MagicTavern Git packages found')
    with repository_lock(root):
        index_path = root / 'index.json'
        index = read_json(index_path) if index_path.exists() else {
            'schema_version': SCHEMA, 'plugins': [], 'package_mappings': [], 'project_snapshots': []}
        if index['schema_version'] != SCHEMA:
            raise ValueError('Unsupported repository schema')
        mapping_keys = []
        for name, commit, folder, metadata in selected:
            refs = []
            for source in sorted((folder / 'Editor/PluginsPackage').rglob('*')):
                if source.suffix.lower() not in ('.zip', '.unitypackage') or not source.is_file():
                    continue
                if source.is_symlink() or not source.resolve().is_relative_to(folder.resolve()):
                    raise ValueError('Plugin source escapes package cache')
                definition = definitions.get(source.name)
                if definition is None:
                    raise ValueError('Missing curated plugin identity for: ' + source.name)
                artifact = stage_artifact(root, source, definition)
                if artifact not in index['plugins']:
                    index['plugins'].append(artifact)
                refs.append({**{k: artifact[k] for k in ('plugin_id', 'version', 'build_id')},
                             'source_path': source.relative_to(folder).as_posix(),
                             'required': None, 'phase': 'unconfirmed', 'order': len(refs)})
            key = name + '@' + commit
            mapping = {'key': key, 'package_name': name, 'package_version': metadata.get('version', ''),
                       'commit': commit, 'package_json_sha256': digest(folder / 'package.json'),
                       'source_kind': 'local_locked_package_cache', 'plugins': refs,
                       'execution_ready': False}
            previous = next((m for m in index['package_mappings'] if m['key'] == key), None)
            previous_values = dict(previous or {})
            if previous and previous.get('remote_sources'):
                previous_values['plugins'] = [dict(p, order=i) for i, p in enumerate(
                    p for p in previous['plugins'] if p['source_path'].startswith('Editor/PluginsPackage/'))]
            if previous and any(previous_values.get(k) != v for k, v in mapping.items() if k != 'source_kind'):
                raise ValueError('Mapping changed for an existing commit: ' + key)
            if not previous:
                index['package_mappings'].append(mapping)
            mapping_keys.append(key)
        if digest(lock_path) != before:
            raise ValueError('Unity lock changed during scan; index not published')
        snapshot = {'project_name': project.name, 'project_path_sha256': hashlib.sha256(str(project).encode()).hexdigest(),
                    'packages_lock_sha256': before, 'mapping_keys': mapping_keys,
                    'scope': 'locked-magictavern-git-packages',
                    'note': 'Archive availability mapping only; not proof of plugin installation.'}
        index['project_snapshots'] = [s for s in index['project_snapshots']
                                      if s['project_path_sha256'] != snapshot['project_path_sha256']] + [snapshot]
        index['plugins'].sort(key=lambda p: (p['plugin_id'], p['build_id']))
        index['package_mappings'].sort(key=lambda m: m['key'])
        verify_index(root, index)
        write_json(index_path, index)
        write_group_index(root, index)
        return index


def verify(root):
    root = Path(root).resolve()
    with repository_lock(root):
        return verify_index(root, read_json(root / 'index.json'))


def verify_index(root, index):
    if index.get('schema_version') != SCHEMA:
        raise ValueError('Unsupported repository schema')
    ids = set()
    for plugin in index['plugins']:
        directory = root / safe_path(plugin['directory'])
        if not directory.resolve().is_relative_to(root):
            raise ValueError('Repository path escapes root')
        manifest = read_json(directory / 'manifest.json')
        display = read_json(directory / 'plugin.json')
        if set(display) != DISPLAY_FIELDS or any(not isinstance(v, str) for v in display.values()):
            raise ValueError('Invalid plugin metadata')
        if (display['version'] != plugin['version'] or display['format'] not in ('zip', 'unitypackage')
                or manifest['build_id'] != plugin['build_id'] or manifest['schema_version'] != SCHEMA
                or manifest['artifact'] != 'artifact.' + display['format']):
            raise ValueError('Build metadata mismatch')
        artifact = directory / safe_path(manifest['artifact'])
        if not artifact.resolve().is_relative_to(directory.resolve()):
            raise ValueError('Artifact escapes build directory')
        if digest(artifact) != plugin['build_id'] or manifest['sha256'] != plugin['build_id']:
            raise ValueError('Artifact hash mismatch')
        if manifest['size'] != artifact.stat().st_size or manifest['plugin_id'] != plugin['plugin_id']:
            raise ValueError('Artifact identity mismatch')
        inspection = inspect_archive(artifact)
        if any(manifest[k] != v for k, v in inspection.items()):
            raise ValueError('Archive manifest mismatch')
        ids.add((plugin['plugin_id'], plugin['version'], plugin['build_id']))
    for mapping in index['package_mappings']:
        for ref in mapping['plugins']:
            if (ref['plugin_id'], ref['version'], ref['build_id']) not in ids:
                raise ValueError('Dangling package-to-plugin mapping')
    for catalog in index.get('external_catalogs', {}).values():
        for version in catalog['versions']:
            identity = (version['plugin_id'], version['version'], version['build_id'])
            if identity not in ids:
                raise ValueError('Dangling external version mapping')
            plugin = next(p for p in index['plugins'] if (p['plugin_id'], p['version'], p['build_id']) == identity)
            if version['directory'] != plugin['directory']:
                raise ValueError('External version directory mismatch')
    return {'plugins': len(ids), 'package_mappings': len(index['package_mappings']),
            'packages_with_plugins': sum(bool(m['plugins']) for m in index['package_mappings'])}
