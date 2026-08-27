// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 依赖树重建 + manifest/本地 diff + 安装编排；Reset 卸载全部 MTP 包
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

        /// <summary>
        /// 清空根包后 SyncManifest，卸载全部 MTP 管理的包。
        /// 成功后再清目录缓存 / local 快照 / 插件资源记录，并强制拉回工程列表。
        /// 不把 MTP 自身重新注册为根包（开发版与发布版路径不同，暂不处理）。
        /// </summary>
        /// <param name="changeInstallMode">
        /// false：按当前 Config.InstallToLocal 卸载。true：按相反模式卸载（切换安装模式时用）。
        /// </param>
        Task<bool> ResetAsync(bool changeInstallMode = false);
    }
}
