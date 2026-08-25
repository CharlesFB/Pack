using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

public class PluginImportWindow : EditorWindow
{
    [SerializeField] VisualTreeAsset windowTree;
    [SerializeField] VisualTreeAsset pluginTree;

    private ListView _lvPluginItemContainer;
    private Label _lbDescription;
    private Button _btnCancel;
    private Button _btnImport;

    private MTPackageInfo _info = new MTPackageInfo();
    private List<string> _pluginKeys = new List<string>();
    private TaskCompletionSource<PluginImportSelection> _completionSource;

    /// <summary>
    /// zip 类型插件本次导入选择的解压父目录（key=插件本地路径，value=用户选择的绝对目录）。
    /// 仅在本窗口生命周期内有效，不写回 MTPackageInfo、不做任何持久化；未选择/选空目录时按 "/Assets" 默认解压。
    /// </summary>
    private readonly Dictionary<string, string> _zipExtractDirs = new Dictionary<string, string>();

    /// <summary>
    /// 用户关闭选择窗后的勾选结果。窗口本身不执行导入。
    /// </summary>
    public sealed class PluginImportSelection
    {
        public bool Confirmed;
        public List<string> SelectedKeys = new List<string>();
        public Dictionary<string, string> ZipExtractDirs = new Dictionary<string, string>();
    }

    /// <summary>
    /// 弹出非模态插件选择窗。不关心结果时用这个即可。
    /// </summary>
    public static void ShowWindow(MTPackageInfo mtPackageInfo)
    {
        _ = ShowWindowAsync(mtPackageInfo);
    }

    /// <summary>
    /// 弹出非模态插件选择窗，返回 Task。调用方必须循环等待 Task.IsCompleted 后再继续宏 / 下一包。
    /// 取消、点关闭、未勾选：Confirmed=false 或 SelectedKeys 为空。窗口不执行 ImportPackage。
    /// </summary>
    public static Task<PluginImportSelection> ShowWindowAsync(MTPackageInfo mtPackageInfo)
    {
        if (mtPackageInfo == null)
        {
            LM.Log("[PluginImportWindow] MTPackageInfo is null, cannot show window with empty package info");
            return Task.FromResult(new PluginImportSelection { Confirmed = false });
        }

        mtPackageInfo.Plugins ??= new Dictionary<string, bool>();

        var window = CreateInstance<PluginImportWindow>();
        window.titleContent = new GUIContent("选择需要导入的插件资源");
        window.minSize = window.maxSize = new Vector2(450, 400);
        window._info = mtPackageInfo;
        window._pluginKeys = mtPackageInfo.Plugins.Keys.ToList();
        window._completionSource = new TaskCompletionSource<PluginImportSelection>();

        window.ShowUtility();
        window.Focus();
        return window._completionSource.Task;
    }

    public void CreateGUI()
    {
        if (windowTree == null)
        {
            LM.LogError("[PluginImportWindow] windowTree 未赋值（请在脚本 Default References 中拖入 UXML）");
            return;
        }

        var root = windowTree.CloneTree();
        root.StretchToParentSize();
        rootVisualElement.Add(root);

        InitWindowComponent();
    }

    private void OnDestroy()
    {
        Complete(new PluginImportSelection { Confirmed = false });
    }

    private void InitWindowComponent()
    {
        _lbDescription = rootVisualElement.Q<Label>("lbDescription");
        _lvPluginItemContainer = rootVisualElement.Q<ListView>("lvPluginItemContainer");
        _btnCancel = rootVisualElement.Q<Button>("btnCancel");
        _btnImport = rootVisualElement.Q<Button>("btnImport");

        BindData();

        if (_btnImport != null)
        {
            _btnImport.clicked += OnImport;
        }

        if (_btnCancel != null)
        {
            _btnCancel.clicked += OnCancel;
        }

        if (_lvPluginItemContainer != null && pluginTree != null)
        {
            _lvPluginItemContainer.makeItem = () => pluginTree.CloneTree();
            _lvPluginItemContainer.bindItem = OnBindItem;
            _lvPluginItemContainer.itemsSource = _pluginKeys;
            _lvPluginItemContainer.selectionType = SelectionType.None;
            _lvPluginItemContainer.Rebuild();
        }
        else if (pluginTree == null)
        {
            LM.LogError("[PluginImportWindow] pluginTree 未赋值（请在脚本 Default References 中拖入 Item UXML）");
        }
    }

    private void BindData()
    {
        _lbDescription.text = $"准备安装 {_info.PackageName}[{_info.Tag}]";
    }

    private void OnBindItem(VisualElement element, int index)
    {
        if (element == null || _pluginKeys == null || index < 0 || index >= _pluginKeys.Count)
        {
            return;
        }

        var toggle = element.Q<Toggle>("tgPluginItem");
        if (toggle == null)
        {
            return;
        }

        string path = _pluginKeys[index];

        toggle.UnregisterValueChangedCallback(OnToggleChanged);

        toggle.label = Path.GetFileName(path);
        toggle.tooltip = path;
        toggle.userData = path;
        toggle.SetValueWithoutNotify(false);

        toggle.RegisterValueChangedCallback(OnToggleChanged);

        var veDirChosen = element.Q<VisualElement>("veDirChosen");
        var btnChooseDir = element.Q<Button>("btnChooseDir");
        var lbUnzipRoot = element.Q<Label>("lbUnzipRoot");
        if (veDirChosen == null || btnChooseDir == null || lbUnzipRoot == null)
        {
            return;
        }

        btnChooseDir.UnregisterCallback<ClickEvent>(OnChooseDirClicked);

        bool isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        veDirChosen.style.display = isZip ? DisplayStyle.Flex : DisplayStyle.None;

        if (isZip)
        {
            btnChooseDir.userData = path;
            RefreshUnzipRootLabel(lbUnzipRoot, path);
            btnChooseDir.RegisterCallback<ClickEvent>(OnChooseDirClicked);
        }
    }

    private void RefreshUnzipRootLabel(Label lbUnzipRoot, string path)
    {
        string dir = null;
        bool hasCustomDir = _zipExtractDirs.TryGetValue(path, out dir) && !string.IsNullOrEmpty(dir);

        lbUnzipRoot.text = hasCustomDir ? dir : "/Assets";
        lbUnzipRoot.tooltip = hasCustomDir ? dir : "默认解到 Assets/ 根目录";
    }

    private void OnChooseDirClicked(ClickEvent evt)
    {
        if (evt.currentTarget is not Button btnChooseDir || btnChooseDir.userData is not string path)
        {
            return;
        }

        string selectedDirectory = EditorUtility.OpenFolderPanel("选择解压目录", "", string.Empty);
        if (string.IsNullOrEmpty(selectedDirectory))
        {
            return;
        }

        _zipExtractDirs[path] = selectedDirectory;

        var lbUnzipRoot = btnChooseDir.parent?.Q<Label>("lbUnzipRoot");
        if (lbUnzipRoot != null)
        {
            RefreshUnzipRootLabel(lbUnzipRoot, path);
        }
    }

    private void OnToggleChanged(ChangeEvent<bool> evt)
    {
        if (_info.Plugins == null)
        {
            return;
        }

        if (evt.currentTarget is Toggle toggle && toggle.userData is string path)
        {
            _info.Plugins[path] = evt.newValue;
        }
    }

    private void OnCancel()
    {
        foreach (var key in _pluginKeys)
        {
            _info.Plugins[key] = false;
        }

        _zipExtractDirs.Clear();
        Complete(new PluginImportSelection { Confirmed = false });
        Close();
    }

    private void OnImport()
    {
        var selectedKeys = _pluginKeys
            .Where(key => _info.Plugins.TryGetValue(key, out var isSelected) && isSelected)
            .ToList();

        Complete(new PluginImportSelection
        {
            Confirmed = true,
            SelectedKeys = selectedKeys,
            ZipExtractDirs = new Dictionary<string, string>(_zipExtractDirs),
        });
        Close();
    }

    private void Complete(PluginImportSelection selection)
    {
        _completionSource?.TrySetResult(selection ?? new PluginImportSelection { Confirmed = false });
    }
}
