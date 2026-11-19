#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
namespace ServiceBase.Upm
{
    [Serializable] internal class LegacyQuery { public string request_id; }
    [Serializable] internal class LegacyAsset { public string path, sha256="", meta_sha256="", guid="", issue=""; public bool folder, exists; }
    [Serializable] internal class LegacyEntry {
        public string id, kind, name, package_name="", tag="", revision="", source_file, source_digest, entry_digest, warning="";
        public bool is_local, migrated;
        public string[] roots=new string[0]; public int file_count, missing;
        public LegacyAsset[] files=new LegacyAsset[0];
    }
    [Serializable] internal class LegacyPreview { public string request_id, preview_digest, error=""; public LegacyEntry[] entries=new LegacyEntry[0]; }
    [Serializable] internal class LegacyArchive { public int schema=1; public string request_id, preview_digest; public LegacyEntry[] entries; }
    [Serializable] internal class LegacyStatus { public string id, name, package_name, source_file, source_digest, status, kind; public int file_count, matched, modified, missing; public string[] roots; }
    internal static class LegacyMigration
    {
        internal static LegacyPreview Reply;
        static IEnumerator scan; static string scanning;
        static readonly string[] Sources={"Packages/upm-install.json","Packages/upm-local.json","Packages/ump-lock.json","Library/upm-dependencies.json","Library/MTPCache/upm-dependencies.json","Packages/plugin-assets.json"};
        static string Hash(string value) { return PluginImporter.HashBytes(Encoding.UTF8.GetBytes(value)); }
        static string PreviewPath(string root,string id) { Guid value;if(!Guid.TryParse(id,out value))throw new IOException("迁移请求 ID 无效");return PluginImporter.In(root,".servicebase/migrations/previews/"+id+".json"); }
        static string ArchivePath(string root,string id) { PreviewPath(root,id);return PluginImporter.In(root,".servicebase/migrations/records/"+id+".json"); }
        static string Text(Dictionary<string,object> raw,string key) { return JsonMap.String(raw,key)??""; }
        static string Label(string value,int limit=256) {
            // Legacy URLs may contain credentials. Only named fields without URL syntax leave Unity.
            if(value.Length>limit||value.Any(c=>c<32)||value.Contains("://")||value.Contains('@'))throw new IOException("旧记录展示字段格式不支持");return value;
        }
        internal static LegacyEntry[] Imported(string root) {
            var directory=PluginImporter.In(root,".servicebase/migrations/records");if(!Directory.Exists(directory))return new LegacyEntry[0];
            var paths=Directory.GetFiles(directory,"*.json");if(paths.Length>200)throw new IOException("迁移记录超过 200 份");
            var entries=new List<LegacyEntry>();
            foreach(var path in paths) {
                PluginImporter.In(root,".servicebase/migrations/records/"+Path.GetFileName(path));
                if(new FileInfo(path).Length>16*1024*1024)throw new IOException("迁移记录过大");
                var archive=JsonUtility.FromJson<LegacyArchive>(File.ReadAllText(path));
                if(archive==null||archive.schema!=1||archive.entries==null)throw new IOException("迁移记录损坏");
                entries.AddRange(archive.entries);
            }
            var unique=entries.GroupBy(e=>e.id).Select(g=>g.First()).ToArray();
            if(unique.Length>500)throw new IOException("已迁移记录超过 500 条");
            return unique;
        }
        internal static void Request(string root,LegacyQuery query) {
            if(query==null||string.IsNullOrEmpty(query.request_id))return;
            if(Reply!=null&&Reply.request_id==query.request_id||scanning==query.request_id)return;
            scanning=query.request_id;Reply=null;scan=Scan(root,query.request_id);
        }
        internal static void Tick(string root,bool busy) {
            if(scan==null||busy||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            try {
                var until=EditorApplication.timeSinceStartup+0.012;
                do {if(!scan.MoveNext()){scan=null;scanning=null;break;}}while(EditorApplication.timeSinceStartup<until);
            } catch(Exception e) {Reply=new LegacyPreview {request_id=scanning,error="旧记录预览失败："+e.Message.Substring(0,Math.Min(400,e.Message.Length))};scan=null;scanning=null;}
        }
        static LegacyAsset Inspect(string root,string relative) {
            if(!relative.StartsWith("Assets/",StringComparison.Ordinal)||relative.EndsWith(".meta",StringComparison.Ordinal))throw new IOException("旧插件包含不支持的资源路径");
            var path=PluginImporter.In(root,relative);var meta=PluginImporter.In(root,relative+".meta");
            var asset=new LegacyAsset {path=relative,folder=Directory.Exists(path),exists=File.Exists(path)||Directory.Exists(path)};
            if(!asset.exists||!File.Exists(meta)){asset.issue="资源或 meta 缺失";return asset;}
            if(new FileInfo(meta).Length>1024*1024||!asset.folder&&new FileInfo(path).Length>512*1024*1024)throw new IOException("旧插件单文件超出核对限制");
            asset.sha256=asset.folder?"":PluginImporter.Hash(path);asset.meta_sha256=PluginImporter.Hash(meta);asset.guid=PluginImporter.GuidAt(meta);
            if(AssetDatabase.GUIDToAssetPath(asset.guid)!=relative)asset.issue="GUID 路径不一致";
            return asset;
        }
        static bool Same(LegacyAsset a,LegacyAsset b) { return JsonUtility.ToJson(a)==JsonUtility.ToJson(b); }
        static IEnumerator Scan(string root,string id) {
            string savedPath=PreviewPath(root,id);
            if(File.Exists(savedPath)) { var old=JsonUtility.FromJson<LegacyPreview>(File.ReadAllText(savedPath));Reply=Summary(old);yield break; }
            var entries=new List<LegacyEntry>();var imported=new HashSet<string>(Imported(root).Select(e=>e.id));int total=0;
            foreach(var source in Sources) {
                string path=PluginImporter.In(root,source);if(!File.Exists(path))continue;
                if(new FileInfo(path).Length>4*1024*1024)throw new IOException("旧记录超过 4 MiB："+source);
                byte[] bytes=File.ReadAllBytes(path);var raw=JsonMap.Read(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));string sourceHash=PluginImporter.HashBytes(bytes);
                string mapKey=source.EndsWith("plugin-assets.json",StringComparison.Ordinal)?"Content":"Dependencies";
                object mapValue;if(!raw.TryGetValue(mapKey,out mapValue)||!(mapValue is Dictionary<string,object>))throw new IOException("不支持的旧记录格式："+source);
                foreach(var pair in ((Dictionary<string,object>)mapValue).OrderBy(p=>p.Key,StringComparer.Ordinal)) {
                    if(entries.Count>=500)throw new IOException("迁移预览超过 500 条");
                    var entry=new LegacyEntry {id=Hash(source+"\n"+pair.Key),source_file=source,source_digest=sourceHash,kind=mapKey=="Content"?"plugin":source.Contains("upm-install")?"root":source.Contains("local")||source.Contains("ump-lock")?"local":"dependency"};
                    entry.migrated=imported.Contains(entry.id);
                    if(entry.kind=="plugin") {
                        int split=pair.Key.IndexOf('#');entry.package_name=Label(split<0?"":pair.Key.Substring(0,split));entry.name=Label(split<0?pair.Key:pair.Key.Substring(split+1));
                        var list=pair.Value as List<object>;if(list==null||list.Count>10000||list.Any(p=>!(p is string)))throw new IOException("旧插件资源列表无效");
                        var paths=list.Cast<string>().Where(p=>p!="Assets").Select(p=>p.EndsWith(".meta",StringComparison.Ordinal)?p.Substring(0,p.Length-5):p).Distinct().OrderBy(p=>p,StringComparer.Ordinal).ToArray();
                        total+=paths.Length;if(total>20000)throw new IOException("本次迁移资源超过 20000 项");
                        var files=new List<LegacyAsset>();
                        foreach(var relative in paths){files.Add(Inspect(root,relative));yield return null;}
                        entry.files=files.ToArray();entry.file_count=files.Count;entry.missing=files.Count(f=>f.issue!="");
                        entry.roots=files.Select(f=>"Assets/"+f.path.Split('/')[1]).Distinct().Take(8).ToArray();
                        entry.warning="仅保存现状基线，仓库版本与原始独占归属未确认；不自动授予删除/覆盖权限"+(entry.missing>0?"；存在缺失或 GUID 异常资源":"");
                    } else {
                        var item=pair.Value as Dictionary<string,object>;if(item==null)throw new IOException("旧工程包记录无效");
                        entry.name=Label(Text(item,"PackageName")==""?pair.Key:Text(item,"PackageName"));
                        entry.tag=Label(Text(item,"Tag"));string revision=Text(item,"Revision");
                        if(Regex.IsMatch(revision,@"^[a-fA-F0-9]{40}$"))entry.revision=revision.ToLowerInvariant();else if(entry.tag=="")entry.tag=Label(revision);
                        object local;entry.is_local=entry.kind=="local"||item.TryGetValue("isLocal",out local)&&local is bool&&(bool)local;
                        entry.warning="历史来源记录；实际安装状态以独立扫描为准，不取得自动卸载权限";
                    }
                    entry.entry_digest=Hash(JsonUtility.ToJson(entry));entries.Add(entry);yield return null;
                }
            }
            var value=new LegacyPreview {request_id=id,entries=entries.ToArray()};value.preview_digest=Hash(JsonUtility.ToJson(value));
            var text=JsonUtility.ToJson(value);if(Encoding.UTF8.GetByteCount(text)>16*1024*1024)throw new IOException("迁移预览超过 16 MiB");
            PluginImporter.Persist(savedPath,text);Reply=Summary(value);
        }
        static LegacyPreview Summary(LegacyPreview value) {
            var result=JsonUtility.FromJson<LegacyPreview>(JsonUtility.ToJson(value));foreach(var e in result.entries)e.files=new LegacyAsset[0];return result;
        }
        internal static void Advance(Journal journal,string root,Action save,Action<string,string,string> report,Action intent) {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            var target=journal.work.target;string destination=ArchivePath(root,target.migration_id);
            try {
                if(!journal.intent) {
                    if(!SessionState.GetBool("ServiceBase.Bridge.Enabled",true))return;
                    var preview=JsonUtility.FromJson<LegacyPreview>(File.ReadAllText(PreviewPath(root,target.migration_id)));
                    if(preview.preview_digest!=target.migration_digest||target.migration_entries==null||target.migration_entries.Length==0)throw new IOException("迁移预览不匹配");
                    var selected=preview.entries.Where(e=>target.migration_entries.Contains(e.id)).ToArray();
                    if(selected.Length!=target.migration_entries.Length||selected.Any(e=>e.migrated))throw new IOException("迁移选择无效");
                    var imported=new HashSet<string>(Imported(root).Select(e=>e.id));
                    if(selected.Any(e=>imported.Contains(e.id)))throw new IOException("所选记录已经迁移，请重新预览");
                    if(imported.Count+selected.Length>500)throw new IOException("迁移后记录总数超过 500 条，请缩小选择范围");
                    foreach(var entry in selected) {
                        if(!Sources.Contains(entry.source_file)||PluginImporter.Hash(PluginImporter.In(root,entry.source_file))!=entry.source_digest)throw new IOException("旧记录已变化，请重新预览");
                        foreach(var file in entry.files)if(!Same(file,Inspect(root,file.path)))throw new IOException("资源已变化，请重新预览："+file.path);
                    }
                    journal.migration_record_json=JsonUtility.ToJson(new LegacyArchive {request_id=target.migration_id,preview_digest=target.migration_digest,entries=selected});
                    save();intent();journal.intent=true;report("running","已确认选择，正在保存独立迁移记录。","");
                }
                // One immutable archive per request makes a lost acknowledgement/restart idempotent.
                if(string.IsNullOrEmpty(journal.migration_record_json)&&File.Exists(destination)) {
                    var raw=File.ReadAllText(destination);var existing=JsonUtility.FromJson<LegacyArchive>(raw);
                    if(existing==null||existing.schema!=1||existing.request_id!=target.migration_id||existing.preview_digest!=target.migration_digest||existing.entries==null||!existing.entries.Select(e=>e.id).OrderBy(e=>e,StringComparer.Ordinal).SequenceEqual(target.migration_entries.OrderBy(e=>e,StringComparer.Ordinal)))throw new IOException("已有迁移归档与恢复目标不符");
                    journal.migration_record_json=raw;save();
                }
                if(string.IsNullOrEmpty(journal.migration_record_json))throw new IOException("迁移恢复记录缺失，请核对");
                if(File.Exists(destination)) {
                    if(File.ReadAllText(destination)!=journal.migration_record_json)throw new IOException("已有迁移记录与本次选择不一致");
                } else PluginImporter.Persist(destination,journal.migration_record_json);
                journal.report.evidence.migration_id=target.migration_id;journal.report.evidence.migration_digest=target.migration_digest;
                journal.report.evidence.record_digest=Hash(journal.migration_record_json);journal.report.evidence.verified=true;
                PluginInventory.Dirty();ProjectPackageState.Dirty();report("succeeded","所选旧记录已迁移；原记录和工程资源保持原样。","");
            } catch(Exception e) { report(journal.intent?"needs_review":"failed","记录迁移未完成："+e.Message,journal.intent?"":"preflight"); }
        }
        internal static IEnumerator ScanStatus(string root,List<LegacyStatus> results) {
            foreach(var entry in Imported(root)) {
                var status=new LegacyStatus {id=entry.id,name=entry.name,package_name=entry.package_name,kind=entry.kind,source_file=entry.source_file,source_digest=entry.source_digest,roots=entry.roots,file_count=entry.files.Length,status="recorded"};
                foreach(var file in entry.files) {
                    var now=Inspect(root,file.path);
                    if(now.issue!="")status.missing++;else if(Same(file,now))status.matched++;else status.modified++;
                    yield return null;
                }
                if(entry.kind=="plugin")status.status=status.missing>0?"partial":status.modified>0?"modified":"baseline_matches";
                results.Add(status);
            }
        }
    }
}
#endif
