#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ServiceBase.Upm
{
    [Serializable] internal class PluginFile {
        public string source_path, path, sha256, meta_sha256, guid, source_meta_sha256 = "", before_sha256 = "", before_meta_sha256 = "";
        public long size; public bool folder, generated, existed, meta_existed;
    }
    [Serializable] internal class PluginRecipe { public int schema; public string plugin_id, build_id, format; public PluginFile[] files; }
    [Serializable] internal class PluginState {
        public PluginFile[] files, import_baseline; public string callback = "", record_json = "", record_digest = "";
        public bool receipt_saved, invoked; public int lifecycle; public string result = "", failure_message = "", receipt_started_at = "";
    }
    [Serializable] internal class PluginRecord { public string plugin_id, build_id, attempt_id, destination_root, verification, import_result; public PluginFile[] files, omitted_files; }
    [Serializable] internal class PluginRequest {
        public string instance_id, session_id, attempt_id, record_json, destination_root;
        public PluginFile[] files;
    }
    internal static class PluginImporter
    {
        static UnityWebRequest network; static string networkKind;
        static double nextCheck, quietSince;
        static bool importing;
        static Journal current; static Action saveCurrent;
        static PluginImporter() {
            AssetDatabase.importPackageCompleted += name => Callback("completed");
            AssetDatabase.importPackageCancelled += name => Callback("cancelled");
            AssetDatabase.importPackageFailed += (name, error) => Callback("failed");
        }
        static void Callback(string state) {
            if (!importing || current == null) return;
            importing = false; current.plugin.callback = state;
            // Keep a failed callback explicit; never infer success from matching files.
            try { saveCurrent(); } catch (Exception) { current.plugin.callback = "persistence_failed"; }
            quietSince = EditorApplication.timeSinceStartup;
        }
        internal static void Close() { if (network != null) { network.Abort(); network.Dispose(); network = null; } }
        internal static string HashBytes(byte[] bytes) { using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static string Hash(string path) { using (var sha=SHA256.Create()) using (var s=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        internal static string GuidAt(string path) { var m=Regex.Match(File.ReadAllText(path), @"(?m)^guid: ([a-fA-F0-9]{32})\s*$"); if (!m.Success) throw new IOException("无效 meta GUID"); return m.Groups[1].Value.ToLowerInvariant(); }
        static void NoLinks(string path) {
            for (var p=new FileInfo(Path.GetFullPath(path)) as FileSystemInfo; p!=null; p=new DirectoryInfo(Path.GetDirectoryName(p.FullName) ?? "/")) {
                if ((p.Exists || Directory.Exists(p.FullName)) && (File.GetAttributes(p.FullName)&FileAttributes.ReparsePoint)!=0) throw new IOException("不支持符号链接路径");
                if (p.FullName==Path.GetPathRoot(p.FullName)) break;
            }
        }
        internal static string In(string root,string relative) {
            if (string.IsNullOrEmpty(relative) || relative.Contains("\\") || relative.Contains(":") || relative.Split('/').Any(p=>p==".." || p=="." || p=="") || relative.Any(c=>c<32)) throw new IOException("非法资源路径");
            var result=Path.GetFullPath(Path.Combine(root,relative));
            if (!result.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.Ordinal)) throw new IOException("资源路径越界");
            NoLinks(result); return result;
        }
        internal static void Persist(string path,string text) {
            NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)); var temp=path+".tmp";
            using (var s=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { var b=Encoding.UTF8.GetBytes(text); s.Write(b,0,b.Length); s.Flush(true); }
            if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
        static void Send(string url,string token,string kind,PluginRequest body,string download=null) {
            networkKind=kind; network=new UnityWebRequest(url+"/api/bridge/plugins/"+kind+"/","POST");
            network.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            network.downloadHandler=download==null ? (DownloadHandler)new DownloadHandlerBuffer() : new DownloadHandlerFile(download);
            network.timeout=120; network.redirectLimit=0;
            network.SetRequestHeader("Content-Type","application/json"); network.SetRequestHeader("X-ServiceBase-Client","unity-bridge-v2");
            if (!string.IsNullOrEmpty(token)) network.SetRequestHeader("Authorization","Bearer "+token);
            network.SendWebRequest();
        }
        internal static PluginFile[] Stage(string bundle,string stage,Target target) {
            if (new FileInfo(bundle).Length!=target.bundle_size || Hash(bundle)!=target.bundle_sha256) throw new IOException("下载文件大小或 SHA-256 不符");
            // Never overwrite a staging tree from an earlier attempt; each unpack validates all members.
            NoLinks(stage); if (Directory.Exists(stage)) Directory.Delete(stage,true); Directory.CreateDirectory(stage);
            long total=0; int count=0; var names=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var archive=ZipFile.OpenRead(bundle)) foreach (var entry in archive.Entries) {
                total+=entry.Length; count++;
                if (total>4L*1024*1024*1024 || count>22000 || !names.Add(entry.FullName) || ((entry.ExternalAttributes>>16)&0xF000)==0xA000) throw new IOException("制品展开超限或存在链接/重复路径");
                if (!(entry.FullName.StartsWith("files/Assets/",StringComparison.Ordinal) || entry.FullName=="recipe.json" || entry.FullName=="import.unitypackage")) throw new IOException("制品含非预期路径");
                var destination=In(stage,entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using (var src=entry.Open()) using (var dst=new FileStream(destination,FileMode.CreateNew)) src.CopyTo(dst);
            }
            var recipePath=Path.Combine(stage,"recipe.json");
            if (Hash(recipePath)!=target.recipe_id) throw new IOException("导入清单哈希不符");
            var recipe=JsonUtility.FromJson<PluginRecipe>(File.ReadAllText(recipePath));
            if (recipe.schema!=1 || recipe.plugin_id!=target.plugin_id || recipe.build_id!=target.build_id || recipe.format!=target.format || recipe.files.Length!=target.file_count) throw new IOException("导入清单身份不符");
            foreach (var file in recipe.files) {
                if (!file.path.StartsWith("Assets/",StringComparison.Ordinal)) throw new IOException("非 Assets 路径");
                var local=In(Path.Combine(stage,"files"),file.path);
                if ((!file.folder && (!File.Exists(local) || Hash(local)!=file.sha256)) || Hash(local+".meta")!=file.meta_sha256 || GuidAt(local+".meta")!=file.guid) throw new IOException("暂存文件核验失败");
            }
            if (target.format=="unitypackage" && Hash(Path.Combine(stage,"import.unitypackage"))!=target.build_id) throw new IOException("原始 unitypackage 哈希不符");
            return recipe.files;
        }
        internal static string ValidateDestination(string root, string selected) {
            if (string.IsNullOrEmpty(selected)) throw new IOException("请选择导入目录");
            string absolute=Path.GetFullPath(selected).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            string assets=Path.GetFullPath(Path.Combine(root,"Assets"));
            if (absolute!=assets && !absolute.StartsWith(assets+Path.DirectorySeparatorChar,StringComparison.Ordinal)) throw new IOException("只能选择当前工程的 Assets 或其子目录");
            NoLinks(absolute);
            string relative="Assets"+absolute.Substring(assets.Length).Replace('\\','/');
            if (relative.Length>512 || !Directory.Exists(absolute) || !AssetDatabase.IsValidFolder(relative)) throw new IOException("请选择已被 Unity 识别的现有资源目录");
            if (relative.Split('/').Any(p=>p.StartsWith(".") || p.EndsWith(".") || p.EndsWith(" ") || p.Contains(":")) || relative.Any(c=>c<32)) throw new IOException("目录名称不受支持");
            return relative;
        }
        internal static string Source(PluginFile file) { return string.IsNullOrEmpty(file.source_path) ? file.path : file.source_path; }
        internal static void Place(PluginFile[] files, Journal journal, string root) {
            if (!journal.work.target.requires_destination) return;
            if (ValidateDestination(root,Path.Combine(root,journal.destination_root))!=journal.destination_root) throw new IOException("已选目录发生变化");
            foreach (var file in files) { file.source_path=file.path; file.path=journal.destination_root+"/"+file.path.Substring("Assets/".Length); }
        }
        internal static void Baseline(PluginFile[] files,string root,HashSet<string> movablePaths=null,PluginFile[] importedFiles=null) {
            foreach (var file in files) {
                var path=In(root,file.path); file.existed=File.Exists(path)||Directory.Exists(path); file.meta_existed=File.Exists(path+".meta");
                if (file.existed && Directory.Exists(path)!=file.folder) throw new IOException("文件与目录冲突："+file.path);
                if (!file.folder && file.existed) file.before_sha256=Hash(path);
                if (file.meta_existed) file.before_meta_sha256=Hash(path+".meta");
                if (file.generated && file.meta_existed) { file.meta_sha256=file.before_meta_sha256; file.guid=GuidAt(path+".meta"); }
                var owned=(importedFiles ?? new PluginFile[0]).FirstOrDefault(f=>f.path==file.path);
                if (owned==null && !file.folder && file.existed && file.before_sha256!=file.sha256) throw new IOException("已有文件内容不同，需先处理覆盖："+file.path);
                var guidPath=AssetDatabase.GUIDToAssetPath(file.guid);
                if (!string.IsNullOrEmpty(guidPath) && guidPath!=file.path && (movablePaths==null || !movablePaths.Contains(guidPath))) throw new IOException("GUID 已被其他资源使用："+file.path);
            }
        }
        internal static bool Verify(PluginFile[] files,string root) {
            if (files==null || files.Length==0) return false;
            foreach (var file in files) {
                var path=In(root,file.path);
                if ((file.folder ? !Directory.Exists(path) : !File.Exists(path) || Hash(path)!=file.sha256) || !File.Exists(path+".meta") || Hash(path+".meta")!=file.meta_sha256 || GuidAt(path+".meta")!=file.guid || AssetDatabase.GUIDToAssetPath(file.guid)!=file.path) return false;
            }
            return true;
        }
        static string RecordPath(Journal journal,string root) {
            var target=journal.work.target;
            return In(root,".servicebase/plugins/records/"+target.plugin_id+"-"+target.build_id+(target.requires_destination ? "-"+HashBytes(Encoding.UTF8.GetBytes(journal.destination_root)).Substring(0,16) : "")+".json");
        }
        static void ImportBaseline(PluginFile[] files,Journal journal,string root) {
            var path=RecordPath(journal,root);
            PluginFile[] imported=null;
            if(File.Exists(path)) {
                var old=JsonUtility.FromJson<PluginRecord>(File.ReadAllText(path));
                if(old.plugin_id!=journal.work.target.plugin_id || old.build_id!=journal.work.target.build_id) throw new IOException("原插件归属记录不匹配");
                imported=old.files;
            }
            Baseline(files,root,null,imported);
        }
        static void Fail(Journal journal,Action<string,string,string> report,string message) {
            importing=false; Close(); journal.report.evidence.operation_finished=true;
            report("failed",message,"plugin_import");
        }
        internal static void Advance(Journal journal,string root,string url,string token,string instance,string session,Action save,Action<string,string,string> report,Action intent) {
            current=journal; saveCurrent=save;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) { quietSince=EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup-quietSince<3) return;
            var target=journal.work.target; var cache=In(root,".servicebase/plugins/cache/"+journal.work.attempt_id);
            var bundle=Path.Combine(cache,"bundle.zip"); var stage=Path.Combine(cache,"staged");
            var request=new PluginRequest {instance_id=instance,session_id=session,attempt_id=journal.work.attempt_id};
            if (network!=null) {
                if (networkKind=="download" && network.downloadedBytes>(ulong)target.bundle_size) {
                    Close(); report("failed","下载内容超过索引大小，已中止。","preflight"); return;
                }
                if (!network.isDone) return;
                var status=network.responseCode; var ok=status==200; var kind=networkKind; network.Dispose(); network=null;
                if ((status==400 || status==413) && kind=="receipt") { Fail(journal,report,"服务端拒绝归档路径记录，已保留本地记录并跳过此插件。"); return; }
                if (!ok) { nextCheck=EditorApplication.timeSinceStartup+10; return; }
                if (kind=="receipt") { journal.plugin.receipt_saved=true; save(); }
                else {
                    try { var files=Stage(bundle,stage,target); Place(files,journal,root); ImportBaseline(files,journal,root); journal.plugin=new PluginState {files=files,lifecycle=1}; save(); }
                    catch (IOException e) { report("failed",e.Message,"preflight"); return; }
                }
            }
            if (journal.report.state=="needs_review" || EditorApplication.timeSinceStartup<nextCheck) return;
            if (!journal.intent) {
                if (!SessionState.GetBool("ServiceBase.Bridge.Enabled",true)) return;
                var boundary=MtpBoundary.Check(root);
                if (boundary!="" || EditorUtility.scriptCompilationFailed) { report("failed",boundary!=""?boundary:"工程编译失败，尚未导入。","preflight"); return; }
                if (target.requires_destination) {
                    if (string.IsNullOrEmpty(journal.destination_root) || !journal.destination_saved) {
                        report("failed","旧 ZIP 任务未保存网页所选目录，尚未写入；请在网页选择根目录后重新导入。","preflight"); return;
                    }
                    try {
                        if (ValidateDestination(root,Path.Combine(root,journal.destination_root))!=journal.destination_root) throw new IOException("已选目录发生变化");
                    } catch (IOException e) { report("failed",e.Message,"preflight"); return; }
                }
                if (journal.plugin==null || journal.plugin.files==null || journal.plugin.files.Length!=target.file_count || target.file_count==0) {
                    DateTime queued;
                    if (DateTime.TryParse(journal.started_at,out queued) && (DateTime.UtcNow-queued.ToUniversalTime()).TotalMinutes>=10) { report("failed","下载在十分钟内未完成，尚未写入工程资源。","preflight"); return; }
                    Directory.CreateDirectory(cache); Send(url,token,"download",request,bundle); return;
                }
                // Recheck the complete baseline immediately before the durable intent and first write.
                try { ImportBaseline(journal.plugin.files,journal,root); } catch (IOException e) { report("failed",e.Message,"preflight"); return; }
                journal.plugin.import_baseline=JsonUtility.FromJson<PluginState>(JsonUtility.ToJson(journal.plugin)).files;
                intent(); journal.intent=true; journal.started_at=DateTime.UtcNow.ToString("o"); report("running","已保存导入意图，开始单条插件导入。","");
                Debug.Log("[ServiceBase Plugin] Start Import "+journal.work.attempt_id);
                AssetDatabase.DisallowAutoRefresh();
                try {
                    foreach (var file in journal.plugin.files) {
                        var destination=In(root,file.path); Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        if (file.folder) Directory.CreateDirectory(destination);
                        if (file.generated && !file.meta_existed) File.Copy(In(Path.Combine(stage,"files"),Source(file)+".meta"),destination+".meta",false);
                        if (target.format=="zip") {
                            if (!file.folder && !file.existed) File.Copy(In(Path.Combine(stage,"files"),Source(file)),destination,false);
                            if (!file.generated && !file.meta_existed) File.Copy(In(Path.Combine(stage,"files"),Source(file)+".meta"),destination+".meta",false);
                        }
                    }
                    journal.plugin.invoked=true; save();
                    if (target.format=="unitypackage") { importing=true; AssetDatabase.ImportPackage(Path.Combine(stage,"import.unitypackage"),false); }
                    else journal.plugin.callback="completed";
                } catch(Exception e) { importing=false; journal.plugin.callback="failed"; journal.plugin.failure_message="导入调用异常："+e.GetType().Name; }
                finally { AssetDatabase.AllowAutoRefresh(); }
                save(); AssetDatabase.Refresh(); quietSince=EditorApplication.timeSinceStartup; return;
            }
            if (journal.plugin==null || journal.plugin.files==null || journal.plugin.files.Length==0) { Fail(journal,report,"归档路径清单缺失，已跳过导入，不重复执行。"); return; }
            // Only the operation result matters. Imported code may replace DLLs,
            // remove obsolete resources, regenerate meta or produce additional files.
            if(string.IsNullOrEmpty(journal.plugin.result)) {
                string callback=journal.plugin.callback;
                if(EditorUtility.scriptCompilationFailed) journal.plugin.result="compile_error";
                else if(callback=="completed") journal.plugin.result="completed";
                else if(callback=="failed" || callback=="cancelled" || callback=="persistence_failed") journal.plugin.result=callback;
                else if(journal.plugin.lifecycle==0 || !journal.plugin.invoked) journal.plugin.result="unknown";
                else if(importing) {
                    DateTime started;
                    if(DateTime.TryParse(journal.started_at,out started) && (DateTime.UtcNow-started.ToUniversalTime()).TotalMinutes<2)return;
                    journal.plugin.result="unknown";
                } else journal.plugin.result="reloaded";
                save();
            }
            importing=false;
            if(string.IsNullOrEmpty(journal.plugin.record_json)) {
                var record=PluginImportVerification.ExecutionRecord(journal);
                var recordPath=RecordPath(journal,root);
                if(File.Exists(recordPath)) {
                    var old=PluginInventory.Load(root,Path.GetFileNameWithoutExtension(recordPath));
                    if(old.record.plugin_id!=target.plugin_id || old.record.build_id!=target.build_id)throw new IOException("原插件记录身份不符");
                    foreach(var file in record.files) {
                        var prior=old.record.files.FirstOrDefault(f=>f.path==file.path);
                        if(prior==null)continue;
                        file.existed=prior.existed;file.meta_existed=prior.meta_existed;
                        file.before_sha256=prior.before_sha256;file.before_meta_sha256=prior.before_meta_sha256;
                    }
                }
                journal.plugin.record_json=JsonUtility.ToJson(record);
                journal.plugin.record_digest=HashBytes(Encoding.UTF8.GetBytes(journal.plugin.record_json));
                Persist(RecordPath(journal,root),journal.plugin.record_json); save();
            }
            // Lost receipt ACKs resend exactly the same record, without rescanning assets.
            if (!journal.plugin.receipt_saved) {
                DateTime receiptStarted;
                if (string.IsNullOrEmpty(journal.plugin.receipt_started_at)) { journal.plugin.receipt_started_at=DateTime.UtcNow.ToString("o"); save(); }
                else if (DateTime.TryParse(journal.plugin.receipt_started_at,out receiptStarted) && (DateTime.UtcNow-receiptStarted.ToUniversalTime()).TotalMinutes>=2) {
                    Fail(journal,report,"插件归档记录回传超时，已保留本地清单并跳过此插件。"); return;
                }
                request.record_json=journal.plugin.record_json; Send(url,token,"receipt",request); return;
            }
            journal.report.evidence.record_digest=journal.plugin.record_digest; journal.report.evidence.recipe_id=target.recipe_id; journal.report.evidence.build_id=target.build_id;
            PluginInventory.Dirty();
            if(journal.plugin.result=="completed" || journal.plugin.result=="reloaded")
                report("succeeded","插件导入调用已完成，原归档路径清单已保存；不核对生成文件或 meta。","");
            else Fail(journal,report,"插件导入已跳过（"+journal.plugin.result+"）。已保留归档清单和现有资源，后续包安装继续。"+journal.plugin.failure_message);

        }
    }
}
#endif
