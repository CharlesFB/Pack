// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: .zip 解压（目标目录由用户手动选择），返回实际写盘的文件条目清单
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 核心解压能力：仅负责把 zip 内容写到磁盘，不处理持久化记录。
    /// 与 UnitypackageImporter 不同，zip 不走 AssetDatabase.ImportPackage，
    /// 目标目录也不固定为 Assets/，而是由调用方/用户显式指定。
    /// </summary>
    public static class ZipImporter
    {
        /// <summary>
        /// 弹出文件夹选择框，让用户手动选择解压的父目录，再解压到该目录下。
        /// </summary>
        /// <param name="zipPath">.zip 绝对路径</param>
        /// <param name="dialogTitle">选择框标题</param>
        /// <param name="defaultDirectory">选择框默认打开的目录</param>
        /// <returns>
        /// 实际写盘的文件条目绝对路径列表（= 用户选择的父目录 拼接 zip 内相对路径）；
        /// 用户取消选择或解压失败时返回 null。
        /// </returns>
        public static List<string> ExtractWithUserSelectedDirectory(string zipPath, string dialogTitle = "选择解压目录",
            string defaultDirectory = "")
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                Debug.LogError($"[ZipImporter] 找不到文件: {zipPath}");
                return null;
            }

            string selectedDirectory = EditorUtility.OpenFolderPanel(dialogTitle, defaultDirectory, string.Empty);
            if (string.IsNullOrEmpty(selectedDirectory))
            {
                Debug.Log("[ZipImporter] 用户取消选择解压目录，跳过解压");
                return null;
            }

            return Extract(zipPath, selectedDirectory);
        }

        /// <summary>
        /// 自动解压：不弹窗，默认解到 Assets/ 根目录；传入 parentDirectory 时改解到该目录下
        /// （用于插件导入流程——默认自动解压，也支持在导入前由用户在 PluginImportWindow 里手动指定目标目录）。
        /// </summary>
        /// <param name="zipPath">.zip 绝对路径</param>
        /// <param name="parentDirectory">解压的目标父目录；为空时默认使用 Application.dataPath（Assets/ 根目录）</param>
        /// <returns>实际写盘的文件条目绝对路径列表</returns>
        public static List<string> ExtractToAssets(string zipPath, string parentDirectory = null)
        {
            string destination = string.IsNullOrEmpty(parentDirectory) ? Application.dataPath : parentDirectory;
            return Extract(zipPath, destination);
        }

        /// <summary>
        /// 将 zip 解压到指定目录（目录不存在会自动创建）。
        /// </summary>
        /// <param name="zipPath">.zip 绝对路径</param>
        /// <param name="destinationDirectory">解压的目标父目录（用户选择的目录）</param>
        /// <returns>实际写盘的文件条目绝对路径列表（= destinationDirectory 拼接 zip 内相对路径），解压失败返回空列表</returns>
        public static List<string> Extract(string zipPath, string destinationDirectory)
        {
            var extractedPaths = new List<string>();
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                Debug.LogError($"[ZipImporter] 找不到文件: {zipPath}");
                return extractedPaths;
            }

            if (string.IsNullOrEmpty(destinationDirectory))
            {
                Debug.LogError("[ZipImporter] 目标目录为空");
                return extractedPaths;
            }

            string normalizedRoot = destinationDirectory.Replace("\\", "/").TrimEnd('/');

            try
            {
                Directory.CreateDirectory(normalizedRoot);

                using (var fs = File.OpenRead(zipPath))
                using (var zipFile = new ZipFile(fs))
                {
                    foreach (ZipEntry entry in zipFile)
                    {
                        if (entry == null)
                        {
                            continue;
                        }

                        string relativePath = NormalizeEntryRelativePath(entry.Name);
                        if (string.IsNullOrEmpty(relativePath))
                        {
                            continue;
                        }

                        string targetPath = Path.Combine(normalizedRoot, relativePath).Replace("\\", "/");

                        if (entry.IsDirectory)
                        {
                            Directory.CreateDirectory(targetPath);
                            continue;
                        }

                        string targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        using (var entryStream = zipFile.GetInputStream(entry))
                        using (var outStream = File.Create(targetPath))
                        {
                            entryStream.CopyTo(outStream);
                        }

                        extractedPaths.Add(targetPath);
                    }
                }

                Debug.Log(
                    $"[ZipImporter] Extract: {Path.GetFileName(zipPath)} -> {normalizedRoot} ({extractedPaths.Count} files)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZipImporter] 解压失败: {zipPath} -> {normalizedRoot}, {ex.Message}");
            }

            return extractedPaths;
        }

        /// <summary>
        /// 将 zip 内相对路径规范为不带开头 "/" 或 "./" 的相对路径（不强加 Assets/ 前缀）。
        /// </summary>
        private static string NormalizeEntryRelativePath(string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
            {
                return string.Empty;
            }

            string path = entryName.Replace("\\", "/").Trim().TrimStart('/');
            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path.Substring(2);
            }

            return path.TrimEnd('/');
        }
    }
}
