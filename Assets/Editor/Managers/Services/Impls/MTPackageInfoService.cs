// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MT 包依赖信息内存状态服务（对应 MTPackageInfoList / Library/upm-dependencies.json）
// ==================================================

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Editor.Data.Models;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class MTPackageInfoService : IMTPackageInfoService
    {
        private readonly Dictionary<string, MTPackageInfo> _dependencies = new Dictionary<string, MTPackageInfo>();

        private IGitLabService GitLabService => ServiceLocator.Get<IGitLabService>();
        private IProjectInfoService ProjectInfoService => ServiceLocator.Get<IProjectInfoService>();

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
            var newPackage = BuildMTPackageInfo(rawPackage, url, tag, revision, depth, isLocal);
            return AddDependency(newPackage);
        }

        private static MTPackageInfo BuildMTPackageInfo(PackageInfo rawPackage, string url, string tag,
            string revision, int depth, bool isLocal)
        {
            var formattedUrl = UrlUtils.FormatUrl(url);
            return new MTPackageInfo
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
        }

        public async Task<MTPackageInfo> FetchPackageInfoAsync(string gitDependencyUrl, int depth = 0,
            bool isLocal = false)
        {
            if (string.IsNullOrEmpty(gitDependencyUrl))
            {
                LM.LogWarning("[MTPackageInfoService] FetchPackageInfoAsync 参数无效: gitDependencyUrl 为空");
                return null;
            }

            // 约定与 package.json 中 dependenciesGit 的写法一致："仓库链接#tag"
            var splitIndex = gitDependencyUrl.LastIndexOf('#');
            if (splitIndex <= 0 || splitIndex == gitDependencyUrl.Length - 1)
            {
                LM.LogError(
                    $"[MTPackageInfoService] FetchPackageInfoAsync url 格式错误，应为 仓库链接#tag: {gitDependencyUrl}");
                return null;
            }

            var url = gitDependencyUrl.Substring(0, splitIndex);
            var tag = gitDependencyUrl.Substring(splitIndex + 1);
            return await FetchPackageInfoAsync(url, tag, depth, isLocal);
        }

        public async Task<MTPackageInfo> FetchPackageInfoAsync(string url, string tag, int depth = 0,
            bool isLocal = false)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(tag))
            {
                LM.LogWarning($"[MTPackageInfoService] FetchPackageInfoAsync 参数无效: url={url}, tag={tag}");
                return null;
            }

            var projectInfo = ProjectInfoService.GetByUrl(url);
            if (projectInfo == null)
            {
                LM.LogError($"[MTPackageInfoService] FetchPackageInfoAsync 找不到对应工程: url={url}");
                return null;
            }

            var rawPackage =
                await GitLabService.GetJsonFileAsync<PackageInfo>(projectInfo.ProjectId, "package.json", tag);
            if (rawPackage == null)
            {
                LM.LogError($"[MTPackageInfoService] FetchPackageInfoAsync 拉取 package.json 失败: url={url}, tag={tag}");
                return null;
            }

            var revision = await GitLabService.ResolveRevisionAsync(projectInfo.ProjectId, tag);
            if (string.IsNullOrEmpty(revision))
            {
                LM.LogError($"[MTPackageInfoService] FetchPackageInfoAsync 解析 revision 失败: url={url}, tag={tag}");
                return null;
            }

            return BuildMTPackageInfo(rawPackage, url, tag, revision, depth, isLocal);
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
