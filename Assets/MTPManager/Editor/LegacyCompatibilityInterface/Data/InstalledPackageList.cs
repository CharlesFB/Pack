// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 手动安装的包的列表
// ==================================================

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// DTO: MTP引入的 MTP自己的Git包（走UPM安装的部分，只存顶层depth==0也就是直接手动安装的，非依赖带下来的）的持久化管理文件，
    /// 映射本地存储的： /Packages/upm-install.json （依赖树顶层包）
    /// MTP管理器自己维护的列表，用于和UPM那边同步包信息
    /// </summary>
    public class InstalledPackageList
    {
        public Dictionary<string, PackageDepInfo> Packages;

        public InstalledPackageList()
        {
            Packages = new Dictionary<string, PackageDepInfo>();
        }

        public List<PackageDepInfo> ToList()
        {
            return Packages.Values.ToList();
        }

        public bool Add(PackageManifest packageManifest, string url, string revision,bool isLocal)
        {
            var newPackageDep = new PackageDepInfo
            {
                PackageName = packageManifest.name,
                Revision = revision,
                Version = packageManifest.version,
                Dependencies = packageManifest.dependenciesGit,
                UnZipPath = null,
                
                Url = PackageDepInfo.FormatUrl(url),
                Depth = 0,
                ScriptDefineSymbols = packageManifest.scriptDefineSymbols,
                isLocal =  isLocal,
            };

            if (Packages.TryGetValue(newPackageDep.PackageName, out var oldDep))
            {
                Debug.Log($"[UpmPackageManager] PackageList.Add, remove old package:{oldDep.Url}#{oldDep.Revision}");
                if(oldDep.UnZipPath!=null) newPackageDep.UnZipPath = new Dictionary<string, string>(oldDep.UnZipPath);
                Packages.Remove(oldDep.PackageName);
            }

            Debug.Log($"[UpmPackageManager] PackageList.Add, add new package:{newPackageDep.Url}#{newPackageDep.Revision}");
            Packages.Add(newPackageDep.PackageName, newPackageDep);
            return true;
        }
        
        public bool Remove(string packageName)
        {
            if (!Packages.ContainsKey(packageName))
                return false;

            Packages.Remove(packageName);
            return true;
        }
    }
}