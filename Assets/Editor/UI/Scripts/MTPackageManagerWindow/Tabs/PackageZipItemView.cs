using System;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

/// <summary>
/// Plugins Tab 单个条目视图：展示插件包（.zip / .unitypackage）名称 + Import/Unzip/Remove 按钮。
/// 当前仅支持假数据展示，真实的下载/解析/导入逻辑由 IPluginPackageService 接入后再补上。
/// </summary>
public class PackageZipItemView : VisualElement
{
    private readonly Label _lbName;
    private readonly Button _btnUnzip;
    private readonly Button _btnRemove;
    private string _pluginFileName;

    public PackageZipItemView()
    {
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MTPUI.PACKAGE_ZIP_ITEM_VIEW_UXML_PATH);
        if (tree == null)
        {
            LM.LogError($"[PackageZipItemView] 未找到布局文件: {MTPUI.PACKAGE_ZIP_ITEM_VIEW_UXML_PATH}");
            return;
        }

        var root = tree.CloneTree();
        Add(root);

        _lbName = root.Q<Label>("lbName");
        _btnUnzip = root.Q<Button>("btnUnZip");
        _btnRemove = root.Q<Button>("btnRemove");

        if (_btnRemove != null)
        {
            _btnRemove.style.display = DisplayStyle.None; // 默认不显示，绑数据后按导入状态决定
        }
    }

    /// <summary>
    /// 绑定插件包展示数据。
    /// </summary>
    /// <param name="pluginFileName">插件文件名（.zip 或 .unitypackage）</param>
    /// <param name="isImported">是否已导入/解压（仅 .unitypackage 有意义）</param>
    public void BindData(string pluginFileName, bool isImported)
    {
        _pluginFileName = pluginFileName;
        bool isUnityPackage = pluginFileName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase);

        if (_lbName != null)
        {
            _lbName.text = pluginFileName;
        }

        if (_btnUnzip != null)
        {
            _btnUnzip.text = isUnityPackage ? (isImported ? "ReImport" : "Import") : "Unzip";
            _btnUnzip.clicked += OnUnzipClicked;
        }

        if (_btnRemove != null)
        {
            _btnRemove.style.display = isUnityPackage && isImported ? DisplayStyle.Flex : DisplayStyle.None;
            _btnRemove.clicked += OnRemoveClicked;
        }
    }

    private void OnUnzipClicked()
    {
        LM.Log($"[PackageZipItemView] TODO: 导入/解压插件包 \"{_pluginFileName}\"（当前为假数据测试，未接入真实逻辑）");
    }

    private void OnRemoveClicked()
    {
        LM.Log($"[PackageZipItemView] TODO: 移除插件包 \"{_pluginFileName}\"（当前为假数据测试，未接入真实逻辑）");
    }
}
