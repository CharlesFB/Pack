// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 插件资源条目列表内存服务接口
// ==================================================

using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPluginAssetService
    {
        /// <summary>
        /// 按插件身份 Key（packageName#pluginFileName）获取资源路径列表；不存在返回 null。
        /// </summary>
        List<string> Get(string pluginKey);

        /// <summary>
        /// 覆盖写入 Content[pluginKey] = assetPaths。
        /// </summary>
        void Set(string pluginKey, List<string> assetPaths);

        /// <summary>
        /// 追加写入 Content[pluginKey]（与已有记录合并去重，不覆盖旧条目）。
        /// 用于 zip 类插件可能被多次解压到不同目录的场景：每次导入都应保留历史解压出的路径，
        /// 以便后续卸载时能清理到全部曾经落盘的文件，而不仅是最近一次导入的路径。
        /// </summary>
        void Append(string pluginKey, List<string> assetPaths);

        /// <summary>
        /// 删除 Content 中指定插件条目。
        /// </summary>
        bool Remove(string pluginKey);

        /// <summary>
        /// 当前内存中的全部条目。
        /// </summary>
        Dictionary<string, List<string>> GetAll();

        PluginAssetList ExportToPluginAssetList();

        void LoadFromPluginAssetList(PluginAssetList list);

        /// <summary>
        /// 将当前内存状态持久化到 MTPPath.PLUGIN_ASSET_LIST_PATH。
        /// </summary>
        void SaveToLocal();

        /// <summary>
        /// 从 MTPPath.PLUGIN_ASSET_LIST_PATH 装载到内存。
        /// </summary>
        void LoadFromLocal(bool createIfNotExist = true);

        /// <summary>
        /// 筛选 addList 中会与已记录资源冲突（覆盖）的路径。
        /// excludePluginKey 传入当前正在处理的插件自身的 key 时，会排除该插件自己已记录的路径
        /// （避免用同一份预分析结果与自己比对，误报 100% 冲突）。
        /// </summary>
        List<string> GetCoveredList(List<string> addList, string excludePluginKey = null);

        /// <summary>
        /// 计算删除指定插件时，仅由该插件独占、可安全卸载的资源路径。
        /// 插件不在记录中则返回空列表。
        /// </summary>
        List<string> GetUncoveredListToRemove(string pluginKey);

        /// <summary>
        /// 删除指定 packageName 下、文件名不在 keepFileNames 中的插件记录。
        /// 仅在已成功列出当前 PluginsPackage 目录后再调用。返回删除条数。
        /// </summary>
        int PruneMissingPlugins(string packageName, IEnumerable<string> keepFileNames);
    }
}
