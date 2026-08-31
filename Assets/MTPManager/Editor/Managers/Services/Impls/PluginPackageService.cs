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
using MagicTavern.FrontEngine.MTPManager.LitJson;
using UnityEditor;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PluginPackageService : IPluginPackageService
    {
        private IGitLabService GitLab => ServiceLocator.Get<IGitLabService>();
        private IMTPackageInfoService MTPackage => ServiceLocator.Get<IMTPackageInfoService>();
        private IPluginAssetService PluginAsset => ServiceLocator.Get<IPluginAssetService>();

        private readonly List<ProjectInfo> _extraPlugins = new List<ProjectInfo>();
        private DateTime _extraPluginsRefreshTime;

        public IReadOnlyList<ProjectInfo> ExtraPlugins => _extraPlugins;
        public DateTime ExtraPluginsRefreshTime => _extraPluginsRefreshTime;

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
        /// 只扫已安装包本地 Editor/PluginsPackage，写入 packageName#fileName Key；不写 PluginAsset。
        /// 已导入状态按 PluginAsset 的 Key 是否有资源清单判定。目录存在时清掉该包下已消失的 PluginAsset 记录。
        /// </summary>
        public void SyncPluginsFromDisk(MTPackageInfo mtPackage)
        {
            if (mtPackage == null)
            {
                return;
            }

#if UNITY_2020_2_OR_NEWER
            mtPackage.Plugins ??= new Dictionary<string, bool>();
#else
            if (mtPackage.Plugins == null)
            {
                mtPackage.Plugins = new Dictionary<string, bool>();
            }
#endif
            mtPackage.Plugins.Clear();

            string pluginDir = PathUtils.GetPluginsDirectory(mtPackage);
            if (string.IsNullOrEmpty(pluginDir) || !Directory.Exists(pluginDir))
            {
                return;
            }

            var fileNames = PathUtils.ListPluginPackageFileNames(pluginDir);
            var recorded = PluginAsset.GetAll() ?? new Dictionary<string, List<string>>();

            foreach (var fileName in fileNames)
            {
                string key = PathUtils.MakePluginKey(mtPackage.PackageName, fileName);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                bool imported = PathUtils.IsPluginKeyRecorded(key, recorded);
                mtPackage.Plugins[key] = imported;
                MTPackage.SetPlugin(mtPackage.PackageName, key, imported);
            }

            if (PluginAsset.PruneMissingPlugins(mtPackage.PackageName, fileNames) > 0)
            {
                PluginAsset.SaveToLocal();
            }
        }

        public async Task<bool> ImportPreparedPluginsAsync(MTPackageInfo mtPackage,
            IEnumerable<string> selectedPluginKeys = null,
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
            if (selectedPluginKeys != null)
            {
                selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in selectedPluginKeys)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        selected.Add(key);
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
                if (selected != null && !selected.Contains(pluginKey))
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

                if (!await ImportSinglePluginFileAsync(pluginFilePath, zipTargetDirectory))
                {
                    continue;
                }

                // zip 必须 Refresh 才会进 AssetDatabase；
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
            string fileName = PathUtils.GetPluginFileNameFromKey(pluginKey);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = pluginKey;
            }
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
                recursive: false);
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

                string fileName = Path.GetFileName(
                    !string.IsNullOrEmpty(item.Name) ? item.Name : item.Path);
                if (string.IsNullOrEmpty(fileName) || !PathUtils.IsPluginFileName(fileName))
                {
                    continue;
                }

                if (!result.Exists(x => string.Equals(x, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(fileName);
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public async Task<List<ProjectInfo>> PullExtraPluginsListAsync()
        {
            _extraPlugins.Clear();
            _extraPluginsRefreshTime = DateTime.Now;
            if (GitLab == null || !GitLab.EnsureClient())
            {
                LM.LogError("[PluginPackageService] PullExtraPluginsList: GitLab Client 不可用");
                return _extraPlugins;
            }

            var trees = await GitLab.GetTreesAsync(
                MTPGit.EXTRA_PLUGINS_PROJECT_ID,
                MTPGit.EXTRA_PLUGINS_DIR,
                MTPGit.EXTRA_PLUGINS_REF,
                recursive: true);
            if (trees == null || trees.Count == 0)
            {
                LM.LogWarning("[PluginPackageService] 2636 plugins/ 目录树为空");
                _extraPluginsRefreshTime = DateTime.Now;
                return _extraPlugins;
            }

            var pluginsDict = new Dictionary<string, ProjectInfo>(StringComparer.Ordinal);
            foreach (var item in trees)
            {
                if (item == null
                    || !string.Equals(item.Type, "blob", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(item.Path))
                {
                    continue;
                }

                string path = item.Path.Replace('\\', '/');
                if (path.IndexOf($"{MTPGit.EXTRA_PLUGINS_DIR}/-", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                if (!path.StartsWith($"{MTPGit.EXTRA_PLUGINS_DIR}/", StringComparison.Ordinal)
                    && !string.Equals(path, MTPGit.EXTRA_PLUGINS_DIR, StringComparison.Ordinal))
                {
                    path = $"{MTPGit.EXTRA_PLUGINS_DIR}/{path}";
                }

                var parts = path.Split('/');
                if (parts.Length < 2
                    || !string.Equals(parts[0], MTPGit.EXTRA_PLUGINS_DIR, StringComparison.Ordinal))
                {
                    continue;
                }

                string pluginName = parts[1];
                if (string.IsNullOrEmpty(pluginName) || pluginName.Contains(".DS_Store"))
                {
                    continue;
                }

                string version = parts.Length >= 3 && !parts[2].Contains(".DS_Store") ? parts[2] : string.Empty;
                string fileName = parts.Length >= 4
                                  && parts[3].EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                    ? parts[3]
                    : string.Empty;

                if (!pluginsDict.TryGetValue(pluginName, out var info))
                {
                    info = new ProjectInfo
                    {
                        PackageName = pluginName,
                        ProjectId = MTPGit.EXTRA_PLUGINS_PROJECT_ID,
                        PackageType = PackageType.ExPlugin,
                        Tags = new List<string>(),
                        PluginFileNames = new List<string>(),
                    };
                    pluginsDict.Add(pluginName, info);
                }

                if (!string.IsNullOrEmpty(version)
                    && !string.IsNullOrEmpty(fileName)
                    && !info.Tags.Contains(version))
                {
                    info.Tags.Add(version);
                    info.PluginFileNames.Add(fileName);
                }
            }

            foreach (var info in pluginsDict.Values)
            {
                info.Tags.Reverse();
                info.PluginFileNames.Reverse();
                _extraPlugins.Add(info);
            }

            _extraPluginsRefreshTime = DateTime.Now;
            LM.Log($"[PluginPackageService] PullExtraPluginsList done. count:{_extraPlugins.Count}");
            return _extraPlugins;
        }

        public string GetExtraPluginFileName(ProjectInfo project, string version)
        {
            if (project?.Tags == null || project.PluginFileNames == null || string.IsNullOrEmpty(version))
            {
                return string.Empty;
            }

            int idx = project.Tags.IndexOf(version);
            if (idx < 0 || idx >= project.PluginFileNames.Count)
            {
                return string.Empty;
            }

            return project.PluginFileNames[idx] ?? string.Empty;
        }

        public async Task<bool> ImportExtraPluginInteractiveAsync(ProjectInfo project, string version)
        {
            string fileName = GetExtraPluginFileName(project, version);
            if (project == null || string.IsNullOrEmpty(fileName))
            {
                LM.LogWarning("[PluginPackageService] ImportExtraPlugin: 当前版本没有 .unitypackage");
                return false;
            }

            if (GitLab == null || !GitLab.EnsureClient())
            {
                LM.LogError("[PluginPackageService] ImportExtraPlugin: GitLab Client 不可用");
                return false;
            }

            string gitPath = $"{MTPGit.EXTRA_PLUGINS_DIR}/{project.PackageName}/{version}/{fileName}";
            string destPath = Path.Combine(Path.GetTempPath(), fileName);
            bool downloaded = await GitLab.DownloadFileAsync(
                MTPGit.EXTRA_PLUGINS_PROJECT_ID, gitPath, destPath, MTPGit.EXTRA_PLUGINS_REF);
            if (!downloaded || !File.Exists(destPath))
            {
                LM.LogError($"[PluginPackageService] ImportExtraPlugin 下载失败: {gitPath}");
                return false;
            }

            AssetDatabase.ImportPackage(destPath, true);
            return true;
        }

        public async Task<PackageJson> GetExtraPluginPackageJsonAsync(ProjectInfo project, string version)
        {
            if (project == null || string.IsNullOrEmpty(project.PackageName)
                || GitLab == null || !GitLab.EnsureClient())
            {
                return null;
            }

            if (!string.IsNullOrEmpty(version))
            {
                var versioned = await TryReadExtraPluginPackageJsonAsync(
                    $"{MTPGit.EXTRA_PLUGINS_DIR}/{project.PackageName}/{version}/package.json");
                if (versioned != null)
                {
                    return versioned;
                }
            }

            return await TryReadExtraPluginPackageJsonAsync(
                $"{MTPGit.EXTRA_PLUGINS_DIR}/{project.PackageName}/package.json");
        }

        private async Task<PackageJson> TryReadExtraPluginPackageJsonAsync(string gitPath)
        {
            string text = await GitLab.GetFileTextAsync(
                MTPGit.EXTRA_PLUGINS_PROJECT_ID, gitPath, MTPGit.EXTRA_PLUGINS_REF, logOnFailure: false);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            try
            {
                return JsonMapper.ToObject<PackageJson>(text);
            }
            catch (Exception e)
            {
                LM.LogWarning($"[PluginPackageService] Extra Plugin package.json 解析失败: {gitPath}\n{e.Message}");
                return null;
            }
        }

        private void EnsurePackageTracked(MTPackageInfo mtPackage)
        {
            if (MTPackage.GetPackage(mtPackage.PackageName) == null)
            {
                MTPackage.AddDependency(mtPackage);
            }
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

        /// <summary>
        /// 按 pluginKey 拆出文件名，再拼本地路径（Packages/ 或 PackageCache）或 Staging。
        /// </summary>
        private static string ResolvePluginFilePath(MTPackageInfo mtPackage, string pluginKey)
        {
            if (mtPackage == null || string.IsNullOrEmpty(pluginKey))
            {
                return string.Empty;
            }

            string fileName = PathUtils.GetPluginFileNameFromKey(pluginKey);
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            string localPath = PathUtils.BuildPluginLocalPath(mtPackage, fileName);
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

            string stagingPath = PathUtils.BuildPluginStagingPath(mtPackage, fileName);
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

            foreach (var kvp in zipExtractDirectories)
            {
                if (PathUtils.PluginKeysEqual(kvp.Key, pluginKey) && !string.IsNullOrEmpty(kvp.Value))
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

                if (result.TryGetValue(key, out var previous))
                {
                    result[key] = previous || imported;
                }
                else
                {
                    result[key] = imported;
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

            return PathUtils.TryParsePluginKey(pluginKey, out var packageName, out _)
                   && string.Equals(packageName, package.PackageName, StringComparison.OrdinalIgnoreCase);
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

            foreach (var key in plugins.Keys)
            {
                if (PathUtils.PluginKeysEqual(key, pluginKey))
                {
                    return key;
                }
            }

            return string.Empty;
        }
    }
}
