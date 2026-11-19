#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
namespace ServiceBase.Upm
{
    internal static class PackageRemover
    {
        static RemoveRequest removal;static ListRequest preflight;static double quietSince,due;
        internal static bool Busy{get{return removal!=null&&!removal.IsCompleted||preflight!=null&&!preflight.IsCompleted;}}
        internal static void Reset(){if(!Busy){removal=null;preflight=null;}}
        static bool Verify(Journal j,string root){
            var manifest=JsonMap.Object(JsonMap.Read(File.ReadAllText(Path.Combine(root,"Packages/manifest.json"))),"dependencies");
            var locked=JsonMap.Object(JsonMap.Read(File.ReadAllText(Path.Combine(root,"Packages/packages-lock.json"))),"dependencies");
            return !manifest.ContainsKey(j.work.target.name)&&!locked.ContainsKey(j.work.target.name)&&!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p=>p.name==j.work.target.name);
        }
        internal static void Advance(Journal j,string root,Action save,Action<string,string,string> report,Action intent){
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode){quietSince=EditorApplication.timeSinceStartup;return;}
            if(EditorApplication.timeSinceStartup-quietSince<3||EditorApplication.timeSinceStartup<due)return;
            var t=j.work.target;
            if(removal!=null){
                if(!removal.IsCompleted)return;
                j.add_finished=true;j.add_failed=removal.Status==StatusCode.Failure;removal=null;
                report("verifying","UPM Remove 已返回，核对 manifest、lock 和注册信息。","");quietSince=EditorApplication.timeSinceStartup;return;
            }
            if(!j.intent&&j.report.state=="accepted"){
                if(!SessionState.GetBool("ServiceBase.Bridge.Enabled",true))return;
                try{
                    string boundary=MtpBoundary.Check(root);if(boundary!="")throw new IOException(boundary);
                    if(EditorUtility.scriptCompilationFailed)throw new IOException("工程存在编译错误，尚未卸载");
                    if(t.previous==null||t.name=="com.magictavern.mtpmanager")throw new IOException("卸载目标不受支持");
                    if(preflight==null){preflight=Client.List(true,true);return;}
                    if(!preflight.IsCompleted)return;
                    bool valid=preflight.Status==StatusCode.Success;preflight=null;if(!valid)throw new IOException("UPM 预检查未完成");
                    var state=ProjectPackageState.Read(root);var current=state.packages.FirstOrDefault(p=>p.name==t.name);
                    if(current==null||!current.direct||current.source!=t.previous.source||current.version!=t.previous.version||(current.source=="git"&&current.commit!=t.previous.commit))throw new IOException("包状态与卸载预览不同，请重新预览");
                    if(state.assets_packages.Any(p=>p.dependencies.Any(d=>d.name==t.name))||state.packages.Any(p=>p.name!=t.name&&p.dependencies.Any(d=>d.name==t.name)))throw new IOException("其他已注册包仍引用该包，尚未卸载");
                    j.manifest_before=File.ReadAllText(Path.Combine(root,"Packages/manifest.json"));j.lock_before=File.ReadAllText(Path.Combine(root,"Packages/packages-lock.json"));
                    string backup=PluginImporter.In(root,".servicebase/upm/backups/"+j.work.attempt_id);
                    PluginImporter.Persist(Path.Combine(backup,"manifest.json"),j.manifest_before);PluginImporter.Persist(Path.Combine(backup,"packages-lock.json"),j.lock_before);save();
                }catch(Exception e){report("failed","卸载预检失败："+e.Message,"preflight");return;}
                intent();j.intent=true;j.started_at=DateTime.UtcNow.ToString("o");report("running","卸载意图和清单备份已保存，执行一次 UPM Remove。","");
                Debug.Log("[ServiceBase UPM] Start Remove "+j.work.attempt_id);removal=Client.Remove(t.name);return;
            }
            if(EditorUtility.scriptCompilationFailed){report("needs_review","卸载后存在编译错误，请修复并重新核对。","");return;}
            due=EditorApplication.timeSinceStartup+5;
            try{
                if(Verify(j,root)){
                    j.report.evidence=new Evidence{name=t.name,identifier=t.identifier,source=t.source,removed=true,manifest_match=true,lock_match=true};
                    report("succeeded","目标包已从 manifest、lock 和已注册包中移除。","");return;
                }
            }catch(Exception){/* Wait for UPM to settle; never repeat Remove. */}
            if(j.add_finished&&j.add_failed){report("failed","UPM 明确返回卸载失败，目标尚未移除。","remove_completed");return;}
            DateTime started;
            if(!DateTime.TryParse(j.started_at,out started)||(DateTime.UtcNow-started.ToUniversalTime()).TotalMinutes>=10)report("needs_review","卸载结果未知，保留工程锁、日志和备份，不重复 Remove。","");
            else if(j.report.state!="verifying")report("verifying","等待卸载后的包状态稳定，只核对、不重做。","");
        }
    }
}
#endif
