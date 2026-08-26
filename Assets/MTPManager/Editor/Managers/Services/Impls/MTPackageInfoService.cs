// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MT 包依赖信息内存状态服务（对应 MTPackageInfoList / Library/upm-dependencies.json）
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
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
        private IPackageJsonService PackageJsonService => ServiceLocator.Get<IPackageJsonService>();
        private IRootPackageService RootPackageService => ServiceLocator.Get<IRootPackageService>();

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

        public MTPackageInfo AddDependency(PackageJson rawPackage, string url, string tag, string revision, int depth, bool isLocal)
        {
            var newPackage = BuildMTPackageInfo(rawPackage, url, tag, revision, depth, isLocal);
            return AddDependency(newPackage);
        }

        private MTPackageInfo BuildMTPackageInfo(PackageJson rawPackage, string url, string tag,
            string revision, int depth, bool isLocal, int projectId = 0)
        {
            var formattedUrl = UrlUtils.FormatUrl(url);
            var project = ProjectInfoService.GetByUrl(formattedUrl, tag)
                          ?? ProjectInfoService.GetByUrl(formattedUrl)
                          ?? (projectId > 0 ? ProjectInfoService.Get(projectId) : null);
            if (projectId <= 0)
            {
                projectId = project?.ProjectId ?? 0;
            }

            // 与旧 CollectPackagesToInstall 一致：节点 Url 用工程 HttpUrl，避免 ssh 链接进安装器。
            var urlToStore = !string.IsNullOrEmpty(project?.HttpUrl)
                ? UrlUtils.FormatUrl(project.HttpUrl)
                : formattedUrl;

            var package = new MTPackageInfo
            {
                PackageName = rawPackage.name,
                Tag = tag,
                Revision = revision,
                Dependencies = ToInstallDependencyMap(rawPackage.dependenciesGit),
                Plugins = new Dictionary<string, bool>(),
                Url = urlToStore,
                ProjectId = projectId,
                Depth = depth,
                ScriptDefineSymbols = rawPackage.scriptDefineSymbols,
                isLocal = isLocal,
            };
            FillPluginsFromLocalPackage(package);
            return package;
        }

        /// <summary>
        /// 依赖树带入的 git 安装链接补上 DEFAULT_TOKEN；ssh/git@ 一并转成 https。
        /// </summary>
        private static Dictionary<string, string> ToInstallDependencyMap(Dictionary<string, string> dependenciesGit)
        {
            if (dependenciesGit == null)
            {
                return null;
            }

            var result = new Dictionary<string, string>(dependenciesGit.Count);
            foreach (var kvp in dependenciesGit)
            {
                result[kvp.Key] = UrlUtils.ToInstallGitUrl(kvp.Value, MTPGit.DEFAULT_TOKEN);
            }

            return result;
        }

        /// <summary>
        /// 从已安装包本地 Editor/PluginsPackage（不递归）扫 .zip / .unitypackage，写入 Plugins。
        /// Key 为 packageName#pluginFileName；Value 按 PluginAsset 记录判定是否已导入。
        /// 目录存在时，同步清掉该包下已不在目录中的 PluginAsset 记录。
        /// </summary>
        private static void FillPluginsFromLocalPackage(MTPackageInfo package)
        {
            if (package == null)
            {
                return;
            }

            package.Plugins ??= new Dictionary<string, bool>();
            package.Plugins.Clear();

            string pluginDir = PathUtils.GetPluginsDirectory(package);
            if (string.IsNullOrEmpty(pluginDir) || !Directory.Exists(pluginDir))
            {
                return;
            }

            var fileNames = PathUtils.ListPluginPackageFileNames(pluginDir);
            var pluginAsset = ServiceLocator.Get<IPluginAssetService>();
            var recorded = pluginAsset?.GetAll() ?? new Dictionary<string, List<string>>();

            foreach (var fileName in fileNames)
            {
                string key = PathUtils.MakePluginKey(package.PackageName, fileName);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                package.Plugins[key] = PathUtils.IsPluginKeyRecorded(key, recorded);
            }

            if (pluginAsset != null && pluginAsset.PruneMissingPlugins(package.PackageName, fileNames) > 0)
            {
                pluginAsset.SaveToLocal();
            }
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

            var projectInfo = ProjectInfoService.GetByUrl(url, tag)
                              ?? ProjectInfoService.GetByUrl(url);
            if (projectInfo == null)
            {
                LM.LogError($"[MTPackageInfoService] FetchPackageInfoAsync 找不到对应工程: url={url}");
                return null;
            }

            var rawPackage =
                await GitLabService.GetJsonFileAsync<PackageJson>(projectInfo.ProjectId, "package.json", tag);
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

            var urlToStore = !string.IsNullOrEmpty(projectInfo.HttpUrl) ? projectInfo.HttpUrl : url;
            return BuildMTPackageInfo(rawPackage, urlToStore, tag, revision, depth, isLocal, projectInfo.ProjectId);
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
                LM.Log($"[MTPackageInfoService] 依赖版本冲突: {existingPackage.PackageName} " +
                                 $"构建依赖树时同时被要求使用 {existingPackage.Tag} 和 {newPackage.Tag}，" +
                                 $"将自动选用较新版本。");
            }

            if (existingPackage.IsLaterThan(newPackage.Revision, newPackage.Tag))
            {
                return existingPackage;
            }

            return newPackage;
        }

        public MTPackageInfo GetPackage(string packageName)
        {
            return _dependencies.TryGetValue(packageName, out var package) ? package : null;
        }

        public MTPackageInfo GetPackageByProject(ProjectInfo project)
        {
            if (project == null)
            {
                return null;
            }

            foreach (var package in _dependencies.Values)
            {
                if (package != null && UrlUtils.MatchesProject(project, package.Url, package.Tag))
                {
                    return package;
                }
            }

            return null;
        }

        public List<MTPackageInfo> GetAllPackages() => _dependencies.Values.ToList();

        public bool SetPlugin(string packageName, string pluginKey, bool installed)
        {
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(pluginKey))
            {
                return false;
            }

            if (!_dependencies.TryGetValue(packageName, out var package) || package == null)
            {
                return false;
            }

            EnsurePluginsInitialized(package);
            string storedKey = pluginKey;
            foreach (var existingKey in package.Plugins.Keys)
            {
                if (!PathUtils.PluginKeysEqual(existingKey, pluginKey))
                {
                    continue;
                }

                storedKey = existingKey;
                break;
            }

            package.Plugins[storedKey] = installed;
            return true;
        }

        public List<string> GetAllKeys()
        {
            return  _dependencies?.Keys.ToList();
        }

        public List<PackageJson> GetAllUsedBy(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return new List<PackageJson>();
            }

            var res = new List<PackageJson>();
            foreach (var info in _dependencies.Values)
            {
                if (info?.Dependencies == null || !info.Dependencies.ContainsKey(packageName))
                {
                    continue;
                }

                var json = PackageJsonService.Get(info.ProjectId.ToString(), info.Tag);
                if (json != null)
                {
                    res.Add(json);
                }
            }

            return res;
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
                FillPluginsFromLocalPackage(kvp.Value);
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

        public void Clear()
        {
            _dependencies.Clear();
        }

        public async Task RebuildFromRootsAsync(bool installToLocal)
        {
            Clear();
            var roots = RootPackageService.GetAllRoots();
            if (roots == null || roots.Count == 0)
            {
                LM.Log("[MTPackageInfoService] RebuildFromRoots: 根包列表为空，依赖树已清空");
                return;
            }

            var visited = new HashSet<string>();
            foreach (var root in roots)
            {
                if (root == null || string.IsNullOrEmpty(root.Url) || string.IsNullOrEmpty(root.Tag))
                {
                    LM.LogWarning(
                        $"[MTPackageInfoService] RebuildFromRoots 跳过无效根包: {root?.PackageName}");
                    continue;
                }

                await WalkDependencyAsync(root.Url, root.Tag, 0, installToLocal, visited);
            }

            LM.Log($"[MTPackageInfoService] RebuildFromRoots done. nodes:{_dependencies.Count}, roots:{roots.Count}");
        }

        private async Task WalkDependencyAsync(
            string url, string tag, int depth, bool installToLocal, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(tag))
            {
                return;
            }

            var visitKey = $"{UrlUtils.FormatUrl(url)}#{tag}";
            if (!visited.Add(visitKey))
            {
                return;
            }

            var node = await ResolveNodeAsync(url, tag, depth, installToLocal);
            if (node == null)
            {
                return;
            }

            AddDependency(node);

            if (node.Dependencies == null || node.Dependencies.Count == 0)
            {
                return;
            }

            foreach (var depUrl in node.Dependencies.Values)
            {
                if (string.IsNullOrEmpty(depUrl))
                {
                    continue;
                }

                var splitIndex = depUrl.LastIndexOf('#');
                if (splitIndex <= 0 || splitIndex == depUrl.Length - 1)
                {
                    LM.LogError($"[MTPackageInfoService] 依赖格式错误，应为 仓库链接#tag: {depUrl}");
                    continue;
                }

                await WalkDependencyAsync(
                    depUrl.Substring(0, splitIndex),
                    depUrl.Substring(splitIndex + 1),
                    depth + 1,
                    installToLocal,
                    visited);
            }
        }

        private async Task<MTPackageInfo> ResolveNodeAsync(string url, string tag, int depth, bool isLocal)
        {
            var formattedUrl = UrlUtils.FormatUrl(url);
            var project = ProjectInfoService.GetByUrl(formattedUrl, tag)
                          ?? ProjectInfoService.GetByUrl(formattedUrl);
            if (project != null && project.ProjectId > 0)
            {
                var cached = PackageJsonService.Get(project.ProjectId.ToString(), tag);
                if (cached != null)
                {
                    var revision = await GitLabService.ResolveRevisionAsync(project.ProjectId, tag);
                    if (string.IsNullOrEmpty(revision))
                    {
                        revision = tag;
                    }

                    var urlToStore = !string.IsNullOrEmpty(project.HttpUrl) ? project.HttpUrl : formattedUrl;
                    return BuildMTPackageInfo(
                        cached, urlToStore, tag, revision, depth, isLocal, project.ProjectId);
                }
            }

            return await FetchPackageInfoAsync(formattedUrl, tag, depth, isLocal);
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
