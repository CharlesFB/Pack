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
        /// 从已安装包本地 Editor/PluginsPackage（不递归）发现 .zip / .unitypackage，
        /// 写入 MTPackageInfo.Plugins（key = packageName#fileName）。
        /// 不写 PluginAsset；资源清单仅在导入成功后落盘。已导入状态按 PluginAsset Key 回填。
        /// </summary>
        /// <param name="persist">
        /// 是否立即写入依赖树（SaveDependenciesToLocal）。
        /// </param>
        Task<bool> PreparePluginsAsync(MTPackageInfo mtPackage, bool persist = true);

        /// <summary>
        /// 从已安装包本地路径导入插件（.unitypackage / .zip）。
        /// selectedPluginKeys 为空则导入该包 Plugins 中全部资源项；
        /// zipExtractDirectories 为 zip 类型插件指定解压父目录（key=pluginKey，value=用户选择的绝对目录），
        /// 缺省或值为空时按 "/Assets" 根目录解压（该选择仅作用于本次导入，不做任何持久化）。
        /// 导入每个插件前，会用 IPluginAssetService.GetCoveredList 计算是否会覆盖已记录的资源路径，
        /// 若有冲突会弹窗单独询问用户是否继续，取消则跳过该插件（不影响其余插件继续导入）。
        /// 导入成功后立刻写入 PluginAsset 与 Plugins Value=true（每个插件单独落盘，避免 Refresh 域重载丢失）。
        /// </summary>
        /// <param name="persist">是否立即写入依赖树与 PluginAsset 清单。</param>
        /// <param name="skipRecorded">
        /// 为 true 时跳过 PluginAsset 已有资源清单的插件（安装器域重载恢复用）。
        /// 手动 Import / ReImport / Unzip 应为 false。
        /// </param>
        Task<bool> ImportPreparedPluginsAsync(MTPackageInfo mtPackage,
            IEnumerable<string> selectedPluginKeys = null,
            IReadOnlyDictionary<string, string> zipExtractDirectories = null,
            bool persist = true, bool skipRecorded = false);

        /// <summary>
        /// 卸载该包已导入的插件资源，并清理 Plugins / PluginAssetList 记录。
        /// 会先汇总本次将要删除的全部资源路径，用一次 DisplayDialog 整体展示给用户确认（与旧工程一致），
        /// 用户取消则整体放弃、不做任何改动。
        /// </summary>
        /// <param name="showConfirmDialog">
        /// 是否弹窗确认。用户主动卸载包时应为 true。
        /// </param>
        bool RemoveImportedPlugins(MTPackageInfo mtPackage, bool showConfirmDialog = true);

        /// <summary>
        /// 卸载单个已导入插件。
        /// </summary>
        bool RemoveImportedPlugin(MTPackageInfo mtPackage, string pluginKey, bool showConfirmDialog = true);

        /// <summary>
        /// 列出远端 Editor/PluginsPackage 当前目录下的 .zip / .unitypackage 文件名（不递归）。
        /// </summary>
        Task<List<string>> ListRemotePluginFileNamesAsync(int projectId, string reference);
    }
}
