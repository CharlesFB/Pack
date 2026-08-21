// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 按路径清单 / unitypackage 文件移除已导入的 Assets 资源
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    public static class UnitypackageRemover
    {
        /// <summary>
        /// 按资源路径清单删除 Assets。
        /// </summary>
        public static PluginRemoveResult RemoveByPathnames(IEnumerable<string> pathnames, bool refreshAssetDatabase = true)
        {
            var result = new PluginRemoveResult();
            if (pathnames == null)
            {
                return result;
            }

            var normalizedPaths = pathnames
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(NormalizeProjectPath)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .ToList();

            // 逐个 DeleteAsset 都会单独触发 AssetDatabase 导入检查；
            // 用 StartAssetEditing/StopAssetEditing 把整批删除合并成一次处理
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string pathname in normalizedPaths)
                {
                    RemoveSinglePathname(pathname, result);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (refreshAssetDatabase && result.RemovedCount > 0)
            {
                AssetDatabase.Refresh();
            }

            return result;
        }

        /// <summary>
        /// 按 .unitypackage 文件解析清单后删除对应 Assets。
        /// </summary>
        public static PluginRemoveResult RemoveByUnityPackageFile(string packagePath, bool refreshAssetDatabase = true)
        {
            if (string.IsNullOrEmpty(packagePath))
            {
                return new PluginRemoveResult();
            }

            var infos = UnitypackageAnalyzer.GetPackageInfos(packagePath, false);
            return RemoveByPackageEntries(infos, refreshAssetDatabase);
        }

        /// <summary>
        /// 批量按 .unitypackage 文件列表解析并删除对应 Assets（合并为一次 AssetEditing）。
        /// </summary>
        public static PluginRemoveResult RemoveByUnityPackageFiles(IEnumerable<string> packagePaths,
            bool refreshAssetDatabase = true)
        {
            var result = new PluginRemoveResult();
            if (packagePaths == null)
            {
                return result;
            }

            var allEntries = new List<UnitypackageAssetInfo>();
            foreach (string packagePath in packagePaths)
            {
                if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
                {
                    Debug.LogWarning($"[UnitypackageRemover] unitypackage missing, skip: {packagePath}");
                    continue;
                }

                allEntries.AddRange(UnitypackageAnalyzer.GetPackageInfos(packagePath, false));
            }

            return RemoveByPackageEntries(allEntries, refreshAssetDatabase);
        }

        /// <summary>
        /// 按已解析的资源条目删除 Assets。
        /// </summary>
        public static PluginRemoveResult RemoveByPackageEntries(List<UnitypackageAssetInfo> infos,
            bool refreshAssetDatabase = true)
        {
            var result = new PluginRemoveResult();
            if (infos == null || infos.Count == 0)
            {
                return result;
            }

            infos.Sort((a, b) => string.CompareOrdinal(b.pathname, a.pathname));

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var info in infos)
                {
                    RemovePackageEntry(info, result);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (refreshAssetDatabase && result.RemovedCount > 0)
            {
                AssetDatabase.Refresh();
            }

            return result;
        }

        private static void RemovePackageEntry(UnitypackageAssetInfo info, PluginRemoveResult result)
        {
            if (info == null || string.IsNullOrEmpty(info.pathname))
            {
                return;
            }

            string pathname = NormalizeProjectPath(info.pathname);
            if (!pathname.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                if (pathname.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"[UnitypackageRemover] Skip Packages path during cleanup: {pathname}");
                }

                result.SkippedCount++;
                result.SkippedPaths.Add(pathname);
                return;
            }

            string absolutePath = info.AbsolutePath;
            if (info.isDirectory)
            {
                if (!Directory.Exists(absolutePath))
                {
                    return;
                }

                if (IsDirectoryVirtuallyEmpty(absolutePath))
                {
                    DeleteAssetSafe(pathname, result);
                }
                else
                {
                    result.SkippedCount++;
                    result.SkippedPaths.Add(pathname);
                    Debug.LogWarning($"[UnitypackageRemover] Skip non-empty directory: {pathname}");
                }

                return;
            }

            if (!File.Exists(absolutePath))
            {
                return;
            }

            DeleteAssetSafe(pathname, result);
        }

        private static void RemoveSinglePathname(string pathname, PluginRemoveResult result)
        {
            if (!pathname.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                result.SkippedCount++;
                result.SkippedPaths.Add(pathname);
                return;
            }

            string absolutePath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, pathname)
                .Replace("\\", "/");
            if (Directory.Exists(absolutePath))
            {
                if (IsDirectoryVirtuallyEmpty(absolutePath))
                {
                    DeleteAssetSafe(pathname, result);
                }
                else
                {
                    result.SkippedCount++;
                    result.SkippedPaths.Add(pathname);
                    Debug.LogWarning($"[UnitypackageRemover] Skip non-empty directory: {pathname}");
                }

                return;
            }

            if (!File.Exists(absolutePath))
            {
                return;
            }

            DeleteAssetSafe(pathname, result);
        }

        public static bool IsDirectoryVirtuallyEmpty(string absolutePath)
        {
            if (!Directory.Exists(absolutePath))
            {
                return true;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(absolutePath))
            {
                string fileName = Path.GetFileName(entry);
                if (fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        public static void DeleteAssetSafe(string projectRelativePath, PluginRemoveResult result = null)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return;
            }

            projectRelativePath = NormalizeProjectPath(projectRelativePath);
            if (!projectRelativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[UnitypackageRemover] Skip non-Assets path: {projectRelativePath}");
                result?.SkippedPaths.Add(projectRelativePath);
                return;
            }

            try
            {
                bool success = AssetDatabase.DeleteAsset(projectRelativePath);
                if (success)
                {
                    Debug.Log($"[UnitypackageRemover] Removed: {projectRelativePath}");
                    if (result != null)
                    {
                        result.RemovedCount++;
                    }
                }
                else
                {
                    Debug.LogWarning($"[UnitypackageRemover] Failed to remove: {projectRelativePath}");
                    result?.FailedPaths.Add(projectRelativePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnitypackageRemover] Exception removing: {projectRelativePath}\n{ex.Message}");
                result?.FailedPaths.Add(projectRelativePath);
            }
        }

        private static string NormalizeProjectPath(string path)
        {
            return path.Replace("\\", "/").Trim().TrimEnd('/');
        }
    }
}
