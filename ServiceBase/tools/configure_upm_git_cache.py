"""Opt-in workaround for Git auto-maintenance racing Unity's temporary clone scan.

Only <project>/Library/PackageCache/.tmp-*/clone/.git matches. Normal repositories
keep their current settings. --remove removes only our exact include value.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time
import uuid

POLICY = '# ServiceBase: Unity discards these short-lived Git clones after import.\n[gc]\n\tauto = 0\n[maintenance]\n\tauto = false\n'


def configure(project, global_config, action):
    project=Path(project).resolve();global_config=Path(global_config).expanduser().resolve()
    if not (project/'Packages/manifest.json').is_file() or any(c in str(project) for c in '*?[]\n\r'):
        raise ValueError('需要规范的现有 Unity 工程路径')
    policy=project/'.servicebase/upm/git-cache-policy.config'
    key='includeIf.gitdir:'+str(project/'Library/PackageCache')+'/.tmp-*/clone/.git.path'
    def git(*args):
        return subprocess.run(['git','config','--file',str(global_config),*args],text=True,capture_output=True)
    previous=git('--get-all',key)
    if previous.returncode not in (0,1):raise RuntimeError('无法读取 Git 配置')
    installed=str(policy) in previous.stdout.splitlines()
    result=dict(project=str(project),scope=str(project/'Library/PackageCache')+'/.tmp-*/clone/.git',policy=str(policy),installed=installed)
    if action=='show':return result
    if action=='apply' and policy.exists() and policy.read_text()!=POLICY:
        raise ValueError('策略文件已有其他内容，未覆盖')
    if (action=='apply' and not installed) or (action=='remove' and installed):
        backup=Path(__file__).resolve().parent.parent/'.runtime/backups'/('git-upm-cache-'+time.strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:6])
        backup.mkdir(parents=True,mode=0o700)
        if global_config.exists():
            with (backup/'global-git-config').open('xb') as f:
                os.chmod(f.name,0o600);f.write(global_config.read_bytes())
        (backup/'scope.json').write_text(json.dumps(dict(key=key,value=str(policy),global_config=str(global_config)),indent=2))
        result['backup']=str(backup)
    if action=='apply':
        policy.parent.mkdir(parents=True,exist_ok=True)
        if not policy.exists():policy.write_text(POLICY)
        if not installed:
            global_config.parent.mkdir(parents=True,exist_ok=True)
            if git('--add',key,str(policy)).returncode:raise RuntimeError('无法保存限定目录的 Git include')
        result['installed']=True
    elif action=='remove' and installed:
        if git('--fixed-value','--unset-all',key,str(policy)).returncode:raise RuntimeError('无法移除 Git include')
        result['installed']=False
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project',required=True,type=Path)
    parser.add_argument('--global-config',type=Path,default=Path.home()/'.gitconfig')
    parser.add_argument('action',choices=['show','apply','remove'])
    args=parser.parse_args()
    print(json.dumps(configure(args.project,args.global_config,args.action),ensure_ascii=False,indent=2))


if __name__=='__main__':main()
