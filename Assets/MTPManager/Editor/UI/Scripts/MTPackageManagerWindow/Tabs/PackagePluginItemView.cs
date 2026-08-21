using System;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

/// <summary>
/// Plugins Tab 单个条目视图：展示插件包（.zip / .unitypackage）名称 + Import/Unzip/Remove 按钮。
/// 名称颜色 / 按钮可点击态由 (isInstalled, isImported) 两个状态决定，具体规则见 BindData 注释。
/// 按钮点击逻辑先留空（仅打日志占位），真实的导入/卸载调用后续接入。
/// </summary>
public class PackagePluginItemView : VisualElement
{
    // 未安装：名称浅灰色；已安装但未导入：名称白色（默认文本色）；已安装且已导入：名称浅黄色。
    private static readonly StyleColor NotInstalledColor = new StyleColor(new Color(140f / 255f, 140f / 255f, 140f / 255f));
    private static readonly StyleColor InstalledColor = new StyleColor(new Color(224f / 255f, 224f / 255f, 224f / 255f));
    private static readonly StyleColor ImportedColor = new StyleColor(new Color(240f / 255f, 222f / 255f, 130f / 255f));

    private readonly Label _lbName;
    private readonly Button _btnUnzip;
    private readonly Button _btnRemove;
    private string _pluginFileName;

    public PackagePluginItemView()
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

        // 订阅放在构造函数里只做一次：ListView 会复用同一个实例反复 BindData，
        // 如果放在 BindData 里每次都会重复叠加一次回调，导致点击一次触发多次操作。
        if (_btnUnzip != null)
        {
            _btnUnzip.clicked += OnUnzipClicked;
        }

        if (_btnRemove != null)
        {
            _btnRemove.clicked += OnRemoveClicked;
        }
    }

    /// <summary>
    /// 绑定插件包展示数据。
    /// </summary>
    /// <param name="pluginFileName">插件文件名（.zip 或 .unitypackage，相对 Editor/PluginsPackage）</param>
    /// <param name="isInstalled">
    /// 当前查看的 Tag 对应的包是否已安装（依赖树里存在且 Revision/Tag 与当前查看的一致）。
    /// 未安装时名称显示浅灰色，Import/Unzip 按钮禁用点击。
    /// </param>
    /// <param name="isImported">
    /// 仅在 isInstalled 为 true 时有意义：该插件是否已经导入/解压过
    /// （即 MTPackageInfo.Plugins[路径] == true）。
    /// .unitypackage 已导入时按钮文案变为 "ReImport"；.zip 不论是否导入过，按钮文案始终为 "Unzip"。
    /// 两者已导入时都会显示 Remove 按钮。
    /// </param>
    public void BindData(string pluginFileName, bool isInstalled, bool isImported)
    {
        _pluginFileName = pluginFileName;
        bool isZip = !string.IsNullOrEmpty(pluginFileName)
            && pluginFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (_lbName != null)
        {
            _lbName.text = pluginFileName;
            _lbName.style.color = ResolveNameColor(isInstalled, isImported);
        }

        if (_btnUnzip != null)
        {
            _btnUnzip.text = isZip ? "Unzip" : (isImported ? "ReImport" : "Import");
            _btnUnzip.SetEnabled(isInstalled);
        }

        if (_btnRemove != null)
        {
            _btnRemove.style.display = isInstalled && isImported ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static StyleColor ResolveNameColor(bool isInstalled, bool isImported)
    {
        if (!isInstalled)
        {
            return NotInstalledColor;
        }

        return isImported ? ImportedColor : InstalledColor;
    }

    private void OnUnzipClicked()
    {
        LM.Log($"[PackageZipItemView] TODO: 导入/解压插件包 \"{_pluginFileName}\"（按钮点击逻辑待接入 IPluginPackageService）");
    }

    private void OnRemoveClicked()
    {
        LM.Log($"[PackageZipItemView] TODO: 移除插件包 \"{_pluginFileName}\"（按钮点击逻辑待接入 IPluginPackageService）");
    }
}
