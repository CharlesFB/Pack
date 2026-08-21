// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 商祥鹏
// @Maintainer: 商祥鹏
// @Description: 依赖信息组件
// ==================================================

using System.Collections.Generic;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// 依赖信息组件
    /// 用于显示包需要的unity依赖和git依赖
    /// </summary>
    public class DependencyItemView : VisualElement
    {
        private List<PackageInstallInfo> _mtpPackList;

        private readonly VisualElement _root;
        private TextField _txtPackName;
        private TextField _txtVersion;

        private Button _btnSelect;

        private Dependency _dependency;

        public DependencyItemView(bool isGitDependency)
        {
            var visualTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EditorDefine.GetResPath("DependencyItemView.uxml"));
            _root = visualTree.CloneTree();
            Add(_root);
            InitView(isGitDependency);
        }

        private void InitView(bool isGitDependency)
        {
            _txtPackName = _root.Q<TextField>("txPackName");
            _txtVersion = _root.Q<TextField>("txVersionAddress");
            _btnSelect = _root.Q<Button>("btnSelect");
            if (isGitDependency)
            {
                _txtVersion.label = "Address";
                _btnSelect.clickable.clicked += OnShowPackageName;
            }
            else
            {
                _btnSelect.visible = false;
                _btnSelect.SetEnabled(false);
                _btnSelect.style.width = 0;
            }

            _txtPackName.RegisterValueChangedCallback(OnPackNameChanged);
            _txtVersion.RegisterValueChangedCallback(OnDependencyMsgChanged);
        }

        private void OnShowPackageName()
        {
            if (_mtpPackList == null || _mtpPackList.Count == 0)
            {
                Debug.LogWarning("[DependencyItemView] Package list is not loaded yet, please wait...");
                return;
            }

            var menu = new GenericMenu();

            async void Callback(object v)
            {
                var package = v as PackageInstallInfo;
                if (package == null) return;

                string version = package.Revision;
                if (version.Contains("debug"))
                {
                    foreach (string projectInfoVersion in package.ProjectInfo.Versions)
                    {
                        if (!projectInfoVersion.Contains("debug"))
                        {
                            version = projectInfoVersion;
                            break;
                        }
                    }

                    if (version.Contains("debug"))
                    {
                        Debug.LogError($"{package.ProjectInfo.PackageName}没有正式发布版本");
                        return;
                    }
                }

                var manifest = await MTPManager.MTPManager.GetPackageJson(package.ProjectInfo, version);
                if (_txtPackName != null) _txtPackName.value = manifest.name;
                if (_txtVersion != null) _txtVersion.value = package.ProjectInfo.HttpUrl + "#" + version;
            }

            foreach (var packName in _mtpPackList)
            {
                var projectInfoPackageName = packName.ProjectInfo.PackageName;
                menu.AddItem(new GUIContent(projectInfoPackageName), false, Callback, packName);
            }

            menu.DropDown(_txtPackName.worldBound);
        }

        private void OnPackNameChanged(ChangeEvent<string> evt)
        {
            if (_dependency != null) _dependency.packName = evt.newValue;
        }

        private void OnDependencyMsgChanged(ChangeEvent<string> evt)
        {
            if (_dependency != null) _dependency.dependencyMsg = evt.newValue;
        }

        public void BindData(List<PackageInstallInfo> list, Dependency dependency)
        {
            _dependency = dependency;
            _mtpPackList = list;
            _txtPackName.value = dependency.packName;
            _txtVersion.value = dependency.dependencyMsg;
        }
    }
}