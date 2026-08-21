// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳、商祥鹏
// @Description: 发布界面，发布检查使用的界面
// ==================================================

using System;
using System.Collections.Generic;
using System.Text;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// 发布界面，发布检查使用的界面
    /// </summary>
    public class PackagePublishWindow : PackageCreateWindow
    {
        public static PackagePublishWindow s_instance;

        private Label _lbError;
        private Label _lbWarning;
        private VisualElement _nodeCheck;

        // private List<string> _errors;
        // private List<string> _warning;

        protected override string Uxml
        {
            get { return "PackagePublishWindow.uxml"; }
        }

        public static string GetCurrentChangelog()
        {
            if (s_instance == null || s_instance.packageManifestView == null)
                return null;
                
            var changelogField = s_instance.packageManifestView.GetChangelogField();
            return changelogField?.value;
        }

        public new static void Load(PackageManifest manifest, Action<PackageManifest> onConfirm,
            string confirmTxt = "Publish", Action onCancel = null, string cancelTxt = "Cancel")
        {
            try
            {
                s_instance = GetWindow<PackagePublishWindow>();
                s_instance.Init(manifest, onConfirm, onCancel, confirmTxt, cancelTxt);
                s_instance.titleContent = new GUIContent("PackagePublishWindow");
            }
            catch (Exception e)
            {
                // Debug.Log($"[PackageCreateWindow]-------exceptionL{e.Message}");
                Debug.LogException(e);
                if (s_instance != null)
                {
                    s_instance.Close();
                    s_instance = null;
                }
            }
        }

#if PACKAGE_DEV
        [MenuItem("UIToolkit/Close PublishWindow")]
#endif
        public new static void Dispose()
        {
            s_instance = GetWindow<PackagePublishWindow>();
            if (s_instance != null)
            {
                Debug.Log("[PackageCreateWindow]-------Dispose");

                s_instance.Close();
                s_instance = null;
            }
        }

        protected override void InitUI()
        {
            base.InitUI();
            _nodeCheck = rootVisualElement.Q<VisualElement>("nodeCheck");
            _nodeCheck.visible = false;
            _nodeCheck.style.width = new StyleLength(Length.Percent(_nodeCheck.visible ? 20 : 0));
            _lbError = rootVisualElement.Q<Label>("lbError");
            _lbWarning = rootVisualElement.Q<Label>("lbWarning");
            btnCreate = rootVisualElement.Q<Button>("btnPublish");
            if (btnCreate != null)
            {
                btnCreate.clickable.clicked += OnCreate;
                btnCreate.text = txtConfirm;
            }
        }

        protected override void CheckManifest(ProjectInfo projectInfo, out List<string> err, out List<string> warning)
        {
            //PackageDevUtils.IsManifestHasError(manifest, out err, out warning);
            PackageManifestCheckUtils.CheckManifest(manifest, projectInfo, out err, out warning);

            var sb = new StringBuilder();
            foreach (var e in err)
            {
                sb.AppendLine(e);
            }

            _lbError.text = sb.ToString();

            sb.Clear();
            foreach (var e in warning)
            {
                sb.AppendLine(e);
            }

            _lbWarning.text = sb.ToString();

            _nodeCheck.visible = err.Count > 0 || warning.Count > 0;
            _nodeCheck.style.width = new StyleLength(Length.Percent(_nodeCheck.visible ? 20 : 0));
        }
    }
}