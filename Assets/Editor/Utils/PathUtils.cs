// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 提供解析MTP所需目录相关的内联方法
// ==================================================

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

            return Path.Combine(packageRoot, MTPPath.PLUGINS_PACKAGE_DIR, pluginFileName);
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
                    : Path.Combine(localRoot, MTPPath.PLUGINS_PACKAGE_DIR, pluginFileName);
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
