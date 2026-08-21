// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: .unitypackage 导入（单文件 / 批量）
// ==================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 核心导入能力：仅负责 AssetDatabase.ImportPackage，不处理持久化记录。
    /// </summary>
    public static class UnitypackageImporter
    {
        /// <summary>
        /// 导入单个 .unitypackage。
        /// </summary>
        /// <param name="packagePath">.unitypackage 绝对或工程相对路径</param>
        /// <param name="interactive">是否弹出导入确认对话框</param>
        /// <returns>是否成功发起导入</returns>
        public static bool Import(string packagePath, bool interactive = false)
        {
            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            {
                Debug.LogWarning($"[UnitypackageImporter] unitypackage missing, skip: {packagePath}");
                return false;
            }
            AssetDatabase.StartAssetEditing();
            try
            {
                AssetDatabase.ImportPackage(packagePath, interactive);
                Debug.Log($"[UnitypackageImporter] Import: {Path.GetFileName(packagePath)}");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            return true;
        }

        /// <summary>
        /// 批量导入 .unitypackage。
        /// </summary>
        /// <param name="packagePaths">待导入路径集合</param>
        /// <param name="interactive">是否弹出导入确认对话框</param>
        /// <returns>成功发起导入的文件数量</returns>
        public static int ImportBatch(IEnumerable<string> packagePaths, bool interactive = false)
        {
            int importedCount = 0;
            if (packagePaths == null)
            {
                return importedCount;
            }
            
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string packagePath in packagePaths)
                {
                    if (Import(packagePath, interactive))
                    {
                        importedCount++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            
            return importedCount;
        }
    }
}
