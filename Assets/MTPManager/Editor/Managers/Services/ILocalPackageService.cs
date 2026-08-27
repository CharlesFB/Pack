// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 内嵌包（本地安装）磁盘现状内存服务接口（对应 Packages/upm-local.json）
// ==================================================

using System.Collections.Generic;
using Editor.Data.Models;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 管理当前以内嵌方式（git clone 到 /Packages/{PackageName}）安装在磁盘上的包快照。
    /// 反映磁盘现状，用于和目标依赖树 diff 出需要新增/移除的内嵌包，
    /// 只在本地安装/卸载执行成功后由 SyncManifest 更新。
    /// </summary>
    public interface ILocalPackageService
    {
        /// <summary>
        /// 新增或更新一条内嵌包记录（会强制将 isLocal 置为 true）。
        /// </summary>
        MTPackageInfo AddOrUpdateLocal(MTPackageInfo localPackage);

        /// <summary>
        /// 移除一条内嵌包记录；不存在返回 false。
        /// </summary>
        bool RemoveLocal(string packageName);

        MTPackageInfo GetLocal(string packageName);
        bool ContainsLocal(string packageName);
        List<MTPackageInfo> GetAllLocals();

        MTPackageInfoList ExportToMTPackageInfoList();
        void LoadFromMTPackageInfoList(MTPackageInfoList list);

        /// <summary>
        /// 从 MTPPath.LOCAL_MTP_INFO_PATH 装载到内存。
        /// </summary>
        void LoadFromLocal(bool createIfNotExist = true);

        /// <summary>
        /// 将当前内存状态持久化到 MTPPath.LOCAL_MTP_INFO_PATH。
        /// </summary>
        void SaveToLocal();

        void Clear();
    }
}
