// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description:处理Sample相关流程
// ==================================================

using System.Collections.Generic;
using System.IO;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// 处理Sample相关流程
    /// Sample文件夹 --> ~Sample文件夹
    /// </summary>
    public static class PackageSampleUtils
    {
        public static void CreateSamplesFolder(string manifestDisplayName)
        {
            var sampleFolderPath = $"Assets/Samples_{manifestDisplayName}";
            if (!AssetDatabase.IsValidFolder(sampleFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", $"Samples_{manifestDisplayName}");
            }

            var realSampleFolder = $"{Application.dataPath}/{manifestDisplayName}/Samples~";
            if (!File.Exists(realSampleFolder))
            {
                Directory.CreateDirectory(realSampleFolder);
            }
        }

        public static void SyncSamplesFolder(string manifestDisplayName)
        {
            var srcFolder = $"{Application.dataPath}/Samples_{manifestDisplayName}";
            var desFolder = $"{Application.dataPath}/{manifestDisplayName}/Samples~";
            FileUtil.CopyFileOrDirectory(srcFolder, desFolder);
        }

        public static List<PackageManifest.Sample> SyncAndGetSamplesInfo(string manifestDisplayName)
        {
            var sampleList = new List<PackageManifest.Sample>();
            var srcFolder = $"{Application.dataPath}/Samples_{manifestDisplayName}/";
            if (!Directory.Exists(srcFolder))
                return sampleList;

            var desFolder = $"{Application.dataPath}/{manifestDisplayName}/Samples~/";
            var srcDirs = Directory.GetDirectories(srcFolder);
            FileUtil.ReplaceDirectory(srcFolder, desFolder);
            foreach (var dir in srcDirs)
            {
                var dirName = dir.Substring(srcFolder.Length);
                var destDir = desFolder + dirName;
                Debug.LogFormat("Copy folder:{0} to {1}", dirName, destDir);
                // FileUtil.ReplaceDirectory(dir, destDir);
                sampleList.Add(new PackageManifest.Sample()
                {
                    displayName = dirName,
                    description = "",
                    path = $"Samples~/{dirName}"
                });
            }

            return sampleList;
        }
    }
}