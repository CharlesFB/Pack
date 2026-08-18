// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MT 包依赖信息内存状态服务（对应 MTPackageInfoList / Library/upm-dependencies.json）
// ==================================================

using System.Collections.Generic;
using System.Linq;
using Editor.Data.Models;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class MTPackageInfoService : IMTPackageInfoService
    {
        private readonly Dictionary<string, MTPackageInfo> _dependencies = new Dictionary<string, MTPackageInfo>();

        public MTPackageInfo AddDependency(MTPackageInfo newPackage)
        {
            EnsurePluginsInitialized(newPackage);

            if (_dependencies.TryGetValue(newPackage.PackageName, out var existingPackage))
            {
                if (existingPackage.Revision != newPackage.Revision)
                {
                    existingPackage = TryToMerge(existingPackage, newPackage);
                    _dependencies[existingPackage.PackageName] = existingPackage;
                }

                return existingPackage;
            }

            _dependencies.Add(newPackage.PackageName, newPackage);
            return newPackage;
        }

        public MTPackageInfo AddDependency(PackageInfo rawPackage, string url, string tag, string revision, int depth, bool isLocal)
        {
            var formattedUrl = UrlUtils.FormatUrl(url);
            var newPackage = new MTPackageInfo
            {
                PackageName = rawPackage.name,
                Tag = tag,
                Revision = revision,
                Dependencies = rawPackage.dependenciesGit,
                Plugins = new Dictionary<string, bool>(),
                Url = formattedUrl,
                Repository = formattedUrl,
                Depth = depth,
                ScriptDefineSymbols = rawPackage.scriptDefineSymbols,
                isLocal = isLocal,
            };
            return AddDependency(newPackage);
        }

        public void RemoveDependency(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return;
            }

            _dependencies.Remove(packageName);
        }

        private MTPackageInfo TryToMerge(MTPackageInfo existingPackage, MTPackageInfo newPackage)
        {
            if (newPackage.Depth == 0)
            {
                return newPackage;
            }

            if (existingPackage.Depth == 0)
            {
                return existingPackage;
            }

            if (existingPackage.Revision != newPackage.Revision)
            {
                LM.LogWarning($"[MTPackageInfoService] 依赖版本冲突: {existingPackage.PackageName} " +
                                 $"同时被要求安装 {existingPackage.Revision} 和 {newPackage.Revision}，" +
                                 $"将自动选用较新版本。");
            }

            if (existingPackage.IsLaterTran(newPackage.Revision, newPackage.Tag))
            {
                return existingPackage;
            }

            return newPackage;
        }

        public MTPackageInfo GetPackage(string packageName)
        {
            return _dependencies.TryGetValue(packageName, out var package) ? package : null;
        }

        public List<MTPackageInfo> GetAllPackages() => _dependencies.Values.ToList();

        public bool SetPlugin(string packageName, string pluginLocalPath, bool installed)
        {
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(pluginLocalPath))
            {
                return false;
            }

            if (!_dependencies.TryGetValue(packageName, out var package) || package == null)
            {
                return false;
            }

            EnsurePluginsInitialized(package);
            package.Plugins[pluginLocalPath] = installed;
            return true;
        }

        public Dictionary<string, bool> GetPlugins(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)
                || !_dependencies.TryGetValue(packageName, out var package)
                || package == null)
            {
                return null;
            }

            EnsurePluginsInitialized(package);
            return new Dictionary<string, bool>(package.Plugins);
        }

        public MTPackageInfoList ExportToMTPackageInfoList()
        {
            return new MTPackageInfoList
            {
                Dependencies = new Dictionary<string, MTPackageInfo>(_dependencies)
            };
        }

        public void LoadFromMTPackageInfoList(MTPackageInfoList list)
        {
            _dependencies.Clear();
            if (list?.Dependencies == null)
            {
                return;
            }

            foreach (var kvp in list.Dependencies)
            {
                EnsurePluginsInitialized(kvp.Value);
                _dependencies[kvp.Key] = kvp.Value;
            }
        }

        public void LoadDependenciesFromLocal(bool createIfNotExist = true)
        {
            var list = JsonUtils.LoadFromFile<MTPackageInfoList>(MTPPath.MT_PACKAGE_INFO_LIST_PATH, createIfNotExist);
            LoadFromMTPackageInfoList(list);
        }

        public void SaveDependenciesToLocal()
        {
            JsonUtils.SaveToFile(MTPPath.MT_PACKAGE_INFO_LIST_PATH, ExportToMTPackageInfoList());
        }

        private static void EnsurePluginsInitialized(MTPackageInfo package)
        {
            if (package == null)
            {
                return;
            }

            if (package.Plugins == null)
            {
                package.Plugins = new Dictionary<string, bool>();
            }
        }
    }
}
