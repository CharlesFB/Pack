// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 远端 MT 工程信息内存服务接口
// ==================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IProjectInfoService
    {
        /// <summary>
        /// 缓存是否过期或为空（反序列化后 RefreshTime 默认为 MinValue，会触发重新拉取）。
        /// </summary>
        bool NeedUpdate { get; }

        DateTime RefreshTime { get; set; }

        /// <summary>
        /// 按包名查找工程信息。
        /// </summary>
        ProjectInfo Get(string packageName);

        /// <summary>
        /// 按远端 ProjectId 查找工程信息。
        /// </summary>
        ProjectInfo Get(int projectId);

        /// <summary>
        /// 按仓库 URL 查找工程信息（与 SshUrl / HttpUrl 比对，输入会先 FormatUrl）。
        /// </summary>
        ProjectInfo GetByUrl(string url);

        List<ProjectInfo> GetAll();

        ProjectInfoList ExportToProjectInfoList();

        void LoadFromProjectInfoList(ProjectInfoList list);

        /// <summary>
        /// 将当前内存状态持久化到 MTPPath.PROJECT_INFO_LIST_PATH。
        /// </summary>
        void SaveToLocal();

        /// <summary>
        /// 从 MTPPath.PROJECT_INFO_LIST_PATH 装载到内存。
        /// 默认不创建空文件（本地缺失时应走 EnsureInitializedAsync / PullFromGit）。
        /// </summary>
        void LoadFromLocal(bool createIfNotExist = false);

        /// <summary>
        /// 初始化装载：本地文件存在则读盘；不存在则 force 从 Git 拉取并本地持久化。不会创建空本地文件。
        /// </summary>
        Task EnsureInitializedAsync();

        /// <summary>
        /// 从 GitLab 管理工程拉取 upm-list.json，写入内存并本地持久化。
        /// </summary>
        /// <param name="force">为 true 时忽略 TTL，强制拉取</param>
        /// <returns>成功拉取并装载返回 true；跳过（未过期）或失败返回 false</returns>
        Task<bool> PullFromGitAsync(bool force = false);
    }
}
