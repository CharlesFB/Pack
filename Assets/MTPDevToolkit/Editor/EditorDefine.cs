// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 编辑器资源路径配置，用于切换开发和正式环境的资源路径
// ==================================================

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    public static class EditorDefine
    {
#if PACKAGE_DEV
        private const string RESOURCES_PATH = "Assets/MTPDevToolkit/";
#else
        private const string RESOURCES_PATH = "Packages/com.magictavern.mtpdevtoolkit/";
#endif

        internal static string GetResPath(string relativePath)
        {
            return $"{RESOURCES_PATH}Editor/Resources/{relativePath}";
        }

        internal static string GetBasePath(string relativePath)
        {
            return $"{RESOURCES_PATH}{relativePath}";
        }
    }
}