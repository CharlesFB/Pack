using System;
using System.Collections.Generic;
using System.Linq;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class MTPackageManagerWindow : EditorWindow
{
    private const string SORT_ASC_LABEL = "Sort: Name (asc)";
    private const string SORT_DESC_LABEL = "Sort: Name (desc)";
    private const string SHOW_ALL_PREFIX = "Show All";
    private const string DEBUG_SUFFIX = "debug";
    
    [SerializeField]
    private VisualTreeAsset mtpWindow = null;

    private TabView _detailTabView;

    // NavigationBar
    private TextField _tfSearch;
    private Button _btnTextfieldClear;
    private DropdownField _ddSort;

    // lvProjectList
    private ListView _lvProjectList;
    private List<ProjectInfo> _displayedProjects = new List<ProjectInfo>();
    private ProjectInfo _selectedProject;
    private bool _sortDescending;

    // InfoView
    private Label _lbDisplayName;
    private Label _lbAuthorEmail;
    private DropdownField _ddVersion;
    private Label _lbPackageName;
    private Label _lbDebugType;
    private Label _lbReleaseType;
    private Label _lbDocumentation;
    private Label _lbLicenses;
    private string _documentationUrl;
    private bool _versionShowAll;

    private static IProjectInfoService ProjectInfoService => ServiceLocator.Get<IProjectInfoService>();
    private static IMTPackageInfoService MTPackageInfoService => ServiceLocator.Get<IMTPackageInfoService>();

    public static void ShowWindow()
    {
        MTPackageManagerWindow wnd = GetWindow<MTPackageManagerWindow>();
        wnd.titleContent = new GUIContent("MTPackageManagerWindow");
    }

    public void CreateGUI()
    {
        if (mtpWindow == null)
        {
            Debug.LogError("[MTPackageManagerWindow] mtpWindow (VisualTreeAsset) 未赋值，" +
                "请在 MTPackageManagerWindow.cs 的 Inspector -> Default References 中拖入 MTPackageManagerWindow.uxml");
            return;
        }

        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // Instantiate UXML
        VisualElement basic = mtpWindow.Instantiate();
        basic.StretchToParentSize();
        root.Add(basic);

        InitDetailTabView(root);
        InitNavigationBar(root);
        InitInfoView(root);
        InitProjectList(root);

        RefreshProjectList();
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

    /// <summary>
    /// 重新拉取 lvProjectList 数据源并刷新 InfoView（供测试脚本在改动假数据后强制刷新已打开的窗口）。
    /// CreateGUI 首次创建窗口时会自动调用一次，这里额外暴露出来仅用于窗口已存在、需要外部触发刷新的场景。
    /// </summary>
    public void RefreshProjectListView()
    {
        RefreshProjectList();
    }

    private void InitNavigationBar(VisualElement root)
    {
        _tfSearch = root.Q<TextField>("tfSearch");
        _btnTextfieldClear = root.Q<Button>("btnTextfieldClear");
        _ddSort = root.Q<DropdownField>("ddSort");

        if (_tfSearch != null)
        {
            // TextField 默认非 delayed，逐字符触发 ChangeEvent，天然满足“实时筛选”的要求
            _tfSearch.RegisterValueChangedCallback(_ => RefreshProjectList());
        }

        if (_btnTextfieldClear != null)
        {
            _btnTextfieldClear.clicked += OnClearSearchClicked;
        }

        if (_ddSort != null)
        {
            _ddSort.choices = new List<string> { SORT_ASC_LABEL, SORT_DESC_LABEL };
            _ddSort.SetValueWithoutNotify(SORT_ASC_LABEL);
            _ddSort.RegisterValueChangedCallback(OnSortChanged);
        }
    }

    private void OnClearSearchClicked()
    {
        if (_tfSearch == null)
        {
            return;
        }

        _tfSearch.SetValueWithoutNotify(string.Empty);
        RefreshProjectList();
    }

    private void OnSortChanged(ChangeEvent<string> evt)
    {
        _sortDescending = evt.newValue == SORT_DESC_LABEL;
        RefreshProjectList();
    }

    private void InitInfoView(VisualElement root)
    {
        var infoView = root.Q<VisualElement>("infoView");
        if (infoView == null)
        {
            return;
        }

        _lbDisplayName = infoView.Q<Label>("lbDisplayName");
        _lbAuthorEmail = infoView.Q<Label>("lbAuthorEmail");
        _ddVersion = infoView.Q<DropdownField>("ddVersion");
        _lbPackageName = infoView.Q<Label>("lbPackageName");
        _lbDebugType = infoView.Q<Label>("lbDebugType");
        _lbReleaseType = infoView.Q<Label>("lbReleaseType");
        _lbDocumentation = infoView.Q<Label>("lbDocumentation");
        _lbLicenses = infoView.Q<Label>("lbLicenses");

        if (_lbDocumentation != null)
        {
            _lbDocumentation.RegisterCallback<PointerUpEvent>(OnDocumentationClicked);
        }

        // Licenses 暂时不接真实数据，先在代码里关掉显示，等需求明确后再补。
        if (_lbLicenses != null)
        {
            _lbLicenses.style.display = DisplayStyle.None;
        }
    }

    private void OnDocumentationClicked(PointerUpEvent evt)
    {
        if (string.IsNullOrEmpty(_documentationUrl))
        {
            return;
        }

        Application.OpenURL(_documentationUrl);
    }

    private void InitProjectList(VisualElement root)
    {
        _lvProjectList = root.Q<ListView>("lvProjectList");
        if (_lvProjectList == null)
        {
            return;
        }

        _lvProjectList.makeItem = () => new ProjectItemView();
        _lvProjectList.bindItem = OnBindProjectItem;
        _lvProjectList.selectionType = SelectionType.Single;
        _lvProjectList.itemsSource = _displayedProjects;
        _lvProjectList.selectionChanged += OnProjectSelectionChanged;
    }

    private void OnBindProjectItem(VisualElement element, int index)
    {
        if (element is ProjectItemView itemView && index >= 0 && index < _displayedProjects.Count)
        {
            itemView.BindData(_displayedProjects[index]);
            itemView.SetSelected(_lvProjectList != null && _lvProjectList.selectedIndex == index);
        }
    }

    private void OnProjectSelectionChanged(IEnumerable<object> selectedItems)
    {
        _selectedProject = selectedItems?.OfType<ProjectInfo>().FirstOrDefault();
        UpdateSelectedItemVisual();
        RefreshInfoView();
    }

    /// <summary>
    /// 手动接管 lvProjectList 选中项的背景色，替代 Unity ListView 默认的蓝色选中高亮。
    /// </summary>
    private void UpdateSelectedItemVisual()
    {
        _lvProjectList?.RefreshItems();
    }

    /// <summary>
    /// 重新按黑名单 / 搜索 / 排序规则计算 lvProjectList 的数据源，并尽量保留原先选中的工程。
    /// </summary>
    private void RefreshProjectList()
    {
        string previousSelectedPackageName = _selectedProject?.PackageName;

        _displayedProjects = BuildFilteredSortedProjects();

        if (_lvProjectList != null)
        {
            _lvProjectList.itemsSource = _displayedProjects;
        }

        int indexToSelect = -1;
        if (!string.IsNullOrEmpty(previousSelectedPackageName))
        {
            indexToSelect = _displayedProjects.FindIndex(project =>
                string.Equals(project.PackageName, previousSelectedPackageName, StringComparison.Ordinal));
        }

        if (indexToSelect < 0 && _displayedProjects.Count > 0)
        {
            indexToSelect = 0;
        }

        _lvProjectList?.Rebuild();

        if (indexToSelect >= 0)
        {
            _selectedProject = _displayedProjects[indexToSelect];
            // 因此这里手动补一次选中背景色的刷新
            _lvProjectList?.SetSelectionWithoutNotify(new[] { indexToSelect });
        }
        else
        {
            _selectedProject = null;
            _lvProjectList?.ClearSelection();
        }

        UpdateSelectedItemVisual();
        RefreshInfoView();
    }

    private List<ProjectInfo> BuildFilteredSortedProjects()
    {
        var blackList = ConfigManager.Instance.Current?.BlackList ?? new List<string>();
        string searchText = _tfSearch?.value?.Trim() ?? string.Empty;

        var candidates = ProjectInfoService.GetAll()
            .Where(project => project != null && !blackList.Contains(project.ProjectId.ToString()))
            .ToList();

        if (!string.IsNullOrEmpty(searchText))
        {
            candidates = candidates
                .Where(project => ResolveDisplayNameForSort(project)
                    .IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        bool descending = _sortDescending;
        candidates.Sort((a, b) =>
        {
            // 第一优先级：已安装 > 未安装
            bool aInstalled = MTPackageInfoService.GetPackage(a.PackageName) != null;
            bool bInstalled = MTPackageInfoService.GetPackage(b.PackageName) != null;
            if (aInstalled != bInstalled)
            {
                return aInstalled ? -1 : 1;
            }

            // 第二优先级：按 DisplayName 升序/降序
            int cmp = string.Compare(ResolveDisplayNameForSort(a), ResolveDisplayNameForSort(b),
                StringComparison.OrdinalIgnoreCase);
            return descending ? -cmp : cmp;
        });

        return candidates;
    }

    private static string ResolveDisplayNameForSort(ProjectInfo project)
    {
        return ProjectDisplayUtils.GetDisplayName(project, ProjectDisplayUtils.ResolveDisplayTag(project));
    }

    /// <summary>
    /// 用当前选中工程刷新 InfoView（DisplayName / Author / Version 下拉 / Package）。
    /// Tag 解析规则同 ProjectItemView：已安装用安装的 Tag，否则用最高 Tag。
    /// </summary>
    private void RefreshInfoView()
    {
        _versionShowAll = false;

        if (_selectedProject == null)
        {
            SetInfoViewEmpty();
            return;
        }

        string tag = ProjectDisplayUtils.ResolveDisplayTag(_selectedProject);
        PopulateVersionDropdown(_selectedProject, tag);
        ApplyTagInfoToInfoView(_selectedProject, tag);
    }

    private void SetInfoViewEmpty()
    {
        SetLabel(_lbDisplayName, "N/A");
        SetLabel(_lbAuthorEmail, "N/A");
        SetLabel(_lbPackageName, "N/A");
        ApplyDocumentationLink(null);
        ClearDetailTabs();

        if (_ddVersion != null)
        {
            _ddVersion.UnregisterValueChangedCallback(OnVersionChanged);
            _ddVersion.choices = new List<string>();
            _ddVersion.SetValueWithoutNotify(string.Empty);
        }
    }

    private void ApplyTagInfoToInfoView(ProjectInfo project, string tag)
    {
        var tagInfo = ProjectDisplayUtils.GetTagInfo(project, tag);
        SetLabel(_lbDisplayName, tagInfo?.displayName ?? "N/A");
        SetLabel(_lbAuthorEmail, tagInfo?.author?.email ?? "N/A");
        SetLabel(_lbPackageName, tagInfo?.name ?? "N/A");
        if (!string.IsNullOrEmpty(tag)) SwitchType(tag.EndsWith(DEBUG_SUFFIX));
        ApplyDocumentationLink(tagInfo?.documentationUrl);
        RefreshDetailTabs(project, tag, tagInfo);
    }

    /// <summary>
    /// lbDocumentation 超链接：url 为空时用 tooltip 提示"没有提供文档"，不响应点击；
    /// 否则 tooltip 直接展示 url，点击时用 Application.OpenURL 打开。
    /// </summary>
    private void ApplyDocumentationLink(string url)
    {
        _documentationUrl = url;

        if (_lbDocumentation == null)
        {
            return;
        }

        _lbDocumentation.tooltip = string.IsNullOrEmpty(url) ? "没有提供文档" : url;
    }

    /// <summary>
    /// 按 Description / Version History / Dependencies / Samples / Plugins 的固定顺序重建详情 Tab。
    /// isExactInstalledTag 表示"当前查看的 Tag 恰好就是已安装的那个版本"：
    /// 只有这种情况下 Samples / Plugins 才能确定对应的是本地真实已安装的内容，否则一律视为未安装。
    /// </summary>
    private void RefreshDetailTabs(ProjectInfo project, string tag, PackageJson tagInfo)
    {
        ClearDetailTabs();

        if (project == null || tagInfo == null)
        {
            return;
        }

        var installed = MTPackageInfoService.GetPackage(project.PackageName);
        bool isExactInstalledTag = installed != null
            && string.Equals(installed.Tag, tag, StringComparison.Ordinal);

        AddDetailTab("Description", BuildDescriptionTab(tagInfo.description));
        AddDetailTab("Version History", BuildChangelogTab(tagInfo.changelog));
        AddDetailTab("Dependencies", BuildDependenciesPlaceholderTab());
        AddDetailTab("Samples", BuildSamplesTab(tagInfo, isExactInstalledTag));
        AddDetailTab("Plugins", BuildPluginsTab(tagInfo, installed, isExactInstalledTag));
    }

    private static VisualElement BuildDescriptionTab(string description)
    {
        var scrollView = new ScrollView();
        scrollView.style.flexGrow = 1;

        var label = new Label(string.IsNullOrEmpty(description) ? "N/A" : description);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.paddingTop = 8;
        label.style.paddingRight = 8;
        label.style.paddingBottom = 8;
        label.style.paddingLeft = 8;
        scrollView.Add(label);

        return scrollView;
    }

    private static VisualElement BuildChangelogTab(string changelog)
    {
        var changelogView = new PackageChangelogView();
        changelogView.BindData(changelog);
        return changelogView;
    }

    /// <summary>
    /// Dependencies 先留空占位，细节（展示依赖树 / PackageDependencyView 等）之后再补充。
    /// </summary>
    private static VisualElement BuildDependenciesPlaceholderTab()
    {
        var placeholder = new VisualElement();
        placeholder.style.flexGrow = 1;
        return placeholder;
    }

    /// <summary>
    /// 只有 isExactInstalledTag 为 true 时才会尝试用 Sample.FindByPackage 查找本地真实 Sample，
    /// 否则全部按"未安装"传 null 绑定（按钮禁用 + tooltip 提示），逻辑与旧工程一致。
    /// </summary>
    private static VisualElement BuildSamplesTab(PackageJson tagInfo, bool isExactInstalledTag)
    {
        var container = new VisualElement();
        container.style.flexGrow = 1;

        var samples = tagInfo?.samples;
        if (samples == null || samples.Count == 0)
        {
            container.Add(new Label("No samples available"));
            return container;
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

            var itemView = new PackageSampleItemView();
            itemView.BindData(sample.displayName, matched);
            container.Add(itemView);
        }

        return container;
    }

    /// <summary>
    /// isInstalled（= isExactInstalledTag &amp;&amp; installed != null）为 false 时，
    /// 全部插件按"未安装"绑定：名称浅灰色，Import/Unzip 按钮禁用。
    /// 为 true 时，用 PathUtils.BuildPluginLocalPath(installed, 文件名) 还原出该插件在
    /// MTPackageInfo.Plugins 字典里的 key，查出对应的导入状态。
    /// </summary>
    private static VisualElement BuildPluginsTab(PackageJson tagInfo, MTPackageInfo installed,
        bool isExactInstalledTag)
    {
        var container = new VisualElement();
        container.style.flexGrow = 1;

        var plugins = tagInfo?.plugins;
        if (plugins == null || plugins.Count == 0)
        {
            container.Add(new Label("No plugins available"));
            return container;
        }

        bool isInstalled = isExactInstalledTag && installed != null;
        foreach (var pluginFileName in plugins)
        {
            bool isImported = false;
            if (isInstalled)
            {
                string key = PathUtils.BuildPluginLocalPath(installed, pluginFileName);
                isImported = !string.IsNullOrEmpty(key)
                    && installed.Plugins != null
                    && installed.Plugins.TryGetValue(key, out var imported)
                    && imported;
            }

            var itemView = new PackageZipItemView();
            itemView.BindData(pluginFileName, isInstalled, isImported);
            container.Add(itemView);
        }

        return container;
    }

    private void PopulateVersionDropdown(ProjectInfo project, string selectedTag)
    {
        if (_ddVersion == null)
        {
            return;
        }

        _ddVersion.UnregisterValueChangedCallback(OnVersionChanged);

        var allTags = ProjectDisplayUtils.GetVisibleSortedTags(project);
        _ddVersion.choices = BuildVersionChoices(allTags, _versionShowAll, selectedTag);
        _ddVersion.SetValueWithoutNotify(selectedTag ?? string.Empty);

        _ddVersion.RegisterValueChangedCallback(OnVersionChanged);
    }

    /// <summary>
    /// 超过 MAX_VERSION_DROPDOWN_COUNT 条时只展示前 N 条 + 一个 "Show All (总数)" 选项，
    /// 点击该选项后展开显示全部（同老工程 PackageDetailView 的 Show All 交互）。
    /// 若当前选中 Tag 不在被截断的前 N 条里（如安装的是一个较旧的 Tag），会强制插入，避免下拉框显示不出当前值。
    /// </summary>
    private static List<string> BuildVersionChoices(List<string> allTags, bool showAll, string mustIncludeTag)
    {
        if (allTags.Count <= MTPUI.MAX_VERSION_DROPDOWN_COUNT || showAll)
        {
            return new List<string>(allTags);
        }

        var choices = allTags.Take(MTPUI.MAX_VERSION_DROPDOWN_COUNT).ToList();
        if (!string.IsNullOrEmpty(mustIncludeTag) && allTags.Contains(mustIncludeTag)
            && !choices.Contains(mustIncludeTag))
        {
            choices.Insert(0, mustIncludeTag);
        }

        choices.Add($"{SHOW_ALL_PREFIX} ({allTags.Count})");
        return choices;
    }

    private void OnVersionChanged(ChangeEvent<string> evt)
    {
        if (_selectedProject == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(evt.newValue) && evt.newValue.StartsWith(SHOW_ALL_PREFIX, StringComparison.Ordinal))
        {
            // 点的是 "Show All"：展开全部 Tag，恢复到点击前的选中值，不当作一次真正的版本切换
            _versionShowAll = true;
            PopulateVersionDropdown(_selectedProject, evt.previousValue);
            return;
        }

        ApplyTagInfoToInfoView(_selectedProject, evt.newValue);
    }

    private static void SetLabel(Label label, string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    private void SwitchType(bool isDebug)
    {
        _lbReleaseType.style.display = isDebug ? DisplayStyle.None : DisplayStyle.Flex;
        _lbDebugType.style.display = isDebug ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
