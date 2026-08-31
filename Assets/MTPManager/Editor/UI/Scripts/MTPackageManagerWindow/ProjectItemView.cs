using System;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

/// <summary>
/// lvProjectList 单条工程条目视图。展示的 Tag 遵循 ProjectDisplayUtils.ResolveDisplayTag 规则：
/// 已安装（在依赖树中）用安装的 Tag，否则用（受 DebugMode 过滤后）最高 Tag。
/// </summary>
public class ProjectItemView : VisualElement
{
    // hovor 用uss控制，背景色的选择/初始化用代码控制
    private static readonly StyleColor s_selectedBackgroundColor =
        new StyleColor(new Color(84f / 255f, 84f / 255f, 84f / 255f));
    private static readonly StyleColor s_defaultBackgroundColor =
        new StyleColor(new Color(58f / 255f, 58f / 255f, 58f / 255f));
    private static readonly StyleColor s_defaultNameColor =
        new StyleColor(new Color(236f / 255f, 236f / 255f, 236f / 255f));
    private static readonly StyleColor s_disabledNameColor =
        new StyleColor(new Color(128f / 255f, 128f / 255f, 128f / 255f));

    private readonly VisualElement _row;
    private readonly Label _lbDisplayName;
    private readonly Label _lbDep;
    private readonly Label _lbDebug;
    private readonly Label _lbVersion;
    private readonly Label _lbStatus;

    private ProjectInfo _project;

    public ProjectItemView()
    {
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MTPUI.PROJECT_ITEM_VIEW_UXML_PATH);
        if (tree == null)
        {
            LM.LogError($"[ProjectItemView] 未找到布局文件: {MTPUI.PROJECT_ITEM_VIEW_UXML_PATH}");
            return;
        }

        var root = tree.CloneTree();
        Add(root);

        _row = root;
        _row.style.backgroundColor = s_defaultBackgroundColor;
        _lbDisplayName = root.Q<Label>("lbDisplayName");
        _lbDep = root.Q<Label>("lbDep");
        _lbDebug = root.Q<Label>("lbDebug");
        _lbVersion = root.Q<Label>("lbVersion");
        _lbStatus = root.Q<Label>("lbStatus");
    }

    /// <summary>
    /// 由 MTPackageManagerWindow 在选中项变化 / 条目重新绑定时调用，
    /// 直接决定该条目是否展示"选中"背景色，完全接管 Unity ListView 默认的蓝色选中高亮。
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (_row == null)
        {
            return;
        }

        _row.style.backgroundColor = selected ? s_selectedBackgroundColor : s_defaultBackgroundColor;
    }

    public void BindData(ProjectInfo project, bool externalMode = false)
    {
        _project = project;

        if (project == null)
        {
            SetLabel(_lbDisplayName, "N/A");
            SetLabel(_lbVersion, "N/A");
            SetDisplay(_lbDep, false);
            ClearStatusIcon();
            SetDisplay(_lbDebug, false);
            ResetNameColor();
            return;
        }

        if (externalMode || project.PackageType == PackageType.ExPlugin)
        {
            BindExternal(project);
            return;
        }

        string tag = ProjectDisplayUtils.ResolveDisplayTag(project);
        var tagInfo = ProjectDisplayUtils.GetTagInfo(project, tag);

        ResetNameColor();
        SetLabel(_lbDisplayName, ProjectDisplayUtils.GetDisplayName(project, tag));
        SetLabel(_lbVersion, tagInfo?.version ?? "N/A");
        SetDisplay(_lbVersion, true);
        SetLabel(_lbDebug, "Debug");
        SetDisplay(_lbDebug, IsDebugTag(tag));
        SetDisplay(_lbStatus, true);

        var installed = ProjectDisplayUtils.FindInstalled(project);
        bool isInstalled = installed != null;

        SetDisplay(_lbDep, isInstalled && installed.Depth > 0);

        if (isInstalled)
        {
            RefreshStatusIcon(project, installed);
        }
        else
        {
            ClearStatusIcon();
        }
    }

    private void BindExternal(ProjectInfo project)
    {
        string displayName = ProjectDisplayUtils.CapitalizeFirstLetter(project.PackageName);
        bool hasVersion = project.Tags != null && project.Tags.Count > 0;

        SetLabel(_lbDisplayName, string.IsNullOrEmpty(displayName) ? "N/A" : displayName);
        if (_lbDisplayName != null)
        {
            _lbDisplayName.style.color = hasVersion ? s_defaultNameColor : s_disabledNameColor;
        }

        SetDisplay(_lbDep, false);
        SetLabel(_lbDebug, "Plugin");
        SetDisplay(_lbDebug, true);
        SetDisplay(_lbVersion, false);
        SetDisplay(_lbStatus, false);
        ClearStatusIcon();
    }

    private void ResetNameColor()
    {
        if (_lbDisplayName != null)
        {
            _lbDisplayName.style.color = s_defaultNameColor;
        }
    }

    private void RefreshStatusIcon(ProjectInfo project, MTPackageInfo installed)
    {
        bool updateAvailable = false;
        if (installed.Depth == 0)
        {
            // 手动安装（root 节点）时，才需要判断是否有更高版本可更新
            var visibleTags = ProjectDisplayUtils.GetVisibleSortedTags(project);
            updateAvailable = visibleTags.Count > 0
                && !string.Equals(visibleTags[0], installed.Tag, StringComparison.Ordinal);
        }

        string iconPath = updateAvailable
            ? MTPUI.PROJECT_UPDATE_STATUS_ICON_PATH
            : MTPUI.PROJECT_INSTALLED_STATUS_ICON_PATH;

        if (_lbStatus == null)
        {
            return;
        }

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
        _lbStatus.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground();
    }

    /// <summary>
    /// 未安装时调用：只清空 lbStatus 的背景图（视觉上透明），不改 display，
    /// 保留其占位尺寸，避免和 lbDep 一起隐藏导致的布局跳动。
    /// </summary>
    private void ClearStatusIcon()
    {
        if (_lbStatus != null)
        {
            _lbStatus.style.backgroundImage = new StyleBackground();
        }
    }

    private static bool IsDebugTag(string tag)
    {
        return !string.IsNullOrEmpty(tag) && tag.EndsWith("-debug", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetLabel(Label label, string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element != null)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
