// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: Unity manifest.json 内存服务接口
// ==================================================

using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IManifestJsonService
    {
        /// <summary>
        /// 是否包含指定依赖包名。
        /// </summary>
        bool Contains(string packageName);

        /// <summary>
        /// 获取依赖版本/地址字符串；不存在返回 null。
        /// </summary>
        string GetDependency(string packageName);

        /// <summary>
        /// 添加或覆盖一条依赖。
        /// </summary>
        void SetDependency(string packageName, string versionOrUrl);

        /// <summary>
        /// 移除一条依赖；不存在返回 false。
        /// </summary>
        bool RemoveDependency(string packageName);

        /// <summary>
        /// 筛选出 git 依赖（value 含 ".git"）。
        /// </summary>
        Dictionary<string, string> GetGitPackages();

        /// <summary>
        /// 解析 git 依赖的 url 与 revision（url#revision）。
        /// </summary>
        bool GetGitPackageInfo(string packageName, out string url, out string revision);

        /// <summary>
        /// 获取内存的 Manifest 数据
        /// </summary>
        ManifestJson ExportToPackageManifest();

        /// <summary>
        /// 为内存数据赋值（替换内存数据）
        /// </summary>
        void LoadFromPackageManifest(ManifestJson manifestJson);

        /// <summary>
        /// 从 MTPPath.PACKAGE_MANIFEST_PATH 装载到内存（不写回，文件由 UPM 管理）。
        /// </summary>
        void LoadFromLocal();
    }
}
