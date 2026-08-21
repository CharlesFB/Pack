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
        private IProjectInfoService ProjectInfo => ServiceLocator.Get<IProjectInfoService>();
        private IMTPackageInfoService MTPackage => ServiceLocator.Get<IMTPackageInfoService>();
        private IPluginAssetService PluginAsset => ServiceLocator.Get<IPluginAssetService>();

        public async Task<bool> PreparePluginsAsync(MTPackageInfo mtPackage, bool persist = true)
        {
            if (mtPackage == null || string.IsNullOrEmpty(mtPackage.PackageName))
            {
                LM.LogWarning("[PluginPackageService] PreparePluginsAsync: mtPackage 无效");
                return false;
            }

            EnsurePackageTracked(mtPackage);

            List<(string relativeName, string stagingPath, string sourcePathForAnalyze)> plugins;
            if (mtPackage.isLocal)
            {
                plugins = CollectLocalPlugins(mtPackage);
            }
            else
            {
                plugins = await DownloadRemotePluginsAsync(mtPackage);
                if (plugins == null)
                {
                    return false;
                }
            }

            var existingPlugins = MTPackage.GetPlugins(mtPackage.PackageName)
                                ?? new Dictionary<string, bool>();

            int indexed = 0;
            foreach (var item in plugins)
            {
                string pluginKey = PathUtils.BuildPluginLocalPath(mtPackage, item.relativeName);
                if (string.IsNullOrEmpty(pluginKey))
                {
                    continue;
                }

                string analyzePath = !string.IsNullOrEmpty(item.stagingPath) && File.Exists(item.stagingPath)
                    ? item.stagingPath
                    : item.sourcePathForAnalyze;

                if (string.IsNullOrEmpty(analyzePath) || !File.Exists(analyzePath))
                {
                    LM.LogWarning($"[PluginPackageService] 插件文件不存在，跳过解析: {analyzePath}");
                    continue;
                }

                List<string> assetPaths = AnalyzePluginAssetPaths(analyzePath);
                bool installed = existingPlugins.TryGetValue(pluginKey, out var prev) && prev;
                MTPackage.SetPlugin(mtPackage.PackageName, pluginKey, installed);
                PluginAsset.Set(pluginKey, assetPaths);
                indexed++;
            }

            if (persist)
            {
                MTPackage.SaveDependenciesToLocal();
                PluginAsset.SaveToLocal();
            }

            LM.Log(
                $"[PluginPackageService] PreparePlugins done. package:{mtPackage.PackageName}, indexed:{indexed}");
            return true;
        }

        public bool ImportPreparedPlugins(MTPackageInfo mtPackage,
            IEnumerable<string> selectedPluginLocalPaths = null,
            IReadOnlyDictionary<string, string> zipExtractDirectories = null, bool persist = true)
        {
            if (mtPackage == null || string.IsNullOrEmpty(mtPackage.PackageName))
            {
                LM.LogWarning("[PluginPackageService] ImportPreparedPlugins: mtPackage 无效");
                return false;
            }

            EnsurePackageTracked(mtPackage);

            var plugins = MTPackage.GetPlugins(mtPackage.PackageName);
            if (plugins == null || plugins.Count == 0)
            {
                LM.Log($"[PluginPackageService] ImportPreparedPlugins: 无插件记录 {mtPackage.PackageName}");
                return true;
            }

            HashSet<string> selected = null;
            if (selectedPluginLocalPaths != null)
            {
                selected = new HashSet<string>(selectedPluginLocalPaths, StringComparer.Ordinal);
                if (selected.Count == 0)
                {
                    return true;
                }
            }

            int imported = 0;
            var pluginKeys = new List<string>(plugins.Keys);
            foreach (var pluginKey in pluginKeys)
            {
                if (selected != null && !selected.Contains(pluginKey))
                {
                    continue;
                }

                string relativeName = GetRelativePluginNameFromKey(mtPackage, pluginKey);
                string stagingPath = PathUtils.BuildPluginStagingPath(mtPackage, relativeName);
                if (string.IsNullOrEmpty(stagingPath) || !File.Exists(stagingPath))
                {
                    // 本地包可回退到 Packages 下源文件
                    if (mtPackage.isLocal)
                    {
                        stagingPath = PathUtils.BuildPluginLocalPath(mtPackage, relativeName);
                    }
                }

                if (string.IsNullOrEmpty(stagingPath) || !File.Exists(stagingPath))
                {
                    LM.LogWarning($"[PluginPackageService] Import 找不到 Staging 文件: {pluginKey}");
                    continue;
                }

                bool isZip = Path.GetExtension(stagingPath).Equals(".zip", StringComparison.OrdinalIgnoreCase);

                // zip 未选择目录 / 选择了空目录时，一律按 "/Assets" 默认解压；该选择只作用于本次导入，不做持久化
                string zipTargetDirectory = null;
                if (isZip && zipExtractDirectories != null
                    && zipExtractDirectories.TryGetValue(pluginKey, out var chosenDir)
                    && !string.IsNullOrEmpty(chosenDir))
                {
                    zipTargetDirectory = chosenDir;
                }

                // 无论 .unitypackage 还是 .zip，都统一按“插件”处理：先算出本次会落盘的资源清单（zip 已拼接目标父目录），
                // GetCoveredList 检测是否会覆盖别的插件已记录的资源，逐插件单独弹窗确认
                List<string> plannedAssetPaths = AnalyzePluginAssetPaths(stagingPath, zipTargetDirectory);
                // 排除插件自身在 Prepare 阶段已记录的预分析结果，避免误判为跟自己冲突
                var covered = PluginAsset.GetCoveredList(plannedAssetPaths, pluginKey);
                if (covered != null && covered.Count > 0 && !ConfirmOverwrite(pluginKey, covered))
                {
                    LM.Log($"[PluginPackageService] 用户取消覆盖，跳过插件: {pluginKey}");
                    continue;
                }

                if (!ImportSinglePluginFile(stagingPath, zipTargetDirectory))
                {
                    continue;
                }

                // zip 可能多次解压到不同目录，用 Append 合并去重，保留历史落盘路径供卸载时一并清理；
                // unitypackage 目标路径固定，Append 结果等价于 Set
                PluginAsset.Append(pluginKey, plannedAssetPaths);
                MTPackage.SetPlugin(mtPackage.PackageName, pluginKey, true);
                imported++;
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

            var plugins = MTPackage.GetPlugins(mtPackage.PackageName);
            if (plugins == null || plugins.Count == 0)
            {
                return true;
            }

            var keys = new List<string>(plugins.Keys);

            // 先汇总本次会真正删除的全部资源路径（仅统计已安装、且未被其它插件共享的部分），
            // 用一次弹窗整体展示给用户确认，而不是像导入那样逐插件分别询问
            var uncoveredByKey = new Dictionary<string, List<string>>();
            var allPathsToRemove = new List<string>();
            foreach (var pluginKey in keys)
            {
                bool wasInstalled = plugins.TryGetValue(pluginKey, out var installed) && installed;
                if (!wasInstalled)
                {
                    continue;
                }

                var uncovered = PluginAsset.GetUncoveredListToRemove(pluginKey) ?? new List<string>();
                uncoveredByKey[pluginKey] = uncovered;
                allPathsToRemove.AddRange(uncovered);
            }

            if (showConfirmDialog && allPathsToRemove.Count > 0 && !ConfirmRemoveAll(mtPackage, allPathsToRemove))
            {
                LM.Log($"[PluginPackageService] 用户取消卸载插件，未做任何改动: {mtPackage.PackageName}");
                return false;
            }

            foreach (var pluginKey in keys)
            {
                bool wasInstalled = plugins.TryGetValue(pluginKey, out var installed) && installed;
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

        private async Task<List<(string relativeName, string stagingPath, string sourcePathForAnalyze)>>
            DownloadRemotePluginsAsync(MTPackageInfo mtPackage)
        {
            var result = new List<(string, string, string)>();
            var project = ResolveProjectInfo(mtPackage);
            if (project == null || project.ProjectId <= 0)
            {
                LM.LogError(
                    $"[PluginPackageService] 找不到 ProjectInfo: package={mtPackage.PackageName}");
                return null;
            }

            string reference = ResolveReference(mtPackage);
            if (string.IsNullOrEmpty(reference))
            {
                LM.LogError(
                    $"[PluginPackageService] 缺少 Revision/Tag: package={mtPackage.PackageName}");
                return null;
            }

            var trees = await GitLab.GetTreesAsync(project.ProjectId, MTPPath.PLUGINS_PACKAGE_DIR, reference,
                recursive: true);
            if (trees == null)
            {
                // 目录不存在视为无插件
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

                string remotePath = string.IsNullOrEmpty(item.Path)
                    ? Path.Combine(MTPPath.PLUGINS_PACKAGE_DIR, relative).Replace("\\", "/")
                    : item.Path.Replace("\\", "/");

                string stagingPath = PathUtils.BuildPluginStagingPath(mtPackage, relative);
                bool ok = await GitLab.DownloadFileAsync(project.ProjectId, remotePath, stagingPath, reference);
                if (!ok)
                {
                    LM.LogWarning(
                        $"[PluginPackageService] 下载插件失败: {remotePath}@{reference}");
                    continue;
                }

                result.Add((relative, stagingPath, stagingPath));
            }

            return result;
        }

        private static List<(string relativeName, string stagingPath, string sourcePathForAnalyze)>
            CollectLocalPlugins(MTPackageInfo mtPackage)
        {
            var result = new List<(string, string, string)>();
            string pluginDir = Path.Combine(PathUtils.BuildLocalPackageRoot(mtPackage.PackageName),
                MTPPath.PLUGINS_PACKAGE_DIR);
            if (!Directory.Exists(pluginDir))
            {
                return result;
            }

            var files = new List<string>();
            files.AddRange(Directory.GetFiles(pluginDir, "*.unitypackage", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(pluginDir, "*.zip", SearchOption.AllDirectories));

            foreach (var file in files)
            {
                string relative = file.Substring(pluginDir.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace("\\", "/");
                if (string.IsNullOrEmpty(relative))
                {
                    continue;
                }

                string stagingPath = PathUtils.BuildPluginStagingPath(mtPackage, relative);
                try
                {
                    string stagingDir = Path.GetDirectoryName(stagingPath);
                    if (!string.IsNullOrEmpty(stagingDir))
                    {
                        Directory.CreateDirectory(stagingDir);
                    }

                    File.Copy(file, stagingPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    LM.LogWarning(
                        $"[PluginPackageService] 复制本地插件到 Staging 失败: {file}\n{ex.Message}");
                    result.Add((relative, string.Empty, file));
                    continue;
                }

                result.Add((relative, stagingPath, stagingPath));
            }

            return result;
        }

        private void EnsurePackageTracked(MTPackageInfo mtPackage)
        {
            if (MTPackage.GetPackage(mtPackage.PackageName) == null)
            {
                MTPackage.AddDependency(mtPackage);
            }
        }

        private ProjectInfo ResolveProjectInfo(MTPackageInfo mtPackage)
        {
            if (mtPackage.ProjectId > 0)
            {
                var byId = ProjectInfo.Get(mtPackage.ProjectId);
                if (byId != null)
                {
                    return byId;
                }
            }

            var byName = ProjectInfo.Get(mtPackage.PackageName);
            if (byName != null)
            {
                return byName;
            }

            if (!string.IsNullOrEmpty(mtPackage.Url))
            {
                return ProjectInfo.GetByUrl(mtPackage.Url);
            }

            return null;
        }

        private static string ResolveReference(MTPackageInfo mtPackage)
        {
            if (!string.IsNullOrEmpty(mtPackage.Revision))
            {
                return mtPackage.Revision;
            }

            return mtPackage.Tag;
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
        private static bool ImportSinglePluginFile(string pluginFilePath, string zipTargetDirectory = null)
        {
            string ext = Path.GetExtension(pluginFilePath);
            if (ext.Equals(".unitypackage", StringComparison.OrdinalIgnoreCase))
            {
                return UnitypackageImporter.Import(pluginFilePath, interactive: false);
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
    }
}
