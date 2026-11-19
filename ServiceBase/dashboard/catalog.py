"""Service-owned GitLab catalog. Credentials never enter a Unity/browser payload."""
import base64
import copy
import hashlib
import json
import os
import re
from pathlib import Path

import gitlab
from django.conf import settings
from django.core.cache import cache
from django.utils import timezone
from .models import CatalogCache, GitLabSettings


def source():
    """
    解析管理员配置、环境变量或本地 MTP 配置，返回缓存指纹与 GitLab 连接参数。凭据仅供服务器内部使用。
    """
    managed = GitLabSettings.objects.filter(pk=1).first()
    token = managed.private_token if managed else os.environ.get('MTP_GITLAB_TOKEN', '')
    url = os.environ.get('MTP_GITLAB_URL', 'https://gitlab.mte.io/')
    if not token and settings.LOCAL_MODE:
        path = os.environ.get('MTP_CONFIG_PATH', '')
        locator = settings.BASE_DIR / '.runtime/catalog-source.json'
        if not path and locator.exists():
            path = json.loads(locator.read_text()).get('config_path', '')
        if path:
            config = json.loads(base64.b64decode(Path(path).read_bytes(), validate=True))
            token, url = config.get('PrivateToken', ''), config.get('Url', url)
    if managed:
        token, url = managed.private_token, managed.url
    if not token:
        raise ValueError('请配置服务端 MTP_CONFIG_PATH 或 MTP_GITLAB_TOKEN。')
    from urllib.parse import urlsplit
    parsed = urlsplit(url)
    if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.query or parsed.fragment:
        raise ValueError('GitLab 地址必须为不含凭据的 HTTPS 地址。')
    ref = managed.ref if managed else os.environ.get('MTP_GITLAB_REF', 'master')
    key = hashlib.sha256((url + '\0' + token + '\0' + ref).encode()).hexdigest()  # 凭据、地址或分支变化后使用独立缓存，避免读取旧来源的目录。
    return key, url, token, ref


def blacklist_settings():
    """
    读取服务端黑名单与 Debug 开关；尚未配置时兼容本地 MTP 黑名单。
    """
    managed = GitLabSettings.objects.filter(pk=1).first()
    if managed:
        return managed.blacklist, managed.debug_mode
    if settings.LOCAL_MODE:
        try:
            path = os.environ.get('MTP_CONFIG_PATH', '')
            locator = settings.BASE_DIR / '.runtime/catalog-source.json'
            if not path and locator.exists():
                path = json.loads(locator.read_text()).get('config_path', '')
            if path:
                config = json.loads(base64.b64decode(Path(path).read_bytes()))
                return [str(v) for v in config.get('BlackList', [])], False
        except (ValueError, OSError, KeyError):
            pass
    return [], False


def latest_tag(
    tags,  # 候选标签字符串列表
):
    """
    优先选择 Release 标签，再按数字版本与标签字符串取最新项；空列表返回空字符串。
    """
    def key(
        tag,  # 待比较的版本标签
    ):
        """
        构造 Release 优先、数字版本优先的标签比较键。
        """
        match = re.search(r'\d+\.\d+(?:\.\d+)?(?:\.\d+)?', tag)
        version = tuple(map(int, match[0].split('.'))) if match else (0, 0)
        return (not tag.endswith('-debug'), version, tag)
    return max(tags, key=key, default='')


def metadata(
    raw,  # 原始 package.json 元数据字典
):
    """
    从原始包元数据提取允许展示的字段，只保留依赖名称与标签，不输出仓库地址。
    """
    fields = {'display_name':'displayName', 'version':'version', 'description':'description',
              'unity':'unity', 'license':'license', 'changelog':'changelog'}
    result = {k: raw.get(v, '') if isinstance(raw.get(v, ''), str) else '' for k,v in fields.items()}
    author = raw.get('author', '')
    result['author'] = author.get('name', '') if isinstance(author, dict) else str(author or '')
    deps = raw.get('dependenciesGit') or {}
    result['dependencies'] = [{'name':name, 'requested_tag':str(value).rsplit('#', 1)[1] if '#' in str(value) else ''}
                              for name,value in deps.items()]
    return result


def normalize(
    directory,  # 远端工程目录索引
    info,  # 按工程和标签组织的元数据缓存
):
    """
    将目录索引与各版本元数据合并为缓存记录，以工程 ID 和短包名去重。
    """
    entries, versions = directory['Packages'], info['PackageCache']
    if not isinstance(entries, list) or not isinstance(versions, dict):
        raise ValueError('Invalid catalog schema')
    output, seen = [], set()
    for entry in entries:
        short, project_id = entry['PackageName'], str(entry['ProjectId'])
        identity = project_id + ':' + short
        if identity in seen:
            continue
        seen.add(identity)
        tags = entry.get('Tags') or []
        if not isinstance(tags, list) or any(not isinstance(t, str) for t in tags):
            raise ValueError('Invalid tags')
        tag = latest_tag(tags)
        raw_versions = versions.get(project_id, {})
        raw = raw_versions.get(tag) or {}
        name = raw.get('name') or next((raw_versions[t].get('name') for t in tags if raw_versions.get(t, {}).get('name')), short)
        # Persist only whitelisted display data; repository URLs/config tokens are excluded.
        output.append(dict(catalog_id=identity, name=name, short_name=short, tag=tag,
                           available_tags=tags, versions={t:metadata(raw_versions[t]) for t in tags if t in raw_versions},
                           **metadata(raw)))
    return output


def refresh_catalog():
    """
    从同一 Git commit 拉取两份目录缓存；并发同步时返回 False，失败时保留已有缓存。
    """
    key, url, token, ref = source()
    lock = 'catalog-refresh:' + key  # 短期同步互斥键；生产 Redis 在不同 Web 进程之间共享。
    if not cache.add(lock, True, 90):
        return False
    try:
        with gitlab.Gitlab(url, private_token=token, timeout=12, retry_transient_errors=False) as gl:
            project = gl.projects.get(647, lazy=True)
            # Resolve once so both aggregate files belong to the same commit.
            commit = project.commits.get(ref).id
            def read(
                path,  # 指定 commit 中的仓库文件路径
            ):
                """
                流式读取指定 commit 的目录文件并解析 JSON，限制单文件大小。
                """
                data = bytearray()  # 当前目录文件的下载缓冲区，回调中限制最大长度。
                def append(
                    chunk,  # 当前下载的字节数据块
                ):
                    """
                    累计下载的数据块，超过目录文件大小限制时立即中断读取。
                    """
                    data.extend(chunk)
                    if len(data) > 32 * 1024 * 1024:
                        raise ValueError('Catalog exceeds limit')
                project.files.raw(file_path=path, ref=commit, streamed=True, action=append)
                return json.loads(data)
            packages = normalize(read('upm-list.json'), read('upm-cache.json'))
        CatalogCache.objects.update_or_create(key=key, defaults={'packages':packages, 'revision':commit})
        return True
    finally:
        cache.delete(lock)


def sort_key(
    pkg,  # 待处理的包记录
):
    """
    生成已安装根包、已安装依赖、未安装包的分组顺序，再按显示名升序比较。
    """
    installed = pkg.get('installation_status') == 'installed'
    group = (0 if pkg.get('depth') == 0 else 1) if installed else 2
    return group, pkg.get('display_name') or pkg.get('name') or '', pkg.get('catalog_id', '')


def merge_snapshot(
    snapshot,  # Unity 上报的工程包快照
):
    """
    保留工程依赖记录，合并服务端完整目录和安装状态，再应用排序与黑名单过滤。
    """
    result = copy.deepcopy(snapshot)
    records = [p for p in result.get('packages', []) if isinstance(p.get('depth'), int) and p['depth'] >= 0]  # 只保留工程依赖记录，旧 Bridge 上传的目录项不会被误认为已安装。
    try:
        key, _, _, _ = source()
        catalog = CatalogCache.objects.filter(key=key).first()
    except (ValueError, OSError, KeyError):
        catalog = None
    if result.get('adapter') == 'servicebase':
        from .package_presentation import restore_catalog_tags, visible_project_packages
        records=visible_project_packages(result,catalog.packages if catalog else [])
        if catalog is not None:restore_catalog_tags(records, catalog.packages)
    result['project_packages'] = copy.deepcopy(records)
    if catalog is None:
        result.update(packages=sorted(records, key=sort_key), catalog_state='missing', catalog_count=0,
                      catalog_detail='服务端尚无远端包列表缓存，请点击同步包信息。')
        return result
    used, packages = set(), []  # 记录已匹配的安装项下标，防止同名目录条目重复认领同一个安装记录。
    for item in catalog.packages:
        tags, name, short = item['available_tags'], item['name'], item['short_name']
        candidates = [(i,p) for i,p in enumerate(records) if i not in used and (not p.get('catalog_id') or p['catalog_id'] == item['catalog_id'])]
        match = next(((i,p) for i,p in candidates if p.get('catalog_id') == item['catalog_id']), None)
        if match is None:
            match = next(((i,p) for i,p in candidates if p.get('tag') in tags and p.get('name') in (name, short)), None)
        if match is None:
            match = next(((i,p) for i,p in candidates if p.get('name') == short or (p.get('name') == name and (not p.get('tag') or p['tag'].startswith(short + '-') or (p.get('installation_source')=='独立执行器实际注册信息' and sum(c['name']==name for c in catalog.packages)==1)))), None)
        pkg = {k:copy.deepcopy(v) for k,v in item.items() if k not in ('versions','short_name')}
        pkg['tag_metadata'] = copy.deepcopy(item['versions'])
        pkg.update(depth=-1, installation_status='not_installed', installation_source='工程没有此包的安装记录', is_local=False, revision='')
        if match:
            i, record = match
            used.add(i)
            pkg.update(item['versions'].get(record.get('tag'), {}))
            pkg.update({k:v for k,v in record.items() if v not in ('', None) and k not in ('catalog_id', 'available_tags')})
            if record.get('installation_status') in (None, '', 'unknown'):
                pkg.update(installation_status='installed', installation_source='MTP 工程依赖记录（未逐项校验磁盘）')
        if match:
            for field in ('display_name', 'version', 'description', 'author', 'unity', 'license', 'changelog'):
                result['project_packages'][match[0]][field] = pkg.get(field, '')
        packages.append(pkg)
    orphans = [p for i,p in enumerate(records) if i not in used]
    blacklist, debug_mode = blacklist_settings()
    total = len(packages)
    if not debug_mode:
        packages = [p for p in packages if p['catalog_id'].split(':',1)[0] not in blacklist or p.get('installation_status') == 'installed']
    result['catalog_total'] = total
    result['catalog_hidden'] = total - len(packages)
    result.update(packages=sorted(packages, key=sort_key), catalog_state='ready', catalog_count=len(packages),
                  catalog_revision=catalog.revision, catalog_updated_at=catalog.updated_at.isoformat(),
                  catalog_detail=f'远端包列表 · {len(packages)} 个包（黑名单隐藏 {total-len(packages)} 个）；工程有 {len(orphans)} 个包的记录，可在依赖树查看。')
    return result
