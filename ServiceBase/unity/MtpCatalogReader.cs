#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
namespace ServiceBase.UnityBridge
{
    // Read the same 647 project catalog/cache as MTP; never fetch, install or mutate MTP state.
    internal static class MtpCatalogReader
    {
        private sealed class CachedFile { internal DateTime time; internal long size; internal object value; }
        private static readonly Dictionary<string, CachedFile> Files = new Dictionary<string, CachedFile>();  // 按文件状态复用本地反序列化结果；此读取器仅保留为兼容路径。
        /// <summary>
        /// 按文件路径、修改时间和大小复用已反序列化的目录缓存。
        /// </summary>
        private static object Load(
            string path, // 待读取的本地文件路径
            Type type // 反序列化目标类型
        )
        {
            if (type == null || !File.Exists(path)) return null;
            var file = new FileInfo(path);
            if (file.Length > 32 * 1024 * 1024) throw new InvalidDataException("Catalog cache too large");
            CachedFile old;
            if (Files.TryGetValue(path, out old) && old.time == file.LastWriteTimeUtc && old.size == file.Length) return old.value;
            var utils = MtpReflection.Find("JsonUtils");
            var method = utils?.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static);
            if (method == null || !method.IsGenericMethodDefinition) throw new MissingMethodException("LoadFromFile");
            object data = method.MakeGenericMethod(type).Invoke(null, new object[] {path, false});
            Files[path] = new CachedFile {time=file.LastWriteTimeUtc, size=file.Length, value=data};
            return data;
        }
        /// <summary>
        /// 反射读取指定字段。
        /// </summary>
        private static object Field(
            object value, // 输入字段对象或待哈希文本
            string name // 目标字段名称
        ) { return value?.GetType().GetField(name)?.GetValue(value); }
        /// <summary>
        /// 读取字符串字段；无值时返回空字符串。
        /// </summary>
        private static string Text(
            object value, // 输入字段对象或待哈希文本
            string name // 目标字段名称
        ) { return Field(value,name) as string ?? ""; }
        /// <summary>
        /// 从标签中解析用于排序的数字版本。
        /// </summary>
        private static Version VersionOf(
            string tag // 版本标签
        )
        {
            var match = Regex.Match(tag ?? "", @"\d+\.\d+(?:\.\d+)?(?:\.\d+)?");
            Version version;
            return Version.TryParse(match.Value, out version) ? version : new Version(0,0);
        }
        /// <summary>
        /// 优先选择 Release，再按数字版本从新到旧选择标签。
        /// </summary>
        internal static string LatestTag(
            List<string> tags // 候选版本标签列表
        )
        {
            // Prefer release tags, then semantic version (1.10 > 1.9), without mutating MTP's list.
            var ordered = new List<string>(tags);
            ordered.Sort((a,b) => {
                int debug = a.EndsWith("-debug",StringComparison.Ordinal).CompareTo(b.EndsWith("-debug",StringComparison.Ordinal));
                if (debug != 0) return debug;
                int version = VersionOf(b).CompareTo(VersionOf(a));
                return version != 0 ? version : string.CompareOrdinal(b,a);
            });
            return ordered.Count > 0 ? ordered[0] : "";
        }
        /// <summary>
        /// 兼容读取本地完整目录缓存；当前生产采集由 Python 提供完整目录。
        /// </summary>
        internal static void Read(
            string projectRoot, // Unity 工程根目录
            MtpSnapshot result // 用于写入读取结果的快照
        )
        {
            if (result.state != "ready") return;
            try
            {
                bool legacy = result.adapter == "legacy";
                string directory = Path.Combine(projectRoot, legacy ? "Library" : "Library/MTPCache");
                var catalog = Load(Path.Combine(directory,"upm-list.json"),MtpReflection.Find("ProjectInfoList"));
                var entries = Field(catalog,"Packages") as IEnumerable;
                if (entries == null)
                { result.catalog_state="missing"; result.detail += "；完整包目录缓存缺失，请先在 MTP 刷新目录"; return; }
                object metadata = null;
                try { metadata = Load(Path.Combine(directory,"upm-cache.json"),MtpReflection.Find(legacy ? "PackageJsonCache" : "PackageInfoCache")); }
                catch { /* Catalog can still be shown when optional tag metadata is unavailable. */ }
                Merge(entries, Field(metadata,"PackageCache") as IDictionary, result);
                result.catalog_state="ready";
                result.detail = "647 工程完整包目录（本地 MTP 缓存）；安装状态按当前工程记录及已注册包判断。";
                result.source += "; " + (legacy ? "Library" : "Library/MTPCache") + "/upm-list.json";
            }
            catch
            { result.catalog_state="error"; result.detail += "；完整包目录读取失败，当前仅显示工程已有记录"; }
        }
        /// <summary>
        /// 合并本地目录元数据与工程安装记录，保留同名但不同工程的目录项。
        /// </summary>
        internal static void Merge(
            IEnumerable entries, // 目录工程条目
            IDictionary metadata, // 按工程及标签索引的缓存元数据
            MtpSnapshot result // 用于写入读取结果的快照
        )
        {
            var installed = new List<MtpPackageSnapshot>(result.packages);
            var output = new List<MtpPackageSnapshot>();
            var identities = new HashSet<string>();
            var used = new HashSet<MtpPackageSnapshot>();
            foreach (var entry in entries)
            {
                string shortName = Text(entry,"PackageName");
                if (string.IsNullOrEmpty(shortName)) continue;
                string projectId = Convert.ToString(Field(entry,"ProjectId"));
                var tags = new List<string>();
                var values = Field(entry,"Tags") as IEnumerable;
                if (values != null) foreach (var tag in values) if (tag is string && !string.IsNullOrEmpty((string)tag)) tags.Add((string)tag);
                var versions = metadata != null && metadata.Contains(projectId) ? metadata[projectId] as IDictionary : null;
                string selected = LatestTag(tags);
                object json = versions != null && versions.Contains(selected) ? versions[selected] : null;
                string canonical = Text(json,"name");
                // Older cached tags may supply identity even when the newest tag metadata is missing.
                if (canonical == "" && versions != null)
                    foreach (var tag in tags) if (versions.Contains(tag) && Text(versions[tag],"name") != "") { canonical=Text(versions[tag],"name"); break; }
                var existing = installed.Find(p => !used.Contains(p) && tags.Contains(p.tag));
                if (existing == null)
                    existing = installed.Find(p => !used.Contains(p) && (p.name == shortName || (p.name == canonical && (string.IsNullOrEmpty(p.tag) || p.tag.StartsWith(shortName + "-",StringComparison.Ordinal)))));
                MtpPackageSnapshot package;
                if (existing != null)
                {
                    package = existing; used.Add(existing); selected = existing.tag;
                    json = versions != null && versions.Contains(selected) ? versions[selected] : null;
                    if (package.installation_status == "unknown")
                    { package.installation_status="installed"; package.installation_source="MTP 工程依赖记录（未逐项校验磁盘）"; }
                }
                else package = new MtpPackageSnapshot { name=canonical != "" ? canonical : shortName, tag=selected, revision="", depth=-1, installation_status="not_installed", installation_source="当前工程没有此包的安装记录" };
                package.catalog_id=projectId+":"+shortName;
                if (!identities.Add(package.catalog_id)) continue;
                package.available_tags=tags;
                PackageMetadataReader.CopyCached(json,package);
                if (package.display_name == "") package.display_name=shortName;
                if (existing == null)
                {
                    var dependencies = Field(json,"dependenciesGit") as IDictionary;
                    if (dependencies != null)
                        foreach (DictionaryEntry dependency in dependencies)
                        {
                            string reference = dependency.Value as string ?? "";
                            int hash = reference.LastIndexOf('#');
                            package.dependencies.Add(new MtpDependencySnapshot {name=(string)dependency.Key,requested_tag=hash>=0?reference.Substring(hash+1):""});
                        }
                }
                output.Add(package);
            }
            result.catalog_count=output.Count;
            // Keep already installed packages even if the current catalog no longer contains them.
            foreach (var p in installed) if (!used.Contains(p))
            {
                if (p.installation_status == "unknown") { p.installation_status="installed"; p.installation_source="MTP 工程依赖记录"; }
                output.Add(p);
            }
            output.Sort((a,b)=>string.CompareOrdinal(a.display_name == "" ? a.name : a.display_name,b.display_name == "" ? b.name : b.display_name));
            result.packages=output;
        }
    }
}
#endif
