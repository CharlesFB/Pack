// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 提供解析MTP所需目录相关的内联方法
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;

namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class PathUtils
    {
        /// <summary>
        /// 取一个路径序列的文件名序列
        /// </summary>
        internal static List<string> GetFileNames(List<string> filePaths)
        {
            List<string> fileNames = new List<string>();
            foreach (string filePath in filePaths)
            {
                fileNames.Add(Path.GetFileName(filePath));
            }
            return fileNames;
        }
        
        /// <summary>
        /// 取 Revision 前 10 位作为 PackageCache 目录 short hash。
        /// </summary>
        internal static string GetShortHash(string revision)
        {
            if (string.IsNullOrEmpty(revision))
            {
                return string.Empty;
            }

            int length = 10; //UPM同款记录短哈希为commit的前10位
            return revision.Length <= length ? revision : revision.Substring(0, length);
        }

        /// <summary>
        /// UPM 全名 com.xxx.foo 取最后一段 foo；已是短名则原样返回。
        /// </summary>
        internal static string GetPackageShortName(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return packageName;
            }

            int lastDot = packageName.LastIndexOf('.');
            return lastDot >= 0 && lastDot < packageName.Length - 1
                ? packageName.Substring(lastDot + 1)
                : packageName;
        }

        /// <summary>
        /// ProjectInfo.PackageName（git 短名）是否对得上 MTPackageInfo / package.json 的 name。
        /// 以全名按 '.' 分割的最后一段为准。
        /// </summary>
        internal static bool MatchesProjectPackageName(string projectPackageName, string fullOrShortName)
        {
            if (string.IsNullOrEmpty(projectPackageName) || string.IsNullOrEmpty(fullOrShortName))
            {
                return false;
            }

            if (string.Equals(projectPackageName, fullOrShortName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(
                projectPackageName,
                GetPackageShortName(fullOrShortName),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 计算远端包在 PackageCache 下的根路径：
        /// PACKAGE_CACHE_ROOT/{packageName}@{shortHash}
        /// </summary>
        internal static string BuildPackageCacheRoot(string packageName, string revision)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return string.Empty;
            }

            string shortHash = GetShortHash(revision);
            if (string.IsNullOrEmpty(shortHash))
            {
                return Path.Combine(MTPPath.PACKAGE_CACHE_ROOT, packageName);
            }

            return Path.Combine(MTPPath.PACKAGE_CACHE_ROOT, $"{packageName}@{shortHash}");
        }

        /// <summary>
        /// 计算插件本地路径：
        /// PACKAGE_CACHE_ROOT/{packageName}@{shortHash}/PLUGINS_PACKAGE_DIR/{pluginFileName}
        /// </summary>
        internal static string BuildPluginLocalPath(string packageName, string revision, string pluginFileName)
        {
            if (string.IsNullOrEmpty(pluginFileName))
            {
                return string.Empty;
            }

            string packageRoot = BuildPackageCacheRoot(packageName, revision);
            if (string.IsNullOrEmpty(packageRoot))
            {
                return string.Empty;
            }

            return NormalizePluginKey(Path.Combine(packageRoot, MTPPath.PLUGINS_PACKAGE_DIR, pluginFileName));
        }

        /// <summary>
        /// 磁盘路径统一为正斜杠。仅用于文件系统路径，不是插件身份 Key。
        /// 身份 Key 用 MakePluginKey / TryParsePluginKey。
        /// </summary>
        internal static string NormalizePluginKey(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace("\\", "/");
        }

        internal const char PluginKeySeparator = '#';

        /// <summary>
        /// 插件身份 Key：{packageName}#{pluginFileName}。pluginFileName 只取文件名。
        /// </summary>
        internal static string MakePluginKey(string packageName, string pluginFileName)
        {
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(pluginFileName))
            {
                return string.Empty;
            }

            return packageName + PluginKeySeparator + Path.GetFileName(pluginFileName);
        }

        /// <summary>
        /// 按第一个 # 拆解插件身份 Key。
        /// </summary>
        internal static bool TryParsePluginKey(string pluginKey, out string packageName, out string pluginFileName)
        {
            packageName = null;
            pluginFileName = null;
            if (string.IsNullOrEmpty(pluginKey))
            {
                return false;
            }

            int separatorIndex = pluginKey.IndexOf(PluginKeySeparator);
            if (separatorIndex <= 0 || separatorIndex >= pluginKey.Length - 1)
            {
                return false;
            }

            packageName = pluginKey.Substring(0, separatorIndex);
            pluginFileName = Path.GetFileName(pluginKey.Substring(separatorIndex + 1));
            return !string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(pluginFileName);
        }

        internal static string GetPluginFileNameFromKey(string pluginKey)
        {
            return TryParsePluginKey(pluginKey, out _, out var fileName) ? fileName : string.Empty;
        }

        internal static bool PluginKeysEqual(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsPluginFileName(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName))
            {
                return false;
            }

            string fileName = Path.GetFileName(pathOrName);
            return fileName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                   || fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 列出 Editor/PluginsPackage 当前目录下的 .zip / .unitypackage（不递归）。
        /// </summary>
        internal static List<string> ListPluginPackageFileNames(string pluginDirectory)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(pluginDirectory) || !Directory.Exists(pluginDirectory))
            {
                return names;
            }

            foreach (var pattern in new[] { "*.unitypackage", "*.zip" })
            {
                foreach (var file in Directory.GetFiles(pluginDirectory, pattern, SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(name) || !IsPluginFileName(name))
                    {
                        continue;
                    }

                    if (!names.Exists(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        internal static bool IsPluginKeyRecorded(string pluginKey, IReadOnlyDictionary<string, List<string>> recorded)
        {
            if (recorded == null || string.IsNullOrEmpty(pluginKey))
            {
                return false;
            }

            foreach (var kvp in recorded)
            {
                if (!PluginKeysEqual(kvp.Key, pluginKey))
                {
                    continue;
                }

                return kvp.Value != null && kvp.Value.Count > 0;
            }

            return false;
        }

        /// <summary>
        /// 计算插件暂存路径（UPM 安装前下载/解析/导入用）：
        /// PLUGIN_STAGING_ROOT/{packageName}@{shortHash}/{pluginFileName}
        /// </summary>
        internal static string BuildPluginStagingPath(string packageName, string revision, string pluginFileName)
        {
            if (string.IsNullOrEmpty(pluginFileName))
            {
                return string.Empty;
            }

            string shortHash = GetShortHash(revision);
            string folderName = string.IsNullOrEmpty(shortHash)
                ? packageName
                : $"{packageName}@{shortHash}";

            if (string.IsNullOrEmpty(folderName))
            {
                return string.Empty;
            }

            return Path.Combine(MTPPath.PLUGIN_STAGING_ROOT, folderName, pluginFileName);
        }

        /// <summary>
        /// 按 MTPackageInfo 计算插件暂存路径。
        /// </summary>
        internal static string BuildPluginStagingPath(MTPackageInfo mtPackageInfo, string pluginFileName)
        {
            if (mtPackageInfo == null || string.IsNullOrEmpty(pluginFileName))
            {
                return string.Empty;
            }

            return BuildPluginStagingPath(mtPackageInfo.PackageName, mtPackageInfo.Revision, pluginFileName);
        }

        /// <summary>
        /// 本地 file: 包根路径：Packages/{packageName}
        /// </summary>
        internal static string BuildLocalPackageRoot(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return string.Empty;
            }

            string projectPath = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            return projectPath != null
                ? Path.Combine(projectPath, "Packages", packageName)
                : Path.Combine("Packages", packageName);
        }

        /// <summary>
        /// 按 MTPackageInfo 计算插件本地路径（本地包走 Packages/，远端走 PackageCache 公式）。
        /// </summary>
        internal static string BuildPluginLocalPath(MTPackageInfo mtPackageInfo, string pluginFileName)
        {
            if (mtPackageInfo == null || string.IsNullOrEmpty(pluginFileName))
            {
                return string.Empty;
            }

            if (mtPackageInfo.isLocal)
            {
                string localRoot = BuildLocalPackageRoot(mtPackageInfo.PackageName);
                return string.IsNullOrEmpty(localRoot)
                    ? string.Empty
                    : NormalizePluginKey(Path.Combine(localRoot, MTPPath.PLUGINS_PACKAGE_DIR, pluginFileName));
            }

            return BuildPluginLocalPath(mtPackageInfo.PackageName, mtPackageInfo.Revision, pluginFileName);
        }

        /// <summary>
        /// 查找当前项目内某一个已安装包的包文件存放根路径（按目录修改时间取最新）。
        /// </summary>
        internal static string ResolvePackageCachePath(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return string.Empty;
            }

            if (!Directory.Exists(MTPPath.PACKAGE_CACHE_ROOT))
            {
                return string.Empty;
            }

            var candidatePaths = Directory.GetDirectories(MTPPath.PACKAGE_CACHE_ROOT, packageName + "@*");
            if (candidatePaths.Length == 0)
            {
                return string.Empty;
            }

            string latestPath = candidatePaths[0];
            long latestTicks = Directory.GetLastWriteTimeUtc(latestPath).Ticks;
            for (int i = 1; i < candidatePaths.Length; i++)
            {
                var ticks = Directory.GetLastWriteTimeUtc(candidatePaths[i]).Ticks;
                if (ticks > latestTicks)
                {
                    latestTicks = ticks;
                    latestPath = candidatePaths[i];
                }
            }

            return Path.GetFullPath(latestPath);
        }

        /// <summary>
        /// 解析包内约定插件目录；目录不存在时返回 null。
        /// </summary>
        internal static string GetPluginsDirectory(MTPackageInfo mtPackageInfo, string resolvedPath = null)
        {
            if (!string.IsNullOrEmpty(resolvedPath))
            {
                string directoryFromResolved = Path.Combine(resolvedPath, MTPPath.PLUGINS_PACKAGE_DIR);
                if (Directory.Exists(directoryFromResolved))
                {
                    return directoryFromResolved;
                }
            }

            if (mtPackageInfo == null)
            {
                return null;
            }

            string packageRoot;
            if (mtPackageInfo.isLocal)
            {
                packageRoot = BuildLocalPackageRoot(mtPackageInfo.PackageName);
            }
            else if (!string.IsNullOrEmpty(mtPackageInfo.Revision))
            {
                packageRoot = BuildPackageCacheRoot(mtPackageInfo.PackageName, mtPackageInfo.Revision);
            }
            else
            {
                packageRoot = ResolvePackageCachePath(mtPackageInfo.PackageName);
            }

            if (string.IsNullOrEmpty(packageRoot))
            {
                return null;
            }

            string pluginDirectory = Path.Combine(packageRoot, MTPPath.PLUGINS_PACKAGE_DIR);
            return Directory.Exists(pluginDirectory) ? pluginDirectory : null;
        }
    }
}
