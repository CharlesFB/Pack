// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 远端 MT 工程信息内存状态服务（对应 ProjectInfoList / Library/upm-list.json）
// ==================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class ProjectInfoService : IProjectInfoService
    {
        private const double CACHE_TTL_SECONDS = MTPTime.PROJECT_INFO_LIST_TTL;

        private readonly List<ProjectInfo> _projectCache = new List<ProjectInfo>();
        private DateTime _refreshTime;

        private IGitLabService GitLabService => ServiceLocator.Get<IGitLabService>();

        public bool NeedUpdate =>
            (DateTime.Now - RefreshTime).TotalSeconds > CACHE_TTL_SECONDS
            || _projectCache.Count == 0;

        public DateTime RefreshTime
        {
            get => _refreshTime;
            set => _refreshTime = value;
        }

        public ProjectInfo Get(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            for (int i = 0; i < _projectCache.Count; i++)
            {
                var packageProject = _projectCache[i];
                if (packageProject != null && string.Equals(packageProject.PackageName, packageName, StringComparison.Ordinal))
                {
                    return packageProject;
                }
            }

            return null;
        }

        public ProjectInfo Get(int projectId)
        {
            for (int i = 0; i < _projectCache.Count; i++)
            {
                var package = _projectCache[i];
                if (package != null && package.ProjectId == projectId)
                {
                    return package;
                }
            }

            return null;
        }

        public ProjectInfo Get(int projectId, string packageName)
        {
            if (projectId <= 0 || string.IsNullOrEmpty(packageName))
            {
                return Get(packageName);
            }

            string shortName = PathUtils.GetPackageShortName(packageName);
            for (int i = 0; i < _projectCache.Count; i++)
            {
                var package = _projectCache[i];
                if (package != null
                    && package.ProjectId == projectId
                    && PathUtils.MatchesProjectPackageName(package.PackageName, shortName))
                {
                    return package;
                }
            }

            return null;
        }

        public ProjectInfo GetByUrl(string url)
        {
            return GetByUrl(url, packageName: null);
        }

        public ProjectInfo GetByUrl(string url, string packageName)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            var formattedUrl = UrlUtils.FormatUrl(url);
            string shortName = PathUtils.GetPackageShortName(packageName);
            for (int i = 0; i < _projectCache.Count; i++)
            {
                var project = _projectCache[i];
                if (project == null)
                {
                    continue;
                }

                if (!string.Equals(project.HttpUrl, formattedUrl, StringComparison.Ordinal)
                    && !string.Equals(project.SshUrl, formattedUrl, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(shortName)
                    || PathUtils.MatchesProjectPackageName(project.PackageName, shortName))
                {
                    return project;
                }
            }

            return null;
        }

        public List<ProjectInfo> GetAll()
        {
            return new List<ProjectInfo>(_projectCache);
        }

        public ProjectInfoList ExportToProjectInfoList()
        {
            return new ProjectInfoList
            {
                RefreshTime = _refreshTime,
                Packages = new List<ProjectInfo>(_projectCache)
            };
        }

        public void LoadFromProjectInfoList(ProjectInfoList list)
        {
            _projectCache.Clear();
            _refreshTime = default;

            if (list == null)
            {
                return;
            }

            _refreshTime = list.RefreshTime;
            if (list.Packages == null)
            {
                return;
            }

            _projectCache.AddRange(list.Packages);
        }

        public void SaveToLocal()
        {
            JsonUtils.SaveToFile(MTPPath.PROJECT_INFO_LIST_PATH, ExportToProjectInfoList());
        }

        public void LoadFromLocal(bool createIfNotExist = false)
        {
            var list = JsonUtils.LoadFromFile<ProjectInfoList>(MTPPath.PROJECT_INFO_LIST_PATH, createIfNotExist);
            LoadFromProjectInfoList(list);
        }

        public async Task EnsureInitializedAsync()
        {
            if (System.IO.File.Exists(MTPPath.PROJECT_INFO_LIST_PATH))
            {
                LoadFromLocal(createIfNotExist: false);
                return;
            }

            LM.Log("[ProjectInfoService] 本地 upm-list.json 不存在，force 从 Git 拉取。");
            await PullFromGitAsync(force: true);
        }

        public async Task<bool> PullFromGitAsync(bool force = false)
        {
            if (!force && !NeedUpdate)
            {
                return false;
            }

            var remote = await GitLabService.GetJsonFileAsync<ProjectInfoList>(
                MTPGit.SOURCE_PROJECT_ID,
                MTPGit.GIT_PROJECT_INFO_LIST_PATH);

            if (remote == null)
            {
                LM.LogError(
                    $"[ProjectInfoService] PullFromGit 失败: projectId={MTPGit.SOURCE_PROJECT_ID}, path={MTPGit.GIT_PROJECT_INFO_LIST_PATH}");
                return false;
            }

            remote.RefreshTime = DateTime.Now;
            LoadFromProjectInfoList(remote);
            SaveToLocal();
            return true;
        }
    }
}
