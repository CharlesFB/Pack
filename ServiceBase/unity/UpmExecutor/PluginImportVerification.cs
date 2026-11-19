#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ServiceBase.Upm
{
    internal static class PluginImportVerification
    {
        internal const string ArchivePolicy = "archive-execution-v1";
        internal static PluginRecord ExecutionRecord(Journal journal) {
            var files=JsonUtility.FromJson<PluginState>(JsonUtility.ToJson(journal.plugin)).files;
            return new PluginRecord {plugin_id=journal.work.target.plugin_id,build_id=journal.work.target.build_id,
                attempt_id=journal.work.attempt_id,destination_root=journal.destination_root,verification=ArchivePolicy,
                import_result=journal.plugin.result,files=files,omitted_files=new PluginFile[0]};
        }
        internal const string Policy = "unity-import-v1";
        // Exact archived build: VersionHandler removes its obsolete 1.2.137 marker.
        // Never infer successful import from a plugin name or a missing directory.
        internal static bool MayOmit(Target target, PluginFile file) {
            if(target.format!="unitypackage" || target.build_id!="6db6b031d4fbd18a73e8b56455245b24355a9cfc558b7ae042326503bb01bc2a" || file.existed || file.meta_existed) return false;
            return (file.folder && file.generated && (file.path=="Assets/PlayServicesResolver" || file.path=="Assets/PlayServicesResolver/Editor")) ||
                (!file.folder && file.path=="Assets/PlayServicesResolver/Editor/play-services-resolver_v1.2.137.0.txt" && file.sha256=="9746742f6f187698a449657be32d14ca7b3a0fa7e783b45c7894e5733eba0f15");
        }
        internal static bool Check(Journal journal,string root,out PluginRecord record,out string mismatch) {
            try { return CheckFiles(journal,root,out record,out mismatch); }
            catch(IOException) { record=null; mismatch="资源或 meta 正在变化、无法读取或格式无效"; return false; }
            catch(UnauthorizedAccessException) { record=null; mismatch="无法读取插件资源或 meta，请检查文件权限"; return false; }
        }
        static bool CheckFiles(Journal journal,string root,out PluginRecord record,out string mismatch) {
            record=null; mismatch="";
            // Once sealed, the ownership snapshot is immutable. Later edits must never
            // be accepted as further importer normalization (including lost receipt ACKs).
            if(!string.IsNullOrEmpty(journal.plugin.record_json)) {
                record=JsonUtility.FromJson<PluginRecord>(journal.plugin.record_json);
                if(!PluginImporter.Verify(record.files,root)) { mismatch="已保存的插件文件或 meta 发生变化"; return false; }
                foreach(var omitted in record.omitted_files ?? new PluginFile[0]) {
                    var p=PluginImporter.In(root,omitted.path);
                    if(File.Exists(p)||Directory.Exists(p)||File.Exists(p+".meta")) { mismatch="已清理的历史文件重新出现："+omitted.path; return false; }
                }
                return true;
            }
            var matched=new List<PluginFile>(); var omittedFiles=new List<PluginFile>();
            foreach(var original in journal.plugin.files) {
                var file=JsonUtility.FromJson<PluginFile>(JsonUtility.ToJson(original));
                var path=PluginImporter.In(root,file.path);
                bool exists=File.Exists(path)||Directory.Exists(path), meta=File.Exists(path+".meta");
                if(!exists && !meta && MayOmit(journal.work.target,file)) { omittedFiles.Add(file); continue; }
                if(!exists || !meta) { mismatch="资源或 meta 缺失："+file.path; return false; }
                if(Directory.Exists(path)!=file.folder || (!file.folder && PluginImporter.Hash(path)!=file.sha256)) { mismatch="文件内容不符："+file.path; return false; }
                if(PluginImporter.GuidAt(path+".meta")!=file.guid || AssetDatabase.GUIDToAssetPath(file.guid)!=file.path) { mismatch="GUID 或资源路径不符："+file.path; return false; }
                var actualMeta=PluginImporter.Hash(path+".meta");
                // Unity rewrites importer serialization/platform settings. Keep the
                // archive hash as provenance and capture the settled imported hash.
                if(journal.work.target.format=="unitypackage") {
                    file.source_meta_sha256=file.meta_sha256;
                    file.meta_sha256=actualMeta;
                } else if(actualMeta!=file.meta_sha256) { mismatch="meta 内容不符："+file.path; return false; }
                matched.Add(file);
            }
            if(matched.Count==0) { mismatch="没有完整资源证据"; return false; }
            record=new PluginRecord {plugin_id=journal.work.target.plugin_id,build_id=journal.work.target.build_id,attempt_id=journal.work.attempt_id,
                destination_root=journal.destination_root,verification=journal.work.target.format=="unitypackage"?Policy:"",files=matched.ToArray(),omitted_files=omittedFiles.ToArray()};
            return true;
        }
    }
}
#endif
