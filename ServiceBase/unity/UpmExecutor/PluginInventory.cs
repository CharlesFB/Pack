#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace ServiceBase.Upm
{
    [Serializable] internal class PluginStatus {
        public string record_key, record_digest, plugin_id, build_id, destination_root, status, import_result;
        public int file_count, matched, modified, missing;
        public string[] roots, samples;
    }
    [Serializable] internal class PluginSnapshot {
        public string scanned_at, request_key, error = "";
        public PluginStatus[] installations;
        public LegacyStatus[] legacy_installations=new LegacyStatus[0];
    }
    internal sealed class OwnedPlugin {
        internal string key, digest, raw;
        internal PluginRecord record;
    }
    internal static class PluginInventory
    {
        static IEnumerator scan;
        static double due;
        static string requested = "initial";
        internal static PluginSnapshot Snapshot;
        static PluginInventory() { EditorApplication.projectChanged += Dirty; }
        internal static void Dirty() { Snapshot=null; due = Math.Min(due, EditorApplication.timeSinceStartup + 3); }
        internal static void Request(string key) {
            if (!string.IsNullOrEmpty(key) && key != requested) { requested = key; scan=null; due = 0; }
        }
        internal static string RecordPath(string root, string key) {
            if (string.IsNullOrEmpty(key) || key.Length > 256 || key.Any(c => !char.IsLetterOrDigit(c) && c!='-' && c!='_' && c!='.')) throw new IOException("无效插件记录标识");
            return PluginImporter.In(root, ".servicebase/plugins/records/" + key + ".json");
        }
        internal static OwnedPlugin Load(string root, string key) {
            var path = RecordPath(root, key);
            if (new FileInfo(path).Length > 2400000) throw new IOException("插件记录超限");
            var raw = File.ReadAllText(path); var record = JsonUtility.FromJson<PluginRecord>(raw);
            if (record == null || string.IsNullOrEmpty(record.plugin_id) || string.IsNullOrEmpty(record.build_id) || record.files == null || record.files.Length == 0 || record.files.Length > 10000) throw new IOException("插件归属记录不完整");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in record.files) {
                if (file == null || !file.path.StartsWith("Assets/", StringComparison.Ordinal) || !paths.Add(file.path)) throw new IOException("插件归属路径无效");
                PluginImporter.In(root,file.path);
                if (string.IsNullOrEmpty(file.meta_sha256) || file.meta_sha256.Length != 64 || string.IsNullOrEmpty(file.guid) || file.guid.Length != 32 || (!file.folder && (file.sha256 == null || file.sha256.Length != 64))) throw new IOException("插件归属哈希缺失");
            }
            return new OwnedPlugin { key=key, digest=PluginImporter.HashBytes(System.Text.Encoding.UTF8.GetBytes(raw)), raw=raw, record=record };
        }
        internal static string[] Keys(string root) {
            var directory=PluginImporter.In(root,".servicebase/plugins/records");
            if (!Directory.Exists(directory)) return new string[0];
            var paths=Directory.EnumerateFiles(directory,"*.json").Take(2001).ToArray();
            if (paths.Length>2000) throw new IOException("插件记录超过 2000 项");
            return paths.Select(Path.GetFileNameWithoutExtension).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        }
        internal static string Check(string root, PluginFile file) {
            var path=PluginImporter.In(root,file.path);
            bool asset=File.Exists(path)||Directory.Exists(path), meta=File.Exists(path+".meta");
            if (!asset && !meta) return "missing";
            if (!asset || !meta || Directory.Exists(path)!=file.folder) return "modified";
            if ((!file.folder && PluginImporter.Hash(path)!=file.sha256) || PluginImporter.Hash(path+".meta")!=file.meta_sha256 || PluginImporter.GuidAt(path+".meta")!=file.guid || AssetDatabase.GUIDToAssetPath(file.guid)!=file.path) return "modified";
            return "installed";
        }
        internal static void Tick(string root, bool busy) {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) { scan=null; return; }
            try {
                if (scan==null) {
                    if (EditorApplication.timeSinceStartup<due) return;
                    scan=Scan(root,requested);
                }
                // Yield between files so large repositories do not freeze an entire editor frame.
                double until=EditorApplication.timeSinceStartup+0.015;
                do { if (!scan.MoveNext()) { scan=null; due=EditorApplication.timeSinceStartup+60; break; } }
                while (EditorApplication.timeSinceStartup<until);
            } catch (Exception error) {
                scan=null; due=EditorApplication.timeSinceStartup+30;
                Snapshot=new PluginSnapshot {scanned_at=DateTime.UtcNow.ToString("o"),request_key=requested,error="插件记录核对失败："+error.GetType().Name,installations=new PluginStatus[0]};
            }
        }
        static IEnumerator Scan(string root,string request) {
            var results=new List<PluginStatus>();
            foreach (var key in Keys(root)) {
                var owned=Load(root,key); var r=owned.record;
                var item=new PluginStatus {record_key=key,record_digest=owned.digest,plugin_id=r.plugin_id,build_id=r.build_id,destination_root=r.destination_root ?? "",file_count=r.files.Length,
                    import_result=r.import_result ?? "",
                    roots=r.files.Select(f=>"Assets/"+f.path.Split('/')[1]).Distinct().Take(8).ToArray()};
                // The inventory describes the archive list, not compiler output.
                item.matched=r.files.Length;
                item.status="recorded";
                item.samples=new string[0]; results.Add(item);
                yield return null;
            }
            var legacy=new List<LegacyStatus>();var legacyScan=LegacyMigration.ScanStatus(root,legacy);
            while(legacyScan.MoveNext())yield return null;
            var value=new PluginSnapshot {legacy_installations=legacy.ToArray(),scanned_at=DateTime.UtcNow.ToString("o"),request_key=request,installations=results.ToArray()};
            if (System.Text.Encoding.UTF8.GetByteCount(JsonUtility.ToJson(value))>1500000) throw new IOException("插件状态摘要超限");
            PluginImporter.Persist(PluginImporter.In(root,".servicebase/plugins/status.json"),JsonUtility.ToJson(value,true));
            Snapshot=value;
        }
    }
}
#endif
