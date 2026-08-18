using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class MTPackageManagerWindow : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset m_VisualTreeAsset = default;

    private TabView _detailTabView;

    [MenuItem("Window/UI Toolkit/MTPackageManagerWindow")]
    public static void ShowExample()
    {
        MTPackageManagerWindow wnd = GetWindow<MTPackageManagerWindow>();
        wnd.titleContent = new GUIContent("MTPackageManagerWindow");
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // Instantiate UXML
        VisualElement labelFromUXML = m_VisualTreeAsset.Instantiate();
        labelFromUXML.StretchToParentSize();
        root.Add(labelFromUXML);

        InitDetailTabView(root);
    }

    /// <summary>
    /// 把通用 TabView 的按钮行/内容区分别接到 UXML 里的 "tabs" / "tabView" 占位节点上。
    /// </summary>
    private void InitDetailTabView(VisualElement root)
    {
        var tabsBar = root.Q<VisualElement>("tabs");
        var tabViewContainer = root.Q<VisualElement>("tabView");
        if (tabsBar == null || tabViewContainer == null)
        {
            return;
        }

        _detailTabView = new TabView();
        tabsBar.Add(_detailTabView.TabBar);
        tabViewContainer.Add(_detailTabView.ContentContainer);
    }

    /// <summary>
    /// 向包详情 Tab 区添加一个 Tab（供业务逻辑及测试脚本调用）。
    /// </summary>
    public void AddDetailTab(string tabName, VisualElement content)
    {
        _detailTabView?.AddTab(tabName, content);
    }

    /// <summary>
    /// 清空当前所有详情 Tab，切换选中 package 时用于重建 Tab 集合。
    /// </summary>
    public void ClearDetailTabs()
    {
        _detailTabView?.ClearTabs();
    }
}
