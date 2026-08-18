// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 内嵌包磁盘现状内存状态服务（对应 MTPackageInfoList / Packages/upm-local.json）
// ==================================================

using System.Collections.Generic;
using System.Linq;
using Editor.Data.Models;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class LocalPackageService : ILocalPackageService
    {
        // 嵌入包，通过 git clone 放在 /Packages 目录下
        private readonly Dictionary<string, MTPackageInfo> _localPackages = new Dictionary<string, MTPackageInfo>();

        public MTPackageInfo AddOrUpdateLocal(MTPackageInfo localPackage)
        {
            if (localPackage == null || string.IsNullOrEmpty(localPackage.PackageName))
            {
                return null;
            }

            localPackage.isLocal = true;
            _localPackages[localPackage.PackageName] = localPackage;
            return localPackage;
        }

        public bool RemoveLocal(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return false;
            }

            return _localPackages.Remove(packageName);
        }

        public MTPackageInfo GetLocal(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            return _localPackages.TryGetValue(packageName, out var package) ? package : null;
        }

        public bool ContainsLocal(string packageName)
        {
            return !string.IsNullOrEmpty(packageName) && _localPackages.ContainsKey(packageName);
        }

        public List<MTPackageInfo> GetAllLocals() => _localPackages.Values.ToList();

        public MTPackageInfoList ExportToMTPackageInfoList()
        {
            return new MTPackageInfoList
            {
                Dependencies = new Dictionary<string, MTPackageInfo>(_localPackages)
            };
        }

        public void LoadFromMTPackageInfoList(MTPackageInfoList list)
        {
            _localPackages.Clear();
            if (list?.Dependencies == null)
            {
                return;
            }

            foreach (var kvp in list.Dependencies)
            {
                _localPackages[kvp.Key] = kvp.Value;
            }
        }

        public void LoadFromLocal(bool createIfNotExist = true)
        {
            var list = JsonUtils.LoadFromFile<MTPackageInfoList>(MTPPath.LOCAL_MTP_INFO_PATH, createIfNotExist);
            LoadFromMTPackageInfoList(list);
        }

        public void SaveToLocal()
        {
            JsonUtils.SaveToFile(MTPPath.LOCAL_MTP_INFO_PATH, ExportToMTPackageInfoList());
        }
    }
}
