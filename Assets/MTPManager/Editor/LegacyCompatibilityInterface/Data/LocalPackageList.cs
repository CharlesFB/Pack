// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 本地安装列表和Unity安装类表对应
// ==================================================

using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// DTO: MTP引入的管理持久化Local安装的包的类，结构继承自InstalledPackageList，映射本地 /Packages/ump-lock.json （严重怀疑打错字了，upm-lock.json或许更合理）
    /// </summary>
    public class LocalPackageList : InstalledPackageList
    {
        public bool Add(PackageDepInfo newPackageDep)
        {
            if (Packages.TryGetValue(newPackageDep.PackageName, out var oldDep))
            {
                Debug.Log($"[UpmPackageManager] PackageList.Add, remove old package:{oldDep.Url}#{oldDep.Revision}");
                Packages.Remove(oldDep.PackageName);
            }

            Debug.Log($"[UpmPackageManager] PackageList.Add, add new package:{newPackageDep.Url}#{newPackageDep.Revision}");
            Packages.Add(newPackageDep.PackageName, newPackageDep);
            return true;
        }
    }
}