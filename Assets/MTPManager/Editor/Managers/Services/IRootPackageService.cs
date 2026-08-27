// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 依赖树 Root 声明内存服务接口（对应 Packages/upm-install.json）
// ==================================================

using System.Collections.Generic;
using Editor.Data.Models;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 管理用户手动选中要安装的根包清单（Depth=0 的"意图"）。
    /// 随 git 提交，团队共享；只在用户显式安装/卸载某根包时改动，
    /// 是构建依赖树时的遍历起点，不由 SyncManifest 自动增删。
    /// </summary>
    public interface IRootPackageService
    {
        /// <summary>
        /// 新增或更新一个根包声明（会强制将 Depth 置为 0）。
        /// </summary>
        MTPackageInfo AddOrUpdateRoot(MTPackageInfo rootPackage);

        /// <summary>
        /// 移除一个根包声明；不存在返回 false。
        /// </summary>
        bool RemoveRoot(string packageName);

        MTPackageInfo GetRoot(string packageName);
        bool ContainsRoot(string packageName);
        List<MTPackageInfo> GetAllRoots();

        MTPackageInfoList ExportToMTPackageInfoList();
        void LoadFromMTPackageInfoList(MTPackageInfoList list);

        /// <summary>
        /// 从 MTPPath.INSTALLED_MTP_INFO_PATH 装载到内存。
        /// </summary>
        void LoadFromLocal(bool createIfNotExist = true);

        /// <summary>
        /// 将当前内存状态持久化到 MTPPath.INSTALLED_MTP_INFO_PATH。
        /// </summary>
        void SaveToLocal();

        void Clear();
    }
}
