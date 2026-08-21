// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 包依赖表，用于解析包信息时使用
// ==================================================

using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 包依赖表，用于解析包信息时使用
    /// DTO: 对应持久化层的 Library/upm-dependencies.json，管理项目生命周期内的包依赖树
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class PackageDepInfoList
    {
        //便于序列化
        public Dictionary<string, PackageDepInfo> Dependencies;
        
        public PackageDepInfoList()
        {
            Dependencies = new Dictionary<string, PackageDepInfo>();
        }

        public PackageDepInfo Add(PackageDepInfo newDepInfo)
        {
            if (Dependencies.TryGetValue(newDepInfo.PackageName, out var depInfo))
            {
                if (depInfo.Revision != newDepInfo.Revision)
                {
                    depInfo = TryToMerge(depInfo, newDepInfo);
                    Dependencies[depInfo.PackageName] = depInfo;
                }
            }
            else
            {
                Dependencies.Add(newDepInfo.PackageName, newDepInfo);
                depInfo = newDepInfo;
                depInfo.RefCount = 0;
            }

            depInfo.RefCount++;

            return depInfo;
        }

        public PackageDepInfo Add(PackageManifest packageManifest, string url, string revision, int depth,bool isInstallToLocal)
        {
            return Add(new PackageDepInfo
            {
                PackageName = packageManifest.name,
                Revision = revision,
                Version = packageManifest.version,
                Dependencies = packageManifest.dependenciesGit,
                Url = PackageDepInfo.FormatUrl(url),
                Depth = depth,
                ScriptDefineSymbols = packageManifest.scriptDefineSymbols,
                isLocal = isInstallToLocal,
            });
        }

        public void Remove(PackageDepInfo depInfo)
        {
            Remove(depInfo.PackageName);
        }

        private void Remove(string packageName)
        {
            if (Dependencies.TryGetValue(packageName, out var depInfo))
            {
                depInfo.RefCount--;
                if (depInfo.RefCount <= 0)
                {
                    Dependencies.Remove(packageName);
                }
            }
        }

        public PackageDepInfo Get(string packageName)
        {
            if (Dependencies.TryGetValue(packageName, out var packageInfo))
            {
                return packageInfo;
            }

            return null;
        }

        public PackageDepInfo GetByUrl(string url)
        {
            foreach (var depInfo in Dependencies.Values)
            {
                if (depInfo.Url == url)
                    return depInfo;
            }

            return null;
        }

        private PackageDepInfo TryToMerge(PackageDepInfo depInfo, PackageDepInfo newDepInfo)
        {
            // 新包手动
            if (newDepInfo.Depth == 0)
            {
                newDepInfo.RefCount = depInfo.RefCount;
                // newDepInfo.PluginUnityPackagePaths = UnityPackageUtils.ResolvePluginPathsForPersist(
                //     newDepInfo.PluginUnityPackagePaths,
                //     depInfo.PluginUnityPackagePaths);
                return newDepInfo;
            }
            
            // 老包手动安装
            if (depInfo.Depth == 0)
            {
                return depInfo;
            }

            // BUG-09: 检测版本冲突并发出警告
            if (depInfo.Revision != newDepInfo.Revision)
            {
                Debug.LogWarning($"[MTPManager] 依赖版本冲突: {depInfo.PackageName} " +
                                 $"同时被要求安装 {depInfo.Revision} 和 {newDepInfo.Revision}，" +
                                 $"将自动选用较新版本。");
            }

            if (depInfo.IsLaterTran(newDepInfo.Revision, newDepInfo.Version))
            {
                return depInfo;
            }

            newDepInfo.RefCount = depInfo.RefCount;
            // newDepInfo.PluginUnityPackagePaths = UnityPackageUtils.ResolvePluginPathsForPersist(
            //     newDepInfo.PluginUnityPackagePaths,
            //     depInfo.PluginUnityPackagePaths);
            return newDepInfo;
        }

        public List<PackageDepInfo> ToList()
        {
            return Dependencies.Values.ToList();
        }

        public bool ContainKey(string packageName)
        {
            return Dependencies.ContainsKey(packageName);
        }
    }
}