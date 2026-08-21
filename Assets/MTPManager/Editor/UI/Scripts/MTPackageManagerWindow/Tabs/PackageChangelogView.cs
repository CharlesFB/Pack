using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine.UIElements;

/// <summary>
/// 包更新日志 Tab 视图：只负责容器搭建、数据绑定与何时刷新，
/// 具体的 Markdown 解析/渲染见 "MarkdownUIRenderer"。
/// </summary>
public class PackageChangelogView : TabElementBase
{
    private readonly VisualElement _changelogContainer;
    private string _changelogContent;

    public PackageChangelogView()
    {
        AddToClassList("package-changelog-view");

        AddStyleSheetIfExists(MTPUI.PACKAGE_CHANGELOG_VIEW_STYLE_PATH);
        AddStyleSheetIfExists(MTPUI.MARKDOWN_STYLE_PATH);
        AddStyleSheetIfExists(MTPUI.PROJECT_TAB_ITEM_STYLE_PATH);

        _changelogContainer = new VisualElement { name = "ChangelogContainer" };
        _changelogContainer.AddToClassList("package-changelog-view__container");
        Add(_changelogContainer);
    }

    /// <summary>
    /// 绑定 Changelog 原始 Markdown 内容并刷新视图。
    /// </summary>
    public void BindData(string changelogContent)
    {
        _changelogContent = changelogContent;
        RefreshView();
    }

    private void RefreshView()
    {
        _changelogContainer.Clear();

        if (string.IsNullOrEmpty(_changelogContent))
        {
            var noChangelogLabel = new Label("No changelog available");
            noChangelogLabel.AddToClassList("package-tab-view__empty-label");
            _changelogContainer.Add(noChangelogLabel);
            return;
        }

        var scrollView = new ScrollView();
        scrollView.AddToClassList("package-changelog-view__scroll");

        MarkdownUIRenderer.RenderInto(scrollView, _changelogContent);

        _changelogContainer.Add(scrollView);
    }
}
