using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

public class MTPackageManagerWindow : EditorWindow
{
    private const string SORT_ASC_LABEL = "Sort: Name (asc)";
    private const string SORT_DESC_LABEL = "Sort: Name (desc)";
    private const string SHOW_ALL_PREFIX = "Show All";
    private const string DEBUG_SUFFIX = "debug";
    private const string PACKAGE_TYPE_NORMAL_LABEL = "Package: FrontEngine";
    private const string PACKAGE_TYPE_EXTERNAL_LABEL = "Package: External";
    private const string NO_VERSION_LABEL = "无可用版本";
    private const string INSTALL_LABEL = "Install";
    private const string UPDATE_LABEL = "Update";
    private const string INSTALL_LOCAL_LABEL = "Install(Local)";
    private const string UPDATE_LOCAL_LABEL = "Update(Local)";
    
    [SerializeField]
    private VisualTreeAsset mtpWindow = null;

    private TabView _detailTabView;

    // NavigationBar
    private TextField _tfSearch;
    private Button _btnTextfieldClear;
    private DropdownField _ddSort;
    private DropdownField _ddPackage;
    private Button _btnSetting;
    private Button _btnReset;
    private Button _btnClear;
    private Button _btnQuickInstall;
    private Button _btnDependenciesGraph;

    // lvProjectList
    private ListView _lvProjectList;
    private List<ProjectInfo> _displayedProjects = new List<ProjectInfo>();
    private ProjectInfo _selectedProject;
    private Label _lbUpdateTime;
    private Button _btnRefresh;
    [NonSerialized] private bool _isRefreshing;
    [NonSerialized] private bool _isInstalling;
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
    private Button _btnInstall;
    private Button _btnUninstall;
    private string _documentationUrl;
    private bool _versionShowAll;
    private int _tagInfoLoadSeq;

    private static IProjectInfoService ProjectInfoService => ServiceLocator.Get<IProjectInfoService>();
    private static IPackageJsonService PackageJsonService => ServiceLocator.Get<IPackageJsonService>();
    private static IMTPackageInfoService MTPackageInfoService => ServiceLocator.Get<IMTPackageInfoService>();
    private static IRootPackageService RootPackageService => ServiceLocator.Get<IRootPackageService>();
    private static IPackageSyncService PackageSyncService => ServiceLocator.Get<IPackageSyncService>();
    private static IPackageInstallerService PackageInstallerService => ServiceLocator.Get<IPackageInstallerService>();
    private static ILocalPackageService LocalPackageService => ServiceLocator.Get<ILocalPackageService>();

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

        // EditorWindow 会序列化字段；安装中域重载后 _isInstalling 不能继续为 true，否则按钮会一直禁用
        _isInstalling = false;
        _isRefreshing = false;
        RefreshProjectList();
    }
    
    #region TabView
    
    /// <summary>
    /// 按 Description / Version History / Dependencies / Samples / Plugins 的固定顺序重建详情 Tab。
    /// isExactInstalledTag 表示"当前查看的 Tag 恰好就是已安装的那个版本"
    /// 只有这种情况下 Samples / Plugins 才能确定对应的是本地真实已安装的内容，否则一律视为未安装。
    /// </summary>
    private void RefreshDetailTabs(ProjectInfo project, string tag, PackageJson tagInfo)
    {
        ClearDetailTabs();

        if (project == null || tagInfo == null)
        {
            return;
        }

        var installed = FindInstalled(project);
        bool isExactInstalledTag = installed != null
            && string.Equals(installed.Tag, tag, StringComparison.Ordinal);

        AddDetailTab("Description", BuildDescriptionTab(tagInfo.description));
        AddDetailTab("Version History", BuildChangelogTab(tagInfo.changelog));
        AddDetailTab("Dependencies", BuildDependenciesTab(tagInfo));
        AddDetailTab("Samples", BuildSamplesTab(tagInfo, isExactInstalledTag));
        AddDetailTab("Plugins", BuildPluginsTab(tagInfo, installed, isExactInstalledTag));
    }

    ///  ------ 下面依次为5个tab的构建 ------
    /// <summary>
    /// 构建描述标签，装填UTF-8解析后的包描述信息
    /// </summary>
    private static VisualElement BuildDescriptionTab(string description)
    {
        var scrollView = new ScrollView();
        scrollView.style.flexGrow = 1;
        scrollView.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(MTPUI.PROJECT_TAB_ITEM_STYLE_PATH));

        var label = new Label();
        if (string.IsNullOrEmpty(description))
        {
            label.AddToClassList("package-tab-view__empty-label");
            label.text = "No description available";
        }
        else
        {
            label.AddToClassList("package-tab-view__label");
            label.text = description;
        }
        
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.paddingTop = 8;
        label.style.paddingRight = 8;
        label.style.paddingBottom = 8;
        label.style.paddingLeft = 8;
        scrollView.Add(label);

        return scrollView;
    }

    /// <summary>
    /// 构建打印当前tag的changelog的标签，用一层markdown解析做包装
    /// </summary>
    private static VisualElement BuildChangelogTab(string changelog)
    {
        var changelogView = new PackageChangelogView();
        changelogView.BindData(changelog);
        return changelogView;
    }

    /// <summary>
    /// Dependencies Tab 的容器搭建 / 数据装换逻辑见 "PackageDependenciesView"
    /// </summary>
    private static VisualElement BuildDependenciesTab(PackageJson tagInfo)
    {
        var view = new PackageDependenciesView();
        view.BindData(tagInfo);
        return view;
    }

    /// <summary>
    /// Samples Tab 的容器搭建 / 数据装换逻辑见 "PackageSampleView"
    /// </summary>
    private static VisualElement BuildSamplesTab(PackageJson tagInfo, bool isExactInstalledTag)
    {
        var view = new PackageSamplesView();
        view.BindData(tagInfo, isExactInstalledTag);
        return view;
    }

    /// <summary>
    /// Plugins Tab 的容器搭建 / 数据装换逻辑见 "PackagePluginsView"
    /// </summary>
    private static VisualElement BuildPluginsTab(PackageJson tagInfo, MTPackageInfo installed,
        bool isExactInstalledTag)
    {
        var view = new PackagePluginsView();
        view.BindData(tagInfo, installed, isExactInstalledTag);
        return view;
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
    /// 向包详情 Tab 区添加一个 Tab
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

    #endregion TabView

    #region NavigationBar

    private void InitNavigationBar(VisualElement root)
    {
        _tfSearch = root.Q<TextField>("tfSearch");
        _btnTextfieldClear = root.Q<Button>("btnTextfieldClear");
        _ddSort = root.Q<DropdownField>("ddSort");
        _ddPackage = root.Q<DropdownField>("ddPackage");
        
        _btnSetting = root.Q<Button>("btnSetting");
        _btnReset = root.Q<Button>("btnReset");
        _btnClear = root.Q<Button>("btnClear");
        _btnQuickInstall = root.Q<Button>("btnQuickInstall");
        _btnDependenciesGraph = root.Q<Button>("btnDependenciesGraph");

        if (_btnSetting != null)
        {
            _btnSetting.clicked += OnSettingClicked;
        }

        if (_btnReset != null)
        {
            _btnReset.clicked += OnResetClicked;
        }

        if (_btnClear != null)
        {
            _btnClear.clicked += OnClearClicked;
        }

        if (_btnQuickInstall != null)
        {
            _btnQuickInstall.clicked += OnQuickInstallClicked;
        }

        if (_btnDependenciesGraph != null)
        {
            _btnDependenciesGraph.clicked += OnDependenciesGraphClicked;
        }

        if (_tfSearch != null)
        {
            // TextField 默认非 delayed，逐字符触发 ChangeEvent
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

        if (_ddPackage != null)
        {
            // 目前只有 Normal 类型的包接入该窗口，因此只提供这一个选项；
            // 后续如果要接入 ExPlugin 等其它 PackageType，在这里追加选项 + 映射即可。
            _ddPackage.choices = new List<string> { PACKAGE_TYPE_NORMAL_LABEL };
            _ddPackage.SetValueWithoutNotify(PACKAGE_TYPE_NORMAL_LABEL);
            _ddPackage.RegisterValueChangedCallback(_ => RefreshProjectList());
        }
    }

    private void OnDependenciesGraphClicked()
    {
        DependenciesGraphWindow.Load();
    }

    private void OnQuickInstallClicked()
    {
        QuickInstallWindow.ShowExample();
    }
    
    private void OnClearClicked()
    {
        // 暂时没什么要做的
    }
    
    private async void OnResetClicked()
    {
        if (_isRefreshing || _isInstalling)
        {
            return;
        }

        var ret = EditorUtility.DisplayDialog(
                "确认重置 MTP 管理器状态",
                "你正在重置 MTP 状态，该操作将会卸载所有 MTP 管理的包（及其携带插件与可能存在的 UPM 附属依赖），是否继续",
                "确认重置",
                "取消"
            );
        if (!ret)
        {
            return;
        }

        _isRefreshing = true;
        _btnRefresh?.SetEnabled(false);
        _btnReset?.SetEnabled(false);
        RefreshActionButtons();

        try
        {
            EditorUtility.DisplayProgressBar("Reset MTP", "Uninstalling MTP packages...", 0.3f);
            bool ok = await PackageSyncService.ResetAsync();
            if (!ok)
            {
                EditorUtility.DisplayDialog(
                    "重置未完成",
                    "重置未能同步完成，部分包可能仍处于安装状态。请查看 Console 后重试。",
                    "确定");
            }

            EditorUtility.DisplayProgressBar("Reset MTP", "Refreshing list...", 0.9f);
            RefreshProjectList();
        }
        catch (Exception e)
        {
            LM.LogWarning($"[MTPackageManagerWindow] Reset failed. Error: {e.Message}");
            EditorUtility.DisplayDialog("重置失败", e.Message, "确定");
        }
        finally
        {
            _isRefreshing = false;
            _btnRefresh?.SetEnabled(true);
            _btnReset?.SetEnabled(true);
            RefreshActionButtons();
            EditorUtility.ClearProgressBar();
        }
    }

    private void OnSettingClicked()
    {
        MTPSettingWindow.ShowSettingWindow();
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

    #endregion NavigationBar

    #region ProjectList
    /// <summary>
    /// 重新拉取 lvProjectList 数据源并刷新 InfoView。
    /// CreateGUI 首次创建窗口时会自动调用一次，这里提供对外手动刷新的接口。
    /// </summary>
    public void RefreshProjectListView()
    {
        RefreshProjectList();
    }

    /// <summary>
    /// 刷新当前已打开的 MTP 窗口（安装批次结束 / 域重载后的列表状态）。
    /// delayCall 避免打断正在进行的 EditorWindow 重建。
    /// </summary>
    public static void RefreshOpenWindows()
    {
        EditorApplication.delayCall += RefreshOpenWindowsImmediate;
    }

    private static void RefreshOpenWindowsImmediate()
    {
        var windows = Resources.FindObjectsOfTypeAll<MTPackageManagerWindow>();
        if (windows == null)
        {
            return;
        }

        foreach (var window in windows)
        {
            if (window == null)
            {
                continue;
            }

            // 安装批次已结束（或域重载后的恢复已跑完），不能再按“安装中”禁用按钮
            window._isInstalling = false;
            window._isRefreshing = false;
            window.RefreshProjectListView();
        }
    }
    
    private void InitProjectList(VisualElement root)
    {
        _lbUpdateTime = root.Q<Label>("lbUpdateTime");
        _btnRefresh = root.Q<Button>("btnRefresh");
        _lvProjectList = root.Q<ListView>("lvProjectList");

        if (_btnRefresh != null)
        {
            _btnRefresh.clicked += OnRefreshClicked;
        }
        
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
    /// 点一下就强制从 Git 拉取最新数据。
    /// </summary>
    private async void OnRefreshClicked()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        _btnRefresh?.SetEnabled(false);
        RefreshActionButtons();
        
        try
        {
            EditorUtility.DisplayProgressBar("Refreshing Project List", "Pulling project info list...", 0.2f);
            await ProjectInfoService.PullFromGitAsync(force: true);
            EditorUtility.DisplayProgressBar("Refreshing Project List", "Pulling Package json cache...", 0.6f);
            await PackageJsonService.PullFromGitAsync(force: true);
            EditorUtility.DisplayProgressBar("Refreshing Project List", "Checking for packages to update...", 0.8f);
            // var updatablePackages = CheckNewVersion();
            // if (updatablePackages.Count > 0)
            // {
            //     // TODO : 需要更新的tag，显示更新图标
            //     LM.Log($"[MTPackageManagerWindow] Refresh 完成，发现 {updatablePackages.Count} 个已安装包有新版本可更新: " +
            //         string.Join(", ", updatablePackages));
            // }
            // else
            // {
            //     LM.Log("[MTPackageManagerWindow] Refresh 完成，未发现新版本。");
            // }
            //
            // // 内部会重建 _displayedProjects、刷新 lvProjectList / lbUpdateTime / InfoView，
            // // ProjectItemView.RefreshStatusIcon 也会在这次刷新里按最新数据重新计算"是否可更新"图标。
            // EditorUtility.DisplayProgressBar("Refreshing Project List", "Refreshing list to show...", 0.9f);
            RefreshProjectList();
            EditorUtility.DisplayProgressBar("Refreshing Project List", "Finished", 1f);
        }
        catch (Exception e)
        {
            LM.LogWarning($"[MTPackageManagerWindow] Failed to refresh project cache. Error: {e.Message}]");
        }
        finally
        {
            _isRefreshing = false;
            _btnRefresh?.SetEnabled(true);
            RefreshActionButtons();
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>
    /// 拉取最新数据后检查所有手动安装的包（依赖树里 Depth == 0 的根节点）
    /// 是否有比当前安装 Tag 更高的可见 Tag 可供更新，返回有更新的包名列表。
    /// 判定规则与 ProjectItemView.RefreshStatusIcon 保持一致（同样用 GetVisibleSortedTags 取最高可见 Tag）。
    /// </summary>
    private List<string> CheckNewVersion()
    {
        var updatable = new List<string>();

        foreach (var installed in MTPackageInfoService.GetAllPackages())
        {
            if (installed == null || installed.Depth != 0 || string.IsNullOrEmpty(installed.PackageName))
            {
                continue;
            }

            var project = ProjectInfoService.GetByUrl(installed.Url, installed.Tag)
                          ?? ProjectInfoService.GetByUrl(installed.Url);
            if (project == null)
            {
                continue;
            }

            var visibleTags = ProjectDisplayUtils.GetVisibleSortedTags(project);
            if (visibleTags.Count > 0 && !string.Equals(visibleTags[0], installed.Tag, StringComparison.Ordinal))
            {
                updatable.Add(installed.PackageName);
            }
        }

        return updatable;
    }

    private void UpdateRefreshTime()
    {
        if (_lbUpdateTime != null)
        {
            _lbUpdateTime.text = $"Last Update {ProjectInfoService.RefreshTime:yyyy-MM-dd HH:mm:ss}";
        }
    }

    /// <summary>
    /// 手动接管 lvProjectList 选中项的背景色，替代 Unity ListView 默认的蓝色选中高亮。
    /// </summary>
    private void UpdateSelectedItemVisual()
    {
        _lvProjectList?.RefreshItems();
    }

    /// <summary>
    /// 重新按黑名单 / 模糊搜索 / 排序规则计算 lvProjectList 的数据源，并尽量保留原先选中的工程。
    /// </summary>
    private void RefreshProjectList()
    {
        int previousSelectedProjectId = _selectedProject?.ProjectId ?? 0;
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
                (previousSelectedProjectId <= 0 || project.ProjectId == previousSelectedProjectId)
                && string.Equals(project.PackageName, previousSelectedPackageName, StringComparison.Ordinal));
        }

        if (indexToSelect < 0 && _displayedProjects.Count > 0)
        {
            indexToSelect = 0;
        }

        _lvProjectList?.RefreshListView();

        if (indexToSelect >= 0)
        {
            _selectedProject = _displayedProjects[indexToSelect];
            _lvProjectList?.SetSelectionWithoutNotify(new[] { indexToSelect });
        }
        else
        {
            _selectedProject = null;
            _lvProjectList?.ClearSelection();
        }

        UpdateSelectedItemVisual(); // 更新选中项
        UpdateRefreshTime();        // 更新上次刷新时间
        RefreshInfoView();          // 更新InfoView的内容
    }

    private List<ProjectInfo> BuildFilteredSortedProjects()
    {
        string searchText = _tfSearch?.value?.Trim() ?? string.Empty;
        var candidates = ProjectInfoService.GetAll();
        bool descending = _sortDescending;
        return ProjectDisplayUtils.BuildFilteredPackageList(candidates, descending, searchText);
    }
    #endregion ProjectList

    #region InfoView

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
        _btnInstall = infoView.Q<Button>("btnInstall");
        _btnUninstall = infoView.Q<Button>("btnUninstall");

        if (_lbDocumentation != null)
        {
            _lbDocumentation.RegisterCallback<PointerUpEvent>(OnDocumentationClicked);
        }

        if (_btnInstall != null)
        {
            _btnInstall.clicked += OnInstallButtonClicked;
        }

        if (_btnUninstall != null)
        {
            _btnUninstall.clicked += OnUninstallPackage;
        }

        // Licenses 暂时不接真实数据，先在代码里关掉显示。
        if (_lbLicenses != null)
        {
            _lbLicenses.style.display = DisplayStyle.None;
        }

        if (_btnUninstall != null)
        {
            _btnUninstall.text = "Uninstall";
        }

        RefreshActionButtons();
    }

    private void OnDocumentationClicked(PointerUpEvent evt)
    {
        if (string.IsNullOrEmpty(_documentationUrl))
        {
            return;
        }

        Application.OpenURL(_documentationUrl);
    }

    /// <summary>
    /// 用当前选中工程刷新 InfoView。
    /// Tag 解析规则同 ProjectItemView，已安装用安装的 Tag，否则用最高 Tag。
    /// </summary>
    private void RefreshInfoView()
    {
        _versionShowAll = false;

        if (_selectedProject == null)
        {
            LM.Log("[MTPackageManagerWindow] 空工程选择");
            SetInfoViewEmpty();
            return;
        }

        string tag = ProjectDisplayUtils.ResolveDisplayTag(_selectedProject);
        PopulateVersionDropdown(_selectedProject, tag);
        ApplyTagInfoToInfoView(_selectedProject, tag);
    }

    private void SetInfoViewEmpty()
    {
        _tagInfoLoadSeq++;
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

        RefreshActionButtons();
    }

    private async void ApplyTagInfoToInfoView(ProjectInfo project, string tag)
    {
        // Debug.Log("调整tag： " + tag);
        int seq = ++_tagInfoLoadSeq;
        var tagInfo = ProjectDisplayUtils.GetTagInfo(project, tag);
        SetLabel(_lbDisplayName, ProjectDisplayUtils.GetDisplayName(project, tag));
        SetLabel(_lbAuthorEmail, tagInfo?.author?.email ?? "N/A");
        SetLabel(_lbPackageName, tagInfo?.name ?? "N/A");
        SwitchType(tag);
        ApplyDocumentationLink(tagInfo?.documentationUrl);
        RefreshDetailTabs(project, tag, tagInfo);
        RefreshActionButtons();

        if (project == null || project.ProjectId <= 0 || string.IsNullOrEmpty(tag) || tagInfo == null)
        {
            return;
        }

        bool filled = await PackageJsonService.EnsurePluginsAndChangelogAsync(project.ProjectId, tag);
        if (!filled || seq != _tagInfoLoadSeq)
        {
            return;
        }

        RefreshDetailTabs(project, tag, ProjectDisplayUtils.GetTagInfo(project, tag));
        RefreshActionButtons();
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

    private void PopulateVersionDropdown(ProjectInfo project, string selectedTag)
    {
        if (_ddVersion == null)
        {
            return;
        }

        _ddVersion.UnregisterValueChangedCallback(OnVersionChanged);

        var allTags = ProjectDisplayUtils.GetVisibleSortedTags(project);
        _ddVersion.choices = BuildVersionChoices(allTags, _versionShowAll, selectedTag);
        _ddVersion.SetValueWithoutNotify(allTags.Count > 0 ? (selectedTag ?? string.Empty) : NO_VERSION_LABEL);

        _ddVersion.RegisterValueChangedCallback(OnVersionChanged);
    }

    /// <summary>
    /// 超过 MAX_VERSION_DROPDOWN_COUNT 条时只展示前 N 条 + 一个 "Show All (总数)" 选项，
    /// 点击该选项后展开显示全部（同老工程 PackageDetailView 的 Show All 交互）。
    /// 若当前选中 Tag 不在被截断的前 N 条里（如安装的是一个较旧的 Tag），会强制插入，避免下拉框显示不出当前值。
    /// 没有任何可展示的 Tag 时（project 没有 Tag，或非 debug 模式下全被过滤掉），展示一条占位选项。
    /// </summary>
    private static List<string> BuildVersionChoices(List<string> allTags, bool showAll, string mustIncludeTag)
    {
        if (allTags.Count == 0)
        {
            return new List<string> { NO_VERSION_LABEL };
        }

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
            // 展开全部 Tag，恢复到点击前的选中值，不当作一次真正的版本切换
            _versionShowAll = true;
            PopulateVersionDropdown(_selectedProject, evt.previousValue);
            // // 模拟一次点击事件，强行把下拉列表再次展开 (这部分代码按需去掉吧，用于用户体验优化的)
            // _ddVersion.schedule.Execute(() =>
            // {
            //     using var mouseDownEvent = MouseDownEvent.GetPooled();
            //     mouseDownEvent.target = _ddVersion;
            //     _ddVersion.SendEvent(mouseDownEvent);
            // });
            return;
        }

        if (string.Equals(evt.newValue, NO_VERSION_LABEL, StringComparison.Ordinal))
        {
            // 占位选项，不是真正的 Tag，不触发信息刷新
            return;
        }

        ApplyTagInfoToInfoView(_selectedProject, evt.newValue);
    }
    
    private void SwitchType(string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            _lbReleaseType.style.display = DisplayStyle.None;
            _lbDebugType.style.display = DisplayStyle.None;
            return;
        }

        if (_lbReleaseType == null || _lbDebugType == null)
        {
            LM.Log("[MTPackageManagerWindow] _lbReleaseType & _lbDebugType is null");
            return;
        }
        bool isDebug = tag.EndsWith(DEBUG_SUFFIX);
        _lbReleaseType.style.display = isDebug ? DisplayStyle.None : DisplayStyle.Flex;
        _lbDebugType.style.display = isDebug ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>
    /// 未安装：只显示 Install。已安装：Install 改为 Update。
    /// Uninstall 仅根包（Depth == 0）显示；Depth &gt; 0 的依赖带入包、以及本工程包都不显示。
    /// 已显示 Uninstall 时，当前下拉 Tag 与已安装 Tag 不一致则禁用（与旧工程一致）。
    /// </summary>
    private void RefreshActionButtons()
    {
        if (_btnInstall == null && _btnUninstall == null)
        {
            return;
        }

        var project = _selectedProject;
        string tag = GetSelectedTag();
        bool hasValidTag = !string.IsNullOrEmpty(tag);
        var installed = FindInstalled(project);
        bool isSelf = IsSelfPackage(project, tag);
        bool isInstalled = isSelf || installed != null;
        bool busy = _isInstalling || _isRefreshing;
        bool toLocal = ConfigManager.Instance.Current?.InstallToLocal ?? false;
        bool showUninstall = isInstalled && !isSelf && installed != null && installed.Depth == 0;
        bool uninstallEnabled = showUninstall && !busy
            && string.Equals(tag, installed.Tag, StringComparison.Ordinal);

        if (_btnInstall != null)
        {
            bool showInstall = project != null;
            _btnInstall.style.display = showInstall ? DisplayStyle.Flex : DisplayStyle.None;
            _btnInstall.text = isInstalled
                ? (toLocal ? UPDATE_LOCAL_LABEL : UPDATE_LABEL)
                : (toLocal ? INSTALL_LOCAL_LABEL : INSTALL_LABEL);
            _btnInstall.SetEnabled(showInstall && hasValidTag && !busy);
        }

        if (_btnUninstall != null)
        {
            _btnUninstall.style.display = showUninstall ? DisplayStyle.Flex : DisplayStyle.None;
            _btnUninstall.SetEnabled(uninstallEnabled);
        }
    }

    private void OnInstallButtonClicked()
    {
        if (_isInstalling || _isRefreshing)
        {
            return;
        }

        var installed = FindInstalled(_selectedProject);
        if (installed != null || IsSelfPackage(_selectedProject, GetSelectedTag()))
        {
            OnUpdatePackage();
        }
        else
        {
            OnInstallPackage();
        }
    }

    private async void OnInstallPackage()
    {
        if (!TryBeginInstall("Install"))
        {
            return;
        }

        try
        {
            var package = await FetchSelectedPackageInfoAsync();
            if (package == null)
            {
                LM.LogError("[MTPackageManagerWindow] 安装失败：无法拉取所选 Tag 的包信息");
                return;
            }

            RootPackageService.AddOrUpdateRoot(package);
            RootPackageService.SaveToLocal();
            bool ok = await PackageSyncService.SyncManifestAsync();
            LM.Log(ok
                ? $"[MTPackageManagerWindow] 安装成功: {package.PackageName}#{package.Tag}"
                : $"[MTPackageManagerWindow] 安装失败: {package.PackageName}#{package.Tag}");
            RefreshProjectList();
        }
        catch (Exception e)
        {
            LM.LogError($"[MTPackageManagerWindow] 安装异常: {e}");
        }
        finally
        {
            EndInstall();
        }
    }

    private async void OnUninstallPackage()
    {
        if (!TryBeginInstall("Uninstall"))
        {
            return;
        }

        try
        {
            var installed = FindInstalled(_selectedProject);
            if (installed == null
                || string.Equals(installed.PackageName, MTPGit.SELF_PACKAGE_NAME, StringComparison.Ordinal)
                || IsSelfPackage(_selectedProject, GetSelectedTag()))
            {
                LM.LogWarning("[MTPackageManagerWindow] 本工程包不可卸载");
                return;
            }

            var dependents = MTPackageInfoService.GetAllUsedBy(installed.PackageName);
            if (dependents != null && dependents.Count > 0)
            {
                var names = string.Join(", ", dependents.ConvertAll(d => d.name ?? d.displayName));
                bool confirmed = EditorUtility.DisplayDialog(
                    "卸载确认",
                    $"以下已安装的包依赖 {installed.PackageName}：\n{names}\n\n确定仍要卸载吗？",
                    "确定卸载",
                    "取消");
                if (!confirmed)
                {
                    return;
                }
            }

            if (!RootPackageService.ContainsRoot(installed.PackageName))
            {
                EditorUtility.DisplayDialog(
                    "无法卸载",
                    $"{installed.PackageName} 不是手动安装的根包，由其他包依赖引入。请先卸载对应根包。",
                    "确定");
                return;
            }

            RootPackageService.RemoveRoot(installed.PackageName);
            RootPackageService.SaveToLocal();
            bool ok = await PackageSyncService.SyncManifestAsync();
            LM.Log(ok
                ? $"[MTPackageManagerWindow] 卸载成功: {installed.PackageName}"
                : $"[MTPackageManagerWindow] 卸载失败: {installed.PackageName}");
            RefreshProjectList();
        }
        catch (Exception e)
        {
            LM.LogError($"[MTPackageManagerWindow] 卸载异常: {e}");
        }
        finally
        {
            EndInstall();
        }
    }

    private async void OnUpdatePackage()
    {
        if (!TryBeginInstall("Update"))
        {
            return;
        }

        try
        {
            var installed = FindInstalled(_selectedProject);
            var package = await FetchSelectedPackageInfoAsync();
            if (package == null)
            {
                LM.LogError("[MTPackageManagerWindow] 更新失败：无法拉取所选 Tag 的包信息");
                return;
            }

            RootPackageService.AddOrUpdateRoot(package);
            RootPackageService.SaveToLocal();

            bool sameTag = installed == null
                || string.Equals(installed.Tag, package.Tag, StringComparison.Ordinal);

            if (sameTag)
            {
                // 同 tag 只发 UpdateAdd（按最新 hash 再 Add），不发 UpdateRemove，也不走插件导入。
                var requests = new List<MTPackageInstallerRequest>
                {
                    new MTPackageInstallerRequest
                    {
                        RequestType = RequestType.UpdateAdd,
                        RequestPackageInfo = package,
                    }
                };
                await PackageInstallerService.StartHandlingInstallerRequest(requests);
                if (requests[0].RequestState != RequestState.Success)
                {
                    LM.LogError($"[MTPackageManagerWindow] 同 Tag 更新失败: {package.PackageName}#{package.Tag}");
                    if (installed != null)
                    {
                        RootPackageService.AddOrUpdateRoot(installed);
                        RootPackageService.SaveToLocal();
                    }
                    return;
                }
            }

            bool ok = await PackageSyncService.SyncManifestAsync();
            LM.Log(ok
                ? $"[MTPackageManagerWindow] 更新成功: {package.PackageName}#{package.Tag}"
                : $"[MTPackageManagerWindow] 更新失败: {package.PackageName}#{package.Tag}");
            RefreshProjectList();
        }
        catch (Exception e)
        {
            LM.LogError($"[MTPackageManagerWindow] 更新异常: {e}");
        }
        finally
        {
            EndInstall();
        }
    }

    private bool TryBeginInstall(string action)
    {
        if (_isInstalling || _isRefreshing || _selectedProject == null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(GetSelectedTag()))
        {
            LM.LogWarning($"[MTPackageManagerWindow] {action} 中止：未选择有效 Tag");
            return false;
        }

        _isInstalling = true;
        RefreshActionButtons();
        return true;
    }

    private void EndInstall()
    {
        _isInstalling = false;
        RefreshActionButtons();
    }

    private async Task<MTPackageInfo> FetchSelectedPackageInfoAsync()
    {
        var project = _selectedProject;
        string tag = GetSelectedTag();
        if (project == null || string.IsNullOrEmpty(tag))
        {
            return null;
        }

        var url = !string.IsNullOrEmpty(project.HttpUrl) ? project.HttpUrl : project.SshUrl;
        if (string.IsNullOrEmpty(url))
        {
            LM.LogError($"[MTPackageManagerWindow] 工程缺少仓库 URL: {project.PackageName}");
            return null;
        }

        bool toLocal = ConfigManager.Instance.Current?.InstallToLocal ?? false;
        return await MTPackageInfoService.FetchPackageInfoAsync(UrlUtils.FormatUrl(url), tag, 0, toLocal);
    }

    private string GetSelectedTag()
    {
        if (_ddVersion != null)
        {
            var value = _ddVersion.value;
            if (!string.IsNullOrEmpty(value)
                && !string.Equals(value, NO_VERSION_LABEL, StringComparison.Ordinal)
                && !value.StartsWith(SHOW_ALL_PREFIX, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return ProjectDisplayUtils.ResolveDisplayTag(_selectedProject);
    }

    /// <summary>
    /// 按工程仓库链接查找已安装节点。
    /// </summary>
    private static MTPackageInfo FindInstalled(ProjectInfo project)
    {
        return ProjectDisplayUtils.FindInstalled(project);
    }

    private static bool IsSelfPackage(ProjectInfo project, string tag)
    {
        var tagInfo = ProjectDisplayUtils.GetTagInfo(project, tag);
        if (tagInfo != null
            && string.Equals(tagInfo.name, MTPGit.SELF_PACKAGE_NAME, StringComparison.Ordinal))
        {
            return true;
        }

        var installed = FindInstalled(project);
        if (installed != null
            && string.Equals(installed.PackageName, MTPGit.SELF_PACKAGE_NAME, StringComparison.Ordinal))
        {
            return true;
        }

        if (project != null)
        {
            var selfInstalled = MTPackageInfoService.GetPackage(MTPGit.SELF_PACKAGE_NAME);
            if (selfInstalled != null && UrlUtils.MatchesProject(project, selfInstalled.Url, selfInstalled.Tag))
            {
                return true;
            }
        }

        return false;
    }

    #endregion InfoView

    private static void SetLabel(Label label, string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }
}
