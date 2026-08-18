using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

/// <summary>
/// Samples Tab 单个条目视图：展示 Sample 名称 + Import/ReImport 按钮。
/// 当前仅支持假数据展示，真实的 UnityEditor.PackageManager.UI.Sample 导入逻辑待接入。
/// </summary>
public class PackageSampleItemView : VisualElement
{
    private readonly Label _lbName;
    private readonly Button _btnImport;
    private string _sampleName;

    public PackageSampleItemView()
    {
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MTPUI.PACKAGE_SAMPLE_ITEM_VIEW_UXML_PATH);
        if (tree == null)
        {
            LM.LogError($"[PackageSampleItemView] 未找到布局文件: {MTPUI.PACKAGE_SAMPLE_ITEM_VIEW_UXML_PATH}");
            return;
        }

        var root = tree.CloneTree();
        Add(root);

        _lbName = root.Q<Label>("lbName");
        _btnImport = root.Q<Button>("btnImport");
    }

    /// <summary>
    /// 绑定 Sample 展示数据。
    /// </summary>
    /// <param name="sampleName">Sample 显示名称</param>
    /// <param name="isImported">是否已导入（决定按钮显示 Import 还是 ReImport）</param>
    public void BindData(string sampleName, bool isImported)
    {
        _sampleName = sampleName;

        if (_lbName != null)
        {
            _lbName.text = sampleName;
        }

        if (_btnImport != null)
        {
            _btnImport.text = isImported ? "ReImport" : "Import";
            _btnImport.clicked += OnImportClicked;
        }
    }

    private void OnImportClicked()
    {
        LM.Log($"[PackageSampleItemView] TODO: 导入 Sample \"{_sampleName}\"（当前为假数据测试，未接入真实导入逻辑）");
    }
}
