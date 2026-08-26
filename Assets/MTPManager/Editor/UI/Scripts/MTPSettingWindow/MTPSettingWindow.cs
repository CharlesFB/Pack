using System;
using System.Collections.Generic;
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
        var window = CreateInstance<MTPSettingWindow>();
        window.titleContent = new GUIContent("MTP Config Settings");
        window.minSize = new Vector2(450, 450);
        
        window.ShowUtility();
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

        // if (_tgDebugMode != null)
        //     _tgDebugMode.RegisterValueChangedCallback(OnChangeDebugMode);
        // if (_tgDevTag != null)
        //     _tgDevTag.RegisterValueChangedCallback(OnChangeDevTag);
        // if (_tgLogOnConsole != null)
        //     _tgLogOnConsole.RegisterValueChangedCallback(OnChangeLogOnConsole);
        // if (_tgInstallToLocal != null)
        //     _tgInstallToLocal.RegisterValueChangedCallback(OnChangeInstallToLocal);

        try
        {
            SetConfigValues();
        }
        catch(NullReferenceException e)
        {
            LM.LogError("[MTPSettingWindow] 空指针异常，可能存在有未绑定成功的控件：" + e.Message);
        }
    }

    private void SetConfigValues()
    {
        var config = ConfigManager.Instance.Current;
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

    private void OnSave()
    {
        var config = ConfigManager.Instance.Current;
        config.Url = _tfUrl.value;
        config.PrivateToken = _tfToken.value;
        config.GroupId = _tfGroupID.value;
        config.BlackList.Clear();
        foreach (var child in _foBlackList.Children())
        {
            var lbName = child.Q<Label>("lbName");
            config.BlackList.Add(lbName.text);
        }

        config.refreshFrequency = Enum.Parse<RefreshTime>(_ddRefreshTime.value);
        config.DebugMode = _tgDebugMode.value;
        config.ShowDevTag = _tgDevTag.value;
        config.LogOnConsole = _tgLogOnConsole.value;
        config.InstallToLocal = _tgInstallToLocal.value;
        config.ProjectTag = _tfProjectTag.value;
        ConfigManager.Instance.Save();
        MTPackageManagerWindow.RefreshOpenWindows();
        Close();
    }

    private void OnReset()
    {
        ConfigManager.Instance.Current.ResetToDefault();
        ClearConfigValues();
        SetConfigValues();
    }

    private void OnChangeDebugMode(ChangeEvent<bool> evt)
    {
        
    }
    
    private void OnChangeDevTag(ChangeEvent<bool> evt)
    {
        
    }

    private void OnChangeLogOnConsole(ChangeEvent<bool> evt)
    {
        
    }

    private void OnChangeInstallToLocal(ChangeEvent<bool> evt)
    {
        
    }
}
