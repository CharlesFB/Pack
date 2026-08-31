// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 依赖树 Root 声明内存状态服务（对应 MTPackageInfoList / Packages/upm-install.json）
// ==================================================

using System.Collections.Generic;
using System.Linq;
using Editor.Data.Models;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class RootPackageService : IRootPackageService
    {
        // 依赖树 root，控制构建依赖树
        private readonly Dictionary<string, MTPackageInfo> _rootPackages = new Dictionary<string, MTPackageInfo>();

        public MTPackageInfo AddOrUpdateRoot(MTPackageInfo rootPackage)
        {
            if (rootPackage == null || string.IsNullOrEmpty(rootPackage.PackageName))
            {
                return null;
            }

            rootPackage.Depth = 0;
            _rootPackages[rootPackage.PackageName] = rootPackage;
            return rootPackage;
        }

        public bool RemoveRoot(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return false;
            }

            return _rootPackages.Remove(packageName);
        }

        public MTPackageInfo GetRoot(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            return _rootPackages.TryGetValue(packageName, out var package) ? package : null;
        }

        public bool ContainsRoot(string packageName)
        {
            return !string.IsNullOrEmpty(packageName) && _rootPackages.ContainsKey(packageName);
        }

        public List<MTPackageInfo> GetAllRoots() => _rootPackages.Values.ToList();

        public MTPackageInfoList ExportToMTPackageInfoList()
        {
            return new MTPackageInfoList
            {
                Dependencies = new Dictionary<string, MTPackageInfo>(_rootPackages)
            };
        }

        public void LoadFromMTPackageInfoList(MTPackageInfoList list)
        {
            _rootPackages.Clear();
            if (list?.Dependencies == null)
            {
                return;
            }

            foreach (var kvp in list.Dependencies)
            {
                _rootPackages[kvp.Key] = kvp.Value;
            }
        }

        public void LoadFromLocal(bool createIfNotExist = true)
        {
            var list = JsonUtils.LoadFromFile<MTPackageInfoList>(MTPPath.INSTALLED_MTP_INFO_PATH, createIfNotExist);
            PackagePersistUtils.InjectPrivateTokens(list);
            LoadFromMTPackageInfoList(list);
        }

        public void SaveToLocal()
        {
            JsonUtils.SaveToFile(
                MTPPath.INSTALLED_MTP_INFO_PATH,
                PackagePersistUtils.CloneWithoutPrivateTokens(ExportToMTPackageInfoList()));
        }

        public void Clear()
        {
            _rootPackages.Clear();
        }
    }
}
