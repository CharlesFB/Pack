using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

public class QuickInstallWindow : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset quickInstallWindow = default;
    [SerializeField]
    private VisualTreeAsset packageListItem = default;

    private Toggle _tgAll;
    private ListView _lvPackageList;
    private Button _btnInstall;
    private Button _btnReset;
    private Button _btnLoad;
    private Button _btnSave;

    private IPackageSyncService PackageSyncService => ServiceLocator.Get<IPackageSyncService>();
    private IMTPackageInfoService MTPInfoService => ServiceLocator.Get<IMTPackageInfoService>();
    private IProjectInfoService ProjectInfoService => ServiceLocator.Get<IProjectInfoService>();
    private IRootPackageService RootPackageService => ServiceLocator.Get<IRootPackageService>();

    private List<ProjectInfo> _packageList = new List<ProjectInfo>();
    private Dictionary<string, MTPackageInfo> _openedRoots = new Dictionary<string, MTPackageInfo>();
    private Dictionary<string, MTPackageInfo> _openedTree = new Dictionary<string, MTPackageInfo>();
    private readonly HashSet<string> _selectedKeys = new HashSet<string>();
    private readonly Dictionary<string, string> _pendingTags = new Dictionary<string, string>();

    private bool _isBusy;

    [MenuItem("MTPTest/QuickInstallWindow")]
    public static void ShowExample()
    {
        QuickInstallWindow wnd = GetWindow<QuickInstallWindow>();
        wnd.titleContent = new GUIContent("QuickInstallWindow");
        wnd.minSize = new Vector2(860, 700);
    }

    public void CreateGUI()
    {
        if (quickInstallWindow == null)
        {
            LM.LogError("[QuickInstallWindow] quickInstallWindow (VisualTreeAsset) 未赋值");
            return;
        }

        VisualElement root = quickInstallWindow.Instantiate();
        root.StretchToParentSize();
        rootVisualElement.Add(root);

        SnapshotLiveInstallState();
        BuildPackageList();
        RestoreSelectionFromOpened();
        InitializeWindow();
    }

    /// <summary>
    /// 打开时拷贝线上根包与依赖树，仅用于初始勾选、Tag、DEP 角标和 Reset；编辑过程不再重算树。
    /// </summary>
    private void SnapshotLiveInstallState()
    {
        _openedRoots = ClonePackages(RootPackageService.GetAllRoots());
        _openedTree = ClonePackages(MTPInfoService.GetAllPackages());
    }

    private void RestoreSelectionFromOpened()
    {
        _selectedKeys.Clear();
        _pendingTags.Clear();
        foreach (var project in _packageList)
        {
            var node = ProjectDisplayUtils.FindInTree(project, _openedTree);
            if (node != null
                && !string.IsNullOrEmpty(node.PackageName)
                && _openedRoots.ContainsKey(node.PackageName))
            {
                _selectedKeys.Add(GetItemKey(project));
            }
        }
    }

    private void BuildPackageList()
    {
        _packageList = ProjectDisplayUtils.BuildFilteredPackageList(ProjectInfoService.GetAll());
        _packageList.Sort((a, b) =>
        {
            var nodeA = ProjectDisplayUtils.FindInTree(a, _openedTree);
            var nodeB = ProjectDisplayUtils.FindInTree(b, _openedTree);
            return ProjectDisplayUtils.CompareProjectListOrder(
                nodeA,
                ProjectDisplayUtils.GetDisplayName(a, GetChosenTag(a)),
                nodeB,
                ProjectDisplayUtils.GetDisplayName(b, GetChosenTag(b)));
        });
    }

    private void InitializeWindow()
    {
        _tgAll = rootVisualElement.Q<Toggle>("tgAll");
        _lvPackageList = rootVisualElement.Q<ListView>("lvItemList");
        _btnInstall = rootVisualElement.Q<Button>("btnInstall");
        _btnReset = rootVisualElement.Q<Button>("btnReset");
        _btnLoad = rootVisualElement.Q<Button>("btnLoad");
        _btnSave = rootVisualElement.Q<Button>("btnSave");

        if (_btnInstall != null)
            _btnInstall.clicked += OnInstall;
        if (_btnReset != null)
            _btnReset.clicked += OnReset;
        if (_btnLoad != null)
            _btnLoad.clicked += OnLoad;
        if (_btnSave != null)
            _btnSave.clicked += OnSave;
        if (_tgAll != null)
            _tgAll.RegisterValueChangedCallback(OnSelectAllChange);

        if (packageListItem == null)
        {
            LM.LogError("[QuickInstallWindow] packageListItem (VisualTreeAsset) 未赋值");
            return;
        }

        if (_lvPackageList == null)
        {
            LM.LogError("[QuickInstallWindow] 未找到 ListView (lvItemList)");
            return;
        }

        _lvPackageList.makeItem = MakeItem;
        _lvPackageList.bindItem = OnBindItem;
        _lvPackageList.itemsSource = _packageList;
        _lvPackageList.selectionType = SelectionType.None;
        RefreshList();
    }

    private VisualElement MakeItem()
    {
        var item = packageListItem.CloneTree();
        var tgState = item.Q<Toggle>("tgState");
        var btnVersion = item.Q<Button>("btnVersion");

        if (tgState != null)
        {
            tgState.RegisterValueChangedCallback(OnItemStateChanged);
        }

        if (btnVersion != null)
        {
            btnVersion.clicked += () => OnItemVersionClicked(btnVersion);
        }

        return item;
    }

    private void OnBindItem(VisualElement item, int index)
    {
        if (item == null || _packageList == null || index < 0 || index >= _packageList.Count)
        {
            return;
        }

        var tgState = item.Q<Toggle>("tgState");
        var lbInstalled = item.Q<Label>("lbInstalled");
        var lbDep = item.Q<Label>("lbDep");
        var lbPackageName = item.Q<Label>("lbPackageName");
        var lbDisplayName = item.Q<Label>("lbDisplayName");
        var btnVersion = item.Q<Button>("btnVersion");

        var project = _packageList[index];
        var openedNode = ProjectDisplayUtils.FindInTree(project, _openedTree);
        string displayTag = GetChosenTag(project);
        string displayName = ProjectDisplayUtils.GetDisplayName(project, displayTag);
        var tagInfo = ProjectDisplayUtils.GetTagInfo(project, displayTag);
        string packageName = !string.IsNullOrEmpty(tagInfo?.name)
            ? tagInfo.name
            : project.PackageName;
        bool selected = IsSelected(project);
        bool installed = openedNode != null;
        bool openedRoot = installed
                          && !string.IsNullOrEmpty(openedNode.PackageName)
                          && _openedRoots.ContainsKey(openedNode.PackageName);

        if (lbPackageName != null)
        {
            lbPackageName.text = packageName;
        }

        if (lbDisplayName != null)
        {
            if (!string.IsNullOrEmpty(displayName))
            {
                lbDisplayName.text = displayName;
            }
            else if (!string.IsNullOrEmpty(packageName))
            {
                int dot = packageName.LastIndexOf('.');
                lbDisplayName.text = ProjectDisplayUtils.CapitalizeFirstLetter(
                    dot >= 0 ? packageName.Substring(dot + 1) : packageName);
            }
        }

        if (btnVersion != null)
        {
            btnVersion.userData = index;
            btnVersion.text = displayTag ?? string.Empty;
        }

        if (tgState != null)
        {
            tgState.userData = index;
            tgState.showMixedValue = false;
            tgState.SetValueWithoutNotify(selected);
        }

        if (lbInstalled != null)
        {
            lbInstalled.style.display = installed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (lbDep != null)
        {
            lbDep.style.display = installed && !openedRoot ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void OnItemStateChanged(ChangeEvent<bool> evt)
    {
        if (_isBusy)
        {
            RefreshList();
            return;
        }

        if (evt.target is not Toggle tg || tg.userData is not int index)
        {
            return;
        }

        if (index < 0 || index >= _packageList.Count)
        {
            return;
        }

        var project = _packageList[index];
        string key = GetItemKey(project);
        if (evt.newValue)
        {
            _selectedKeys.Add(key);
        }
        else
        {
            _selectedKeys.Remove(key);
        }

        RefreshSelectAllToggle();
    }

    private void OnItemVersionClicked(Button btnVersion)
    {
        if (_isBusy || btnVersion == null || btnVersion.userData is not int index)
        {
            return;
        }

        if (index < 0 || index >= _packageList.Count)
        {
            return;
        }

        var project = _packageList[index];
        var menu = new GenericMenu();
        string current = GetChosenTag(project);
        var tags = ProjectDisplayUtils.GetVisibleSortedTags(project);
        if (!string.IsNullOrEmpty(current) && !tags.Contains(current))
        {
            tags.Insert(0, current);
        }

        foreach (var tag in tags)
        {
            string selected = tag;
            menu.AddItem(new GUIContent(selected), current == selected, () =>
            {
                OnVersionSelected(project, btnVersion, selected);
            });
        }

        menu.DropDown(btnVersion.worldBound);
    }

    private void OnVersionSelected(ProjectInfo project, Button btnVersion, string tag)
    {
        if (_isBusy || project == null || string.IsNullOrEmpty(tag))
        {
            return;
        }

        _pendingTags[GetItemKey(project)] = tag;
        if (btnVersion != null)
        {
            btnVersion.text = tag;
        }
    }

    private void RefreshList()
    {
        if (_lvPackageList != null)
        {
            _lvPackageList.itemsSource = _packageList;
            _lvPackageList.RefreshListView();
        }

        RefreshSelectAllToggle();
    }

    private void RefreshSelectAllToggle()
    {
        if (_tgAll == null || _packageList == null)
        {
            return;
        }

        int selectedCount = 0;
        foreach (var project in _packageList)
        {
            if (IsSelected(project))
            {
                selectedCount++;
            }
        }

        _tgAll.SetValueWithoutNotify(selectedCount > 0 && selectedCount == _packageList.Count);
        _tgAll.showMixedValue = selectedCount > 0 && selectedCount < _packageList.Count;
    }

    private static Dictionary<string, MTPackageInfo> ClonePackages(IEnumerable<MTPackageInfo> source)
    {
        var clone = new Dictionary<string, MTPackageInfo>();
        if (source == null)
        {
            return clone;
        }

        foreach (var package in source)
        {
            if (package == null || string.IsNullOrEmpty(package.PackageName))
            {
                continue;
            }

            clone[package.PackageName] = ClonePackage(package);
        }

        return clone;
    }

    private string GetChosenTag(ProjectInfo project)
    {
        if (project != null && _pendingTags.TryGetValue(GetItemKey(project), out var pending)
            && !string.IsNullOrEmpty(pending))
        {
            return pending;
        }

        var node = ProjectDisplayUtils.FindInTree(project, _openedTree);
        if (node != null && !string.IsNullOrEmpty(node.Tag))
        {
            return node.Tag;
        }

        return ProjectDisplayUtils.ResolveDisplayTag(project);
    }

    private static string GetItemKey(ProjectInfo project)
    {
        return project == null ? string.Empty : $"{project.ProjectId}{project.PackageName}";
    }

    private bool IsSelected(ProjectInfo project)
    {
        return project != null && _selectedKeys.Contains(GetItemKey(project));
    }

    private static MTPackageInfo ClonePackage(MTPackageInfo source)
    {
        if (source == null)
        {
            return null;
        }

        return new MTPackageInfo
        {
            PackageName = source.PackageName,
            Tag = source.Tag,
            Revision = source.Revision,
            Url = source.Url,
            ScriptDefineSymbols = source.ScriptDefineSymbols,
            ProjectId = source.ProjectId,
            Depth = source.Depth,
            isLocal = source.isLocal,
            Dependencies = source.Dependencies != null
                ? new Dictionary<string, string>(source.Dependencies)
                : null,
            Plugins = source.Plugins != null
                ? new Dictionary<string, bool>(source.Plugins)
                : new Dictionary<string, bool>(),
        };
    }

    private async void OnInstall()
    {
        if (_isBusy)
        {
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Quick Install",
            "将按当前勾选的根包清单同步安装（未勾选的根包会被卸载，仍被依赖的包会留在树上）。确定继续？",
            "安装",
            "取消");
        if (!confirmed)
        {
            return;
        }

        _isBusy = true;
        SetControlsEnabled(false);
        EditorUtility.DisplayProgressBar("Quick Install", "正在准备根包清单...", 0.2f);
        try
        {
            var roots = new Dictionary<string, MTPackageInfo>();
            int total = Math.Max(_packageList.Count, 1);
            for (int i = 0; i < _packageList.Count; i++)
            {
                var project = _packageList[i];
                if (!IsSelected(project))
                {
                    continue;
                }

                EditorUtility.DisplayProgressBar(
                    "Quick Install",
                    $"正在解析根包: {project.PackageName}",
                    0.2f + 0.3f * i / total);

                var root = await CreateDraftRootAsync(project, GetChosenTag(project));
                if (root == null || string.IsNullOrEmpty(root.PackageName))
                {
                    LM.LogWarning($"[QuickInstallWindow] Install 跳过无效项: {project.PackageName}");
                    continue;
                }

                roots[root.PackageName] = root;
            }

            EnsureSelfRootPreserved(roots);

            EditorUtility.DisplayProgressBar("Quick Install", "正在同步清单...", 0.6f);
            RootPackageService.Clear();
            foreach (var root in roots.Values)
            {
                RootPackageService.AddOrUpdateRoot(ClonePackage(root));
            }

            RootPackageService.SaveToLocal();
            bool ok = await PackageSyncService.SyncManifestAsync();
            if (!ok)
            {
                LM.LogError("[QuickInstallWindow] Install 失败：SyncManifest 未成功");
                return;
            }

            LM.Log($"[QuickInstallWindow] Install 完成，根包数:{roots.Count}");
            MTPackageManagerWindow.RefreshOpenWindows();
            Close();
        }
        catch (Exception e)
        {
            LM.LogError($"[QuickInstallWindow] Install 异常: {e}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            SetControlsEnabled(true);
            _isBusy = false;
        }
    }

    private void OnReset()
    {
        if (_isBusy)
        {
            return;
        }

        RestoreSelectionFromOpened();
        RefreshList();
    }

    private void OnLoad()
    {
        if (_isBusy)
        {
            return;
        }

        string path = EditorUtility.OpenFilePanel("请打开安装配置 json 文件", Environment.CurrentDirectory, "json");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var saved = JsonUtils.LoadFromFile<Dictionary<string, string>>(path, false);
        if (saved == null)
        {
            LM.LogError($"[QuickInstallWindow] Load 失败: {path}");
            return;
        }

        _selectedKeys.Clear();
        _pendingTags.Clear();
        foreach (var project in _packageList)
        {
            string key = GetItemKey(project);
            if (!saved.TryGetValue(key, out var tag) || string.IsNullOrEmpty(tag))
            {
                continue;
            }

            _pendingTags[key] = tag;
            _selectedKeys.Add(key);
        }

        RefreshList();
    }

    private void OnSave()
    {
        if (_isBusy)
        {
            return;
        }

        var saveMsg = new Dictionary<string, string>();
        foreach (var project in _packageList)
        {
            if (!IsSelected(project))
            {
                continue;
            }

            string tag = GetChosenTag(project);
            if (string.IsNullOrEmpty(tag))
            {
                continue;
            }

            saveMsg[GetItemKey(project)] = tag;
        }

        string path = EditorUtility.SaveFilePanel(
            "请选择保存位置",
            Environment.CurrentDirectory,
            "InstallVersion.json",
            "json");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        JsonUtils.SaveToFile(path, saveMsg);
        LM.Log($"[QuickInstallWindow] 已保存 {saveMsg.Count} 条根包到 {path}");
    }

    private void OnSelectAllChange(ChangeEvent<bool> evt)
    {
        if (_isBusy)
        {
            RefreshSelectAllToggle();
            return;
        }

        _selectedKeys.Clear();
        if (evt.newValue)
        {
            foreach (var project in _packageList)
            {
                _selectedKeys.Add(GetItemKey(project));
            }
        }

        RefreshList();
    }

    private async Task<MTPackageInfo> CreateDraftRootAsync(ProjectInfo project, string tag)
    {
        if (project == null || string.IsNullOrEmpty(tag))
        {
            return null;
        }

        bool toLocal = GetInstallToLocal();
        var fromCache = TryCreateRootFromCache(project, tag, toLocal);
        if (fromCache != null)
        {
            return fromCache;
        }

        var url = ResolveProjectUrl(project);
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var fetched = await MTPInfoService.FetchPackageInfoAsync(url, tag, 0, toLocal);
        if (fetched != null)
        {
            fetched.Depth = 0;
            fetched.isLocal = toLocal;
        }

        return fetched;
    }

    private static MTPackageInfo TryCreateRootFromCache(ProjectInfo project, string tag, bool toLocal)
    {
        var tagInfo = ProjectDisplayUtils.GetTagInfo(project, tag);
        if (tagInfo == null || string.IsNullOrEmpty(tagInfo.name))
        {
            return null;
        }

        var url = ResolveProjectUrl(project);
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        return new MTPackageInfo
        {
            PackageName = tagInfo.name,
            Tag = tag,
            Url = url,
            ProjectId = project.ProjectId,
            Depth = 0,
            isLocal = toLocal,
            Dependencies = tagInfo.dependenciesGit != null
                ? new Dictionary<string, string>(tagInfo.dependenciesGit)
                : null,
            Plugins = new Dictionary<string, bool>(),
        };
    }

    private void EnsureSelfRootPreserved(Dictionary<string, MTPackageInfo> roots)
    {
        const string SELF_NAME = MTPGit.SELF_PACKAGE_NAME;
        if (roots.ContainsKey(SELF_NAME))
        {
            return;
        }

        var live = RootPackageService.GetRoot(SELF_NAME)
                   ?? MTPInfoService.GetPackage(SELF_NAME)
                   ?? (_openedRoots.TryGetValue(SELF_NAME, out var opened) ? opened : null);
        if (live == null)
        {
            LM.LogWarning("[QuickInstallWindow] 未找到 MTP Manager 自身，Install 不会补回");
            return;
        }

        var clone = ClonePackage(live);
        clone.Depth = 0;
        roots[SELF_NAME] = clone;
        LM.Log("[QuickInstallWindow] 已保留 MTP Manager 自身，不允许通过 QuickInstall 卸载");
    }

    private void SetControlsEnabled(bool enabled)
    {
        _lvPackageList?.SetEnabled(enabled);
        _btnInstall?.SetEnabled(enabled);
        _btnReset?.SetEnabled(enabled);
        _btnLoad?.SetEnabled(enabled);
        _btnSave?.SetEnabled(enabled);
        _tgAll?.SetEnabled(enabled);
    }

    private static bool GetInstallToLocal()
    {
        return ConfigManager.Instance.Current?.InstallToLocal ?? false;
    }

    private static string ResolveProjectUrl(ProjectInfo project)
    {
        if (project == null)
        {
            return null;
        }

        return !string.IsNullOrEmpty(project.HttpUrl) ? project.HttpUrl : project.SshUrl;
    }
}
