// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 远端 MT 工程信息内存状态服务（对应 ProjectInfoList / Library/upm-list.json）
// ==================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitLabApiClient.Models.Tags.Responses;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class ProjectInfoService : IProjectInfoService
    {
        private readonly List<ProjectInfo> _projectCache = new List<ProjectInfo>();
        private DateTime _refreshTime;

        private IGitLabService GitLabService => ServiceLocator.Get<IGitLabService>();

        public bool NeedUpdate =>
            (DateTime.Now - RefreshTime).TotalSeconds > ConfigManager.Instance.GetRefreshSecond()
            || _projectCache.Count == 0;

        public DateTime RefreshTime
        {
            get => _refreshTime;
            set => _refreshTime = value;
        }

        public void Clear()
        {
            _projectCache.Clear();
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
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            for (int i = 0; i < _projectCache.Count; i++)
            {
                var project = _projectCache[i];
                if (UrlUtils.MatchesProjectRepo(project, url))
                {
                    return project;
                }
            }

            return null;
        }

        public ProjectInfo GetByUrl(string url, string tagOrPrefix)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(tagOrPrefix))
            {
                return GetByUrl(url);
            }

            for (int i = 0; i < _projectCache.Count; i++)
            {
                var project = _projectCache[i];
                if (UrlUtils.MatchesProject(project, url, tagOrPrefix))
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

            if (ConfigManager.Instance.Current?.DebugMode ?? false)
            {
                return await PullProjectListFullAsync();
            }

            return await PullProjectListCacheAsync();
        }

        private async Task<bool> PullProjectListCacheAsync()
        {
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

        public async Task<bool> PullProjectListFullAsync()
        {
            var config = ConfigManager.Instance.Current;
            var packageInfoList = new List<ProjectInfo>();
            var seenProjectIds = new HashSet<int>();

            if (!string.IsNullOrEmpty(config?.GroupId))
            {
                var group = await GitLabService.GetGroupAsync(config.GroupId);
                if (group?.Projects == null)
                {
                    LM.LogError($"[ProjectInfoService] PullProjectListFull 拉取 Group 失败: groupId={config.GroupId}");
                }
                else
                {
                    var activeProjects = group.Projects.Where(p => p != null && !p.Archived).ToList();
                    var tagTasks = activeProjects.Select(p => GitLabService.GetTagsAsync(p.Id)).ToList();
                    var allTags = tagTasks.Count > 0
                        ? await Task.WhenAll(tagTasks)
                        : Array.Empty<IList<Tag>>();

                    for (int i = 0; i < activeProjects.Count; i++)
                    {
                        var project = activeProjects[i];
                        seenProjectIds.Add(project.Id);
                        ParseTagsToProjectInfos(
                            project.Id, project.SshUrlToRepo, project.HttpUrlToRepo, allTags[i], packageInfoList);
                    }
                }
            }

            if (!string.IsNullOrEmpty(config?.ProjectId))
            {
                var extra = await GitLabService.GetProjectAsync(config.ProjectId);
                if (extra != null && seenProjectIds.Add(extra.Id))
                {
                    var tags = await GitLabService.GetTagsAsync(extra.Id);
                    ParseTagsToProjectInfos(
                        extra.Id, extra.SshUrlToRepo, extra.HttpUrlToRepo, tags, packageInfoList);
                }
            }

            if (packageInfoList.Count == 0 && string.IsNullOrEmpty(config?.GroupId)
                && string.IsNullOrEmpty(config?.ProjectId))
            {
                LM.LogError("[ProjectInfoService] PullProjectListFull 失败: GroupId / ProjectId 均为空");
                return false;
            }

            var remote = new ProjectInfoList
            {
                RefreshTime = DateTime.Now,
                Packages = packageInfoList
            };
            LoadFromProjectInfoList(remote);
            SaveToLocal();
            LM.Log($"[ProjectInfoService] PullProjectListFull done. packages:{packageInfoList.Count}");
            return true;
        }

        private static void ParseTagsToProjectInfos(int projectId, string sshUrl, string httpUrl,
            IEnumerable<Tag> tags, List<ProjectInfo> result)
        {
            if (result == null)
            {
                return;
            }

            var tagDic = new Dictionary<string, List<string>>();
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    if (tag == null || string.IsNullOrEmpty(tag.Name))
                    {
                        continue;
                    }

                    var splitTag = tag.Name.Split('-');
                    if (splitTag.Length <= 1)
                    {
                        continue;
                    }

                    var head = splitTag[0];
                    var version = splitTag[splitTag.Length - 1];
                    if (IsInvalidPackage(head, version))
                    {
                        continue;
                    }

                    if (!tagDic.TryGetValue(head, out var list))
                    {
                        list = new List<string>();
                        tagDic.Add(head, list);
                    }

                    list.Add(tag.Name);
                }
            }

            foreach (var kvp in tagDic)
            {
                VersionUtils.SortTags(kvp.Value);
                result.Add(new ProjectInfo
                {
                    ProjectId = projectId,
                    PackageName = kvp.Key,
                    SshUrl = UrlUtils.FormatUrl(sshUrl),
                    HttpUrl = UrlUtils.FormatUrl(httpUrl),
                    Tags = kvp.Value,
                    PackageType = PackageType.Normal
                });
            }
        }

        /// <summary>
        /// head 为 dev / 含 '.' 或 '_' 则丢弃；
        /// 末段不是 debug 且无法解析为 Version 则丢弃。
        /// </summary>
        private static bool IsInvalidPackage(string head, string version)
        {
            if (head == "dev")
            {
                return true;
            }

            if (head.Contains(".") || head.Contains("_"))
            {
                return true;
            }

            if (version == "debug")
            {
                return false;
            }

            return !Version.TryParse(version, out _);
        }
    }
}
