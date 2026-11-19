#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
namespace ServiceBase.Upm
{
    [Serializable] internal class RemovedFile { public string path, outcome, reason; }
    [Serializable] internal class RemovalResult {
        public string plugin_id, build_id, record_key, record_digest, attempt_id, policy="archive-list-v1";
        public RemovedFile[] files;
    }
    [Serializable] internal class RemovalState {
        public string original_json, result_json, result_digest;
        public RemovedFile[] files;
        public bool receipt_saved;
    }
    internal static class PluginRemover
    {
        static UnityWebRequest network;
        static double due, quietSince;
        internal static void Close() { if (network!=null) { network.Abort(); network.Dispose(); network=null; } }
        static RemovalState Plan(Journal journal,string root) {
            var target=journal.work.target;
            var owned=PluginInventory.Load(root,target.record_key);
            if (owned.digest!=target.record_digest || owned.record.plugin_id!=target.plugin_id || owned.record.build_id!=target.build_id || owned.record.files.Length!=target.file_count) throw new IOException("本地插件归属记录已变化，请刷新网页后重试");
            var shared=new HashSet<string>(LegacyMigration.Imported(root).Where(e=>e.kind=="plugin").SelectMany(e=>e.files.Select(f=>f.path)),StringComparer.OrdinalIgnoreCase);
            foreach (var key in PluginInventory.Keys(root)) if (key!=owned.key) {
                // A damaged record makes ownership uncertain: fail before touching any asset.
                foreach (var file in PluginInventory.Load(root,key).record.files) shared.Add(file.path);
            }
            var results=new List<RemovedFile>();
            var localRoots=AssetsPackages.Read(root).Where(p=>!owned.record.files.Any(f=>f.path==p.root+"/package.json")).Select(p=>p.root).ToArray();
            foreach (var file in owned.record.files) {
                var path=PluginImporter.In(root,file.path);
                string outcome="removed", reason="原归档列出且未被其他包占用";
                if (shared.Contains(file.path) || (file.folder && shared.Any(p=>p.StartsWith(file.path+"/",StringComparison.OrdinalIgnoreCase))) || localRoots.Any(p=>file.path==p||file.path.StartsWith(p+"/",StringComparison.OrdinalIgnoreCase))) { outcome="retained"; reason="其他包或插件记录仍引用"; }
                else if (!File.Exists(path)&&!Directory.Exists(path)&&!File.Exists(path+".meta")) { outcome="missing"; reason="归档路径已不存在"; }
                else if ((File.Exists(path)||Directory.Exists(path))&&Directory.Exists(path)!=file.folder) { outcome="retained"; reason="路径已变为不同类型"; }
                results.Add(new RemovedFile {path=file.path,outcome=outcome,reason=reason});
            }
            return new RemovalState {original_json=owned.raw,files=results.ToArray()};
        }
        static void Execute(Journal journal,string root) {
            var source=JsonUtility.FromJson<PluginRecord>(journal.removal.original_json);
            var lookup=source.files.ToDictionary(f=>f.path);
            // Files first, deepest folders last. Never recursively delete a directory.
            var ordered=journal.removal.files.OrderBy(f=>lookup[f.path].folder ? 1 : 0).ThenByDescending(f=>f.path.Length);
            foreach (var item in ordered) {
                if (item.outcome!="removed") continue;
                var file=lookup[item.path];
                var path=PluginImporter.In(root,file.path);
                if (file.folder && Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) { item.outcome="retained"; item.reason="目录中仍有保留或额外文件"; continue; }
                string backup=PluginImporter.In(root,".servicebase/plugins/trash/"+journal.work.attempt_id+"/"+file.path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (file.folder) { if(Directory.Exists(path))Directory.Delete(path,false); }
                else if(File.Exists(path))File.Move(path,backup);
                if(File.Exists(path+".meta"))File.Move(path+".meta",backup+".meta");
            }
        }
        static bool Verify(Journal journal,string root) {
            foreach (var item in journal.removal.files) if (item.outcome=="removed" || item.outcome=="missing") {
                var path=PluginImporter.In(root,item.path);
                if (File.Exists(path) || Directory.Exists(path) || File.Exists(path+".meta")) return false;
            }
            return true;
        }
        internal static void Advance(Journal journal,string root,string url,string token,string instance,string session,Action save,Action<string,string,string> report,Action intent) {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) { quietSince=EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup-quietSince<3 || EditorApplication.timeSinceStartup<due || journal.report.state=="needs_review") return;
            if (network!=null) {
                if (!network.isDone) return;
                long status=network.responseCode; network.Dispose(); network=null;
                if (status==400 || status==413) { report("needs_review","服务端拒绝删除回执，请核对本地日志。",""); return; }
                if (status!=200) { due=EditorApplication.timeSinceStartup+10; return; }
                journal.removal.receipt_saved=true; save();
            }
            if (!journal.intent) {
                if (!SessionState.GetBool("ServiceBase.Bridge.Enabled",true)) return;
                try {
                    var boundary=MtpBoundary.Check(root);
                    if (boundary!="") throw new IOException(boundary);
                    journal.removal=Plan(journal,root); save();
                } catch (Exception e) { report("failed","删除预检失败："+e.Message,"preflight"); return; }
                intent(); journal.intent=true; report("running","正在按归档清单移出未共享的文件并清理空父目录。","");
                Debug.Log("[ServiceBase Plugin] Start Remove "+journal.work.attempt_id);
                AssetDatabase.DisallowAutoRefresh();
                try { Execute(journal,root); save(); }
                catch (Exception e) { report("needs_review","删除中断，已保留日志及移出的文件；只核对，不重复删除："+e.GetType().Name,""); }
                finally { AssetDatabase.AllowAutoRefresh(); }
                AssetDatabase.Refresh(); quietSince=EditorApplication.timeSinceStartup; return;
            }
            if (journal.removal==null || journal.removal.files==null || journal.removal.files.Length!=journal.work.target.file_count || string.IsNullOrEmpty(journal.removal.original_json)) { report("needs_review","删除意图已存在但本地清单缺失，已停止自动操作。",""); return; }
            try {
                if (!Verify(journal,root)) { report("needs_review","待删除资源尚未完全移出或已重新出现，请人工核对；不会重复删除。",""); return; }
                var target=journal.work.target;
                var recordPath=PluginInventory.RecordPath(root,target.record_key);
                var tombstone=PluginImporter.In(root,".servicebase/plugins/removed/"+journal.work.attempt_id+".json");
                if (File.Exists(recordPath) && PluginImporter.Hash(recordPath)!=target.record_digest) throw new IOException("插件记录在删除期间变化");
                if (!File.Exists(recordPath) && !File.Exists(tombstone)) throw new IOException("原始归属记录及删除存档同时缺失");
                if (File.Exists(tombstone) && PluginImporter.Hash(tombstone)!=target.record_digest) throw new IOException("删除存档不匹配");
                if (!File.Exists(tombstone)) PluginImporter.Persist(tombstone,journal.removal.original_json);
                if (File.Exists(recordPath)) File.Delete(recordPath);
                if (string.IsNullOrEmpty(journal.removal.result_json)) {
                    journal.removal.result_json=JsonUtility.ToJson(new RemovalResult {plugin_id=target.plugin_id,build_id=target.build_id,record_key=target.record_key,record_digest=target.record_digest,attempt_id=journal.work.attempt_id,files=journal.removal.files});
                    journal.removal.result_digest=PluginImporter.HashBytes(Encoding.UTF8.GetBytes(journal.removal.result_json)); save();
                }
            } catch (Exception e) { report("needs_review","删除记录核对失败："+e.GetType().Name,""); return; }
            if (!journal.removal.receipt_saved) {
                network=new UnityWebRequest(url+"/api/bridge/plugins/removal-receipt/","POST");
                network.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new PluginRequest {instance_id=instance,session_id=session,attempt_id=journal.work.attempt_id,record_json=journal.removal.result_json})));
                network.downloadHandler=new DownloadHandlerBuffer(); network.timeout=30; network.redirectLimit=0;
                network.SetRequestHeader("Content-Type","application/json"); network.SetRequestHeader("X-ServiceBase-Client","unity-bridge-v2");
                if (!string.IsNullOrEmpty(token)) network.SetRequestHeader("Authorization","Bearer "+token);
                network.SendWebRequest(); return;
            }
            int removed=journal.removal.files.Count(f=>f.outcome=="removed"), retained=journal.removal.files.Count(f=>f.outcome=="retained");
            journal.report.evidence.record_digest=journal.removal.result_digest; journal.report.evidence.build_id=journal.work.target.build_id;
            PluginInventory.Dirty();
            report("succeeded","插件记录已移除：移出 "+removed+" 项，保留 "+retained+" 项；移出文件备份位于 .servicebase/plugins/trash。","");
        }
    }
}
#endif
