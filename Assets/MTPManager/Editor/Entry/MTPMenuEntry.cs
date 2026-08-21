// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MTP包管理器主界面的菜单入口类
// ==================================================

using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class MTPMenuEntry
    {
        [MenuItem("MTTools/MTP Manager")]
        public static void OpenPackageManagerWindow()
        {
            var window = EditorWindow.GetWindow<MTPackageManagerWindow>();
            window.titleContent = new GUIContent("MT Package Manager");
            window.minSize = new Vector2(1200, 700);
            window.Show();
            // 窗口若已经打开，CreateGUI 不会重新执行，这里显式再刷新一次，保证展示的是最新的真实数据
            window.RefreshProjectListView();
        }
    }
}