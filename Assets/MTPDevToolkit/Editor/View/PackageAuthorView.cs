// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: Package作者信息视图
// ==================================================

using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// 作者信息组件
    /// 用于显示作者信息
    /// </summary>
    public class PackageAuthorView : VisualElement
    {
        private readonly VisualElement _root;
        private TextField _txtName;
        private TextField _txtEmail;
        private TextField _txtUrl;

        private PackageManifest.Author _author;

        public PackageAuthorView()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EditorDefine.GetResPath("AuthorView.uxml"));
            _root = visualTree.CloneTree();
            Add(_root);

            InitView();
        }

        public PackageAuthorView(VisualElement root)
        {
            _root = this.InitFromExistVisualElement(root);
            InitView();
        }

        private void InitView()
        {
            _txtName = _root.Q<TextField>("txtAuthorName");
            _txtName.RegisterValueChangedCallback(evt =>
            {
                if (_author != null)
                    _author.name = evt.newValue;
            });
            _txtEmail = _root.Q<TextField>("txtEmail");
            _txtEmail.RegisterValueChangedCallback(evt =>
            {
                if (_author != null)
                    _author.email = evt.newValue;
            });
            _txtUrl = _root.Q<TextField>("txtUrl");
            _txtUrl.RegisterValueChangedCallback(evt =>
            {
                if (_author != null)
                    _author.url = evt.newValue;
            });
            RefreshView();
        }

        private void RefreshView()
        {
            if (_author == null)
                return;

            if (_txtName != null) _txtName.value = _author.name;
            if (_txtEmail != null) _txtEmail.value = _author.email;
            if (_txtUrl != null) _txtUrl.value = _author.url;
        }

        public void BindData(PackageManifest.Author author)
        {
            _author = author;
            RefreshView();
        }
    }
}