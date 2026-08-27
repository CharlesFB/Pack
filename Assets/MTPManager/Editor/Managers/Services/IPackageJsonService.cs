// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: PackageJson 缓存内存服务接口
// ==================================================

using System;
using System.Collections.Generic;
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

        void Clear();

        void Add(string projectId, string tag, PackageJson packageJson);

        PackageJson Get(string projectId, string tag);

        /// <summary>
        /// 获取所有的 Is Using 显示部分，用于dependencies展示
        /// </summary>
        List<PackageJson> GetAllIsUsing(PackageJson tagInfo);

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

        /// <summary>
        /// 选中某一 projectId+tag 时：若内存缓存条目存在且 plugins / changelog 仍为 null，
        /// 则按需从 GitLab 补拉并写回该条缓存（没有插件写入空列表，没有 CHANGELOG.md 写入空字符串）。
        /// 两条都已有值则直接返回 false，不访问网络。
        /// </summary>
        /// <returns>是否改写了缓存（调用方据此决定是否刷新 Tab）。</returns>
        Task<bool> EnsurePluginsAndChangelogAsync(int projectId, string tag);
    }
}
