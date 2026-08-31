// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 在 Unity Package Manager 导航栏最左侧注入 MTP 入口按钮
// ==================================================

using System;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MagicTavern.FrontEngine.MTPManager
{
    [InitializeOnLoad]
    public class UPMExtension : IPackageManagerExtension
    {
        private const string ButtonName = "mtButton";

        private PackageExtensionView _view;

        static UPMExtension()
        {
            PackageManagerExtensions.RegisterExtension(new UPMExtension());
        }

        public VisualElement CreateExtensionUI()
        {
            if (_view == null)
            {
                _view = new PackageExtensionView(MTPMenuEntry.OpenPackageManagerWindow);
            }

            return _view;
        }

        public void OnPackageSelectionChange(PackageInfo packageInfo)
        {
            _view?.Refresh();
        }

        public void OnPackageAddedOrUpdated(PackageInfo packageInfo)
        {
            _view?.Refresh();
        }

        public void OnPackageRemoved(PackageInfo packageInfo)
        {
        }

        private class PackageExtensionView : VisualElement
        {
            private readonly Action _onClick;

            public PackageExtensionView(Action onClick)
            {
                _onClick = onClick;
                pickingMode = PickingMode.Ignore;
                style.display = DisplayStyle.None;
                RegisterCallback<AttachToPanelEvent>(_ => schedule.Execute(Refresh));
            }

            public void Refresh()
            {
                var root = this.GetRoot();
                if (root == null || root.Q<ToolbarButton>(ButtonName) != null)
                {
                    return;
                }

                var addButton = root.Q("toolbarAddMenu")
                                ?? root.Q("toolbarAddButton")
                                ?? root.Q("moreAddOptionsButton");
                if (addButton?.parent != null)
                {
                    addButton.parent.Insert(0, CreateButton());
                    return;
                }

                var toolbar = root.Q<Toolbar>();
                if (toolbar != null)
                {
                    toolbar.Insert(0, CreateButton());
                }
            }

            private ToolbarButton CreateButton()
            {
                var button = new ToolbarButton(_onClick)
                {
                    name = ButtonName,
                    tooltip = "Open MTP Manager",
                };
                button.style.alignItems = Align.Center;
                button.style.justifyContent = Justify.Center;
                button.style.height = 20;
                button.style.width = 22;
                button.style.paddingLeft = 1;
                button.style.paddingRight = 1;
                button.style.marginLeft = 1;
                button.style.marginRight = 1;
                button.style.borderRightWidth = 1;
                button.style.flexShrink = 0;

                var image = new VisualElement();
                image.pickingMode = PickingMode.Ignore;
                image.style.width = 19;
                image.style.height = 19;
                var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(MTPUI.LOGO_SMALL_PATH);
                if (logo != null)
                {
                    image.style.backgroundImage = new StyleBackground(logo);
                }

                button.Add(image);
                return button;
            }
        }
    }
}
