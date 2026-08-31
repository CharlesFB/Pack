// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 用于提供VisualElement相关的便捷操作
// ==================================================

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 用于提供VisualElement相关的便捷操作
    /// </summary>
    public static class VisualElementExtension
    {
        public static void AddStyleSheetIfExists(this VisualElement element, string path)
        {
            if (element == null || string.IsNullOrEmpty(path))
            {
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (styleSheet != null)
            {
                element.styleSheets.Add(styleSheet);
            }
        }

        public static void SetBackgroundImage(this VisualElement element, string path)
        {
            if (element == null)
            {
                return;
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            element.style.backgroundImage = tex != null
                ? new StyleBackground(tex)
                : new StyleBackground();
        }

        public static VisualElement GetRoot(this VisualElement element)
        {
            if (element.panel != null)
                return element.panel.visualTree;
            var realRoot = element;
            while (realRoot.parent != null)
                realRoot = realRoot.parent;
            return realRoot;
        }

        public static VisualElement InitFromExistVisualElement(this VisualElement root,VisualElement child)
        {
            var parentRoot = child.parent;
            var index = parentRoot.IndexOf(child);
            root.Add(child);
            parentRoot.Insert(index, root);
            return child;
        }

        public static void RefreshListView(this ListView listview)
        {
#if UNITY_2021_2_OR_NEWER
            listview.RefreshItems();
#elif UNITY_2021_1_OR_NEWER
            listview.Rebuild();
#else
            listview.Refresh();
#endif
        }
    }
}