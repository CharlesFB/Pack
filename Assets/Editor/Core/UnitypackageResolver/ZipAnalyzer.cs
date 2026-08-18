// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 解析 .zip 插件包内的资源路径列表（按目标解压父目录拼出最终落盘路径）
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    public static class ZipAnalyzer
    {
        /// <summary>
        /// 解析 zip 内全部文件条目路径，按 parentDirectory 拼出最终持久化路径：
        /// - parentDirectory 为空：视为解到 Assets/ 根目录，返回 "Assets/..." 工程相对路径；
        /// - parentDirectory 指向 Assets/ 或其子目录：同样转换为 "Assets/..." 工程相对路径；
        /// - parentDirectory 指向工程外任意目录：返回绝对路径（= parentDirectory 拼接 zip 内相对路径）。
        /// 并补充缺失的父级目录路径（不越过 Assets 或 parentDirectory 本身）。
        /// </summary>
        public static List<string> GetAssetPathnames(string zipPath, string parentDirectory = null)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                Debug.LogError($"[ZipAnalyzer] 找不到文件: {zipPath}");
                return result;
            }

            bool underAssets = TryResolveAssetsRelativePrefix(parentDirectory, out string prefix, out string absoluteRoot);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using (var fs = File.OpenRead(zipPath))
                using (var zipFile = new ZipFile(fs))
                {
                    foreach (ZipEntry entry in zipFile)
                    {
                        if (entry == null || !entry.IsFile)
                        {
                            continue;
                        }

                        string relative = NormalizeEntryRelativePath(entry.Name);
                        if (string.IsNullOrEmpty(relative))
                        {
                            continue;
                        }

                        string pathname = underAssets ? prefix + "/" + relative : absoluteRoot + "/" + relative;
                        if (!seen.Add(pathname))
                        {
                            continue;
                        }

                        result.Add(pathname);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZipAnalyzer] 解析失败: {zipPath}, {ex.Message}");
            }

            string stopAtRoot = underAssets ? "Assets" : absoluteRoot;
            return FixMissingDirectories(result, stopAtRoot);
        }

        /// <summary>
        /// 判断 parentDirectory 是否落在 Assets/ 根目录（或其子目录）下：
        /// - 是：prefix 输出对应的 "Assets"（或 "Assets/子目录"）工程相对前缀；
        /// - 否：absoluteRoot 输出规范化后的绝对目录，供拼接绝对路径使用。
        /// </summary>
        private static bool TryResolveAssetsRelativePrefix(string parentDirectory, out string prefix,
            out string absoluteRoot)
        {
            string assetsRoot = Application.dataPath.Replace("\\", "/").TrimEnd('/');
            string resolved = string.IsNullOrEmpty(parentDirectory)
                ? assetsRoot
                : parentDirectory.Replace("\\", "/").TrimEnd('/');

            absoluteRoot = resolved;

            if (string.Equals(resolved, assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                prefix = "Assets";
                return true;
            }

            if (resolved.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "Assets" + resolved.Substring(assetsRoot.Length);
                return true;
            }

            prefix = null;
            return false;
        }

        /// <summary>
        /// 补充 pathnames 列表中可能缺失的父级目录，直到 stopAtRoot（含）为止（不含 stopAtRoot 本身）。
        /// </summary>
        public static List<string> FixMissingDirectories(List<string> pathnames, string stopAtRoot = "Assets")
        {
            if (pathnames == null || pathnames.Count == 0)
            {
                return pathnames ?? new List<string>();
            }

            string normalizedStop = string.IsNullOrEmpty(stopAtRoot)
                ? "Assets"
                : stopAtRoot.Replace("\\", "/").TrimEnd('/');

            pathnames.Sort(StringComparer.Ordinal);
            var set = new HashSet<string>(StringComparer.Ordinal);
            int len = pathnames.Count;
            for (int i = 0; i < len; ++i)
            {
                if (string.IsNullOrEmpty(pathnames[i]))
                {
                    continue;
                }

                string cur = pathnames[i].Replace("\\", "/").Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(cur))
                {
                    continue;
                }

                pathnames[i] = cur;
                set.Add(cur);

                int lastIndex = cur.LastIndexOf('/');
                string parent = lastIndex > 0 ? cur.Substring(0, lastIndex) : string.Empty;
                while (!string.IsNullOrEmpty(parent)
                       && !string.Equals(parent, normalizedStop, StringComparison.OrdinalIgnoreCase)
                       && !set.Contains(parent))
                {
                    pathnames.Add(parent);
                    set.Add(parent);
                    lastIndex = parent.LastIndexOf('/');
                    parent = lastIndex > 0 ? parent.Substring(0, lastIndex) : string.Empty;
                }
            }

            return pathnames;
        }

        /// <summary>
        /// 将 zip 内相对路径规范为不带开头 "/" 或 "./" 的相对路径。
        /// </summary>
        internal static string NormalizeEntryRelativePath(string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
            {
                return string.Empty;
            }

            string path = entryName.Replace("\\", "/").Trim().TrimStart('/');
            if (string.IsNullOrEmpty(path) || path.EndsWith("/", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path.Substring(2);
            }

            return path;
        }
    }
}
