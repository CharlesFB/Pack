// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳、商祥鹏
// @Description: Package作者信息视图
// ==================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// MTP包创建界面
    /// </summary>
    public class PackageCreateWindow : EditorWindow
    {
        // private static readonly Regex s_regPackageID = new Regex("^([^@]+)@([^#]+)(#(.+))?$", RegexOptions.Compiled);

        // ReSharper disable once UnusedMember.Local
        // 目前没有使用，为以后预留
        private static readonly Regex s_regPackageVersion =
            new Regex(@"^([1-9]\d|[1-9])(\.([1-9]\d|\d)){2}$", RegexOptions.Compiled);

        private static PackageCreateWindow s_instance;

        protected Action<PackageManifest> onConfirm;
        protected Action onCancel;
        protected string txtConfirm;
        protected string txtCancel;

        public PackageManifestView packageManifestView;
        protected Button btnCreate;

        protected PackageManifest manifest;

        protected virtual string Uxml => "PackageCreateWindow.uxml";
        protected virtual string USS => "MTPWindow.uss";

        public static void Load(PackageManifest manifest, Action<PackageManifest> onConfirm,
            string confirmTxt = "Create", Action onCancel = null, string cancelTxt = "Cancel")
        {
            try
            {
                s_instance = GetWindow<PackageCreateWindow>();
                s_instance.Init(manifest, onConfirm, onCancel, confirmTxt, cancelTxt);
                s_instance.titleContent = new GUIContent("PackageCreateWindow");
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
        [MenuItem("UIToolkit/Close PackageCreateWindow")]
#endif
        public static void Dispose()
        {
            s_instance = GetWindow<PackageCreateWindow>();
            if (s_instance != null)
            {
                Debug.Log("[PackageCreateWindow]-------Dispose");

                s_instance.Close();
                s_instance = null;
            }
        }

        protected void Init(PackageManifest manifest, Action<PackageManifest> onConfirm, Action onCancel,
            string txtConfirm, string txtCancel)
        {
            // Debug.Log("[PackageCreateWindow]-------init");

            this.manifest = manifest;
            this.onConfirm = onConfirm;
            this.onCancel = onCancel;
            this.txtConfirm = txtConfirm;
            this.txtCancel = txtCancel;

            RefreshManifestView();
        }

        private void CreateGUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EditorDefine.GetResPath(Uxml));
            var uiElement = visualTree.CloneTree();
            rootVisualElement.Add(uiElement);
            uiElement.StretchToParentSize();

            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(EditorDefine.GetResPath(USS));
            rootVisualElement.styleSheets.Add(style);
            InitData();
            InitUI();
        }

        protected virtual void InitUI()
        {
            btnCreate = rootVisualElement.Q<Button>("btnCreate");
            if (btnCreate != null)
            {
                btnCreate.clickable.clicked += OnCreate;
                btnCreate.text = txtConfirm;
            }

            // var btnSample = rootVisualElement.Q<Button>("btnSample");
            // if (btnSample != null)
            // {
            //     btnSample.clickable.clicked += OnAddSample;
            // }

            InitManifestView();
        }

        private void InitManifestView()
        {
            packageManifestView = new PackageManifestView(rootVisualElement.Q<VisualElement>("PackageManifestView"));
        }

        protected virtual void RefreshManifestView()
        {
            packageManifestView.BindData(manifest);
            btnCreate.text = txtConfirm;
        }

        protected async void OnCreate()
        {
            var projectInfo = await PackageDevUtils.PullProjectInfoWithRemote(manifest);
            //save dependencies msg To manifest
            SaveMsgToManifest();

            //check format error
            CheckManifest(projectInfo, out var err, out var warning);

            //save manifest msg To Local
            SaveManifestToLocal(err, warning);
        }

        private void InitData()
        {
            if (manifest == null)
            {
                manifest = new PackageManifest
                {
                    name = "com.magictavern.package",
                    displayName = "Package",
                    version = "1.0.0",
                    description = "a package for...",
                    unity = "2019.4",
                    author = new PackageManifest.Author()
                };
            }
        }

        protected virtual void SaveMsgToManifest()
        {
            if (packageManifestView != null)
            {
                packageManifestView.SaveDependencyData();
            }
        }

        protected virtual void CheckManifest(ProjectInfo projectInfo, out List<string> err, out List<string> warning)
        {
            //PackageDevUtils.IsManifestHasError(manifest, out err, out warning);
            PackageManifestCheckUtils.CheckManifest(manifest, projectInfo, out err, out warning);
        }

        protected virtual void SaveManifestToLocal(List<string> err, List<string> warning)
        {
            if (err.Count > 0)
            {
                if (EditorUtility.DisplayDialog("保存修改", "当前配置有错误，是否仍然保存修改？", "保存", "取消"))
                {
                    onConfirm?.Invoke(manifest);
                }

                return;
            }

            if (warning.Count > 0)
            {
                if (!EditorUtility.DisplayDialog("Warning", "package.json has warnings , ignore or fix", "ignore",
                        "fix"))
                {
                    return;
                }
            }

            Debug.Log("btn create clicked!");
            onConfirm?.Invoke(manifest);
            Close();
        }
    }
}