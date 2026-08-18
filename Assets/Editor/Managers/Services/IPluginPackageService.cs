// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 插件包编排服务接口（Prepare / Import / Remove）
// ==================================================

using System.Collections.Generic;
using System.Threading.Tasks;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPluginPackageService
    {
        /// <summary>
        /// 从远端（或本地包目录）发现插件，下载到 Staging，解析资源路径，
        /// 写入 MTPackageInfo.Plugins（默认 installed=false，已有则保留）与 PluginAssetList，并落盘。
        /// </summary>
        Task<bool> PreparePluginsAsync(MTPackageInfo mtPackage);

        /// <summary>
        /// 从 Staging 导入已准备的插件（.unitypackage / .zip）。
        /// selectedPluginLocalPaths 为空则导入该包 Plugins 中全部条目；
        /// zipExtractDirectories 为 zip 类型插件指定解压父目录（key=pluginLocalPath，value=用户选择的绝对目录），
        /// 缺省或值为空时按 "/Assets" 根目录解压（该选择仅作用于本次导入，不做任何持久化）。
        /// 导入每个插件前，会用 IPluginAssetService.GetCoveredList 计算是否会覆盖已记录的资源路径，
        /// 若有冲突会弹窗单独询问用户是否继续，取消则跳过该插件（不影响其余插件继续导入）。
        /// </summary>
        bool ImportPreparedPlugins(MTPackageInfo mtPackage, IEnumerable<string> selectedPluginLocalPaths = null,
            IReadOnlyDictionary<string, string> zipExtractDirectories = null);

        /// <summary>
        /// 卸载该包已导入的插件资源，并清理 Plugins / PluginAssetList 记录。
        /// </summary>
        bool RemoveImportedPlugins(MTPackageInfo mtPackage);

        /// <summary>
        /// 列出远端 Editor/PluginsPackage 下的 .zip / .unitypackage 文件名（相对 PluginsPackage）。
        /// </summary>
        Task<List<string>> ListRemotePluginFileNamesAsync(int projectId, string reference);
    }
}
