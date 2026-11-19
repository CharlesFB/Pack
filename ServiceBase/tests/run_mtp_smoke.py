"""Compile Bridge and test against a supplied local MTP assembly; reads only.
python tests/run_mtp_smoke.py --unity /path/Unity.app --project /path/project --legacy
"""
import argparse
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--unity', type=Path, required=True)
parser.add_argument('--project', type=Path, required=True)
parser.add_argument('--legacy', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
content = args.unity / 'Contents'
assemblies = args.project / 'Library/ScriptAssemblies'
managed = content / 'Managed'
mode = 'LegacyMtpSmoke' if args.legacy else 'MtpApiSmoke'
output = root / '.runtime' / (mode + '.exe')
output.parent.mkdir(exist_ok=True)
references = [assemblies / 'Magictavern.FrontEngine.MTPManager.dll',
              content / 'MonoBleedingEdge/lib/mono/4.8-api/Facades/netstandard.dll', managed / 'UnityEngine/UnityEditor.CoreModule.dll', managed / 'UnityEngine/UnityEngine.dll']
references += [managed / 'UnityEngine' / (name + '.dll') for name in
               ('UnityEngine.IMGUIModule', 'UnityEngine.CoreModule', 'UnityEngine.JSONSerializeModule', 'UnityEngine.UnityWebRequestModule')]
subprocess.run([str(content / 'MonoBleedingEdge/bin/mcs'), '-define:UNITY_EDITOR', '-out:' + str(output),
                *['-r:' + str(p) for p in references], *map(str, sorted((root / 'unity').glob('*.cs'))),
                str(root / 'tests' / (mode + '.cs'))], check=True)
environment = dict(os.environ, MONO_PATH=os.pathsep.join(map(str, [assemblies, managed, managed / 'UnityEngine'])))
cache = args.project / ('Library/upm-dependencies.json' if args.legacy else 'Library/MTPCache/upm-dependencies.json')
subprocess.run([str(content / 'MonoBleedingEdge/bin/mono'), str(output), str(cache),
                str(root / '.runtime/mtp-api-snapshot.json')], env=environment, check=True)
