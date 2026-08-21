// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 用于提供VisualElement相关的便捷操作
// ==================================================

using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 用于提供VisualElement相关的便捷操作
    /// </summary>
    public static class VisualElementExtension
    {
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
#if UNITY_2021_1_OR_NEWER
            listview.Rebuild();
#else
            listview.Refresh();
#endif
        }
    }
}