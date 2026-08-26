// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MTPackageManagerWindow 手动测试入口
// ==================================================

using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager.Test
{
    public static class MTPackageManagerWindowTest
    {
        [MenuItem("MTPTest/Package Manager Window")]
        public static void OpenPackageManagerWindow()
        {
            var window = EditorWindow.GetWindow<MTPackageManagerWindow>();
            window.titleContent = new GUIContent("MT Package Manager");
            window.minSize = new Vector2(1200, 700);
            window.Show();
            // 窗口若已经打开，CreateGUI 不会重新执行，这里显式再刷新一次，保证展示的是最新的真实数据
            window.RefreshProjectListView();
            Debug.Log("[MTPTest/Package Manager Window] opened MTPackageManagerWindow (real data)");
        }
        
        
        [MenuItem("MTPTest/MTPSettingWindow")]
        public static void ShowSettingWindow()
        {
            MTPSettingWindow wnd = EditorWindow.GetWindow<MTPSettingWindow>();
            wnd.titleContent = new GUIContent("MTPSettingWindow");
            wnd.minSize = new Vector2(470, 480);
        }
    }
}
