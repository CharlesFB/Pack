// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: .unitypackage 导入（单文件 / 批量）
// ==================================================

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
        /// 发起导入并等到 Unity 回调完成（completed / failed / cancelled）。
        /// 不等待随后的编译/域重载；调用方应先落盘再 WaitUntilIdle，避免记录被冲掉。
        /// ImportPackage 本身是异步的，调用返回不代表资源已进 AssetDatabase。
        /// </summary>
        public static async Task<bool> ImportAndWaitAsync(string packagePath, bool interactive = false)
        {
            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            {
                Debug.LogWarning($"[UnitypackageImporter] unitypackage missing, skip: {packagePath}");
                return false;
            }

            bool completed = false;
            bool success = true;
            void OnCompleted(string _)
            {
                completed = true;
                success = true;
            }

            void OnFailed(string name, string error)
            {
                completed = true;
                success = false;
                Debug.LogError($"[UnitypackageImporter] Import failed: {name}, {error}");
            }

            void OnCancelled(string name)
            {
                completed = true;
                success = false;
                Debug.LogWarning($"[UnitypackageImporter] Import cancelled: {name}");
            }

            AssetDatabase.importPackageCompleted += OnCompleted;
            AssetDatabase.importPackageFailed += OnFailed;
            AssetDatabase.importPackageCancelled += OnCancelled;
            try
            {
                AssetDatabase.ImportPackage(packagePath, interactive);
                Debug.Log($"[UnitypackageImporter] Import: {Path.GetFileName(packagePath)}");

                int timeoutCount = 0;
                while (!completed)
                {
                    if (timeoutCount >= MTPTime.INSTALLATION_TIMEOUT)
                    {
                        Debug.LogError(
                            $"[UnitypackageImporter] Import wait timeout: {Path.GetFileName(packagePath)}");
                        return false;
                    }

                    await Task.Delay(MTPTime.INSTALLATION_CHECK_TIME);
                    timeoutCount += MTPTime.INSTALLATION_CHECK_TIME;
                }

                return success;
            }
            finally
            {
                AssetDatabase.importPackageCompleted -= OnCompleted;
                AssetDatabase.importPackageFailed -= OnFailed;
                AssetDatabase.importPackageCancelled -= OnCancelled;
            }
        }

        /// <summary>
        /// 批量导入，逐个等到完成后再进行下一个。
        /// </summary>
        public static async Task<int> ImportBatchAndWaitAsync(IEnumerable<string> packagePaths,
            bool interactive = false)
        {
            int importedCount = 0;
            if (packagePaths == null)
            {
                return importedCount;
            }

            foreach (string packagePath in packagePaths)
            {
                if (await ImportAndWaitAsync(packagePath, interactive))
                {
                    await EditorBusyWait.WaitUntilIdleAsync();
                    importedCount++;
                }
            }

            return importedCount;
        }
    }
}
