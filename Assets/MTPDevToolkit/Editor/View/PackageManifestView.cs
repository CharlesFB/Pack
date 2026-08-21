// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳、商祥鹏
// @Description: Package Json视图组件
// ==================================================

using System.Collections.Generic;
using System.Linq;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using System;
using System.Text;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// Package Json视图组件
    /// </summary>
    public class PackageManifestView : VisualElement
    {
        private readonly VisualElement _rootVisualElement;
        private PackageManifest _manifest;
        private string _packagePath;

        private TextField _txtName;
        private TextField _txtDisplayName;
        private TextField _txtVersion;
        private TextField _txtDocURL;
        private TextField _txtDesc;
        private TextField _txtUnity;
        private TextField _txtUnityRelease;
        private TextField _txtChangelog;
        private Button _btnGitChangelog;
        private Button _btnAIChangelog;
        private Button _btnRevertChangelog;
        private Label _lblAILoading;
        private PackageAuthorView _packageAuthorView;
        private TextField _txtSymbols;

        // private ListView _sampleListView;
        private ListView _depItemView;
        private ListView _depGitItemView;

        private Button _btnDepAdd;
        private Button _btnGitDepAdd;
        private Button _btnDepDel;
        private Button _btnGitDepDel;

        private List<Dependency> _dependencies;
        private List<Dependency> _gitDependencies;
        private List<PackageInstallInfo> _mtpPackList;

        public PackageManifestView()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Editor/PackageManifestView.uxml");
            _rootVisualElement = visualTree.CloneTree();
            Add(_rootVisualElement);
            _rootVisualElement.StretchToParentSize();
            _packagePath = PackageDevUtils.GetSelectionPath();
            InitView();
        }

        public PackageManifestView(VisualElement rootVisualElement)
        {
            _rootVisualElement = this.InitFromExistVisualElement(rootVisualElement);
            _packagePath = PackageDevUtils.GetSelectionPath();
            InitView();
        }

        private async void InitView()
        {
            _txtName = _rootVisualElement.Q<TextField>("txtName");
            if (_txtName != null)
            {
                _txtName.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                    _manifest.name = evt.newValue;
                });
            }

            _txtDisplayName = _rootVisualElement.Q<TextField>("txtDisplayName");
            if (_txtDisplayName != null)
            {
                _txtDisplayName.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                    _manifest.displayName = evt.newValue;
                });
            }

            _txtVersion = _rootVisualElement.Q<TextField>("txtVersion");
            if (_txtVersion != null)
            {
                _txtVersion.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                    _manifest.version = evt.newValue;
                });
            }

            _txtDocURL = _rootVisualElement.Q<TextField>("txtDocURL");
            if (_txtDocURL != null)
            {
                _txtDocURL.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                    _manifest.documentationUrl = evt.newValue;
                });
            }

            _txtChangelog = _rootVisualElement.Q<TextField>("txtChangelog");
            if (_txtChangelog != null)
            {
                _txtChangelog.multiline = true;
                _txtChangelog.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                });
            }

            // Initialize changelog generation buttons
            _btnGitChangelog = _rootVisualElement.Q<Button>("btnGitChangelog");
            if (_btnGitChangelog != null)
            {
                _btnGitChangelog.clicked += OnGitChangelogClicked;
            }

            _btnRevertChangelog = _rootVisualElement.Q<Button>("btnRevertChangelog");
            if (_btnRevertChangelog != null)
            {
                _btnRevertChangelog.clicked += OnRevertChangelogClicked;
            }

            _btnAIChangelog = _rootVisualElement.Q<Button>("btnAIChangelog");
            if (_btnAIChangelog != null)
            {
                _btnAIChangelog.clicked += OnAIChangelogClicked;
            }

            _lblAILoading = _rootVisualElement.Q<Label>("lblAILoading");

            _txtDesc = _rootVisualElement.Q<TextField>("txtDesc");
            if (_txtDesc != null)
            {
                _txtDesc.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                    _manifest.description = evt.newValue;
                });
            }

            _txtUnity = _rootVisualElement.Q<TextField>("txtUnity");
            if (_txtUnity != null)
            {
                _txtUnity.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                    _manifest.unity = evt.newValue;
                });
            }

            _txtUnityRelease = _rootVisualElement.Q<TextField>("txtUnityRelease");
            if (_txtUnityRelease != null)
            {
                _txtUnityRelease.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                    _manifest.unityRelease = evt.newValue;
                });
            }

            _txtSymbols = _rootVisualElement.Q<TextField>("txtSymbols");
            if (_txtSymbols != null)
            {
                _txtSymbols.RegisterCallback<ChangeEvent<string>>(evt =>
                {
                    if (_manifest == null)
                        return;
                    _manifest.scriptDefineSymbols = evt.newValue;
                });
            }

            InitAuthorView();
            // InitSampleViews();
            InitDepView();
            InitGitDepView();
            InitChangelogView();

            _mtpPackList = await MTPManager.MTPManager.GetPackageInstallList(false);
            RefreshView();
        }

        public void BindData(PackageManifest manifest)
        {
            if (_manifest != manifest)
            {
                _dependencies = null;
                _gitDependencies = null;
            }
            _manifest = manifest;
            RefreshView();
        }

        private void RefreshView()
        {
            if (_manifest == null)
                return;

            if (_txtName != null) _txtName.value = _manifest.name;
            if (_txtDisplayName != null) _txtDisplayName.value = _manifest.displayName;
            if (_txtVersion != null) _txtVersion.value = _manifest.version;
            if (_txtDocURL != null) _txtDocURL.value = _manifest.documentationUrl;
            
            if (_txtDesc != null) _txtDesc.value = _manifest.description;
            if (_txtUnity != null) _txtUnity.value = _manifest.unity;
            if (_txtUnityRelease != null) _txtUnityRelease.value = _manifest.unityRelease;
            if (_txtSymbols != null) _txtSymbols.value = _manifest.scriptDefineSymbols;

            // RefreshSampleViews();
            RefreshAuthorView();
            RefreshDepView();
            RefreshGitDepView();
        }

        private void InitAuthorView()
        {
            var authorElement = _rootVisualElement.Query<VisualElement>("AuthorView");
            _packageAuthorView = new PackageAuthorView(authorElement);
        }

        private void InitChangelogView()
        {
            if (_txtChangelog == null) return;
            if (string.IsNullOrEmpty(_packagePath)) return;

            var projectPath = Path.GetDirectoryName(Application.dataPath);
            var changelogPath = Path.Combine(projectPath, _packagePath, "CHANGELOG.md");
            if (File.Exists(changelogPath))
            {
                var content = File.ReadAllText(changelogPath);
                if (!string.IsNullOrEmpty(content))
                {
                    _txtChangelog.value = content;
                }
            }
        }

        private void RefreshAuthorView()
        {
            if (_packageAuthorView != null)
            {
                _packageAuthorView.BindData(_manifest.author);
            }
        }

        // private void InitSampleViews()
        // {
        //     _sampleListView = _rootVisualElement.Q<ListView>("listSample");
        //     if (_sampleListView != null)
        //     {
        //         _sampleListView.makeItem = OnMakeSample;
        //         _sampleListView.bindItem = OnBindSample;
        //         if (_manifest != null && _manifest.samples != null)
        //         {
        //             _sampleListView.itemsSource = _manifest.samples;
        //         }
        //
        //         // _sampleListView.selectionType = SelectionType.Multiple;
        //
        //         // Callback invoked when the user double clicks an item
        //         // _sampleListView.onItemsChosen += Debug.Log;
        //
        //         // Callback invoked when the user changes the selection inside the ListView
        //         // _sampleListView.onSelectionChange += Debug.Log;
        //     }
        // }

        // public void RefreshSampleViews()
        // {
        //     // var listSample = rootVisualElement.Q<ListView>("listSample");
        //
        //     if (_manifest == null)
        //         return;
        //
        //     if (_manifest.samples == null)
        //         return;
        //
        //     if (_sampleListView != null)
        //     {
        //         _sampleListView.itemsSource = _manifest.samples;
        //         _sampleListView.Refresh();
        //         _sampleListView.style.height = 90; //_manifest.samples.Count * 90;
        //     }
        // }

        // private void OnBindSample(VisualElement element, int index)
        // {
        //     (element as PackageSampleView)?.BindData(_manifest.samples[index]);
        // }
        //
        // private VisualElement OnMakeSample()
        // {
        //     return new PackageSampleView();
        // }

        private void InitDepView()
        {
            _depItemView = _rootVisualElement.Q<ListView>("listDepItem");
            if (_depItemView != null)
            {
                _depItemView.makeItem = OnMakeItem;
                _depItemView.bindItem = OnBindItem;
            }

            _btnDepAdd = _rootVisualElement.Q<Button>("btnDepAdd");
            _btnDepDel = _rootVisualElement.Q<Button>("btnDepDel");
            _btnDepAdd.clickable.clicked += OnDependencyAdd;
            _btnDepDel.clickable.clicked += OnDependencyDel;
        }

        private void RefreshDepView()
        {
            if (_manifest == null || _manifest.dependencies == null)
                return;
            if (_depItemView == null)
                return;
            if (_dependencies == null)
            {
                _dependencies = new List<Dependency>();
                _depItemView.itemsSource = _dependencies;
                var dependencyList = _manifest.dependencies.Keys.ToList();
                foreach (var key in dependencyList)
                {
                    _dependencies.Add(new Dependency(key, _manifest.dependencies[key]));
                    _depItemView.style.height = Mathf.Min(_dependencies.Count * 60, 150);
                }
            }

            _depItemView.itemsSource = _dependencies;
            //_depItemView.RefreshListView();
#if UNITY_2021_1_OR_NEWER
            _depItemView.Rebuild();
#else
            _depItemView.Refresh();
#endif
        }

        private VisualElement OnMakeItem()
        {
            return new DependencyItemView(false);
        }

        private void OnBindItem(VisualElement element, int index)
        {
            var ele = element as DependencyItemView;
            if (ele == null) return;
            if (_depItemView.itemsSource[index] is Dependency itemData)
            {
                ele.BindData(_mtpPackList, itemData);
            }
        }

        private void InitGitDepView()
        {
            _depGitItemView = _rootVisualElement.Q<ListView>("listGitDepItem");
            if (_depGitItemView != null)
            {
                _depGitItemView.makeItem = OnMakeGitItem;
                _depGitItemView.bindItem = OnBindGitItem;
            }

            _btnGitDepAdd = _rootVisualElement.Q<Button>("btnGitDepAdd");
            _btnGitDepDel = _rootVisualElement.Q<Button>("btnGitDepDel");
            _btnGitDepAdd.clickable.clicked += OnGitDependencyAdd;
            _btnGitDepDel.clickable.clicked += OnGitDependencyDel;
        }

        private void RefreshGitDepView()
        {
            if (_manifest == null || _manifest.dependenciesGit == null)
                return;
            if (_depGitItemView == null)
                return;
            if (_gitDependencies == null)
            {
                _gitDependencies = new List<Dependency>();
                var gitDependencyList = _manifest.dependenciesGit.Keys.ToList();
                foreach (var key in gitDependencyList)
                {
                    _gitDependencies.Add(new Dependency(key, _manifest.dependenciesGit[key]));
                    _depGitItemView.style.height = Mathf.Min(_gitDependencies.Count * 60, 150);
                }
            }

            _depGitItemView.itemsSource = _gitDependencies;
            //_depGitItemView.RefreshListView();
#if UNITY_2021_1_OR_NEWER
            _depGitItemView.Rebuild();
#else
            _depGitItemView.Refresh();
#endif
        }

        private VisualElement OnMakeGitItem()
        {
            return new DependencyItemView(true);
        }

        private void OnBindGitItem(VisualElement element, int index)
        {
            var ele = element as DependencyItemView;
            if (ele == null) return;
            if (_depGitItemView.itemsSource[index] is Dependency itemData)
            {
                ele.BindData(_mtpPackList, itemData);
            }
        }

        private void OnDependencyAdd()
        {
            if (_dependencies == null) return;
            if (_depItemView == null) return;
            _dependencies.Insert(0, new Dependency("InputPackageName", "1.0.0"));
            RefreshDepView();
            _depItemView.style.height = Mathf.Min(_dependencies.Count * 60, 150);
            _depItemView.selectedIndex = 0;
            _depItemView.ScrollToItem(0);
        }

        private void OnDependencyDel()
        {
            if (_dependencies == null || _dependencies.Count == 0) return;
            if (_depItemView == null) return;
            if (_depItemView.selectedIndex < 0) return;
            _dependencies.RemoveAt(_depItemView.selectedIndex);
            RefreshDepView();
            _depItemView.style.height = Mathf.Min(_dependencies.Count * 60, 150);
        }

        private void OnGitDependencyAdd()
        {
            if (_gitDependencies == null) return;
            if (_depGitItemView == null) return;
            _gitDependencies.Insert(0, new Dependency("PackageName", "PackageUrl"));
            RefreshGitDepView();
            _depGitItemView.style.height = Mathf.Min(_gitDependencies.Count * 60, 150);
            _depGitItemView.selectedIndex = 0;
            _depGitItemView.ScrollToItem(0);
        }

        private void OnGitDependencyDel()
        {
            if (_gitDependencies == null || _gitDependencies.Count == 0) return;
            if (_depGitItemView == null) return;
            if (_depGitItemView.selectedIndex < 0) return;
            _gitDependencies.RemoveAt(_depGitItemView.selectedIndex);
            RefreshGitDepView();
            _depGitItemView.style.height = Mathf.Min(_gitDependencies.Count * 60, 150);
        }

        public void SaveDependencyData()
        {
            if (_dependencies == null || _gitDependencies == null) return;
            _manifest.dependencies.Clear();
            _manifest.dependenciesGit.Clear();
            foreach (var dependency in _dependencies)
            {
                if (dependency.packName.Equals("InputPackageName") && dependency.dependencyMsg.Equals("1.0.0"))
                {
                    Debug.LogError($"有默认信息未修改,已自动移除此依赖项:\n dependency.packName:{dependency.packName}" +
                                   $"\n dependency.dependencyMsg:{dependency.dependencyMsg}");
                    continue;
                }

                if (_manifest.dependencies.ContainsKey(dependency.packName))
                {
                    Debug.LogError($"{dependency.packName} 有两个依赖版本;分别是" +
                                   $"{_manifest.dependencies[dependency.packName]}和{dependency.dependencyMsg}");
                    continue;
                }

                _manifest.dependencies.Add(dependency.packName, dependency.dependencyMsg);
            }

            foreach (var gitDependency in _gitDependencies)
            {
                if (gitDependency.packName.Equals("PackageName") && gitDependency.dependencyMsg.Equals("PackageUrl"))
                {
                    Debug.LogError($"有默认信息未修改,已自动移除此依赖项:\n gitDependency.packName:{gitDependency.packName}" +
                                   $"\n gitDependency.dependencyMsg:{gitDependency.dependencyMsg}");
                    continue;
                }

                if (_manifest.dependenciesGit.ContainsKey(gitDependency.packName))
                {
                    Debug.LogError($"{gitDependency.packName} 有两个依赖地址\n分别是" +
                                   $"{_manifest.dependenciesGit[gitDependency.packName]}和{gitDependency.dependencyMsg}");
                    continue;
                }

                _manifest.dependenciesGit.Add(gitDependency.packName, gitDependency.dependencyMsg);
            }
        }

        public TextField GetChangelogField()
        {
            return _txtChangelog;
        }

        [Obsolete("Use BindData instead")]
        public void Init(PackageManifest manifest, Action<PackageManifest> onConfirm, Action onCancel,
            string confirmTxt = "OK", string cancelTxt = "Cancel")
        {
            // Implementation of the Init method
        }

        private void OnGitChangelogClicked()
        {
            var projectPath = Path.GetDirectoryName(Application.dataPath);
            var (lastTag, commits) = ChangelogUtils.GetGitChanges(projectPath);
            if (string.IsNullOrEmpty(lastTag) || commits == null || commits.Count == 0)
            {
                Debug.LogWarning("[PackageManifestView] No git changes found to generate changelog");
                return;
            }

            // Get existing changelog content
            var changelogPath = Path.Combine(projectPath, _packagePath, "CHANGELOG.md");
            var existingContent = File.Exists(changelogPath) ? File.ReadAllText(changelogPath) : null;

            // Generate new changes
            var newChanges = ChangelogUtils.GenerateDefaultChangelog(_manifest?.version, lastTag, commits);
            if (string.IsNullOrEmpty(newChanges))
                return;

            string finalChangelog;
            if (existingContent != null && existingContent.Contains("## [Unreleased]"))
            {
                // If there's an [Unreleased] section, append to it
                var unreleasedIndex = existingContent.IndexOf("## [Unreleased]");
                var nextSectionIndex = existingContent.IndexOf("## [", unreleasedIndex + 1);
                var beforeUnreleased = existingContent.Substring(0, unreleasedIndex);
                var afterUnreleased = nextSectionIndex > 0 ? existingContent.Substring(nextSectionIndex) : "";

                finalChangelog = beforeUnreleased + "## [Unreleased]\n\n" + newChanges + "\n\n" + afterUnreleased;
            }
            else
            {
                // Create new changelog with [Unreleased] section
                var sb = new StringBuilder();
                sb.AppendLine("# Changelog");
                sb.AppendLine();
                sb.AppendLine("## [Unreleased]");
                sb.AppendLine();
                sb.AppendLine(newChanges);
                sb.AppendLine();

                // Add existing content if any
                if (!string.IsNullOrEmpty(existingContent))
                {
                    // Skip the title if it exists in the existing content
                    var contentToAdd = existingContent.StartsWith("# Changelog") 
                        ? existingContent.Substring(existingContent.IndexOf("\n\n") + 2)
                        : existingContent;
                    sb.AppendLine(contentToAdd);
                }

                finalChangelog = sb.ToString();
            }

            _txtChangelog.value = finalChangelog;
        }

        private void OnAIChangelogClicked()
        {
            var projectPath = Path.GetDirectoryName(Application.dataPath);
            var (lastTag, commits) = ChangelogUtils.GetGitChanges(projectPath);
            if (string.IsNullOrEmpty(lastTag) || commits == null || commits.Count == 0)
            {
                Debug.LogWarning("[PackageManifestView] No git changes found to generate changelog");
                return;
            }

            // Show loading indicator
            if (_lblAILoading != null)
            {
                _lblAILoading.text = "生成中...";
            }

            // Get existing changelog content
            var changelogPath = Path.Combine(projectPath, _packagePath, "CHANGELOG.md");
            var existingContent = File.Exists(changelogPath) ? File.ReadAllText(changelogPath) : null;

            // Start AI generation
            AIUtils.GenerateChangelog(commits, (aiGeneratedContent) => {
                // Hide loading indicator
                if (_lblAILoading != null)
                {
                    _lblAILoading.text = "";
                }

                if (string.IsNullOrEmpty(aiGeneratedContent))
                    return;

                string finalChangelog;
                if (existingContent != null && existingContent.Contains("## [Unreleased]"))
                {
                    // If there's an [Unreleased] section, append to it
                    var unreleasedIndex = existingContent.IndexOf("## [Unreleased]");
                    var nextSectionIndex = existingContent.IndexOf("## [", unreleasedIndex + 1);
                    var beforeUnreleased = existingContent.Substring(0, unreleasedIndex);
                    var afterUnreleased = nextSectionIndex > 0 ? existingContent.Substring(nextSectionIndex) : "";

                    finalChangelog = beforeUnreleased + "## [Unreleased]\n\n" + aiGeneratedContent + "\n\n" + afterUnreleased;
                }
                else
                {
                    // Create new changelog with [Unreleased] section
                    var sb = new StringBuilder();
                    sb.AppendLine("# Changelog");
                    sb.AppendLine();
                    sb.AppendLine("## [Unreleased]");
                    sb.AppendLine();
                    sb.AppendLine(aiGeneratedContent);
                    sb.AppendLine();

                    // Add existing content if any
                    if (!string.IsNullOrEmpty(existingContent))
                    {
                        // Skip the title if it exists in the existing content
                        var contentToAdd = existingContent.StartsWith("# Changelog") 
                            ? existingContent.Substring(existingContent.IndexOf("\n\n") + 2)
                            : existingContent;
                        sb.AppendLine(contentToAdd);
                    }

                    finalChangelog = sb.ToString();
                }

                _txtChangelog.value = finalChangelog;
            }, _manifest?.version);
        }

        private void OnRevertChangelogClicked()
        {
            var projectPath = Path.GetDirectoryName(Application.dataPath);
            var changelogPath = Path.Combine(projectPath, _packagePath, "CHANGELOG.md");
            
            if (File.Exists(changelogPath))
            {
                var content = File.ReadAllText(changelogPath);
                if (!string.IsNullOrEmpty(content))
                {
                    _txtChangelog.value = content;
                }
                else
                {
                    _txtChangelog.value = "";
                }
            }
            else
            {
                _txtChangelog.value = "";
            }
        }

        /// <summary>
        /// 获取当前包的版本号
        /// </summary>
        /// <returns>版本号字符串，如果未绑定数据则返回null</returns>
        public string GetVersion()
        {
            return _manifest?.version;
        }
    }

    public class Dependency
    {
        public string packName;
        public string dependencyMsg; //依赖信息，如果是unity官方依赖，则信息是版本，如果是git依赖，则是URL地址

        public Dependency(string packName, string dependencyMsg)
        {
            this.dependencyMsg = dependencyMsg;
            this.packName = packName;
        }
    }
}