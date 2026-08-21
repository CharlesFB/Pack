// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 按 ZipImporter 解压出的文件条目清单，从磁盘删除对应文件（及清理产生的空目录）
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 与 UnitypackageRemover 对应：UnitypackageRemover 走 AssetDatabase.DeleteAsset（限定 Assets/ 下），
    /// ZipRemover 走普通文件系统删除（解压目标目录可以是 Assets/ 之外的任意用户选择目录）。
    /// </summary>
    public static class ZipRemover
    {
        /// <summary>
        /// 按 ZipImporter.Extract 返回的绝对路径清单删除文件。
        /// </summary>
        /// <param name="pathnames">待删除的绝对路径清单（文件或目录均可）</param>
        /// <param name="rootDirectory">
        /// 解压时用户选择的父目录（即 ZipImporter 的 destinationDirectory）。
        /// 传入后会在删除文件后，向上清理因此产生的空目录，但不会删除 rootDirectory 本身，也不会越界向上删除；
        /// 不传则只删除清单里列出的文件，不做空目录清理。
        /// </param>
        public static PluginRemoveResult RemoveByPathnames(IEnumerable<string> pathnames, string rootDirectory = null)
        {
            var result = new PluginRemoveResult();
            if (pathnames == null)
            {
                return result;
            }

            string normalizedRoot = string.IsNullOrEmpty(rootDirectory)
                ? string.Empty
                : rootDirectory.Replace("\\", "/").TrimEnd('/');

            var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in pathnames)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                string path = raw.Replace("\\", "/").TrimEnd('/');
                RemoveSinglePath(path, result, touchedDirectories);
            }

            if (!string.IsNullOrEmpty(normalizedRoot))
            {
                CleanupEmptyDirectories(touchedDirectories, normalizedRoot, result);
            }

            return result;
        }

        private static void RemoveSinglePath(string path, PluginRemoveResult result, HashSet<string> touchedDirectories)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    result.RemovedCount++;
                    AddParentDirectory(path, touchedDirectories);
                    return;
                }

                if (Directory.Exists(path))
                {
                    if (!IsDirectoryEmpty(path))
                    {
                        result.SkippedCount++;
                        result.SkippedPaths.Add(path);
                        Debug.LogWarning($"[ZipRemover] Skip non-empty directory: {path}");
                        return;
                    }

                    Directory.Delete(path);
                    result.RemovedCount++;
                    AddParentDirectory(path, touchedDirectories);
                    return;
                }

                // 文件/目录本就不存在，视为已删除，不计数、不报错
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZipRemover] 删除失败: {path}\n{ex.Message}");
                result.FailedPaths.Add(path);
            }
        }

        private static void AddParentDirectory(string path, HashSet<string> touchedDirectories)
        {
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(parent))
            {
                touchedDirectories.Add(parent);
            }
        }

        private static void CleanupEmptyDirectories(HashSet<string> startDirectories, string rootDirectory,
            PluginRemoveResult result)
        {
            var ordered = new List<string>(startDirectories);
            // 先处理路径更长（更深层）的目录，逐层向上清理
            ordered.Sort((a, b) => b.Length.CompareTo(a.Length));

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in ordered)
            {
                CleanupUpwards(dir, rootDirectory, visited, result);
            }
        }

        private static void CleanupUpwards(string directory, string rootDirectory, HashSet<string> visited,
            PluginRemoveResult result)
        {
            string current = directory;
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                bool isRoot = string.Equals(current, rootDirectory, StringComparison.OrdinalIgnoreCase);
                bool insideRoot = current.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase);
                if (isRoot || !insideRoot)
                {
                    // 不删 rootDirectory 本身，也不允许越界向上删除
                    break;
                }

                if (!Directory.Exists(current) || !IsDirectoryEmpty(current))
                {
                    break;
                }

                try
                {
                    Directory.Delete(current);
                    Debug.Log($"[ZipRemover] Removed empty directory: {current}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ZipRemover] 清理空目录失败: {current}\n{ex.Message}");
                    result.FailedPaths.Add(current);
                    break;
                }

                current = Path.GetDirectoryName(current)?.Replace("\\", "/");
            }
        }

        private static bool IsDirectoryEmpty(string path)
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
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
    }
}
