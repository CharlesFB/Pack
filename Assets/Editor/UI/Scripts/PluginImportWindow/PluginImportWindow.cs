using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// zip 类型插件本次导入选择的解压父目录（key=插件本地路径，value=用户选择的绝对目录）。
    /// 仅在本窗口生命周期内有效，不写回 MTPackageInfo、不做任何持久化；未选择/选空目录时按 "/Assets" 默认解压。
    /// </summary>
    private readonly Dictionary<string, string> _zipExtractDirs = new Dictionary<string, string>();

    /// <summary>
    /// 弹出插件选择窗。勾选结果直接写回 packageInfo.Plugins。
    /// </summary>
    public static void ShowWindow(MTPackageInfo packageInfo)
    {
        if (packageInfo == null)
        {
            LM.Log("[PluginImportWindow] MTPackageInfo is null, cannot show window with empty package info");
            return;
        }

        packageInfo.Plugins ??= new Dictionary<string, bool>();

        var window = CreateInstance<PluginImportWindow>();
        window.titleContent = new GUIContent("选择需要导入的插件资源");
        window.minSize = window.maxSize = new Vector2(450, 400);
        window._info = packageInfo;
        window._pluginKeys = packageInfo.Plugins.Keys.ToList();

        window.ShowModalUtility();
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
        toggle.tooltip = path; // hover 的提示消息，显示插件路径
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

        // 只有 .zip 需要选择解压目录，.unitypackage 走 AssetDatabase 导入，不需要该条目
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

        // 用户取消选择（返回空字符串）时不改变当前状态，后续导入仍按 "/Assets" 默认解压
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
            _info.Plugins[key] = false;
        _zipExtractDirs.Clear();
        Close();
    }

    private void OnImport()
    {
        var selectedKeys = _pluginKeys
            .Where(key => _info.Plugins.TryGetValue(key, out var isSelected) && isSelected)
            .ToList();

        var zipExtractDirs = new Dictionary<string, string>(_zipExtractDirs);
        var info = _info;
        Close();

        if (selectedKeys.Count == 0)
        {
            LM.Log("[PluginImportWindow] 未勾选任何插件，跳过导入");
            return;
        }

        // UI 只负责收集勾选项与本次会话内选择的 zip 解压目录，实际的覆盖冲突检测/导入/记录都交给 Service
        ServiceLocator.Get<IPluginPackageService>().ImportPreparedPlugins(info, selectedKeys, zipExtractDirs);
    }
}
