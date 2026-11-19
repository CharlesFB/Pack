"""Read-only GitLab catalog ingestion; all writes go to the service repository."""
import hashlib
import json
import re
import tempfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import gitlab

from .plugin_repository import read_json, repository_lock, safe_path, stage_artifact, verify_index, write_json, write_group_index


def highest_release(name, tags):
    # Match the existing package version convention: compare the numeric suffix,
    # including named release variants, while excluding all debug tags.
    pattern = re.compile(re.escape(name) + r'-(?:.*-)?(\d+(?:\.\d+){1,3})$')
    candidates = []
    for tag in tags:
        match = pattern.fullmatch(tag)
        if match and 'debug' not in tag.lower():
            numbers = tuple(int(v) for v in match[1].split('.'))
            standard = tag == name + '-' + match[1]
            candidates.append((numbers + (0,) * (4 - len(numbers)), standard, tag))
    return max(candidates)[2] if candidates else None


def get_bytes(project, path, commit, limit=32 * 1024**2):
    data = bytearray()
    def append(chunk):
        if len(data) + len(chunk) > limit:
            raise ValueError('Remote file exceeds size limit')
        data.extend(chunk)
    project.files.raw(file_path=path, ref=commit, streamed=True, action=append)
    return bytes(data)


def scan_repository(url, token, project_id, entries):
    results = []
    with gitlab.Gitlab(url, private_token=token, timeout=30, retry_transient_errors=True) as gl:
        project = gl.projects.get(project_id, lazy=True)
        try:
            tags = {tag.name: tag for tag in project.tags.list(iterator=True, per_page=100)}
        except Exception as error:
            return [dict(project_id=project_id, package_short_name=e['PackageName'], state='error',
                         error_type=type(error).__name__) for e in entries]
        for entry in entries:
            name = entry['PackageName']
            row = dict(project_id=project_id, package_short_name=name)
            tag = entry.get('Tag') or highest_release(name, tags)
            if not tag:
                results.append(dict(row, state='no_release', available_tags=[t for t in tags if t.startswith(name + '-')]))
                continue
            try:
                commit = tags[tag].commit['id']
                raw = get_bytes(project, 'package.json', commit)
                metadata = json.loads(raw)
                # Enumerate every page of the full tree so nonstandard plugin locations are reported too.
                files = []
                for item in project.repository_tree(ref=commit, recursive=True, iterator=True, per_page=100):
                    if item['type'] == 'blob' and Path(item['path']).suffix.lower() in ('.zip', '.unitypackage'):
                        files.append({'path': safe_path(item['path']), 'blob_id': item['id'],
                                      'conventional_location': item['path'].startswith('Editor/PluginsPackage/')})
                results.append(dict(row, state='scanned', tag=tag, commit=commit,
                                    package_name=metadata['name'], package_version=metadata.get('version', ''),
                                    package_json_sha256=hashlib.sha256(raw).hexdigest(),
                                    files=sorted(files, key=lambda f: f['path'])))
            except Exception as error:
                results.append(dict(row, state='error', tag=tag, error_type=type(error).__name__))
    return results


def scan_catalog(root, url, token, ref, log=print):
    root = Path(root).resolve()
    root.mkdir(parents=True, exist_ok=True)
    with gitlab.Gitlab(url, private_token=token, timeout=30, retry_transient_errors=True) as gl:
        catalog = gl.projects.get(647, lazy=True)
        commit = catalog.commits.get(ref).id
        entries = json.loads(get_bytes(catalog, 'upm-list.json', commit))['Packages']
    groups = {}
    for entry in entries:
        group = groups.setdefault(int(entry['ProjectId']), [])
        if not any(e['PackageName'] == entry['PackageName'] for e in group):
            group.append({'PackageName': entry['PackageName']})
    rows = []
    with ThreadPoolExecutor(max_workers=4) as pool:
        futures = [pool.submit(scan_repository, url, token, project_id, group) for project_id, group in groups.items()]
        for future in as_completed(futures):
            batch = future.result()
            rows.extend(batch)
            log('Scanned {}/{} entries ({} archive references)'.format(
                len(rows), sum(map(len, groups.values())), sum(len(r.get('files', [])) for r in rows)))
    report = {'schema_version': 1, 'catalog_project_id': 647, 'catalog_commit': commit,
              'selection': 'highest numeric version suffix among package tags without debug; named release variants included; standard tag wins equal version',
              'rows': sorted(rows, key=lambda r: (r['project_id'], r['package_short_name']))}
    with repository_lock(root):
        write_json(root / 'catalog-scan.json', report)
    return report


def download(project, item, commit, target):
    size = 0
    with target.open('wb') as stream:
        def append(chunk):
            nonlocal size
            size += len(chunk)
            if size > 1024**3:
                raise ValueError('Artifact exceeds 1 GiB download limit')
            stream.write(chunk)
        project.files.raw(file_path=item['path'], ref=commit, streamed=True, action=append)
    # Git blob IDs authenticate the Git response, except LFS where the tree stores a pointer.
    if target.stat().st_size < 1024 and target.read_bytes().startswith(b'version https://git-lfs.github.com/spec/v1'):
        raise ValueError('LFS pointer returned instead of artifact')
    sha = hashlib.sha1(('blob ' + str(size) + '\0').encode())
    with target.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024**2), b''):
            sha.update(chunk)
    if sha.hexdigest() != item['blob_id']:
        raise ValueError('Git blob checksum mismatch')


def import_scan(root, report, definitions, url, token, log=print, *, update_catalog_snapshot=True):
    root = Path(root).resolve()
    if any(r['state'] == 'error' for r in report['rows']):
        raise ValueError('Scan has errors; resolve them before publishing')
    unknown = sorted({Path(f['path']).name for r in report['rows'] for f in r.get('files', [])
                      if Path(f['path']).name not in definitions})
    if unknown:
        raise ValueError('Register plugin identities first: ' + ', '.join(unknown))
    # Network data remains in private staging until a complete, verified index can be published.
    with tempfile.TemporaryDirectory(prefix='servicebase-plugins-') as temporary:
        cache, prepared = {}, []
        with gitlab.Gitlab(url, private_token=token, timeout=60, retry_transient_errors=True) as gl:
            for row in report['rows']:
                if row['state'] != 'scanned':
                    continue
                for item in row['files']:
                    blob = item['blob_id']
                    if blob not in cache:
                        target = Path(temporary) / (blob + Path(item['path']).suffix.lower())
                        log('Downloading {} / {}'.format(row['package_short_name'], Path(item['path']).name))
                        download(gl.projects.get(row['project_id'], lazy=True), item, row['commit'], target)
                        cache[blob] = target
                    prepared.append((row, item, cache[blob]))
        with repository_lock(root):
            index = read_json(root / 'index.json') if (root / 'index.json').exists() else {
                'schema_version': 1, 'plugins': [], 'package_mappings': [], 'project_snapshots': []}
            refs_by_key = {}
            for row, item, target in prepared:
                definition = definitions[Path(item['path']).name]
                artifact = stage_artifact(root, target, definition)
                if artifact not in index['plugins']:
                    index['plugins'].append(artifact)
                key = row['package_name'] + '@' + row['commit']
                refs = refs_by_key.setdefault(key, [])
                ref_item = {**{k: artifact[k] for k in ('plugin_id', 'version', 'build_id')},
                            'source_path': item['path'], 'required': None, 'phase': 'unconfirmed', 'order': len(refs)}
                if not any(r['source_path'] == ref_item['source_path'] for r in refs):
                    refs.append(ref_item)
            mapping_keys = []
            for row in report['rows']:
                if row['state'] != 'scanned':
                    continue
                key = row['package_name'] + '@' + row['commit']
                refs = refs_by_key.get(key, [])
                mapping = {'key': key, 'package_name': row['package_name'], 'package_version': row['package_version'],
                           'commit': row['commit'], 'package_json_sha256': row['package_json_sha256'],
                           'source_kind': 'gitlab_release', 'plugins': refs, 'execution_ready': False}
                previous = next((m for m in index['package_mappings'] if m['key'] == key), None)
                if previous:
                    remote_by_path = {r['source_path']: r for r in refs}
                    def same_ref(a, b):
                        return all(a.get(k) == b.get(k) for k in ('plugin_id', 'version', 'build_id', 'source_path'))
                    if (any(not same_ref(p, remote_by_path.get(p['source_path'], {})) for p in previous['plugins'])
                            or previous['package_version'] != row['package_version']):
                        raise ValueError('Local/remote mapping conflict: ' + key)
                    old_paths = {p['source_path'] for p in previous['plugins']}
                    if previous['source_kind'] == 'local_locked_package_cache' and any(
                            p['source_path'].startswith('Editor/PluginsPackage/') and p['source_path'] not in old_paths for p in refs):
                        raise ValueError('Remote conventional plugin set differs from local cache: ' + key)
                    mapping = previous
                    mapping['plugins'] = refs
                else:
                    index['package_mappings'].append(mapping)
                provenance = {'project_id': row['project_id'], 'tag': row['tag'],
                              'catalog_commit': report['catalog_commit'],
                              'package_json_sha256': row['package_json_sha256'], 'verified_git_blobs': True,
                              'scope': 'full_repository_tree'}
                sources = mapping.setdefault('remote_sources', [])
                if provenance not in sources:
                    sources.append(provenance)
                mapping_keys.append(key)
            if update_catalog_snapshot:
                index['catalog_snapshot'] = {'catalog_project_id': 647, 'catalog_commit': report['catalog_commit'],
                                             'selection': report.get('selection', ''),
                                             'mapping_keys': sorted(set(mapping_keys)),
                                             'no_release': [r for r in report['rows'] if r['state'] == 'no_release']}
            index['plugins'].sort(key=lambda p: (p['plugin_id'], p['version'], p['build_id']))
            index['package_mappings'].sort(key=lambda m: m['key'])
            result = verify_index(root, index)
            write_json(root / 'index.json', index)
            write_group_index(root, index)
            return result
