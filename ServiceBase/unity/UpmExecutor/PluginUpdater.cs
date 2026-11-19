#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
namespace ServiceBase.Upm
{
    [Serializable] internal class ReplacementAction { public string path,kind; public PluginFile before,after; }
    [Serializable] internal class PluginUpdateState {
        public string original_json,record_json,record_digest,result_json,result_digest;
        public PluginFile[] files;
        public ReplacementAction[] actions;
        public RemovedFile[] results;
        public bool receipt_saved,result_saved;
    }
    [Serializable] internal class PluginUpdateResult {
        public string plugin_id,build_id,previous_build_id,record_key,record_digest,attempt_id,new_record_digest;
        public RemovedFile[] files;
    }
    internal static class PluginUpdater
    {
        static UnityWebRequest network;static string kind;static double due,quietSince;
        internal static void Close(){if(network!=null){network.Abort();network.Dispose();network=null;}}
        static void Send(string url,string token,string route,PluginRequest body,string download=null){
            kind=route;network=new UnityWebRequest(url+"/api/bridge/plugins/"+route+"/","POST");
            network.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            network.downloadHandler=download==null?(DownloadHandler)new DownloadHandlerBuffer():new DownloadHandlerFile(download);
            network.timeout=120;network.redirectLimit=0;network.SetRequestHeader("Content-Type","application/json");network.SetRequestHeader("X-ServiceBase-Client","unity-bridge-v2");
            if(!string.IsNullOrEmpty(token))network.SetRequestHeader("Authorization","Bearer "+token);network.SendWebRequest();
        }
        static string NewKey(Target t,string destination){return t.plugin_id+"-"+t.build_id+(t.requires_destination?"-"+PluginImporter.HashBytes(Encoding.UTF8.GetBytes(destination)).Substring(0,16):"");}
        static PluginFile Capture(string root,string relative){
            string path=PluginImporter.In(root,relative);bool folder=Directory.Exists(path),exists=folder||File.Exists(path),meta=File.Exists(path+".meta");
            return new PluginFile{path=relative,folder=folder,existed=exists,meta_existed=meta,sha256=exists&&!folder?PluginImporter.Hash(path):"",meta_sha256=meta?PluginImporter.Hash(path+".meta"):""};
        }
        static bool Matches(string root,PluginFile before){
            var now=Capture(root,before.path);
            return now.folder==before.folder&&now.existed==before.existed&&now.meta_existed==before.meta_existed&&now.sha256==before.sha256&&now.meta_sha256==before.meta_sha256;
        }
        static PluginUpdateState Plan(Journal journal,string root,string stage,PluginFile[] files){
            var target=journal.work.target;var owned=PluginInventory.Load(root,target.record_key);
            if(owned.digest!=target.record_digest||owned.record.plugin_id!=target.plugin_id||owned.record.build_id!=target.previous_build_id||owned.record.files.Length!=target.previous_file_count)throw new IOException("旧插件记录已变化，请重新预览");
            if((owned.record.destination_root??"")!=(journal.destination_root??""))throw new IOException("更新不能改变原导入目录");
            if(File.Exists(PluginInventory.RecordPath(root,NewKey(target,journal.destination_root))))throw new IOException("目标构建在此目录已有记录，请先核对现有导入");
            var old=owned.record.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
            var next=files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
            var shared=new HashSet<string>(LegacyMigration.Imported(root).Where(e=>e.kind=="plugin").SelectMany(e=>e.files.Select(f=>f.path)),StringComparer.OrdinalIgnoreCase);
            foreach(var key in PluginInventory.Keys(root))if(key!=owned.key)foreach(var f in PluginInventory.Load(root,key).record.files)shared.Add(f.path);
            // Only a verified, exclusively owned old file can transfer its GUID to a new path.
            var movablePaths=new HashSet<string>(owned.record.files.Where(f=>!f.folder&&!next.ContainsKey(f.path)&&!f.existed&&!f.meta_existed&&!shared.Contains(f.path)&&PluginInventory.Check(root,f)=="installed").Select(f=>f.path),StringComparer.Ordinal);
            var actions=new List<ReplacementAction>();var results=new List<RemovedFile>();
            foreach(var f in files){
                PluginFile prior;string action;
                if(old.TryGetValue(f.path,out prior)){
                    if(prior.path!=f.path||prior.folder!=f.folder)throw new IOException("大小写或文件/目录类型变更需先手动处理："+f.path);
                    if(PluginInventory.Check(root,prior)!="installed")throw new IOException("待更新文件已被修改或缺失："+f.path);
                    if(f.generated){f.guid=prior.guid;f.meta_sha256=prior.meta_sha256;}
                    bool changed=f.sha256!=prior.sha256||f.meta_sha256!=prior.meta_sha256;
                    if(changed&&(prior.existed||prior.meta_existed||shared.Contains(f.path)))throw new IOException("不能覆盖原有或共享文件："+f.path);
                    f.existed=prior.existed;f.meta_existed=prior.meta_existed;f.before_sha256=prior.before_sha256;f.before_meta_sha256=prior.before_meta_sha256;
                    action=changed?"replace":"keep";
                    results.Add(new RemovedFile{path=prior.path,outcome=changed?"replaced":"reused",reason=changed?"由本插件创建，备份后替换":"新版本继续使用"});
                }else{
                    // Existing equal files can be reused, but never acquire deletion ownership.
                    PluginImporter.Baseline(new[]{f},root,movablePaths);action=f.existed?"keep":"add";
                }
                actions.Add(new ReplacementAction{path=f.path,kind=action,before=Capture(root,f.path),after=f});
            }
            foreach(var prior in owned.record.files)if(!next.ContainsKey(prior.path)){
                string check=PluginInventory.Check(root,prior),outcome="removed",reason="旧版本独占文件，备份后移出";
                if(check=="missing"){outcome="missing";reason="文件已不存在";}
                else if(prior.existed||prior.meta_existed||shared.Contains(prior.path)||check!="installed"){outcome="retained";reason="原有、共享或已修改文件，保留";}
                results.Add(new RemovedFile{path=prior.path,outcome=outcome,reason=reason});
                actions.Add(new ReplacementAction{path=prior.path,kind=outcome=="removed"?"remove":"retain",before=Capture(root,prior.path)});
            }
            var removable=new HashSet<string>(actions.Where(a=>a.kind=="remove").Select(a=>a.path),StringComparer.Ordinal);
            foreach(var f in files){
                string path=AssetDatabase.GUIDToAssetPath(f.guid);
                if(!string.IsNullOrEmpty(path)&&path!=f.path&&!removable.Contains(path))throw new IOException("GUID 被保留资源引用："+f.path);
            }
            return new PluginUpdateState{original_json=owned.raw,files=files,actions=actions.ToArray(),results=results.ToArray()};
        }
        static string Backup(string root,Journal j,string path){return PluginImporter.In(root,".servicebase/plugins/updates/"+j.work.attempt_id+"/backup/"+path);}
        static void CopyChecked(string source,string destination,string sha){
            Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(source,destination,true);
            if(PluginImporter.Hash(destination)!=sha)throw new IOException("备份或暂存核验失败");
        }
        static void BackupAll(Journal j,string root){
            foreach(var a in j.update.actions){
                if(!Matches(root,a.before))throw new IOException("更新前文件发生变化："+a.path);
                if(a.kind!="replace"&&a.kind!="remove")continue;
                var path=PluginImporter.In(root,a.path);var backup=Backup(root,j,a.path);
                if(a.before.existed&&!a.before.folder)CopyChecked(path,backup,a.before.sha256);
                if(a.before.meta_existed)CopyChecked(path+".meta",backup+".meta",a.before.meta_sha256);
            }
            PluginImporter.Persist(PluginImporter.In(root,".servicebase/plugins/updates/"+j.work.attempt_id+"/original-record.json"),j.update.original_json);
        }
        static void Execute(Journal j,string root,string stage,Action save){
            foreach(var a in j.update.actions)if(!Matches(root,a.before))throw new IOException("写入前文件发生变化："+a.path);
            foreach(var a in j.update.actions.Where(a=>a.kind=="remove").OrderBy(a=>a.before.folder?1:0).ThenByDescending(a=>a.path.Length)){
                string path=PluginImporter.In(root,a.path);
                if(a.before.folder&&Directory.EnumerateFileSystemEntries(path).Any()){
                    var item=j.update.results.First(r=>r.path==a.path);item.outcome="retained";item.reason="目录仍有保留或额外文件";save();continue;
                }
                if(a.before.folder)Directory.Delete(path,false);else File.Delete(path);
                File.Delete(path+".meta");
            }
            foreach(var a in j.update.actions.Where(a=>a.kind=="add"||a.kind=="replace").OrderBy(a=>a.path.Length)){
                var f=a.after;string destination=PluginImporter.In(root,f.path),source=PluginImporter.In(Path.Combine(stage,"files"),PluginImporter.Source(f));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if(f.folder)Directory.CreateDirectory(destination);else CopyChecked(source,destination,f.sha256);
                // Generated ZIP meta keeps the original GUID and importer settings across versions.
                if(f.generated&&a.before.meta_existed){if(PluginImporter.Hash(destination+".meta")!=f.meta_sha256)throw new IOException("原 meta 发生变化");}
                else CopyChecked(source+".meta",destination+".meta",f.meta_sha256);
            }
        }
        static bool Verify(Journal j,string root){
            if(!PluginImporter.Verify(j.update.files,root))return false;
            foreach(var r in j.update.results)if(r.outcome=="removed"||r.outcome=="missing"){
                string path=PluginImporter.In(root,r.path);
                if(File.Exists(path)||Directory.Exists(path)||File.Exists(path+".meta"))return false;
            }
            return true;
        }
        internal static void Advance(Journal j,string root,string url,string token,string instance,string session,Action save,Action<string,string,string> report,Action intent){
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode){quietSince=EditorApplication.timeSinceStartup;return;}
            if(EditorApplication.timeSinceStartup-quietSince<3||EditorApplication.timeSinceStartup<due||j.report.state=="needs_review")return;
            var target=j.work.target;string cache=PluginImporter.In(root,".servicebase/plugins/cache/"+j.work.attempt_id),bundle=Path.Combine(cache,"bundle.zip"),stage=Path.Combine(cache,"staged");
            var request=new PluginRequest{instance_id=instance,session_id=session,attempt_id=j.work.attempt_id};
            if(network!=null){
                if(kind=="download"&&network.downloadedBytes>(ulong)target.bundle_size){Close();report("failed","下载大小超过索引，尚未更新。","preflight");return;}
                if(!network.isDone)return;
                long status=network.responseCode;string completed=kind;network.Dispose();network=null;
                if((status==400||status==413)&&completed!="download"){report("needs_review","更新回执被拒绝，已保留日志和备份。","");return;}
                if(status!=200){due=EditorApplication.timeSinceStartup+10;return;}
                if(completed=="receipt"){j.update.receipt_saved=true;save();}
                else if(completed=="update-receipt"){j.update.result_saved=true;save();}
                else try{
                    var files=PluginImporter.Stage(bundle,stage,target);PluginImporter.Place(files,j,root);j.update=Plan(j,root,stage,files);save();
                }catch(Exception e){report("failed","更新预检失败："+e.Message,"preflight");return;}
            }
            if(!j.intent){
                if(!SessionState.GetBool("ServiceBase.Bridge.Enabled",true))return;
                string boundary=MtpBoundary.Check(root);
                if(boundary!=""||EditorUtility.scriptCompilationFailed){report("failed",boundary!=""?boundary:"工程有编译错误，尚未更新。","preflight");return;}
                if(j.update==null){
                    DateTime started;if(DateTime.TryParse(j.started_at,out started)&&(DateTime.UtcNow-started.ToUniversalTime()).TotalMinutes>=10){report("failed","下载超时，尚未修改工程。","preflight");return;}
                    Directory.CreateDirectory(cache);Send(url,token,"download",request,bundle);return;
                }
                try{
                    // Rebuild the plan immediately before writing; ownership and sharing may have changed.
                    var files=PluginImporter.Stage(bundle,stage,target);PluginImporter.Place(files,j,root);j.update=Plan(j,root,stage,files);save();BackupAll(j,root);
                }catch(Exception e){report("failed","更新预检或备份失败："+e.Message,"preflight");return;}
                intent();j.intent=true;report("running","旧文件已备份，正在应用新版本文件及清理旧版本残留。","");
                Debug.Log("[ServiceBase Plugin] Start Update "+j.work.attempt_id);
                AssetDatabase.DisallowAutoRefresh();bool editing=false;
                try{AssetDatabase.StartAssetEditing();editing=true;Execute(j,root,stage,save);save();}
                catch(Exception e){report("needs_review","更新部分完成，保留备份和日志，只核对不重做："+e.GetType().Name,"");}
                finally{try{if(editing)AssetDatabase.StopAssetEditing();}finally{AssetDatabase.AllowAutoRefresh();}}
                AssetDatabase.Refresh();quietSince=EditorApplication.timeSinceStartup;return;
            }
            if(j.update==null||j.update.files==null||j.update.actions==null||j.update.results==null){report("needs_review","更新意图已存在但本地明细缺失，请核对备份。","");return;}
            if(EditorUtility.scriptCompilationFailed){report("needs_review","更新后存在编译错误，请修复后重新核对。","");return;}
            try{
                if(!Verify(j,root)){report("needs_review","更新文件或旧文件清理结果未完全匹配，保留工程锁与备份。","");return;}
                string oldPath=PluginInventory.RecordPath(root,target.record_key),archive=PluginImporter.In(root,".servicebase/plugins/superseded/"+j.work.attempt_id+".json");
                if(File.Exists(oldPath)&&PluginImporter.Hash(oldPath)!=target.record_digest)throw new IOException("旧记录已变化");
                if(!File.Exists(archive))PluginImporter.Persist(archive,j.update.original_json);
                if(PluginImporter.Hash(archive)!=target.record_digest)throw new IOException("旧记录备份不匹配");
                if(string.IsNullOrEmpty(j.update.record_json)){
                    j.update.record_json=JsonUtility.ToJson(new PluginRecord{plugin_id=target.plugin_id,build_id=target.build_id,attempt_id=j.work.attempt_id,destination_root=j.destination_root,files=j.update.files});
                    j.update.record_digest=PluginImporter.HashBytes(Encoding.UTF8.GetBytes(j.update.record_json));save();
                }
                string newPath=PluginInventory.RecordPath(root,NewKey(target,j.destination_root));
                if(File.Exists(newPath)&&PluginImporter.Hash(newPath)!=j.update.record_digest)throw new IOException("新记录已变化");
                PluginImporter.Persist(newPath,j.update.record_json);
                if(File.Exists(oldPath))File.Delete(oldPath);
                if(string.IsNullOrEmpty(j.update.result_json)){
                    j.update.result_json=JsonUtility.ToJson(new PluginUpdateResult{plugin_id=target.plugin_id,build_id=target.build_id,previous_build_id=target.previous_build_id,record_key=target.record_key,record_digest=target.record_digest,attempt_id=j.work.attempt_id,new_record_digest=j.update.record_digest,files=j.update.results});
                    j.update.result_digest=PluginImporter.HashBytes(Encoding.UTF8.GetBytes(j.update.result_json));save();
                }
            }catch(Exception e){report("needs_review","更新记录核对失败："+e.Message,"");return;}
            if(!j.update.receipt_saved){request.record_json=j.update.record_json;Send(url,token,"receipt",request);return;}
            if(!j.update.result_saved){request.record_json=j.update.result_json;Send(url,token,"update-receipt",request);return;}
            j.report.evidence.record_digest=j.update.record_digest;j.report.evidence.update_digest=j.update.result_digest;j.report.evidence.recipe_id=target.recipe_id;j.report.evidence.build_id=target.build_id;
            PluginInventory.Dirty();report("succeeded","插件更新及旧文件处理已核验；保留 "+j.update.results.Count(f=>f.outcome=="retained")+" 项，备份在 .servicebase/plugins/updates。","");
        }
    }
}
#endif
