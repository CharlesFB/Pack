using System;
using System.Collections.Generic;
using System.Linq;
using MagicTavern.FrontEngine.MTPManager;
using UnityEngine.UIElements;

/// <summary>
/// Samples Tab 视图：只负责容器搭建与数据绑定，把 taginfo.samples 转成
/// ListView 行数据后交给 PackageSampleItemView 逐条渲染。
/// 只有 isExactInstalledTag 为 true 时才会尝试用 Sample.FindByPackage 查找本地真实 Sample，
/// 否则全部按“未安装”传 null 绑定（按钮禁用 + tooltip 提示）。
/// </summary>
public class PackageSamplesView : TabElementBase
{
    /// <summary>
    /// 需与 PackageSampleItemView.uxml 里 item-row 的 height 保持一致，
    /// 否则 FixedHeight 虚拟化下条目会被裁切或留白。
    /// </summary>
    private const float ITEM_HEIGHT = 40f;

    private readonly struct SampleRow
    {
        public readonly string SampleName;
        public readonly UnityEditor.PackageManager.UI.Sample? Sample;

        public SampleRow(string sampleName, UnityEditor.PackageManager.UI.Sample? sample)
        {
            SampleName = sampleName;
            Sample = sample;
        }
    }

    private readonly VisualElement _container;

    public PackageSamplesView()
    {
        style.flexGrow = 1;

        _container = new VisualElement();
        _container.style.flexGrow = 1;
        AddStyleSheetIfExists(MTPUI.PROJECT_TAB_ITEM_STYLE_PATH);
        Add(_container);
    }

    /// <summary>
    /// 绑定 Samples 展示数据并重建视图。
    /// </summary>
    public void BindData(PackageJson tagInfo, bool isExactInstalledTag)
    {
        _container.Clear();

        var samples = tagInfo?.samples;
        if (samples == null || samples.Count == 0)
        {
            var label = new Label("No samples available");
            label.AddToClassList("package-tab-view__empty-label");
            _container.Add(label);
            return;
        }

        UnityEditor.PackageManager.UI.Sample[] localSamples = null;
        if (isExactInstalledTag && !string.IsNullOrEmpty(tagInfo.name))
        {
            try
            {
                localSamples = UnityEditor.PackageManager.UI.Sample
                    .FindByPackage(tagInfo.name, tagInfo.version)?.ToArray();
            }
            catch (Exception)
            {
                localSamples = null;
            }
        }

        var rows = new List<SampleRow>();
        foreach (var sample in samples)
        {
            UnityEditor.PackageManager.UI.Sample? matched = null;
            if (localSamples != null)
            {
                foreach (var localSample in localSamples)
                {
                    if (string.Equals(localSample.displayName, sample.displayName, StringComparison.Ordinal))
                    {
                        matched = localSample;
                        break;
                    }
                }
            }

            rows.Add(new SampleRow(sample.displayName, matched));
        }

        var listView = new ListView
        {
            itemsSource = rows,
            makeItem = () => new PackageSampleItemView(),
            bindItem = (element, index) =>
            {
                if (element is PackageSampleItemView itemView)
                {
                    var row = rows[index];
                    itemView.BindData(row.SampleName, row.Sample);
                }
            },
#if UNITY_2021_2_OR_NEWER
            fixedItemHeight = ITEM_HEIGHT,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
#else
            itemHeight = (int)ITEM_HEIGHT,
#endif
            selectionType = SelectionType.None,
        };
        listView.style.flexGrow = 1;
        _container.Add(listView);
    }
}
