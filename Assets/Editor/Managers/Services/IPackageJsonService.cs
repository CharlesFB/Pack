// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: PackageJson 缓存内存服务接口
// ==================================================

using System;
using System.Threading.Tasks;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPackageJsonService
    {
        /// <summary>
        /// 缓存是否过期或为空（反序列化后 RefreshTime 默认为 MinValue，会触发重新拉取）。
        /// </summary>
        bool NeedUpdate { get; }

        DateTime RefreshTime { get; set; }

        void Add(string projectId, string tag, PackageJson packageJson);

        PackageJson Get(string projectId, string tag);

        PackageInfoCache ExportToPackageInfoCache();

        void LoadFromPackageInfoCache(PackageInfoCache cache);

        /// <summary>
        /// 将当前内存状态持久化到 MTPPath.PACKAGE_INFO_CACHE_PATH。
        /// </summary>
        void SaveToLocal();

        /// <summary>
        /// 从 MTPPath.PACKAGE_INFO_CACHE_PATH 装载到内存。
        /// 默认不创建空文件。
        /// </summary>
        void LoadFromLocal(bool createIfNotExist = false);

        /// <summary>
        /// 初始化装载：本地文件存在则读盘；不存在则 force 从 Git 拉取并写入本地。
        /// </summary>
        Task EnsureInitializedAsync();

        /// <summary>
        /// 从 GitLab 管理工程拉取 upm-cache.json，写入内存并写入本地。
        /// </summary>
        Task<bool> PullFromGitAsync(bool force = false);

        /// <summary>
        /// 遍历 ProjectInfoList 全部工程的全部 Tag，拉取各自 package.json 写入缓存并写入本地。
        /// </summary>
        Task RebuildFromProjectInfoListAsync();
    }
}
