// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: PackageJson 缓存内存状态服务（对应 PackageInfoCache / upm-cache.json）
// ==================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PackageJsonService : IPackageJsonService
    {
        private const double CACHE_TTL_SECONDS = MTPTime.PACKAGE_INFO_CACHE_TTL;

        private readonly Dictionary<string, Dictionary<string, PackageJson>> _packageCache =
            new Dictionary<string, Dictionary<string, PackageJson>>();

        private DateTime _refreshTime;

        private IGitLabService GitLabService => ServiceLocator.Get<IGitLabService>();
        private IProjectInfoService ProjectInfoService => ServiceLocator.Get<IProjectInfoService>();
        private IPluginPackageService PluginPackageService => ServiceLocator.Get<IPluginPackageService>();

        public bool NeedUpdate =>
            (DateTime.Now - RefreshTime).TotalSeconds > CACHE_TTL_SECONDS
            || _packageCache.Count == 0;

        public DateTime RefreshTime
        {
            get => _refreshTime;
            set => _refreshTime = value;
        }

        public void Add(string projectId, string tag, PackageJson packageJson)
        {
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(tag) || packageJson == null)
            {
                return;
            }

            if (!_packageCache.TryGetValue(projectId, out var packDic))
            {
                packDic = new Dictionary<string, PackageJson>();
                _packageCache.Add(projectId, packDic);
            }

            packDic[tag] = packageJson;
        }

        public PackageJson Get(string projectId, string tag)
        {
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(tag))
            {
                return null;
            }

            if (!_packageCache.TryGetValue(projectId, out var packDic))
            {
                return null;
            }

            return packDic.TryGetValue(tag, out var packageJson) ? packageJson : null;
        }

        public PackageInfoCache ExportToPackageInfoCache()
        {
            var cacheCopy = new Dictionary<string, Dictionary<string, PackageJson>>(_packageCache.Count);
            foreach (var projectKvp in _packageCache)
            {
                cacheCopy[projectKvp.Key] = projectKvp.Value != null
                    ? new Dictionary<string, PackageJson>(projectKvp.Value)
                    : new Dictionary<string, PackageJson>();
            }

            return new PackageInfoCache
            {
                RefreshTime = _refreshTime,
                PackageCache = cacheCopy
            };
        }

        public void LoadFromPackageInfoCache(PackageInfoCache cache)
        {
            _packageCache.Clear();
            _refreshTime = default;

            if (cache == null)
            {
                return;
            }

            _refreshTime = cache.RefreshTime;
            if (cache.PackageCache == null)
            {
                return;
            }

            foreach (var projectKvp in cache.PackageCache)
            {
                _packageCache[projectKvp.Key] = projectKvp.Value != null
                    ? new Dictionary<string, PackageJson>(projectKvp.Value)
                    : new Dictionary<string, PackageJson>();
            }
        }

        public void SaveToLocal()
        {
            JsonUtils.SaveToFile(MTPPath.PACKAGE_INFO_CACHE_PATH, ExportToPackageInfoCache());
        }

        public void LoadFromLocal(bool createIfNotExist = false)
        {
            var cache = JsonUtils.LoadFromFile<PackageInfoCache>(MTPPath.PACKAGE_INFO_CACHE_PATH, createIfNotExist);
            LoadFromPackageInfoCache(cache);
        }

        public async Task EnsureInitializedAsync()
        {
            if (System.IO.File.Exists(MTPPath.PACKAGE_INFO_CACHE_PATH))
            {
                LoadFromLocal(createIfNotExist: false);
                return;
            }

            LM.Log("[PackageJsonService] 本地 upm-cache.json 不存在，force 从 Git 拉取。");
            await PullFromGitAsync(force: true);
        }

        public async Task<bool> PullFromGitAsync(bool force = false)
        {
            if (!force && !NeedUpdate)
            {
                return false;
            }

            var remote = await GitLabService.GetJsonFileAsync<PackageInfoCache>(
                MTPGit.SOURCE_PROJECT_ID,
                MTPGit.GIT_PACKAGE_INFO_CACHE_PATH);

            if (remote == null)
            {
                LM.LogError(
                    $"[PackageJsonService] PullFromGit 失败: projectId={MTPGit.SOURCE_PROJECT_ID}, path={MTPGit.GIT_PACKAGE_INFO_CACHE_PATH}");
                return false;
            }

            remote.RefreshTime = DateTime.Now;
            LoadFromPackageInfoCache(remote);
            SaveToLocal();
            return true;
        }

        public async Task RebuildFromProjectInfoListAsync()
        {
            var projects = ProjectInfoService.GetAll();
            _packageCache.Clear();

            int successCount = 0;
            int failCount = 0;

            foreach (var project in projects)
            {
                if (project == null || project.ProjectId <= 0 || project.Tags == null || project.Tags.Count == 0)
                {
                    continue;
                }

                string projectId = project.ProjectId.ToString();
                foreach (var tag in project.Tags)
                {
                    if (string.IsNullOrEmpty(tag))
                    {
                        continue;
                    }

                    var packageJson = await GitLabService.GetJsonFileAsync<PackageJson>(
                        project.ProjectId, "package.json", tag);

                    if (packageJson == null)
                    {
                        failCount++;
                        LM.LogWarning(
                            $"[PackageJsonService] 拉取 package.json 失败: projectId={projectId}, tag={tag}");
                        continue;
                    }

                    // CHANGELOG.md 是否存在纯属约定，很多仓库压根没有这个文件，
                    // 这属于正常情况而非错误，因此拉取失败时不记录日志，直接留空即代表"没有 CHANGELOG"。
                    packageJson.changelog = await GitLabService.GetFileTextAsync(
                        project.ProjectId, MTPGit.GIT_CHANGELOG_FILE_NAME, tag, logOnFailure: false);

                    // 插件目录同样约定存在，很多包没有 Editor/PluginsPackage，
                    // ListRemotePluginFileNamesAsync 内部已经把"目录不存在"当作正常情况处理（返回空列表，不额外记录日志）。
                    packageJson.plugins = await PluginPackageService.ListRemotePluginFileNamesAsync(
                        project.ProjectId, tag) ?? new List<string>();

                    Add(projectId, tag, packageJson);
                    successCount++;
                }
            }

            _refreshTime = DateTime.Now;
            SaveToLocal();
            LM.Log(
                $"[PackageJsonService] RebuildFromProjectInfoList done. success:{successCount}, fail:{failCount}, projects:{projects.Count}");
        }
    }
}
