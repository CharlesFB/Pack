#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace ServiceBase.UnityBridge
{
    internal static class PackageMetadataReader
    {
        [Serializable] private class Author { public string name = "", email = ""; }
        [Serializable] private class Manifest
        {
            public string name = "", displayName = "", version = "", description = "", unity = "", license = "", changelog = "";
            public Author author = null;
        }
        /// <summary>
        /// 读取字符串字段；无值时返回空字符串。
        /// </summary>
        private static string Text(
            object source, // 缓存或反射读取的源对象
            string key // 需要读取的字段名
        )
        {
            return source?.GetType().GetField(key, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source) as string ?? "";
        }
        /// <summary>
        /// 将缓存元数据中的可展示字段复制到快照，不复制仓库凭据。
        /// </summary>
        internal static void CopyCached(
            object source, // 缓存或反射读取的源对象
            MtpPackageSnapshot target // 待补充的包记录
        )
        {
            if (source == null) return;
            target.display_name = Text(source, "displayName"); target.version = Text(source, "version");
            target.description = Text(source, "description"); target.unity = Text(source, "unity");
            target.license = Text(source, "license"); target.changelog = Text(source, "changelog");
            var author = source.GetType().GetField("author")?.GetValue(source);
            target.author = Text(author, "name");
            var email = Text(author, "email");
            if (email != "") target.author += " <" + email + ">";
        }
        /// <summary>
        /// 使用 UPM 已注册包和本地 package.json 补充元数据及安装依据。
        /// </summary>
        internal static void EnrichInstalled(
            MtpSnapshot snapshot // 当前工程快照
        )
        {
            if (snapshot.packages.Count == 0) return;
            try
            {
                var registered = new Dictionary<string, UnityEditor.PackageManager.PackageInfo>();
                foreach (var info in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()) registered[info.name] = info;
                var locals = new Dictionary<string, string>();
                bool needsLocal = snapshot.packages.Exists(p => p.is_local && !registered.ContainsKey(p.name));
                if (needsLocal)
                    foreach (var path in AssetDatabase.GetAllAssetPaths())
                        if (path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith("/package.json", StringComparison.Ordinal))
                        {
                            var manifest = Read(path);
                            if (manifest != null && !string.IsNullOrEmpty(manifest.name)) locals[manifest.name] = path;
                        }
                foreach (var package in snapshot.packages)
                {
                    if (package.catalog_id != "" && package.depth < 0 && package.installation_status == "not_installed") continue;
                    string path;
                    UnityEditor.PackageManager.PackageInfo info;
                    if (registered.TryGetValue(package.name, out info))
                    {
                        package.installation_status = "installed"; package.installation_source = "UPM 已注册";
                        package.installed_version = info.version;
                        if (string.IsNullOrEmpty(package.display_name)) package.display_name = info.displayName;
                        path = Path.Combine(info.resolvedPath, "package.json");
                    }
                    else if (locals.TryGetValue(package.name, out path))
                    { package.installation_status = "installed"; package.installation_source = "Assets 中的 package.json（未校验全部文件）"; }
                    else
                    {
                        package.installation_status = package.is_local && package.installation_status == "installed" ? "installed" : "not_installed";
                        package.installation_source = package.is_local ? "按 MTP 本地安装记录判断，未校验全部文件" : "UPM 未注册此包";
                        continue;
                    }
                    var json = Read(path);
                    if (json == null || json.name != package.name) continue;
                    package.installed_version = json.version;
                    // Keep tag-specific MTP metadata when available; installed manifest is the fallback.
                    if (string.IsNullOrEmpty(package.display_name)) package.display_name = json.displayName;
                    if (string.IsNullOrEmpty(package.version)) package.version = json.version;
                    if (string.IsNullOrEmpty(package.description)) package.description = json.description;
                    if (string.IsNullOrEmpty(package.unity)) package.unity = json.unity;
                    if (string.IsNullOrEmpty(package.license)) package.license = json.license;
                    if (string.IsNullOrEmpty(package.author) && json.author != null) package.author = json.author.name + (string.IsNullOrEmpty(json.author.email) ? "" : " <" + json.author.email + ">");
                }
            }
            catch { /* Missing metadata stays unknown; keep the dependency snapshot usable. */ }
        }
        /// <summary>
        /// 读取受大小限制的本地 package.json，失败时返回空值。
        /// </summary>
        private static Manifest Read(
            string path // 待读取的本地文件路径
        )
        {
            try { return new FileInfo(path).Length <= 512 * 1024 ? JsonUtility.FromJson<Manifest>(File.ReadAllText(path)) : null; }
            catch { return null; }
        }
    }
}
#endif
