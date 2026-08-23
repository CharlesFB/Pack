// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 依赖树重建 + manifest/本地 diff + 安装编排（对应旧 SyncManifest）
// ==================================================

using System.Threading.Tasks;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPackageSyncService
    {
        /// <summary>
        /// 从根包重建依赖树，与 UPM manifest 或本地内嵌列表 diff，再交给安装器执行。
        /// 不做中断恢复、不写临时安装进度。失败时回滚内存依赖树到本次开始前的快照。
        /// </summary>
        /// <param name="installToLocal">
        /// 空则读 Config.InstallToLocal。true 与 upm-local.json 比；false 与 Packages/manifest.json 比。
        /// </param>
        Task<bool> SyncManifestAsync(bool? installToLocal = null);
    }
}
