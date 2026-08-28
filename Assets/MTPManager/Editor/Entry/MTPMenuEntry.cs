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
            bool alreadyOpen = EditorWindow.HasOpenInstances<MTPackageManagerWindow>();
            var window = EditorWindow.GetWindow<MTPackageManagerWindow>();
            window.titleContent = new GUIContent("MT Package Manager");
            window.minSize = new Vector2(1200, 700);
            window.Show();
            // 首次打开 CreateGUI 已 RefreshProjectList；仅窗口已存在时补一次，避免算两遍列表
            if (alreadyOpen)
            {
                window.RefreshProjectListView();
            }
        }
    }
}