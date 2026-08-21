using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

/// <summary>
/// 通用可复用 Tab 组件：Tab 按钮与对应内容均由代码动态添加/移除。
/// 外观（颜色、间距、hover/active 状态）统一走 USS（见 TabView.uss），
/// 本类只负责结构组装与激活态切换逻辑。
/// </summary>
public class TabView : VisualElement
{
    private const string USS_ROOT = "tab-view";
    private const string USS_TAB_BAR = "tab-view__tab-bar";
    private const string USS_CONTENT_CONTAINER = "tab-view__content-container";
    private const string USS_PAGE = "tab-view__page";
    private const string USS_TAB_BUTTON = "tab-button";
    private const string USS_TAB_BUTTON_ACTIVE = "tab-button--active";

    private readonly VisualElement _tabBar;
    private readonly VisualElement _contentContainer;
    private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
    private readonly Dictionary<string, VisualElement> _tabPages = new Dictionary<string, VisualElement>();
    private string _activeTabName;

    public string ActiveTabName => _activeTabName;

    /// <summary>
    /// Tab 按钮行。若外部布局需要把按钮行和内容区分别挂到不同容器
    /// Add(tabView.TabBar) / Add(tabView.ContentContainer)。
    /// </summary>
    public VisualElement TabBar => _tabBar;

    /// <summary>Tab 内容区，见 <see cref="TabBar"/> 说明。</summary>
    public VisualElement ContentContainer => _contentContainer;

    public TabView()
    {
        AddToClassList(USS_ROOT);

        _tabBar = new VisualElement { name = "TabBar" };
        _tabBar.AddToClassList(USS_TAB_BAR);
        Add(_tabBar);

        _contentContainer = new VisualElement { name = "TabContentContainer" };
        _contentContainer.AddToClassList(USS_CONTENT_CONTAINER);
        Add(_contentContainer);
    }

    /// <summary>
    /// 添加一个 Tab。若 tabName 已存在则忽略。第一个添加的 Tab 会被自动激活。
    /// </summary>
    public void AddTab(string tabName, VisualElement content)
    {
        if (string.IsNullOrEmpty(tabName) || content == null || _tabButtons.ContainsKey(tabName))
        {
            return;
        }

        var page = new VisualElement { name = tabName };
        page.AddToClassList(USS_PAGE);
        page.style.display = DisplayStyle.None;
        page.Add(content);
        _contentContainer.Add(page);
        _tabPages[tabName] = page;

        var tabButton = new Button(() => SelectTab(tabName)) { text = tabName };
        tabButton.AddToClassList(USS_TAB_BUTTON);
        _tabBar.Add(tabButton);
        _tabButtons[tabName] = tabButton;

        if (_activeTabName == null)
        {
            SelectTab(tabName);
        }
    }

    /// <summary>
    /// 移除一个 Tab。若移除的是当前激活 Tab，则自动切换到剩余 Tab 中的第一个。
    /// </summary>
    public void RemoveTab(string tabName)
    {
        if (_tabButtons.TryGetValue(tabName, out var button))
        {
            _tabBar.Remove(button);
            _tabButtons.Remove(tabName);
        }

        if (_tabPages.TryGetValue(tabName, out var page))
        {
            _contentContainer.Remove(page);
            _tabPages.Remove(tabName);
        }

        if (_activeTabName != tabName)
        {
            return;
        }

        _activeTabName = null;
        var nextTabName = _tabPages.Keys.FirstOrDefault();
        if (nextTabName != null)
        {
            SelectTab(nextTabName);
        }
    }

    /// <summary>
    /// 清空所有 Tab，便于切换 package 时重新组装不同的 Tab 集合。
    /// </summary>
    public void ClearTabs()
    {
        _tabBar.Clear();
        _contentContainer.Clear();
        _tabButtons.Clear();
        _tabPages.Clear();
        _activeTabName = null;
    }

    public void SelectTab(string tabName)
    {
        if (tabName == _activeTabName || !_tabPages.ContainsKey(tabName))
        {
            return;
        }

        if (_activeTabName != null)
        {
            if (_tabPages.TryGetValue(_activeTabName, out var prevPage))
            {
                prevPage.style.display = DisplayStyle.None;
            }

            if (_tabButtons.TryGetValue(_activeTabName, out var prevButton))
            {
                prevButton.RemoveFromClassList(USS_TAB_BUTTON_ACTIVE);
            }
        }

        _tabPages[tabName].style.display = DisplayStyle.Flex;
        _tabButtons[tabName].AddToClassList(USS_TAB_BUTTON_ACTIVE);
        _activeTabName = tabName;
    }
}
