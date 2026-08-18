// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: Unity packages-lock.json 读取服务接口（本地由 UPM 管理，不做 MTP 持久化）
// ==================================================

using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPackagesLockService
    {
        /// <summary>
        /// 按包名获取 LockInfo；不存在返回 null。
        /// </summary>
        PackagesLock.LockInfo Get(string packageName);

        /// <summary>
        /// 当前内存中的全部 lock 条目。
        /// </summary>
        Dictionary<string, PackagesLock.LockInfo> GetAll();

        /// <summary>
        /// 从 MTPPath.PACKAGE_LOCK_PATH 装载到内存（不写回）。
        /// </summary>
        void LoadFromLocal();

        /// <summary>
        /// 直接读取本地 packages-lock.json 并返回 DTO（同时刷新内存）。
        /// 文件不存在时返回 null。
        /// </summary>
        PackagesLock LoadFromLocalFile();
    }
}
