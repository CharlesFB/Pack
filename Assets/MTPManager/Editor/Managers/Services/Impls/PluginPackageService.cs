// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 插件包编排服务（远端下载+解析写状态 / 导入 / 卸载）
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PluginPackageService : IPluginPackageService
    {
        private IGitLabService GitLab => ServiceLocator.Get<IGitLabService>();
        private IMTPackageInfoService MTPackage => ServiceLocator.Get<IMTPackageInfoService>();
        private IPluginAssetService PluginAsset => ServiceLocator.Get<IPluginAssetService>();

        public Task<bool> PreparePluginsAsync(MTPackageInfo mtPackage, bool persist = true)
        {
            if (mtPackage == null || string.IsNullOrEmpty(mtPackage.PackageName))
            {
                LM.LogWarning("[PluginPackageService] PreparePluginsAsync: mtPackage 无效");
                return Task.FromResult(false);
            }

            EnsurePackageTracked(mtPackage);
            SyncPluginsFromDisk(mtPackage);

            if (persist)
            {
                MTPackage.SaveDependenciesToLocal();
            }

            int indexed = mtPackage.Plugins?.Count ?? 0;
            LM.Log(
                $"[PluginPackageService] PreparePlugins done. package:{mtPackage.PackageName}, indexed:{indexed}");
            return Task.FromResult(true);
        }

        /// <summary>
        /// 只扫已安装包本地 PluginsPackage，写入 Plugins Key；不写 PluginAsset。
        /// 已导入状态按 PluginAsset 的 Key 是否有资源清单判定。
        /// </summary>
        public void SyncPluginsFromDisk(MTPackageInfo mtPackage)
        {
            if (mtPackage == null)
            {
                return;
            }

            mtPackage.Plugins ??= new Dictionary<string, bool>();
            mtPackage.Plugins.Clear();

            string pluginDir = PathUtils.GetPluginsDirectory(mtPackage);
            if (string.IsNullOrEmpty(pluginDir) || !Directory.Exists(pluginDir))
            {
                return;
            }

            var files = new List<string>();
            files.AddRange(Directory.GetFiles(pluginDir, "*.unitypackage", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(pluginDir, "*.zip", SearchOption.AllDirectories));

            var recorded = PluginAsset.GetAll() ?? new Dictionary<string, List<string>>();
            string pluginDirNormalized = PathUtils.NormalizePluginKey(Path.GetFullPath(pluginDir)).TrimEnd('/');

            foreach (var file in files)
            {
                string fullPath = PathUtils.NormalizePluginKey(Path.GetFullPath(file));
                string relative = fullPath.StartsWith(pluginDirNormalized, StringComparison.OrdinalIgnoreCase)
                    ? fullPath.Substring(pluginDirNormalized.Length).TrimStart('/')
                    : Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(relative) || !IsPluginFileName(relative))
                {
                    continue;
                }

                string key = PathUtils.BuildPluginLocalPath(mtPackage, relative);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                bool imported = false;
                foreach (var kvp in recorded)
                {
                    if (!string.Equals(PathUtils.NormalizePluginKey(kvp.Key), key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    imported = kvp.Value != null && kvp.Value.Count > 0;
                    break;
                }

                mtPackage.Plugins[key] = imported;
                MTPackage.SetPlugin(mtPackage.PackageName, key, imported);
            }
        }

        public async Task<bool> ImportPreparedPluginsAsync(MTPackageInfo mtPackage,
            IEnumerable<string> selectedPluginLocalPaths = null,
            IReadOnlyDictionary<string, string> zipExtractDirectories = null,
            bool persist = true, bool skipRecorded = false)
        {
            if (mtPackage == null || string.IsNullOrEmpty(mtPackage.PackageName))
            {
                LM.LogWarning("[PluginPackageService] ImportPreparedPlugins: mtPackage 无效");
                return false;
            }

            EnsurePackageTracked(mtPackage);

            var plugins = MTPackage.GetPlugins(mtPackage.PackageName)
                          ?? new Dictionary<string, bool>();

            HashSet<string> selected = null;
            if (selectedPluginLocalPaths != null)
            {
                selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in selectedPluginLocalPaths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        selected.Add(PathUtils.NormalizePluginKey(path));
                    }
                }

                if (selected.Count == 0)
                {
                    return true;
                }
            }
            else if (plugins.Count == 0)
            {
                LM.Log($"[PluginPackageService] ImportPreparedPlugins: 无插件记录 {mtPackage.PackageName}");
                return true;
            }

            int imported = 0;
            var pluginKeys = new List<string>(plugins.Keys);
            if (selected != null)
            {
                foreach (var selectedKey in selected)
                {
                    if (ResolvePluginDictKey(plugins, selectedKey).Length == 0)
                    {
                        pluginKeys.Add(selectedKey);
                    }
                }
            }

            foreach (var pluginKey in pluginKeys)
            {
                if (selected != null && !selected.Contains(PathUtils.NormalizePluginKey(pluginKey)))
                {
                    continue;
                }

                if (skipRecorded && IsPluginAssetRecorded(pluginKey))
                {
                    MTPackage.SetPlugin(mtPackage.PackageName, pluginKey, true);
                    imported++;
                    LM.Log($"[PluginPackageService] 跳过已记录插件: {pluginKey}");
                    continue;
                }

                string pluginFilePath = ResolvePluginFilePath(mtPackage, pluginKey);
                if (string.IsNullOrEmpty(pluginFilePath) || !File.Exists(pluginFilePath))
                {
                    LM.LogWarning($"[PluginPackageService] Import 找不到插件文件: {pluginKey}");
                    continue;
                }

                bool isZip = Path.GetExtension(pluginFilePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);

                // zip 未选择目录 / 选择了空目录时，一律按 "/Assets" 默认解压；该选择只作用于本次导入，不做持久化
                string zipTargetDirectory = TryGetZipExtractDirectory(zipExtractDirectories, pluginKey);

                // 无论 .unitypackage 还是 .zip，都统一按“插件”处理：先算出本次会落盘的资源清单（zip 已拼接目标父目录），
                // GetCoveredList 检测是否会覆盖别的插件已记录的资源，逐插件单独弹窗确认
                List<string> plannedAssetPaths = AnalyzePluginAssetPaths(pluginFilePath, zipTargetDirectory);
                // 排除当前插件自身已记录的资源，避免 ReImport 时误判为跟自己冲突
                var covered = PluginAsset.GetCoveredList(plannedAssetPaths, pluginKey);
                if (covered != null && covered.Count > 0 && !ConfirmOverwrite(pluginKey, covered))
                {
                    LM.Log($"[PluginPackageService] 用户取消覆盖，跳过插件: {pluginKey}");
                    continue;
                }

                if (!await ImportSinglePluginFileAsync(pluginFilePath, zipTargetDirectory))
                {
                    continue;
                }

                // zip 可能多次解压到不同目录，用 Append 合并去重，保留历史落盘路径供卸载时一并清理；
                // unitypackage 目标路径固定，Append 结果等价于 Set
                PluginAsset.Append(pluginKey, plannedAssetPaths);
                MTPackage.SetPlugin(mtPackage.PackageName, pluginKey, true);
                imported++;

                // 必须在 Refresh / 域重载之前落盘，否则 zip 解压后编译会冲掉内存中的 PluginAsset。
                if (persist)
                {
                    MTPackage.SaveDependenciesToLocal();
                    PluginAsset.SaveToLocal();
                }

                // zip 必须 Refresh 才会进 AssetDatabase；unitypackage 的 ImportPackage 自己会刷新。
                // 两者都要在落盘之后再等空闲，否则编译/域重载会冲掉未写入的 PluginAsset。
                if (isZip)
                {
                    AssetDatabase.Refresh();
                }

                await EditorBusyWait.WaitUntilIdleAsync();
            }

            if (persist)
            {
                MTPackage.SaveDependenciesToLocal();
                PluginAsset.SaveToLocal();
            }

            LM.Log(
                $"[PluginPackageService] ImportPreparedPlugins done. package:{mtPackage.PackageName}, imported:{imported}");
            return imported > 0 || selected == null || selected.Count == 0;
        }

        /// <summary>
        /// 单个插件覆盖冲突确认弹窗：列表过长时用 "..." 截断展示。
        /// </summary>
        private static bool ConfirmOverwrite(string pluginKey, List<string> coveredPaths)
        {
            const int MAX_SHOWN = 15;
            string fileName = Path.GetFileName(pluginKey);
            string listText = coveredPaths.Count <= MAX_SHOWN
                ? string.Join("\n", coveredPaths)
                : string.Join("\n", coveredPaths.Take(MAX_SHOWN)) + $"\n...（共 {coveredPaths.Count} 条）";

            return EditorUtility.DisplayDialog(
                "导入插件 - 覆盖确认",
                $"插件 {fileName} 将覆盖以下 {coveredPaths.Count} 个已存在的资源：\n\n{listText}\n\n是否继续导入并覆盖？",
                "继续覆盖",
                "取消该插件导入");
        }

        public bool RemoveImportedPlugins(MTPackageInfo mtPackage, bool showConfirmDialog = true)
        {
            if (mtPackage == null || string.IsNullOrEmpty(mtPackage.PackageName))
            {
                LM.LogWarning("[PluginPackageService] RemoveImportedPlugins: mtPackage 无效");
                return false;
            }

            var plugins = CollectPluginKeysForPackage(mtPackage);
            if (plugins.Count == 0)
            {
                return true;
            }

            return RemoveImportedPluginKeys(mtPackage, new List<string>(plugins.Keys), showConfirmDialog);
        }

        public bool RemoveImportedPlugin(MTPackageInfo mtPackage, string pluginKey, bool showConfirmDialog = true)
        {
            if (mtPackage == null || string.IsNullOrEmpty(mtPackage.PackageName)
                || string.IsNullOrEmpty(pluginKey))
            {
                LM.LogWarning("[PluginPackageService] RemoveImportedPlugin: 参数无效");
                return false;
            }

            var plugins = CollectPluginKeysForPackage(mtPackage);
            string resolvedKey = ResolvePluginDictKey(plugins, pluginKey);
            if (string.IsNullOrEmpty(resolvedKey))
            {
                return true;
            }

            return RemoveImportedPluginKeys(mtPackage, new List<string> { resolvedKey }, showConfirmDialog);
        }

        private bool RemoveImportedPluginKeys(MTPackageInfo mtPackage, List<string> keys, bool showConfirmDialog)
        {
            var plugins = CollectPluginKeysForPackage(mtPackage);

            // 先汇总本次会真正删除的全部资源路径（仅统计已安装、且未被其它插件共享的部分），
            // 用一次弹窗整体展示给用户确认，而不是像导入那样逐插件分别询问
            var uncoveredByKey = new Dictionary<string, List<string>>();
            var allPathsToRemove = new List<string>();
            foreach (var pluginKey in keys)
            {
                bool wasInstalled = IsPluginImported(plugins, pluginKey);
                if (!wasInstalled)
                {
                    continue;
                }

                var uncovered = PluginAsset.GetUncoveredListToRemove(pluginKey) ?? new List<string>();
                uncoveredByKey[pluginKey] = uncovered;
                allPathsToRemove.AddRange(uncovered);
            }

            if (showConfirmDialog && allPathsToRemove.Count > 0)
            {
                EditorUtility.ClearProgressBar();
                if (!ConfirmRemoveAll(mtPackage, allPathsToRemove))
                {
                    LM.Log($"[PluginPackageService] 用户取消卸载插件，未做任何改动: {mtPackage.PackageName}");
                    return false;
                }
            }

            foreach (var pluginKey in keys)
            {
                bool wasInstalled = IsPluginImported(plugins, pluginKey);
                List<string> remainingPaths = null;

                if (wasInstalled && uncoveredByKey.TryGetValue(pluginKey, out var uncovered) && uncovered.Count > 0)
                {
                    // unitypackage 记录的都是 "Assets/..." 路径；zip 若解到了工程外自定义目录，
                    // 记录的会是绝对路径，两者分别走 UnitypackageRemover（AssetDatabase）/ ZipRemover（文件系统）
                    var assetsRelativePaths = uncovered
                        .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var externalPaths = uncovered
                        .Where(p => !p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var combined = new PluginRemoveResult();
                    if (assetsRelativePaths.Count > 0)
                    {
                        combined.Merge(
                            UnitypackageRemover.RemoveByPathnames(assetsRelativePaths, refreshAssetDatabase: false));
                    }

                    if (externalPaths.Count > 0)
                    {
                        combined.Merge(ZipRemover.RemoveByPathnames(externalPaths));
                    }

                    // 只有真正失败/被跳过（如文件被占用、目录非空）的路径才需要继续追踪；
                    // 已成功删除（或本就不存在）的路径不再需要记录，避免不管删没删成功都无条件清空记录
                    remainingPaths = new List<string>(combined.FailedPaths);
                    remainingPaths.AddRange(combined.SkippedPaths);
                    if (remainingPaths.Count > 0)
                    {
                        LM.LogWarning(
                            $"[PluginPackageService] 插件 {pluginKey} 有 {remainingPaths.Count} 个资源未能移除，" +
                            $"保留追踪以便下次重试清理: {string.Join(", ", remainingPaths)}");
                    }
                }

                if (remainingPaths != null && remainingPaths.Count > 0)
                {
                    // 仍有资源残留在磁盘上，保留记录、维持"已安装"状态，不能当作已完全卸载
                    PluginAsset.Set(pluginKey, remainingPaths);
                    MTPackage.SetPlugin(mtPackage.PackageName, pluginKey, true);
                    continue;
                }

                PluginAsset.Remove(pluginKey);
                MTPackage.SetPlugin(mtPackage.PackageName, pluginKey, false);
            }

            MTPackage.SaveDependenciesToLocal();
            PluginAsset.SaveToLocal();
            LM.Log($"[PluginPackageService] RemoveImportedPlugins done. package:{mtPackage.PackageName}");
            return true;
        }

        /// <summary>
        /// 卸载确认弹窗：与旧工程一致，整包一次性列出全部将被删除的资源路径（过长时用 "..." 截断），
        /// 而不是像导入侧那样逐插件单独询问。
        /// </summary>
        private static bool ConfirmRemoveAll(MTPackageInfo mtPackage, List<string> allPathsToRemove)
        {
            const int maxShown = 15;
            var distinctPaths = allPathsToRemove.Distinct(StringComparer.Ordinal).ToList();
            string listText = distinctPaths.Count <= maxShown
                ? string.Join("\n", distinctPaths)
                : string.Join("\n", distinctPaths.Take(maxShown)) + $"\n...（共 {distinctPaths.Count} 条）";

            return EditorUtility.DisplayDialog(
                "卸载插件确认",
                $"即将移除 {mtPackage.PackageName} 关联的以下 {distinctPaths.Count} 个插件资源：\n\n{listText}\n\n是否继续？",
                "确定卸载",
                "取消");
        }

        public async Task<List<string>> ListRemotePluginFileNamesAsync(int projectId, string reference)
        {
            var result = new List<string>();
            if (projectId <= 0 || string.IsNullOrEmpty(reference))
            {
                return result;
            }

            var trees = await GitLab.GetTreesAsync(projectId, MTPPath.PLUGINS_PACKAGE_DIR, reference,
                recursive: true);
            if (trees == null)
            {
                return result;
            }

            foreach (var item in trees)
            {
                if (item == null || !string.Equals(item.Type, "blob", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = GetRelativePluginPath(item.Path, item.Name);
                if (string.IsNullOrEmpty(relative) || !IsPluginFileName(relative))
                {
                    continue;
                }

                if (!result.Exists(x => string.Equals(x, relative, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(relative);
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private void EnsurePackageTracked(MTPackageInfo mtPackage)
        {
            if (MTPackage.GetPackage(mtPackage.PackageName) == null)
            {
                MTPackage.AddDependency(mtPackage);
            }
        }

        private static string GetRelativePluginPath(string treePath, string treeName)
        {
            string path = string.IsNullOrEmpty(treePath) ? treeName : treePath;
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            path = path.Replace("\\", "/");
            string prefix = MTPPath.PLUGINS_PACKAGE_DIR.Replace("\\", "/").TrimEnd('/') + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(prefix.Length);
            }

            return path.TrimStart('/');
        }

        private static bool IsPluginFileName(string pathOrName)
        {
            return pathOrName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                   || pathOrName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPluginAssetRecorded(string pluginKey)
        {
            var paths = PluginAsset.Get(pluginKey);
            return paths != null && paths.Count > 0;
        }

        /// <summary>
        /// 解析插件本次会落盘的资源路径清单。.unitypackage 目标路径固定，与 parentDirectory 无关；
        /// .zip 按 parentDirectory 拼出最终路径（为空则按 "/Assets" 默认根目录计算）。
        /// </summary>
        private static List<string> AnalyzePluginAssetPaths(string pluginFilePath, string zipTargetDirectory = null)
        {
            string ext = Path.GetExtension(pluginFilePath);
            if (ext.Equals(".unitypackage", StringComparison.OrdinalIgnoreCase))
            {
                return UnitypackageAnalyzer.GetAssetPathnames(pluginFilePath);
            }

            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return ZipAnalyzer.GetAssetPathnames(pluginFilePath, zipTargetDirectory);
            }

            return new List<string>();
        }

        /// <summary>
        /// 导入单个插件文件。.unitypackage 走 AssetDatabase 导入；.zip 走 ZipImporter 自动解压
        /// （zipTargetDirectory 为空则解到 Assets/ 根目录，否则解到 PluginImportWindow 中用户本次选择的目录）。
        /// 本方法只做调用，具体解压/导入实现都在 Core 下。
        /// </summary>
        private static async Task<bool> ImportSinglePluginFileAsync(string pluginFilePath,
            string zipTargetDirectory = null)
        {
            string ext = Path.GetExtension(pluginFilePath);
            if (ext.Equals(".unitypackage", StringComparison.OrdinalIgnoreCase))
            {
                return await UnitypackageImporter.ImportAndWaitAsync(pluginFilePath, interactive: false);
            }

            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extracted = ZipImporter.ExtractToAssets(pluginFilePath, zipTargetDirectory);
                return extracted != null;
            }

            return false;
        }

        private static string GetRelativePluginNameFromKey(MTPackageInfo mtPackage, string pluginKey)
        {
            if (string.IsNullOrEmpty(pluginKey))
            {
                return string.Empty;
            }

            string normalized = pluginKey.Replace("\\", "/");
            string marker = MTPPath.PLUGINS_PACKAGE_DIR.Replace("\\", "/").TrimEnd('/') + "/";
            int index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return normalized.Substring(index + marker.Length);
            }

            return Path.GetFileName(pluginKey);
        }

        /// <summary>
        /// 按 Plugins Key 解析实际插件文件。优先 PackageCache / Packages 下的本地路径，再回退 Staging。
        /// </summary>
        private static string ResolvePluginFilePath(MTPackageInfo mtPackage, string pluginKey)
        {
            if (string.IsNullOrEmpty(pluginKey))
            {
                return string.Empty;
            }

            string normalized = PathUtils.NormalizePluginKey(pluginKey);
            if (File.Exists(normalized))
            {
                return normalized;
            }

            try
            {
                string fullPath = Path.GetFullPath(normalized);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch (Exception)
            {
                // 非法路径时继续尝试相对名回退
            }

            string relative = GetRelativePluginNameFromKey(mtPackage, pluginKey);
            string localPath = PathUtils.BuildPluginLocalPath(mtPackage, relative);
            if (!string.IsNullOrEmpty(localPath))
            {
                if (File.Exists(localPath))
                {
                    return localPath;
                }

                try
                {
                    string localFull = Path.GetFullPath(localPath);
                    if (File.Exists(localFull))
                    {
                        return localFull;
                    }
                }
                catch (Exception)
                {
                    // ignore
                }
            }

            string stagingPath = PathUtils.BuildPluginStagingPath(mtPackage, relative);
            return !string.IsNullOrEmpty(stagingPath) && File.Exists(stagingPath)
                ? stagingPath
                : string.Empty;
        }

        private static string TryGetZipExtractDirectory(
            IReadOnlyDictionary<string, string> zipExtractDirectories, string pluginKey)
        {
            if (zipExtractDirectories == null || string.IsNullOrEmpty(pluginKey))
            {
                return null;
            }

            if (zipExtractDirectories.TryGetValue(pluginKey, out var chosenDir)
                && !string.IsNullOrEmpty(chosenDir))
            {
                return chosenDir;
            }

            string normalized = PathUtils.NormalizePluginKey(pluginKey);
            if (zipExtractDirectories.TryGetValue(normalized, out chosenDir)
                && !string.IsNullOrEmpty(chosenDir))
            {
                return chosenDir;
            }

            foreach (var kvp in zipExtractDirectories)
            {
                if (string.Equals(PathUtils.NormalizePluginKey(kvp.Key), normalized, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(kvp.Value))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// 卸载时不能只查当前依赖树：SyncManifest 会先 Rebuild，被卸的包已经不在树上。
        /// 合并请求快照 Plugins、树上残留记录、以及 PluginAsset 里属于该包的 Key。
        /// </summary>
        private Dictionary<string, bool> CollectPluginKeysForPackage(MTPackageInfo mtPackage)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            void AddKey(string key, bool imported)
            {
                if (string.IsNullOrEmpty(key))
                {
                    return;
                }

                string normalized = PathUtils.NormalizePluginKey(key);
                if (result.TryGetValue(normalized, out var previous))
                {
                    result[normalized] = previous || imported;
                }
                else
                {
                    result[normalized] = imported;
                }
            }

            if (mtPackage?.Plugins != null)
            {
                foreach (var kvp in mtPackage.Plugins)
                {
                    AddKey(kvp.Key, kvp.Value);
                }
            }

            var tracked = MTPackage.GetPlugins(mtPackage?.PackageName);
            if (tracked != null)
            {
                foreach (var kvp in tracked)
                {
                    AddKey(kvp.Key, kvp.Value);
                }
            }

            var recorded = PluginAsset.GetAll();
            if (recorded != null)
            {
                foreach (var kvp in recorded)
                {
                    if (!IsPluginKeyForPackage(kvp.Key, mtPackage))
                    {
                        continue;
                    }

                    AddKey(kvp.Key, kvp.Value != null && kvp.Value.Count > 0);
                }
            }

            return result;
        }

        private bool IsPluginImported(Dictionary<string, bool> plugins, string pluginKey)
        {
            if (!string.IsNullOrEmpty(pluginKey) && plugins != null)
            {
                if (plugins.TryGetValue(pluginKey, out var imported) && imported)
                {
                    return true;
                }

                string resolved = ResolvePluginDictKey(plugins, pluginKey);
                if (!string.IsNullOrEmpty(resolved) && plugins.TryGetValue(resolved, out imported) && imported)
                {
                    return true;
                }
            }

            var assets = PluginAsset.Get(pluginKey);
            return assets != null && assets.Count > 0;
        }

        private static bool IsPluginKeyForPackage(string pluginKey, MTPackageInfo package)
        {
            if (string.IsNullOrEmpty(pluginKey) || package == null || string.IsNullOrEmpty(package.PackageName))
            {
                return false;
            }

            string key = PathUtils.NormalizePluginKey(pluginKey);
            string name = package.PackageName;
            string cacheRoot = MTPPath.PACKAGE_CACHE_ROOT.Replace("\\", "/").TrimEnd('/');

            if (key.IndexOf(cacheRoot + "/" + name + "@", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (key.IndexOf(cacheRoot + "/" + name + "/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string packagesSegment = "/Packages/" + name + "/";
            if (key.IndexOf(packagesSegment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return key.StartsWith("Packages/" + name + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolvePluginDictKey(Dictionary<string, bool> plugins, string pluginKey)
        {
            if (plugins == null || string.IsNullOrEmpty(pluginKey))
            {
                return string.Empty;
            }

            if (plugins.ContainsKey(pluginKey))
            {
                return pluginKey;
            }

            string normalized = PathUtils.NormalizePluginKey(pluginKey);
            foreach (var key in plugins.Keys)
            {
                if (string.Equals(PathUtils.NormalizePluginKey(key), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            return string.Empty;
        }
    }
}
