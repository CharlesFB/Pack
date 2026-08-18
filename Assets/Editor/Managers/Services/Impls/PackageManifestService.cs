// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: Unity manifest.json 内存状态服务（对应 PackageManifest / Packages/manifest.json）
// ==================================================

using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PackageManifestService : IPackageManifestService
    {
        private readonly Dictionary<string, string> _dependencies = new Dictionary<string, string>();

        public bool Contains(string packageName)
        {
            return !string.IsNullOrEmpty(packageName) && _dependencies.ContainsKey(packageName);
        }

        public string GetDependency(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            return _dependencies.TryGetValue(packageName, out var value) ? value : null;
        }

        public void SetDependency(string packageName, string versionOrUrl)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return;
            }

            _dependencies[packageName] = versionOrUrl;
        }

        /// <inheritdoc />
        public bool RemoveDependency(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return false;
            }

            return _dependencies.Remove(packageName);
        }

        public Dictionary<string, string> GetGitPackages()
        {
            var result = new Dictionary<string, string>();
            foreach (var kvp in _dependencies)
            {
                if (kvp.Value != null && kvp.Value.Contains(".git"))
                {
                    result.Add(kvp.Key, kvp.Value);
                }
            }

            return result;
        }

        public bool GetGitPackageInfo(string packageName, out string url, out string revision)
        {
            url = null;
            revision = null;

            if (string.IsNullOrEmpty(packageName) || !_dependencies.TryGetValue(packageName, out var fullUrl)
                || string.IsNullOrEmpty(fullUrl))
            {
                return false;
            }

            var parts = fullUrl.Split('#');
            if (parts.Length < 2)
            {
                return false;
            }

            url = UrlUtils.FormatUrl(parts[0]);
            revision = parts[parts.Length - 1];
            return true;
        }

        public PackageManifest ExportToPackageManifest()
        {
            return new PackageManifest
            {
                dependencies = new Dictionary<string, string>(_dependencies)
            };
        }

        public void LoadFromPackageManifest(PackageManifest manifest)
        {
            _dependencies.Clear();
            if (manifest?.dependencies == null)
            {
                return;
            }

            foreach (var kvp in manifest.dependencies)
            {
                _dependencies[kvp.Key] = kvp.Value;
            }
        }

        public void LoadFromLocal()
        {
            // createIfNotExist=false：manifest 由 UPM 维护，不存在时不由 MTP 创建
            var manifest = JsonUtils.LoadFromFile<PackageManifest>(MTPPath.PACKAGE_MANIFEST_PATH, false);
            LoadFromPackageManifest(manifest);
        }
    }
}
