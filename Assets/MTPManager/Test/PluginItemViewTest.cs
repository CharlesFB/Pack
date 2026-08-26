// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: PluginItemView 解压目录选择（veDirChosen）手动测试入口
// ==================================================

using System.Collections.Generic;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager.Test
{
    /// <summary>
    /// 专门验证 PluginItemView 里 .zip / .unitypackage 的显示差异：
    /// - .unitypackage 行：不应显示 veDirChosen（解压目录条目）
    /// - .zip 行：应显示 veDirChosen，且 lbUnzipRoot 默认是 "/Assets"；点击 Chosse Dir 后应实时刷新为所选路径。
    /// 解压目录的选择只在窗口会话内有效（PluginImportWindow._zipExtractDirs），不会写回 MTPackageInfo，
    /// 所以这里只能通过窗口内文本变化肉眼验证，无法在关闭窗口后从 fakePackage 读回。
    /// 注意：测试包在磁盘上没有对应插件文件，点击 Import 只会走到「找不到插件文件」的警告日志，不会真的导入。
    /// </summary>
    public static class PluginItemViewTest
    {
        [MenuItem("MTPTest/Plugin Item View Test (ExtractDir)")]
        public static void OpenTest()
        {
            var fakePackage = new MTPackageInfo
            {
                PackageName = "com.magictavern.test.package",
                Tag = "package-1.0.0",
                Revision = "abcdef1234567890abcdef1234567890abcdef12",
                Url = "https://git.example.com/magictavern/test-package.git",
                ProjectId = 12345,
                Depth = 0,
                Plugins = new Dictionary<string, bool>
                {
                    // .unitypackage：不应出现 veDirChosen
                    {
                        "com.magictavern.test.package#DemoPlugin.A.unitypackage",
                        false
                    },
                    // .zip：应显示 veDirChosen，且 lbUnzipRoot 默认 "/Assets"
                    {
                        "com.magictavern.test.package#Tools.zip",
                        false
                    },
                    {
                        "com.magictavern.test.package#MoreTools.zip",
                        false
                    }
                }
            };

            Debug.Log("[PluginItemViewTest] before ShowWindow:");
            LogState(fakePackage);

            PluginImportWindow.ShowWindow(fakePackage);

            Debug.Log("[PluginItemViewTest] after ShowWindow (勾选/Import 或 Cancel 后的最终状态):");
            LogState(fakePackage);
        }

        private static void LogState(MTPackageInfo info)
        {
            foreach (var kvp in info.Plugins)
            {
                string fileName = GetPluginFileName(kvp.Key);
                Debug.Log($"  {fileName} => selected:{kvp.Value}");
            }
        }

        private static string GetPluginFileName(string pluginKey)
        {
            if (string.IsNullOrEmpty(pluginKey))
            {
                return pluginKey;
            }

            int separator = pluginKey.IndexOf('#');
            return separator >= 0 && separator < pluginKey.Length - 1
                ? pluginKey.Substring(separator + 1)
                : pluginKey;
        }
    }
}
