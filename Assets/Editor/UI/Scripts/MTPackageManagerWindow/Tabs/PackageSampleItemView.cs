using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

/// <summary>
/// Samples Tab 单个条目视图：展示 Sample 名称 + Import/ReImport 按钮。
/// 与旧工程一致：sample 为 null 表示当前查看的版本未安装（或本地找不到对应 Sample），
/// 此时按钮禁用并用 tooltip 说明原因；否则按 Sample.isImported 决定按钮文案，并直接调用
/// UnityEditor.PackageManager.UI.Sample.Import() 完成真实导入。
/// </summary>
public class PackageSampleItemView : VisualElement
{
    private readonly Label _lbName;
    private readonly Button _btnImport;
    private Sample _sample;
    private bool _hasSample;

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
    /// <param name="sampleName">Sample 显示名称（来自 package.json 的 samples[].displayName）</param>
    /// <param name="sample">
    /// 当前查看的 Tag 恰好是已安装版本、且能在本地通过 Sample.FindByPackage 找到同名 Sample 时传入其值；
    /// 否则传 null（未安装该包 / 版本不一致 / 本地找不到），此时按钮禁用并展示提示原因。
    /// </param>
    public void BindData(string sampleName, Sample? sample)
    {
        if (_lbName != null)
        {
            _lbName.text = sampleName;
        }

        _hasSample = sample.HasValue;
        if (_hasSample)
        {
            _sample = sample.Value;
        }

        if (_btnImport == null)
        {
            return;
        }

        if (!_hasSample)
        {
            _btnImport.text = "Import";
            _btnImport.SetEnabled(false);
            tooltip = "需先安装该包（且当前查看版本与已安装版本一致）才能导入 Sample";
            return;
        }

        tooltip = string.Empty;
        _btnImport.SetEnabled(true);
        _btnImport.text = _sample.isImported ? "ReImport" : "Import";
        _btnImport.clicked += OnImportClicked;
    }

    private void OnImportClicked()
    {
        if (!_hasSample)
        {
            return;
        }

        bool ok = _sample.isImported
            ? _sample.Import(Sample.ImportOptions.OverridePreviousImports)
            : _sample.Import();

        if (!ok)
        {
            EditorUtility.DisplayDialog("错误",
                "导入 Sample 失败，可能是版本不一致导致，请手动删除旧版本 Sample 后重试。", "OK");
            return;
        }

        if (_btnImport != null)
        {
            _btnImport.text = "ReImport";
        }
    }
}
