// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: Sample视图组件
// ==================================================

using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// Sample视图组件，用于显示Sample相关内容
    /// </summary>
    public class PackageSampleView : VisualElement
    {
        private readonly TextField _txtDisplayName;
        private readonly TextField _txtDesc;
        private readonly TextField _txtPath;

        private PackageManifest.Sample _sample;

        public PackageSampleView()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EditorDefine.GetResPath("SampleView.uxml"));
            var root = visualTree.CloneTree();
            _txtDisplayName = root.Q<TextField>("txtDisplayName");
            _txtDisplayName.RegisterValueChangedCallback(OnDisplayNameChanged);
            _txtDesc = root.Q<TextField>("txtDesc");
            _txtDesc.RegisterValueChangedCallback(OnDescChanged);
            _txtPath = root.Q<TextField>("txtPath");
            _txtPath.RegisterValueChangedCallback(OnPathChanged);
            Add(root);
        }

        private void OnPathChanged(ChangeEvent<string> evt)
        {
            if (_sample != null)
            {
                _sample.path = evt.newValue;
            }
        }

        private void OnDescChanged(ChangeEvent<string> evt)
        {
            if (_sample != null)
            {
                _sample.description = evt.newValue;
            }
        }

        private void OnDisplayNameChanged(ChangeEvent<string> evt)
        {
            if (_sample != null)
            {
                _sample.displayName = evt.newValue;
            }
        }

        public void BindData(PackageManifest.Sample sample)
        {
            _sample = sample;
            _txtDisplayName.value = _sample.displayName;
            _txtDesc.value = _sample.description;
            _txtPath.value = _sample.path;
        }
    }
}