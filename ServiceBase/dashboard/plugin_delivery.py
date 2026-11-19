"""Immutable, bounded plugin delivery plans. No extraction into a Unity project."""
import gzip
import hashlib
import json
import re
import shutil
import stat
import tarfile
import tempfile
import zipfile
from pathlib import Path
from django.conf import settings
from .plugin_api import inside, read_file
from .plugin_repository import digest, inspect_archive, safe_path, write_json

MAX_FILES = 10000
MAX_BYTES = 2 * 1024**3

def delivery_root():
    return Path(getattr(settings, 'PLUGIN_DELIVERY_ROOT', Path(settings.BASE_DIR) / '.runtime/plugin-deliveries'))

def build_source(plugin_id, build_id):
    root = Path(settings.PLUGIN_REPOSITORY_ROOT).resolve()
    item = next((x for x in read_file(root/'index.json')['plugins'] if x['plugin_id']==plugin_id and x['build_id']==build_id), None)
    if not item: raise ValueError('插件构建不存在')
    directory = inside(root, item['directory'])
    display, manifest = read_file(directory/'plugin.json'), read_file(directory/'manifest.json')
    artifact = inside(directory, manifest['artifact'])
    if manifest['sha256'] != build_id or manifest['plugin_id'] != plugin_id or artifact.stat().st_size != manifest['size']:
        raise ValueError('制品索引不一致')
    return root, display, manifest, artifact

def policy(root, plugin_id, build_id, fmt):
    if fmt == 'unitypackage': return {'mode':'unitypackage'}
    if fmt == 'zip': return {'mode':'choose-assets-root'}
    raise ValueError('不支持的插件格式')

def asset_path(path):
    result = safe_path(path)
    if result != path or not result.startswith('Assets/') or any(ord(c)<32 for c in result) or any(p.startswith('.') or p.endswith((' ','.')) for p in result.split('/')):
        raise ValueError('仅支持规范的 Assets 内资源路径')
    return result

def prepare(plugin_id, build_id):
    root, display, manifest, artifact = build_source(plugin_id, build_id)
    if not re.fullmatch('[a-zA-Z0-9._-]+',plugin_id) or not re.fullmatch('[a-f0-9]{64}',build_id): raise ValueError('无效构建标识')
    rule = policy(root,plugin_id,build_id,display['format'])
    if digest(artifact) != build_id: raise ValueError('制品 SHA-256 校验失败')
    inspection = inspect_archive(artifact)
    if inspection.get('package_manager_manifest'): raise ValueError('该 unitypackage 携带 UPM manifest，暂不支持混合导入')
    if inspection.get('non_assets_paths') or inspection.get('missing_meta_paths') or inspection.get('control_character_paths'):
        raise ValueError('该 unitypackage 有非 Assets 路径或缺失元数据，需先整理制品')
    cache = delivery_root(); cache.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(dir=cache,prefix='.prepare-') as temporary:
        stage=Path(temporary); payload=stage/'files'; payload.mkdir()
        def put(path, stream):
            path=asset_path(path); out=payload/path; out.parent.mkdir(parents=True,exist_ok=True)
            if out.exists(): raise ValueError('重复资源路径')
            with out.open('wb') as dst: shutil.copyfileobj(stream,dst,1024*1024)
        if display['format']=='unitypackage':
            mapping={r['guid']:r for r in inspection['files']}
            with gzip.open(artifact,'rb') as gz, tarfile.open(fileobj=gz,mode='r|') as tar:
                for entry in tar:
                    parts=entry.name.removeprefix('./').split('/')
                    if entry.isfile() and len(parts)==2 and parts[0] in mapping and parts[1] in ('asset','asset.meta'):
                        rec=mapping[parts[0]]; put(rec['path']+('.meta' if parts[1]=='asset.meta' else ''),tar.extractfile(entry))
            shutil.copyfile(artifact,stage/'import.unitypackage')
        else:
            with zipfile.ZipFile(artifact) as z:
                for entry in z.infolist():
                    if entry.is_dir(): continue
                    mode=entry.external_attr>>16
                    if stat.S_IFMT(mode) not in (0,stat.S_IFREG): raise ValueError('ZIP 不支持链接或特殊文件')
                    parts=Path(entry.filename).parts
                    if '__MACOSX' in parts or parts[-1]=='.DS_Store' or parts[-1].startswith('._'): continue
                    # Assets is a staging anchor; keep the entire archive path below it.
                    with z.open(entry) as stream: put('Assets/'+entry.filename,stream)
        paths={p.relative_to(payload).as_posix() for p in payload.rglob('*') if p.is_file() and not p.name.endswith('.meta')}
        paths.update(p.relative_to(payload).as_posix()[:-5] for p in payload.rglob('*.meta'))
        for path in list(paths):
            parent=Path(path).parent
            while parent.as_posix()!='Assets': paths.add(parent.as_posix()); parent=parent.parent
        if not paths or len(paths)>MAX_FILES: raise ValueError('资源数量超出支持范围')
        records=[]; guids=set(); folded=set(); total=0
        for path in sorted(paths):
            asset_path(path)
            if path.casefold() in folded: raise ValueError('资源路径存在大小写冲突')
            folded.add(path.casefold()); file=payload/path; meta=payload/(path+'.meta'); folder=not file.is_file()
            generated=not meta.exists()
            if generated:
                guid=hashlib.sha256((plugin_id+'\n'+path).encode()).hexdigest()[:32]
                meta.parent.mkdir(parents=True,exist_ok=True)
                meta.write_text('fileFormatVersion: 2\nguid: '+guid+('\nfolderAsset: yes' if folder else '')+'\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
            text=meta.read_text(encoding='utf-8-sig'); match=re.search(r'^guid: ([a-fA-F0-9]{32})\s*$',text,re.M)
            if not match: raise ValueError('资源 meta 缺少有效 GUID')
            guid=match[1].lower()
            if display['format']=='unitypackage' and not generated:
                original=next((r for r in inspection['files'] if r['path']==path),None)
                if original and original['guid'].lower()!=guid: raise ValueError('unitypackage GUID 与 meta 不一致')
            if guid in guids: raise ValueError('制品内 GUID 重复')
            guids.add(guid)
            if folder and not re.search(r'^folderAsset: yes\s*$',text,re.M): raise ValueError('文件夹 meta 类型不正确')
            if not folder and 'folderAsset: yes' in text: raise ValueError('文件与文件夹 meta 类型不一致')
            size=0 if folder else file.stat().st_size
            total+=size+meta.stat().st_size
            records.append(dict(path=path,folder=folder,sha256='' if folder else digest(file),size=size,meta_sha256=digest(meta),guid=guid,generated=generated))
        if sum(len(f['path'])*3+500 for f in records)>2200000: raise ValueError('归属记录超过本阶段大小限制')
        if total>MAX_BYTES: raise ValueError('展开资源超过 2 GiB')
        requires_destination=display['format']=='zip'
        recipe=dict(schema=1,plugin_id=plugin_id,build_id=build_id,format=display['format'],files=records)
        raw=json.dumps(recipe,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()
        recipe_id=hashlib.sha256(raw).hexdigest(); (stage/'recipe.json').write_bytes(raw)
        # STORED bounds decompression work and allows the client to validate every staged file.
        bundle=stage/'bundle.zip'
        with zipfile.ZipFile(bundle,'w',compression=zipfile.ZIP_STORED) as z:
            for p in sorted(stage.rglob('*')):
                if p.is_file() and p!=bundle:
                    info=zipfile.ZipInfo(p.relative_to(stage).as_posix()); info.external_attr=0o100644<<16
                    with p.open('rb') as src, z.open(info,'w',force_zip64=True) as dst: shutil.copyfileobj(src,dst,1024*1024)
        sha=digest(bundle)
        destination=cache/sha
        if not destination.exists():
            try: stage.rename(destination)
            except FileExistsError: pass
        return dict(name=display['name'],identifier=plugin_id+'@'+build_id,source='plugin',operation='plugin',
                    plugin_id=plugin_id,build_id=build_id,version=display['version'],format=display['format'],
                    **({'requires_destination':True, 'preview_paths':[f['path'][7:] for f in records if not f['folder']][:8]} if requires_destination else {}),
                    recipe_id=recipe_id,bundle_sha256=sha,bundle_size=(destination/'bundle.zip').stat().st_size,file_count=len(records))

def recipe_for(target):
    path=delivery_root()/target['bundle_sha256']/'recipe.json'
    if digest(path)!=target['recipe_id']: raise ValueError('导入清单损坏')
    return json.loads(path.read_text())


def destination_path(value):
    if not isinstance(value,str) or len(value)>512: raise ValueError('无效 ZIP 导入目录')
    if value != 'Assets': asset_path(value)
    return value


def placed_files(target, destination):
    files=recipe_for(target)['files']
    if not target.get('requires_destination'): return files
    destination_path(destination)
    return [dict(f,path=destination+'/'+f['path'][len('Assets/'):]) for f in files]
