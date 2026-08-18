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
                Repository = "https://git.example.com/magictavern/test-package.git",
                Depth = 0,
                Plugins = new Dictionary<string, bool>
                {
                    {
                        "Library/PackageCache/com.magictavern.test.package@abcdef123456/Editor/PluginsPackage/DemoPlugin.A.unitypackage",
                        false
                    },
                    {
                        "Library/PackageCache/com.magictavern.test.package@abcdef123456/Editor/PluginsPackage/DemoPlugin.B.unitypackage",
                        true
                    },
                    {
                        "Library/PackageCache/com.magictavern.test.package@abcdef123456/Editor/PluginsPackage/Extra/Tools.zip",
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
                Debug.Log($"  {System.IO.Path.GetFileName(kvp.Key)} => {kvp.Value}");
            }
        }
    }
}
