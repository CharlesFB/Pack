// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 插件包服务接口（Prepare / Import / Remove）
// ==================================================

using System.Collections.Generic;
using System.Threading.Tasks;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPluginPackageService
    {
        /// <summary>
        /// 从远端（或本地包目录）发现插件，下载到 Staging，解析资源路径，
        /// 写入 MTPackageInfo.Plugins（默认 installed=false，已有则保留）与 PluginAssetList。
        /// </summary>
        /// <param name="persist">
        /// 是否立即写入本地持久化（SaveDependenciesToLocal / PluginAsset.SaveToLocal）。
        /// 独立调用（如手动刷新插件列表）应保持默认 true；
        /// 作为"安装包"流程的一个步骤被调用时应传 false，落盘工作交给安装流程在 UPM 请求成功后统一执行，
        /// 避免 UPM 请求随后失败时，磁盘上残留一份实际未完成安装的插件状态。
        /// </param>
        Task<bool> PreparePluginsAsync(MTPackageInfo mtPackage, bool persist = true);

        /// <summary>
        /// 从 Staging 导入已准备的插件（.unitypackage / .zip）。
        /// selectedPluginLocalPaths 为空则导入该包 Plugins 中全部资源项；
        /// zipExtractDirectories 为 zip 类型插件指定解压父目录（key=pluginLocalPath，value=用户选择的绝对目录），
        /// 缺省或值为空时按 "/Assets" 根目录解压（该选择仅作用于本次导入，不做任何持久化）。
        /// 导入每个插件前，会用 IPluginAssetService.GetCoveredList 计算是否会覆盖已记录的资源路径，
        /// 若有冲突会弹窗单独询问用户是否继续，取消则跳过该插件（不影响其余插件继续导入）。
        /// </summary>
        /// <param name="persist">同 <see cref="PreparePluginsAsync"/> 的 persist 参数说明。</param>
        bool ImportPreparedPlugins(MTPackageInfo mtPackage, IEnumerable<string> selectedPluginLocalPaths = null,
            IReadOnlyDictionary<string, string> zipExtractDirectories = null, bool persist = true);

        /// <summary>
        /// 卸载该包已导入的插件资源，并清理 Plugins / PluginAssetList 记录。
        /// 会先汇总本次将要删除的全部资源路径，用一次 DisplayDialog 整体展示给用户确认（与旧工程一致），
        /// 用户取消则整体放弃、不做任何改动。
        /// </summary>
        /// <param name="showConfirmDialog">
        /// 是否弹窗确认。用户主动卸载包时应为 true；
        /// 作为"安装失败回滚"内部调用时应传 false。
        /// </param>
        bool RemoveImportedPlugins(MTPackageInfo mtPackage, bool showConfirmDialog = true);

        /// <summary>
        /// 列出远端 Editor/PluginsPackage 下的 .zip / .unitypackage 文件名（相对 PluginsPackage）。
        /// </summary>
        Task<List<string>> ListRemotePluginFileNamesAsync(int projectId, string reference);
    }
}
