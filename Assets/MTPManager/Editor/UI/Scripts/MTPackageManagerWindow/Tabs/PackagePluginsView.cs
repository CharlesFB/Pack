using System;
using System.Collections.Generic;
using System.IO;
using MagicTavern.FrontEngine.MTPManager;
using UnityEngine.UIElements;

/// <summary>
/// Plugins Tab 视图：只负责容器搭建与数据绑定，把 taginfo.plugins 转成
/// ListView 行数据后交给 PackagePluginItemView 逐条渲染。
/// isInstalled（= isExactInstalledTag &amp;&amp; installed != null）为 false 时，
/// 全部插件按“未安装”绑定：名称浅灰色，Import/Unzip 按钮禁用。
/// 为 true 时，用 PathUtils.MakePluginKey(installed.PackageName, 文件名) 还原出该插件在
/// MTPackageInfo.Plugins 字典里的 key，查出对应的导入状态。
/// </summary>
public class PackagePluginsView : TabElementBase
{
    /// <summary>
    /// 需与 PackageZipItemView.uxml 里 item-row 的 height 保持一致，
    /// 否则 FixedHeight 虚拟化下条目会被裁切或留白。
    /// </summary>
    private const float ITEM_HEIGHT = 40f;

    private readonly struct PluginRow
    {
        public readonly string FileName;
        public readonly string PluginKey;
        public readonly bool IsInstalled;
        public readonly bool IsImported;

        public PluginRow(string fileName, string pluginKey, bool isInstalled, bool isImported)
        {
            FileName = fileName;
            PluginKey = pluginKey;
            IsInstalled = isInstalled;
            IsImported = isImported;
        }
    }

    private readonly VisualElement _container;
    private PackageJson _tagInfo;
    private MTPackageInfo _installed;
    private bool _isExactInstalledTag;

    public PackagePluginsView()
    {
        style.flexGrow = 1;

        _container = new VisualElement();
        _container.style.flexGrow = 1;

        AddStyleSheetIfExists(MTPUI.PROJECT_TAB_ITEM_STYLE_PATH);

        Add(_container);
    }

    /// <summary>
    /// 绑定 Plugins 展示数据并重建视图。
    /// </summary>
    public void BindData(PackageJson tagInfo, MTPackageInfo installed, bool isExactInstalledTag)
    {
        _tagInfo = tagInfo;
        _installed = installed;
        _isExactInstalledTag = isExactInstalledTag;
        Rebuild();
    }

    private void Rebuild()
    {
        _container.Clear();

        var plugins = _tagInfo?.plugins;
        if (plugins == null || plugins.Count == 0)
        {
            var placeHolder = new Label("No plugins available");
            placeHolder.AddToClassList("package-tab-view__empty-label");
            _container.Add(placeHolder);
            return;
        }

        bool isInstalled = _isExactInstalledTag && _installed != null;
        var rows = new List<PluginRow>();
        foreach (var pluginFileName in plugins)
        {
            ResolvePluginState(_installed, pluginFileName, isInstalled, out var pluginKey, out var isImported);
            rows.Add(new PluginRow(pluginFileName, pluginKey, isInstalled, isImported));
        }

        var listView = new ListView
        {
            itemsSource = rows,
            makeItem = () => new PackagePluginItemView(),
            bindItem = (element, index) =>
            {
                if (element is PackagePluginItemView itemView)
                {
                    var row = rows[index];
                    itemView.BindData(
                        row.FileName,
                        row.PluginKey,
                        isInstalled ? _installed : null,
                        row.IsInstalled,
                        row.IsImported,
                        RefreshSelf);
                }
            },
            fixedItemHeight = ITEM_HEIGHT,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            selectionType = SelectionType.None,
        };
        listView.style.flexGrow = 1;
        _container.Add(listView);
    }

    private void RefreshSelf()
    {
        BindData(_tagInfo, _installed, _isExactInstalledTag);
    }

    private static void ResolvePluginState(MTPackageInfo installed, string pluginFileName, bool isInstalled,
        out string pluginKey, out bool isImported)
    {
        pluginKey = installed != null
            ? PathUtils.MakePluginKey(installed.PackageName, Path.GetFileName(pluginFileName))
            : string.Empty;
        isImported = false;
        if (!isInstalled || installed?.Plugins == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(pluginKey))
        {
            foreach (var kvp in installed.Plugins)
            {
                if (!PathUtils.PluginKeysEqual(kvp.Key, pluginKey))
                {
                    continue;
                }

                pluginKey = kvp.Key;
                isImported = kvp.Value;
                return;
            }
        }

        string fileName = Path.GetFileName(pluginFileName);
        foreach (var kvp in installed.Plugins)
        {
            if (!string.Equals(PathUtils.GetPluginFileNameFromKey(kvp.Key), fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            pluginKey = kvp.Key;
            isImported = kvp.Value;
            return;
        }
    }
}
