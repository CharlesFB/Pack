#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
namespace ServiceBase.Upm
{
    [Serializable] internal class PackageDependency { public string name="", version=""; }
    [Serializable] internal class PackageGoal {
        public string name="",version="",source="",identifier="",commit="",unity="";
        public bool direct;
        public PackageDependency[] dependencies=new PackageDependency[0];
    }
    [Serializable] internal class RegistryState { public string url; public string[] scopes; }
    [Serializable] internal class PackageState {
        public string fingerprint, captured_at, error="";
        public PackageGoal[] packages=new PackageGoal[0],builtins=new PackageGoal[0];
        public RegistryState[] registries=new RegistryState[0];
        public AssetsPackage[] assets_packages=new AssetsPackage[0];
    }
    [Serializable] internal class PluginGoal { public string plugin_id,build_id,destination_root; }
    [Serializable] internal class PlanBaseline { public string manifest,lockfile,fingerprint; }
    internal static class ProjectPackageState
    {
        static double due;
        internal static PackageState Snapshot;
        internal static string Fingerprint(string root) {
            string manifest=File.ReadAllText(Path.Combine(root,"Packages/manifest.json"));
            var path=Path.Combine(root,"Packages/packages-lock.json");string locked=File.Exists(path)?File.ReadAllText(path):"";
            return PluginImporter.HashBytes(Encoding.UTF8.GetBytes(manifest+"\n---lock---\n"+locked+"\n---assets---\n"+AssetsPackages.Fingerprint(root)));
        }
        internal static void Dirty() { due=0;Snapshot=null; }
        internal static string SafeIdentifier(string value) {
            if (string.IsNullOrEmpty(value)) return "";
            Uri uri;
            string parsed=value;
            var scp=System.Text.RegularExpressions.Regex.Match(value,@"^git@([A-Za-z0-9.-]+):(.+)$");
            if(scp.Success)parsed="ssh://git@"+scp.Groups[1].Value+"/"+scp.Groups[2].Value;
            if (parsed.StartsWith("https://",StringComparison.Ordinal)||parsed.StartsWith("ssh://",StringComparison.Ordinal))
                return !value.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c)||c=='\\') && Uri.TryCreate(parsed,UriKind.Absolute,out uri)
                    && (uri.Scheme=="https"?uri.UserInfo=="":uri.UserInfo=="git")
                    && (uri.Query=="" || System.Text.RegularExpressions.Regex.IsMatch(uri.Query,@"^\?path=/[A-Za-z0-9_./-]+$")) ? value : "";
            return System.Text.RegularExpressions.Regex.IsMatch(value,@"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.+-]+)?$") ? value : "";
        }
        internal static PackageDependency[] Dependencies(Dictionary<string,object> raw) {
            var result=new List<PackageDependency>();
            foreach(var key in new[]{"dependencies","dependenciesGit"}) {
                object map;
                if (!raw.TryGetValue(key,out map) || map==null) continue;
                var deps=map as Dictionary<string,object>;if(deps==null)throw new IOException("Invalid dependency map");
                foreach(var pair in deps) result.Add(new PackageDependency {name=pair.Key,version=SafeIdentifier(pair.Value as string)});
            }
            return result.OrderBy(d=>d.name,StringComparer.Ordinal).ToArray();
        }
        internal static PackageState Read(string root) {
            var state=new PackageState {fingerprint=Fingerprint(root),captured_at=DateTime.UtcNow.ToString("o")};
            var manifest=JsonMap.Read(File.ReadAllText(Path.Combine(root,"Packages/manifest.json")));
            var direct=JsonMap.Object(manifest,"dependencies");
            var rows=new List<PackageGoal>();
            foreach(var info in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().OrderBy(p=>p.name,StringComparer.Ordinal)) {
                string source=info.source==PackageSource.Registry?"registry":info.source==PackageSource.Git?"git":info.source==PackageSource.BuiltIn?"builtin":info.source==PackageSource.Embedded?"embedded":"local";
                string identifier=JsonMap.String(direct,info.name);
                if (identifier==null && info.packageId.StartsWith(info.name+"@",StringComparison.Ordinal)) identifier=info.packageId.Substring(info.name.Length+1);
                var goal=new PackageGoal {name=info.name,version=info.version,source=source,identifier=SafeIdentifier(identifier),commit=info.git==null?"":info.git.hash,direct=info.isDirectDependency};
                string metadata=Path.Combine(info.resolvedPath,"package.json");
                if (File.Exists(metadata)) {
                    if (new FileInfo(metadata).Length>1024*1024)throw new IOException("Package metadata too large");
                    goal.dependencies=Dependencies(JsonMap.Read(File.ReadAllText(metadata)));
                }
                rows.Add(goal);
            }
            state.packages=rows.ToArray();
            state.assets_packages=AssetsPackages.Read(root);
            var builtins=new List<PackageGoal>();
            string builtinRoot=Path.Combine(EditorApplication.applicationContentsPath,"Resources/PackageManager/BuiltInPackages");
            if (Directory.Exists(builtinRoot)) foreach(var path in Directory.GetDirectories(builtinRoot).Take(501)) {
                var file=Path.Combine(path,"package.json");if(!File.Exists(file))continue;
                var raw=JsonMap.Read(File.ReadAllText(file));
                builtins.Add(new PackageGoal {name=JsonMap.String(raw,"name"),version=JsonMap.String(raw,"version"),source="builtin",dependencies=Dependencies(raw)});
            }
            state.builtins=builtins.ToArray();
            object value;
            if (manifest.TryGetValue("scopedRegistries",out value) && value is List<object>) {
                state.registries=((List<object>)value).Select(v=>{
                    var r=(Dictionary<string,object>)v;string url=JsonMap.String(r,"url");Uri uri;
                    if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!="")throw new IOException("Scoped registry requires credential-free HTTPS");
                    return new RegistryState {url=url,scopes=((List<object>)r["scopes"]).Cast<string>().ToArray()};
                }).ToArray();
            }
            return state;
        }
        internal static void Tick(string root,bool busy) {
            if(busy||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<due)return;
            due=EditorApplication.timeSinceStartup+10;
            try {
                var state=Read(root);
                if(Encoding.UTF8.GetByteCount(JsonUtility.ToJson(state))>1500000)throw new IOException("Package snapshot too large");
                Snapshot=state;
            } catch(Exception e) {
                Snapshot=new PackageState {fingerprint=new string('0',64),captured_at=DateTime.UtcNow.ToString("o"),error="独立工程包状态读取失败："+e.GetType().Name};
            }
        }
        internal static bool BeforeStart(Journal journal,string root,Action<string,string,string> report) {
            if(string.IsNullOrEmpty(journal.work.expected_fingerprint)||journal.intent)return true;
            try {
                if(Fingerprint(root)!=journal.work.expected_fingerprint)throw new IOException("工程包清单或锁文件已变化，请重新预览计划");
                string baseline=PluginImporter.In(root,".servicebase/plans/"+journal.work.job_id+".json");
                if(!File.Exists(baseline)) {
                    string locked=Path.Combine(root,"Packages/packages-lock.json");
                    PluginImporter.Persist(baseline,JsonUtility.ToJson(new PlanBaseline {manifest=File.ReadAllText(Path.Combine(root,"Packages/manifest.json")),lockfile=File.Exists(locked)?File.ReadAllText(locked):"",fingerprint=journal.work.expected_fingerprint}));
                }
                return true;
            } catch(Exception e) {report(journal.report.state=="accepted"?"failed":"needs_review",e.Message,journal.report.state=="accepted"?"preflight":"");return false;}
        }
        static bool SameJson(object left,object right) {
            var a=left as Dictionary<string,object>;var b=right as Dictionary<string,object>;
            if(a!=null||b!=null)return a!=null&&b!=null&&a.Count==b.Count&&a.All(p=>b.ContainsKey(p.Key)&&SameJson(p.Value,b[p.Key]));
            var x=left as List<object>;var y=right as List<object>;
            if(x!=null||y!=null)return x!=null&&y!=null&&x.Count==y.Count&&x.Select((v,i)=>SameJson(v,y[i])).All(v=>v);
            return object.Equals(left,right);
        }
        internal static void VerifyPlan(Journal journal,string root,Action<string,string,string> report) {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||journal.report.state=="needs_review")return;
            try {
                var target=journal.work.target;
                var state=Read(root);var actual=state.packages.ToDictionary(p=>p.name);
                var manifest=JsonMap.Object(JsonMap.Read(File.ReadAllText(Path.Combine(root,"Packages/manifest.json"))),"dependencies");
                var baseline=JsonUtility.FromJson<PlanBaseline>(File.ReadAllText(PluginImporter.In(root,".servicebase/plans/"+journal.work.job_id+".json")));
                var before=JsonMap.Object(JsonMap.Read(baseline.manifest),"dependencies");
                var beforeConfig=JsonMap.Read(baseline.manifest);beforeConfig.Remove("dependencies");
                var afterConfig=JsonMap.Read(File.ReadAllText(Path.Combine(root,"Packages/manifest.json")));afterConfig.Remove("dependencies");
                if(!SameJson(beforeConfig,afterConfig))throw new IOException("计划执行期间工程 Registry 或其他包配置发生变化");
                var locked=JsonMap.Object(JsonMap.Read(File.ReadAllText(Path.Combine(root,"Packages/packages-lock.json"))),"dependencies");
                var beforeLocked=string.IsNullOrEmpty(baseline.lockfile)?new Dictionary<string,object>():JsonMap.Object(JsonMap.Read(baseline.lockfile),"dependencies");
                if(target.packages==null||target.protected_packages==null||target.plugins==null||target.expected_direct==null)throw new IOException("最终核对清单缺失");
                if(!manifest.Keys.OrderBy(p=>p,StringComparer.Ordinal).SequenceEqual(target.expected_direct.OrderBy(p=>p,StringComparer.Ordinal)))throw new IOException("工程直接依赖集合与计划不一致");
                var allowed=new HashSet<string>(beforeLocked.Keys.Concat(target.packages.Select(p=>p.name)).Concat(target.protected_packages.Select(p=>p.name)));
                if(actual.Keys.Any(n=>!allowed.Contains(n)))throw new IOException("UPM 注册了预览之外的间接依赖，请重新核对依赖图");
                foreach(var wanted in target.packages.Concat(target.protected_packages)) {
                    PackageGoal got;
                    if(!actual.TryGetValue(wanted.name,out got)||got.version!=wanted.version||got.source!=wanted.source||(wanted.source=="git"&&got.commit!=wanted.commit)||(wanted.direct&&!got.direct))throw new IOException("包版本或注册信息不匹配："+wanted.name);
                    var entry=JsonMap.Object(locked,wanted.name);
                    if(JsonMap.String(entry,"source")!=wanted.source)throw new IOException("包锁来源不匹配："+wanted.name);
                    if(wanted.source=="git") {
                        if(JsonMap.String(entry,"hash")!=wanted.commit)throw new IOException("Git 锁定 commit 不匹配："+wanted.name);
                    } else if(wanted.source=="registry"||wanted.source=="builtin") {
                        if(JsonMap.String(entry,"version")!=wanted.version)throw new IOException("包锁定版本不匹配："+wanted.name);
                    } else {
                        var oldEntry=JsonMap.Object(beforeLocked,wanted.name);
                        if(JsonMap.String(entry,"version")!=JsonMap.String(oldEntry,"version"))throw new IOException("计划外本地包路径发生变化："+wanted.name);
                    }
                }
                foreach(var wanted in target.protected_packages.Where(p=>p.direct)) {
                    if(JsonMap.String(before,wanted.name)!=JsonMap.String(manifest,wanted.name))throw new IOException("计划外根包发生变化："+wanted.name);
                }
                foreach(var wanted in target.packages.Where(p=>p.direct)) {
                    string expected=wanted.source=="git"?wanted.identifier:wanted.version;
                    if(JsonMap.String(manifest,wanted.name)!=expected)throw new IOException("包清单未锁定到计划版本："+wanted.name);
                }
                if(target.absent_packages!=null&&target.absent_packages.Any(n=>actual.ContainsKey(n)||locked.ContainsKey(n)||manifest.ContainsKey(n)))throw new IOException("待卸载包仍存在于最终包图");
                if(target.removed_records!=null&&target.removed_records.Any(k=>File.Exists(PluginInventory.RecordPath(root,k))))throw new IOException("旧插件归属记录仍存在");
                var keys=PluginInventory.Keys(root);
                foreach(var wanted in target.plugins) {
                    var record=keys.Select(k=>PluginInventory.Load(root,k)).FirstOrDefault(r=>r.record.plugin_id==wanted.plugin_id&&r.record.build_id==wanted.build_id&&(r.record.destination_root??"")== (wanted.destination_root??""));
                    if(record==null)throw new IOException("插件执行记录缺失："+wanted.plugin_id);
                }
                journal.report.evidence.verified=true;journal.report.evidence.plan_digest=target.plan_digest;
                report("succeeded","目标包与插件执行记录核对通过。"+(EditorUtility.scriptCompilationFailed?"工程仍有编译错误，请在 Unity 中处理。":""),"");
            } catch(Exception e) {report("failed","最终核对未通过："+e.Message,"plan_verify");}
        }
    }
}
#endif
