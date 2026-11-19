"""Resumable local-only migration from source wrappers to plugin directories."""
from pathlib import Path

from .plugin_external_sync import external_plugin_directory
from .plugin_repository import (read_json, repository_lock, verify_index, version_directory,
                                write_group_index, write_json)


def migrate_layout(root):
    root = Path(root).resolve()
    with repository_lock(root):
        index = read_json(root / 'index.json')
        journal_path = root / 'layout-migration.json'
        journal = read_json(journal_path) if journal_path.exists() else None
        if not journal or journal['status'] == 'complete':
            moves = {}
            for catalog in index.get('external_catalogs', {}).values():
                for entry in catalog['versions']:
                    old = entry['directory']
                    target = (Path(external_plugin_directory(entry['package_name'])) /
                              version_directory(entry['version']) / entry['build_id']).as_posix()
                    if old != target:
                        moves[old] = target
            if moves:
                if len(set(moves.values())) != len(moves):
                    raise ValueError('Ambiguous target build directories')
                write_json(root / 'index.before-layout-v2.json', index)
                journal = {'layout_version': 2, 'status': 'moving', 'moves': moves}
                write_json(journal_path, journal)
            else:
                write_group_index(root, index)
                return {'moved_builds': 0, **verify_index(root, index)}
        moves = journal['moves']
        for old, new in moves.items():
            source, target = root / old, root / new
            if not source.resolve().is_relative_to(root) or not target.resolve().is_relative_to(root):
                raise ValueError('Layout path escapes repository')
            if source.exists():
                if target.exists():
                    raise ValueError('Target already exists: ' + new)
                target.parent.mkdir(parents=True, exist_ok=True)
                source.rename(target)
            elif not target.exists():
                raise ValueError('Missing migration build: ' + old)
        for plugin in index['plugins']:
            plugin['directory'] = moves.get(plugin['directory'], plugin['directory'])
        for catalog in index.get('external_catalogs', {}).values():
            for entry in catalog['versions']:
                entry['directory'] = moves.get(entry['directory'], entry['directory'])
        index['layout_version'] = 2
        result = verify_index(root, index)
        write_json(root / 'index.json', index)
        for source_id, catalog in index.get('external_catalogs', {}).items():
            write_json(root / ('external-' + source_id + '-index.json'), catalog)
        write_group_index(root, index)
        # Update generated report links, leaving immutable source paths untouched.
        for report in root.glob('*report.md'):
            text = report.read_text()
            for old, new in moves.items():
                text = text.replace(old + '/', new + '/')
            report.write_text(text)
        old_root = root / 'external-2636'
        if old_root.exists():
            for finder_metadata in old_root.rglob('.DS_Store'):
                if finder_metadata.is_file():
                    finder_metadata.unlink()
            for directory in sorted((p for p in old_root.rglob('*') if p.is_dir()), key=lambda p: len(p.parts), reverse=True):
                if not any(directory.iterdir()):
                    directory.rmdir()
            if not any(old_root.iterdir()):
                old_root.rmdir()
        journal['status'] = 'complete'
        write_json(journal_path, journal)
        return {'moved_builds': len(moves), **result}
