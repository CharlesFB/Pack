using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

public class MTPSettingWindow : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset settingWindow = default;
    [SerializeField]
    private VisualTreeAsset foldoutItem = default;

    // Git Source
    private TextField _tfUrl;
    private TextField _tfToken;
    private TextField _tfGroupID;
    
    // Mode
    private Toggle _tgDebugMode;
    private Toggle _tgDevTag;
    private Toggle _tgLogOnConsole;
    private Toggle _tgInstallToLocal;
    
    // Project Filter
    private TextField _tfProjectTag;
    private Foldout _foBlackList;
    private TextField _tfItemInput;
    private Button _btnAdd;
    
    //Refresh
    private DropdownField _ddRefreshTime;
    
    // Common
    private Button _btnSave;
    private Button _btnReset;

    public static void ShowSettingWindow()
    {
        var window = GetWindow<MTPSettingWindow>();
        window.titleContent = new GUIContent("MTP Config Settings");
        window.minSize = new Vector2(450, 480);
        window.Focus();
    }
    
    public void CreateGUI()
    {
        VisualElement root = settingWindow.CloneTree();
        root.StretchToParentSize();
        rootVisualElement.Add(root);
        
        InitializeWindow();
    }

    private void InitializeWindow()
    {
        _tfUrl = rootVisualElement.Q<TextField>("tfUrl");
        _tfToken = rootVisualElement.Q<TextField>("tfToken");
        _tfGroupID = rootVisualElement.Q<TextField>("tfGroupID");
        
        _tgDebugMode = rootVisualElement.Q<Toggle>("tgDebugMode");
        _tgDevTag = rootVisualElement.Q<Toggle>("tgDevTag");
        _tgLogOnConsole = rootVisualElement.Q<Toggle>("tgLogOnConsole");
        _tgInstallToLocal = rootVisualElement.Q<Toggle>("tgInstallToLocal");
        
        _tfProjectTag = rootVisualElement.Q<TextField>("tfProjectTag");
        _foBlackList = rootVisualElement.Q<Foldout>("foBlackList");
        _tfItemInput = rootVisualElement.Q<TextField>("tfItemInput");
        _btnAdd = rootVisualElement.Q<Button>("btnAdd");
        
        _ddRefreshTime = rootVisualElement.Q<DropdownField>("ddRefreshTime");
        
        _btnSave = rootVisualElement.Q<Button>("btnSave");
        _btnReset = rootVisualElement.Q<Button>("btnReset");

        if (_btnAdd != null)
            _btnAdd.clicked += OnAddBlackList;
        if (_btnSave != null)
            _btnSave.clicked += OnSave;
        if (_btnReset != null)
            _btnReset.clicked += OnReset;
        try
        {
            SetConfigValues();
        }
        catch(NullReferenceException e)
        {
            LM.LogError("[MTPSettingWindow] 空指针异常，可能存在有未绑定成功的控件：" + e.Message);
        }
    }

    private void SetConfigValues([CanBeNull] MTPConfig cfg = null)
    {
        var config = cfg ?? ConfigManager.Instance.Current;
        _tfUrl.value = config.Url;
        _tfToken.value = config.PrivateToken;
        _tfGroupID.value = config.GroupId;
        
        _tgDebugMode.value = config.DebugMode;
        _tgDevTag.value = config.ShowDevTag;
        _tgLogOnConsole.value = config.LogOnConsole;
        _tgInstallToLocal.value = config.InstallToLocal;
        
        _tfProjectTag.value = config.ProjectTag;
        
        foreach (var item in config.BlackList)
        {
            // Debug.Log(item);
            AddFoldoutItem(item);
        }

        _ddRefreshTime.choices = new List<string>();
        foreach (var e in Enum.GetNames(typeof(RefreshTime)))
        {
            _ddRefreshTime.choices.Add(e);
        }
        _ddRefreshTime.value = Enum.GetName(typeof(RefreshTime), config.refreshFrequency);
    }

    private void ClearConfigValues()
    {
        _tfUrl.value = string.Empty;
        _tfToken.value = string.Empty;
        _tfGroupID.value = string.Empty;
        
        _tgDebugMode.value = false;
        _tgDevTag.value = false;
        _tgLogOnConsole.value = false;
        _tgInstallToLocal.value = false;
        
        _tfProjectTag.value = string.Empty;
        _foBlackList.Clear();
        _tfItemInput.value = string.Empty;
    }
    
    private void AddFoldoutItem(string name = null)
    {
        if (foldoutItem == null)
        {
            LM.LogWarning("[MTPSettingWindow] 检查是否有拖入 foldoutItem");
            return;
        }
        VisualElement el = foldoutItem.CloneTree();
        var itemName = el.Q<Label>("lbName");
        var itemRemoveButton = el.Q<Button>("btnRemove");
        if (itemName == null || itemRemoveButton == null)
        {
            LM.LogWarning("[MTPSettingWindow] 检查是否拖入正确的 foldoutItem");
            return;
        }

        if (name == null && string.IsNullOrEmpty(_tfItemInput.value))
        {
            LM.Log("[MTPSettingWindow] 空添加操作");
            return;
        }

        if (name == null && !int.TryParse(_tfItemInput.value, out int _))
        {
            LM.LogWarning($"[MTPSettingWindow] 试图写入一个非数字 ID 进黑名单: BlackListId={_tfItemInput.value}");
            return;
        }
        itemName.text = name ?? _tfItemInput.value;
        itemRemoveButton.clicked += el.RemoveFromHierarchy;
        _foBlackList.Add(el);
    }

    private void OnAddBlackList()
    {
        AddFoldoutItem();
        // if (!string.IsNullOrEmpty(_tfItemInput.value) && int.TryParse(_tfItemInput.value, out int _))
        //     ConfigManager.Instance.Current.BlackList.Add(_tfItemInput.value);
    }

    private async void OnSave()
    {
        var config = ConfigManager.Instance.Current;
        bool changeInstallMode = config.InstallToLocal != _tgInstallToLocal.value;
        if (changeInstallMode)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "正在切换下载模式",
                "检测到正在切换下载模式，切换模式需要重置MTP状态，卸载所有MTP管理的包安装资源，确定要「继续切换模式」吗？",
                "确定",
                "取消"
            );
            if (!confirmed)
            {
                _tgInstallToLocal.SetValueWithoutNotify(config.InstallToLocal);
                return;
            }
        }

        ApplyFormToConfig(config);
        ConfigManager.Instance.Save();

        if (changeInstallMode)
        {
            _btnSave?.SetEnabled(false);
            _btnReset?.SetEnabled(false);
            try
            {
                // 必须先 Save 新 InstallToLocal，再 ResetAsync(true)
                EditorUtility.DisplayProgressBar("Reset MTP", "Switching install mode...", 0.3f);
                bool ok = await ServiceLocator.Get<IPackageSyncService>().ResetAsync(true);
                if (!ok)
                {
                    EditorUtility.DisplayDialog(
                        "重置未完成",
                        "安装模式已保存，但卸载未能全部完成，部分包可能仍处于安装状态。请查看 Console，必要时在主窗口再点一次 Reset。",
                        "确定");
                }
            }
            catch (Exception e)
            {
                LM.LogWarning($"[MTPSettingWindow] 切换安装模式 Reset 失败: {e.Message}");
                EditorUtility.DisplayDialog("重置失败", e.Message, "确定");
            }
            finally
            {
                _btnSave?.SetEnabled(true);
                _btnReset?.SetEnabled(true);
                EditorUtility.ClearProgressBar();
            }
        }

        MTPackageManagerWindow.RefreshOpenWindows();
        Close();
    }

    private void ApplyFormToConfig(MTPConfig config)
    {
        config.Url = _tfUrl.value;
        config.PrivateToken = _tfToken.value;
        config.GroupId = _tfGroupID.value;
        config.BlackList.Clear();
        foreach (var child in _foBlackList.Children())
        {
            var lbName = child.Q<Label>("lbName");
            if (lbName != null && !string.IsNullOrEmpty(lbName.text))
            {
                config.BlackList.Add(lbName.text);
            }
        }

        config.refreshFrequency = Enum.Parse<RefreshTime>(_ddRefreshTime.value);
        config.DebugMode = _tgDebugMode.value;
        config.ShowDevTag = _tgDevTag.value;
        config.LogOnConsole = _tgLogOnConsole.value;
        config.InstallToLocal = _tgInstallToLocal.value;
        config.ProjectTag = _tfProjectTag.value;
    }

    private void OnReset()
    {
        // ConfigManager.Instance.Current.ResetToDefault();
        ClearConfigValues();
        SetConfigValues(ConfigManager.Instance.GetDefault());
    }
}
