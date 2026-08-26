// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: PluginImportWindow 手动测试入口
// ==================================================

using System.Collections.Generic;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager.Test
{
    public static class PluginImportWindowTest
    {
        [MenuItem("MTPTest/Import Test")]
        public static void OpenImportTest()
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
                    {
                        "com.magictavern.test.package#DemoPlugin.A.unitypackage",
                        false
                    },
                    {
                        "com.magictavern.test.package#DemoPlugin.B.unitypackage",
                        true
                    },
                    {
                        "com.magictavern.test.package#Tools.zip",
                        false
                    }
                }
            };

            Debug.Log("[MTPTest/Import Test] before ShowWindow Plugins:");
            foreach (var kvp in fakePackage.Plugins)
            {
                Debug.Log($"  {kvp.Key} => {kvp.Value}");
            }

            PluginImportWindow.ShowWindow(fakePackage);

            Debug.Log("[MTPTest/Import Test] after ShowWindow Plugins:");
            foreach (var kvp in fakePackage.Plugins)
            {
                Debug.Log($"  {GetPluginFileName(kvp.Key)} => {kvp.Value}");
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
