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
    public class PackageInfoService : IPackageInfoService
    {
        private const double CACHE_TTL_SECONDS = MTPTime.PACKAGE_INFO_CACHE_TTL;

        private readonly Dictionary<string, Dictionary<string, PackageInfo>> _packageCache =
            new Dictionary<string, Dictionary<string, PackageInfo>>();

        private DateTime _refreshTime;

        private IGitLabService GitLabService => ServiceLocator.Get<IGitLabService>();
        private IProjectInfoService ProjectInfoService => ServiceLocator.Get<IProjectInfoService>();

        public bool NeedUpdate =>
            (DateTime.Now - RefreshTime).TotalSeconds > CACHE_TTL_SECONDS
            || _packageCache.Count == 0;

        public DateTime RefreshTime
        {
            get => _refreshTime;
            set => _refreshTime = value;
        }

        public void Add(string projectId, string tag, PackageInfo packageJson)
        {
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(tag) || packageJson == null)
            {
                return;
            }

            if (!_packageCache.TryGetValue(projectId, out var packDic))
            {
                packDic = new Dictionary<string, PackageInfo>();
                _packageCache.Add(projectId, packDic);
            }

            packDic[tag] = packageJson;
        }

        public PackageInfo Get(string projectId, string tag)
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
            var cacheCopy = new Dictionary<string, Dictionary<string, PackageInfo>>(_packageCache.Count);
            foreach (var projectKvp in _packageCache)
            {
                cacheCopy[projectKvp.Key] = projectKvp.Value != null
                    ? new Dictionary<string, PackageInfo>(projectKvp.Value)
                    : new Dictionary<string, PackageInfo>();
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
                    ? new Dictionary<string, PackageInfo>(projectKvp.Value)
                    : new Dictionary<string, PackageInfo>();
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

            LM.Log("[PackageInfoService] 本地 upm-cache.json 不存在，force 从 Git 拉取。");
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
                    $"[PackageInfoService] PullFromGit 失败: projectId={MTPGit.SOURCE_PROJECT_ID}, path={MTPGit.GIT_PACKAGE_INFO_CACHE_PATH}");
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

                    var packageJson = await GitLabService.GetJsonFileAsync<PackageInfo>(
                        project.ProjectId, "package.json", tag);

                    if (packageJson == null)
                    {
                        failCount++;
                        LM.LogWarning(
                            $"[PackageInfoService] 拉取 package.json 失败: projectId={projectId}, tag={tag}");
                        continue;
                    }

                    Add(projectId, tag, packageJson);
                    successCount++;
                }
            }

            _refreshTime = DateTime.Now;
            SaveToLocal();
            LM.Log(
                $"[PackageInfoService] RebuildFromProjectInfoList done. success:{successCount}, fail:{failCount}, projects:{projects.Count}");
        }
    }
}
