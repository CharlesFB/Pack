"""Validate post-import metadata snapshots against an immutable archive recipe."""
EDM_BUILD = '6db6b031d4fbd18a73e8b56455245b24355a9cfc558b7ae042326503bb01bc2a'
EDM_MARKER = 'Assets/PlayServicesResolver/Editor/play-services-resolver_v1.2.137.0.txt'
EDM_MARKER_SHA = '9746742f6f187698a449657be32d14ca7b3a0fa7e783b45c7894e5733eba0f15'
POLICY = 'unity-import-v1'
ARCHIVE_POLICY = 'archive-execution-v1'


def archive_execution(target, record, expected):
    """Bind deletion scope to the shipped archive, never to post-import disk contents."""
    if record.get('verification') != ARCHIVE_POLICY:return False
    if target.get('operation') != 'plugin' or record.get('import_result') not in (
            'completed','reloaded','failed','cancelled','persistence_failed','compile_error','unknown'):
        raise ValueError('无效插件执行记录')
    files=record.get('files')
    if not isinstance(files,list) or len(files)!=len(expected) or record.get('omitted_files') not in (None,[]):
        raise ValueError('归档路径清单不完整')
    for actual,wanted in zip(files,expected):
        if not isinstance(actual,dict) or any(actual.get(k)!=wanted[k] for k in ('path','folder','sha256')):
            raise ValueError('记录路径或来源与原归档不符')
        if any(type(actual.get(k)) is not bool for k in ('existed','meta_existed')):
            raise ValueError('缺少导入前路径状态')
    return True


def imported_snapshot(target, record, expected):
    """Old receipts remain strict. Only explicit import snapshots permit meta rewrites."""
    policy = record.get('verification') or ''
    omitted = record.get('omitted_files') or []
    if not policy and not omitted:
        return False, expected
    if policy != POLICY or target.get('format') != 'unitypackage' or target.get('operation') != 'plugin':
        raise ValueError('无效导入核对策略')
    if not isinstance(omitted, list) or len(omitted) > 3:
        raise ValueError('无效历史文件清理证据')
    missing = set()
    by_path = {f['path']: f for f in expected}
    for file in omitted:
        wanted = by_path.get(file.get('path')) if isinstance(file, dict) else None
        if not wanted or wanted['path'] in missing or target['build_id'] != EDM_BUILD:
            raise ValueError('不允许忽略该资源')
        allowed = (wanted['folder'] and wanted['generated'] and wanted['path'] in (
            'Assets/PlayServicesResolver', 'Assets/PlayServicesResolver/Editor')) or (
            not wanted['folder'] and wanted['path'] == EDM_MARKER and wanted['sha256'] == EDM_MARKER_SHA)
        if not allowed or any(file.get(k) != wanted[k] for k in ('path', 'folder', 'sha256', 'meta_sha256', 'guid', 'generated')):
            raise ValueError('历史文件清理证据不符')
        if file.get('existed') is not False or file.get('meta_existed') is not False or file.get('before_sha256') != '' or file.get('before_meta_sha256') != '':
            raise ValueError('不能忽略导入前已有资源')
        missing.add(wanted['path'])
    # A missing generated parent is only valid if all its planned children are absent.
    if any(any(f['path'].startswith(path + '/') and f['path'] not in missing for f in expected) for path in missing):
        raise ValueError('清理目录仍有预期资源')
    return True, [f for f in expected if f['path'] not in missing]
