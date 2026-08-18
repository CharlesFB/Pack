// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MTPackageManagerWindow 手动测试入口
// ==================================================

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager.Test
{
    public static class MTPackageManagerWindowTest
    {
        private const string TEST_CHANGELOG_PATH = "Test/test_CHANGELOG.md";

        [MenuItem("MTPTest/Package Manager Window")]
        public static void OpenPackageManagerWindow()
        {
            var window = EditorWindow.GetWindow<MTPackageManagerWindow>();
            window.titleContent = new GUIContent("MT Package Manager");
            window.minSize = new Vector2(1080, 700);
            window.Show();
            Debug.Log("[MTPTest/Package Manager Window] opened MTPackageManagerWindow");

            AddTestChangelogTab(window);
            AddTestSamplesTab(window);
            AddTestPluginsTab(window);
        }

        private static void AddTestChangelogTab(MTPackageManagerWindow window)
        {
            string mdPath = Path.Combine(Application.dataPath, TEST_CHANGELOG_PATH);
            if (!File.Exists(mdPath))
            {
                Debug.LogError($"[MTPTest/Package Manager Window] 未找到测试 changelog 文件: {mdPath}");
                return;
            }

            string content = File.ReadAllText(mdPath);

            var changelogView = new PackageChangelogView();
            changelogView.BindData(content);

            window.AddDetailTab("Changelog", changelogView);
            Debug.Log("[MTPTest/Package Manager Window] Changelog Tab 已添加");
        }

        /// <summary>
        /// 对应老工程 PackageDetailView 里的 "Samples" Tab：一个包若干 Sample，逐条展示 Import/ReImport。
        /// 先用假数据跑通展示效果，真实数据后续接 UnityEditor.PackageManager.UI.Sample。
        /// </summary>
        private static void AddTestSamplesTab(MTPackageManagerWindow window)
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;

            var fakeSamples = new (string Name, bool IsImported)[]
            {
                ("ExampleScenes", true),
                ("AdvancedDemo", false),
                ("BasicUsage", false),
            };

            foreach (var (name, isImported) in fakeSamples)
            {
                var itemView = new PackageSampleItemView();
                itemView.BindData(name, isImported);
                container.Add(itemView);
            }

            window.AddDetailTab("Samples", container);
            Debug.Log("[MTPTest/Package Manager Window] Samples Tab 已添加（假数据）");
        }

        /// <summary>
        /// 对应老工程 PackageDetailView 里的 "PluginsPackages" Tab：展示包内约定目录下的插件文件
        /// （.zip / .unitypackage），逐条展示 Import/Unzip/Remove。先用假数据跑通展示效果，
        /// 真实数据后续接 IPluginPackageService。
        /// </summary>
        private static void AddTestPluginsTab(MTPackageManagerWindow window)
        {
            var container = new VisualElement();
            container.style.flexGrow = 1;

            var fakePlugins = new (string FileName, bool IsImported)[]
            {
                ("DemoPlugin.A.unitypackage", false),
                ("DemoPlugin.B.unitypackage", true),
                ("Extra_Tools.zip", false),
            };

            foreach (var (fileName, isImported) in fakePlugins)
            {
                var itemView = new PackageZipItemView();
                itemView.BindData(fileName, isImported);
                container.Add(itemView);
            }

            window.AddDetailTab("Plugins", container);
            Debug.Log("[MTPTest/Package Manager Window] Plugins Tab 已添加（假数据）");
        }
    }
}
