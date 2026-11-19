#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
namespace ServiceBase.Upm
{
    [Serializable] internal class AssetsPackage {
        public string name, version, display_name, description, root, manifest_sha256, warning = "";
        public PackageDependency[] dependencies;
    }
    internal static class AssetsPackages
    {
        static string[] paths;
        static AssetsPackages() { EditorApplication.projectChanged += () => { paths=null; ProjectPackageState.Dirty(); }; }
        internal static AssetsPackage[] Read(string root) {
            if(paths==null) paths=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)&&p.EndsWith("/package.json",StringComparison.Ordinal)).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            if(paths.Length>500)throw new IOException("Assets 中 package.json 超过 500 项");
            var rows=new List<AssetsPackage>();
            foreach(var relative in paths) {
                var path=PluginImporter.In(root,relative);
                if(!File.Exists(path))continue;
                if(new FileInfo(path).Length>1024*1024)throw new IOException("本地包元数据超过 1 MiB："+relative);
                var bytes=File.ReadAllBytes(path);var raw=JsonMap.Read(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                var name=JsonMap.String(raw,"name");
                // Ignore unrelated package.json files (for example a tool with no UPM-style name).
                if(string.IsNullOrEmpty(name)||!Regex.IsMatch(name,@"^[a-zA-Z0-9][a-zA-Z0-9._-]{2,159}$"))continue;
                rows.Add(new AssetsPackage {name=name,version=JsonMap.String(raw,"version")??"",display_name=JsonMap.String(raw,"displayName")??name,
                    description=(JsonMap.String(raw,"description")??"").Substring(0,Math.Min(2000,(JsonMap.String(raw,"description")??"").Length)),
                    root=relative.Substring(0,relative.Length-13),manifest_sha256=PluginImporter.HashBytes(bytes),dependencies=ProjectPackageState.Dependencies(raw)});
            }
            var registered=new HashSet<string>(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Select(p=>p.name));
            foreach(var row in rows) {
                if(rows.Count(p=>p.name==row.name)>1)row.warning="Assets 中存在多个同名包，请核对目录";
                if(registered.Contains(row.name))row.warning+=(row.warning==""?"":"；")+"UPM 同时注册了同名包";
            }
            return rows.ToArray();
        }
        internal static string Fingerprint(string root) {
            return string.Join("\n",Read(root).Select(p=>p.root+"\n"+p.manifest_sha256));
        }
    }
}
#endif
