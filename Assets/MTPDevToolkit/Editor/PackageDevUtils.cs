// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳、商祥鹏
// @Description: MTP开发编辑器入口，MTP包的创建、发布相关菜单命令在这里
// ==================================================

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Threading.Tasks;
using MagicTavern.FrontEngine.MTPManager.LitJson;
using MagicTavern.FrontEngine.MTPManager;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using PackageManifest = MagicTavern.FrontEngine.MTPManager.PackageManifest;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// MTP开发过程中使用到的基础功能封装
    /// </summary>
    public static class PackageDevUtils
    {
        private const string BRANCH_MASTER = "master";
        private const string BRANCH_DEV = "develop";
        private const string LOCAL_DEV_REPLACE_PATH = "Packages/DevReplace.json"; //开发包替代git包信息路径

        private static readonly List<string> s_folderList = new List<string>
        {
            "Editor", "Runtime"
        };

        private static readonly List<string> s_fileList = new List<string>
        {
            "package.json", "README.md", "CHANGELOG.md", "LICENSE.md"
        };

        private static readonly char[] s_trimChars = {'\n', '\r'};

        public static void CreatePackageFolder(PackageManifest manifest, bool checkFolderExist = true)
        {
            if (AssetDatabase.IsValidFolder($"Assets/{manifest.displayName}"))
            {
                if (checkFolderExist)
                {
                    if (!EditorUtility.DisplayDialog("Alert", "文件夹已存在，是否仍然在此文件夹建立Package？", "ok", "cancel"))
                    {
                        return;
                    }
                }

                var file = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/{manifest.displayName}/package.json");
                if (file != null)
                {
                    Debug.LogError("[Create Package] It is a package already!");
                    return;
                }
            }
            else
            {
                AssetDatabase.CreateFolder("Assets", manifest.displayName);
                AssetDatabase.Refresh();
            }

            foreach (var folder in s_folderList)
            {
                if (AssetDatabase.IsValidFolder($"Assets/{manifest.displayName}/{folder}"))
                    continue;
                AssetDatabase.CreateFolder($"Assets/{manifest.displayName}", folder);
                var fakeAssembly = new FakeAssemblyDefinition()
                {
                    name = folder == "Runtime"
                        ? $"MagicTavern.FrontEngine.{manifest.displayName}"
                        : $"MagicTavern.FrontEngine.{manifest.displayName}.{folder}",
                };
                if (folder == "Editor")
                {
                    fakeAssembly.includePlatforms.Add("Editor");
                    fakeAssembly.references = new List<string>
                    {
                        $"MagicTavern.FrontEngine.{manifest.displayName}"
                    };
                }

                var fullPath = Path.GetFullPath(
                    $"Assets/{manifest.displayName}/{folder}/{fakeAssembly.name}.asmdef");
                File.WriteAllText(fullPath, JsonMapper.ToJson(fakeAssembly));
            }

            //create test folder
            var testFolder = $"Assets/{manifest.displayName}/Tests";
            var testRuntimeFolder = $"Assets/{manifest.displayName}/Tests/Runtime";
            var testEditorFolder = $"Assets/{manifest.displayName}/Tests/Editor";
            if (!AssetDatabase.IsValidFolder(testFolder))
            {
                AssetDatabase.CreateFolder($"Assets/{manifest.displayName}", "Tests");
                // create runtime folder
                if (!AssetDatabase.IsValidFolder(testRuntimeFolder))
                {
                    AssetDatabase.CreateFolder(testFolder, "Runtime");
                    var fakeAssembly = new FakeAssemblyDefinition()
                    {
                        name = $"MagicTavern.FrontEngine.{manifest.displayName}.Tests",
                        overrideReferences = true,
                        references = new List<string>
                        {
                            "UnityEngine.TestRunner",
                            "UnityEditor.TestRunner",
                            $"MagicTavern.FrontEngine.{manifest.displayName}"
                        },
                        precompiledReferences = new List<string> {"nunit.framework.dll"},
                        defineConstraints = new List<string> {"UNITY_INCLUDE_TESTS"},
                    };
                    var fullPath = Path.GetFullPath($"{testRuntimeFolder}/{fakeAssembly.name}.asmdef");
                    File.WriteAllText(fullPath, JsonMapper.ToJson(fakeAssembly));
                }

                // create editor folder
                if (!AssetDatabase.IsValidFolder(testEditorFolder))
                {
                    AssetDatabase.CreateFolder(testFolder, "Editor");
                    var fakeAssembly = new FakeAssemblyDefinition()
                    {
                        name = $"MagicTavern.FrontEngine.{manifest.displayName}.Editor.Tests",
                        overrideReferences = true,
                        references = new List<string>
                        {
                            "UnityEngine.TestRunner",
                            "UnityEditor.TestRunner",
                            $"MagicTavern.FrontEngine.{manifest.displayName}",
                            $"MagicTavern.FrontEngine.{manifest.displayName}.Editor"
                        },
                        includePlatforms = new List<string> {"Editor"},
                        precompiledReferences = new List<string> {"nunit.framework.dll"},
                        defineConstraints = new List<string> {"UNITY_INCLUDE_TESTS"},
                    };
                    var fullPath = Path.GetFullPath($"{testEditorFolder}/{fakeAssembly.name}.asmdef");
                    File.WriteAllText(fullPath, JsonMapper.ToJson(fakeAssembly));
                }
            }

            AssetDatabase.Refresh();

            foreach (var file in s_fileList)
            {
                var path = $"{Application.dataPath}/{manifest.displayName}/{file}";
                if (File.Exists(path))
                    continue;
                var content = file;
                if (file == "package.json")
                {
                    // content = Json.Serialize(manifest, true);
                    content = JsonMapper.ToJson(manifest, true);
                }

                File.WriteAllText(path, content);
            }

            AssetDatabase.Refresh();
            Debug.Log("[Create Package] create package finished");
        }

        // public static bool IsManifestHasError(PackageManifest manifest, out List<string> err, out List<string> warning,
        //     bool forDebug = false)
        // {
        //     err = new List<string>();
        //     warning = new List<string>();
        //
        //     // check name
        //     var nameError = false;
        //     if (string.IsNullOrEmpty(manifest.name))
        //     {
        //         err.Add("[package name] 包名不能为空!");
        //         nameError = true;
        //     }
        //
        //     if (manifest.name.Length > 50)
        //     {
        //         warning.Add("[package name] 包名太长会影响UI显示!");
        //         // nameError = true;
        //     }
        //
        //     if (manifest.name.Length > 214)
        //     {
        //         err.Add("[package name] 包名过长超出214字符限制!");
        //         nameError = true;
        //     }
        //
        //     var match = s_packageNameRegex.Match(manifest.name);
        //     if (!match.Success || match.Value != manifest.name)
        //     {
        //         err.Add("[package name] 包名无效，请注意检查:\n1.符合xx.xx.xx的形式.\n2.不能包含除数字、小写字母之外的字符!\n");
        //         nameError = true;
        //     }
        //
        //     // check display name
        //
        //     if (string.IsNullOrEmpty(manifest.displayName))
        //     {
        //         err.Add("[package displayName] 显示名称不能为空!");
        //     }
        //
        //     // version
        //
        //     if (string.IsNullOrEmpty(manifest.version))
        //     {
        //         err.Add("[package version] 版本号为空!");
        //     }
        //
        //     match = s_packageVersionRegex.Match(manifest.version);
        //     if (!match.Success || match.Value != manifest.version)
        //     {
        //         err.Add("[package version] 版本号不是合法形式，要符合A.B.C的形式!");
        //     }
        //
        //     if (!nameError && !forDebug)
        //     {
        //         var oldVersion = EditorPrefs.GetString(manifest.name, null);
        //         if (oldVersion != null && oldVersion == manifest.version)
        //         {
        //             err.Add("[package version] 发布新版请更新版本号!");
        //         }
        //     }
        //
        //     if (string.IsNullOrEmpty(manifest.unity))
        //     {
        //         err.Add("[unity version] Unity兼容版本不能为空!");
        //     }
        //
        //     if (string.IsNullOrEmpty(manifest.description))
        //     {
        //         warning.Add("[package description] 描述尽量不要为空!");
        //     }
        //
        //     var unityPackageList = JsonUtils.LoadFromFile<UnityPackageList>("Packages/manifest.json", false);
        //
        //     if (manifest.dependencies != null)
        //     {
        //         foreach (KeyValuePair<string, string> depKvp in manifest.dependencies)
        //         {
        //             if (unityPackageList.dependencies.TryGetValue(depKvp.Key, out var version))
        //             {
        //                 if (version != depKvp.Value)
        //                 {
        //                     Debug.LogError($"{depKvp.Key}: ref:{depKvp.Value}------in manifest:{version}");
        //                     err.Add($"[unity dependencies] Package 依赖包{depKvp.Key}版本不一致!配置：{depKvp.Value}，实际：{version}");
        //                 }
        //             }
        //             else
        //             {
        //                 Debug.LogError($"{depKvp.Key}: ref:{depKvp.Value}------not real dep");
        //                 err.Add($"[unity dependencies] Package 依赖包{depKvp.Key}并无实际依赖！");
        //             }
        //         }
        //     }
        //
        //     if (manifest.dependenciesGit != null)
        //     {
        //         foreach (KeyValuePair<string, string> depKvp in manifest.dependenciesGit)
        //         {
        //             if (unityPackageList.dependencies.TryGetValue(depKvp.Key, out var version))
        //             {
        //                 var formatVersion = PackageDepInfo.FormatUrl(version);
        //                 if (formatVersion != depKvp.Value)
        //                 {
        //                     Debug.LogError($"{depKvp.Key}:git ref:{depKvp.Value}------in manifest:{formatVersion}");
        //                     err.Add($"[unity dependenciesGit] Package Git依赖包{depKvp.Key}版本不一致!配置：{depKvp.Value}，实际：{formatVersion}");
        //                 }
        //             }
        //             else
        //             {
        //                 Debug.LogError($"{depKvp.Key}: ref:{depKvp.Value}------not real dep");
        //                 err.Add($"[unity dependenciesGit] Package Git依赖包{depKvp.Key}并无实际依赖！");
        //             }
        //         }
        //     }
        //
        //     return err.Count > 0;
        // }

        public static void PublishInternal(bool forDebug = false)
        {
            var manifest = GetSelectionPackageManifest(out var filePath);
            if (manifest == null)
                return;

            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            // subtree 发布分支
            var cmd = $"git rev-parse --show-toplevel";
            var rootPath = ShellUtils.ProcessCommandSync(cmd, projectPath).Trim(s_trimChars);
            
            var tagVersion = manifest.version;
            var releaseBranch = GetReleaseBranchName(rootPath, manifest);

            // 获取包目录prefix
            cmd = "git rev-parse --show-prefix";
            var prefix = ShellUtils.ProcessCommandSync(cmd, $"{projectPath}/{filePath}").Trim(s_trimChars);
            
            // subtree 发布分支
            cmd = $"git subtree split --prefix={prefix} --branch {releaseBranch}";
            ShellUtils.ProcessCommandSync(cmd, rootPath);

            // 发布 Tag
            var publishTag = $"{releaseBranch}-{tagVersion}";
            if (forDebug)
            {
                publishTag = $"{releaseBranch}-debug";
            }

            cmd = $"git tag {publishTag} {releaseBranch}";
            ShellUtils.ProcessCommandSync(cmd, rootPath);
            cmd = $"git push origin refs/tags/{publishTag} --verbose";
            ShellUtils.ProcessCommandSync(cmd, rootPath);

            if (!forDebug)
            {
                // 添加开发分支 Tag
                var devBranchTag = $"dev-{publishTag}";
                cmd = $"git tag {devBranchTag}";
                ShellUtils.ProcessCommandSync(cmd, rootPath);
                cmd = $"git push origin refs/tags/{devBranchTag} --verbose";
                ShellUtils.ProcessCommandSync(cmd, rootPath);
            }

            // 不能简单的同步全部tag
            // cmd = $"git push origin {releaseBranch} --tags";
            // ShellUtils.ProcessCommandSync(cmd, projectPath);

            if (forDebug)
                return;

            EditorPrefs.SetString(manifest.name, manifest.version);
        }

        public static string GitStatus()
        {
            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            var cmd = "git status";
            var result = ShellUtils.ProcessCommandSync(cmd, projectPath);
            return result;
        }

        public static bool HaveFileChangeToCommit(string fileName, out string message)
        {
            string cmd;
            string[] nameSplit = fileName.Split('.');
            string name = string.Join("\\.", nameSplit);
            
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            cmd = $"git status --porcelain | grep \'^[M| |A].*{name}\' ";
#elif UNITY_EDITOR_WIN
            cmd = $"git status --porcelain | findstr \'^[M| |A].*{name}\' ";
#endif
            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            message = ShellUtils.ProcessCommandSync(cmd, projectPath);
            
            return !string.IsNullOrEmpty(message);
        }

        public static bool HaveChangeToCommit(out string message)
        {
            message = GitStatus();
            var messages = message.Split('\n', '\r');
            var lastIndex = messages.Length - 1;
            var lastMessage = messages[lastIndex];
            while (string.IsNullOrEmpty(lastMessage) && lastIndex > 0)
            {
                lastIndex--;
                lastMessage = messages[lastIndex];
            }

            return !lastMessage.EndsWith("nothing to commit, working tree clean");
        }

        public static void ClearCurrentVersionTags(bool forDebug)
        {
            var manifest = GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;

            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            var tagVersion = manifest.version;
            var releaseBranch = GetReleaseBranchName(projectPath, manifest);

            var tagName = $"{releaseBranch}-{tagVersion}";
            if (forDebug)
            {
                tagName = $"{releaseBranch}-debug";
            }

            var cmd = $"git tag --delete {tagName}";
            ShellUtils.ProcessCommandSync(cmd, projectPath);
            cmd = $"git push --delete origin refs/tags/{tagName}";
            ShellUtils.ProcessCommandSync(cmd, projectPath);

            if (!forDebug)
            {
                // 添加开发分支 Tag
                var devBranchTag = $"dev-{tagName}";
                cmd = $"git tag --delete {devBranchTag}";
                ShellUtils.ProcessCommandSync(cmd, projectPath);
                cmd = $"git push --delete origin refs/tags/{devBranchTag}";
                ShellUtils.ProcessCommandSync(cmd, projectPath);
            }

            if (forDebug)
                return;
            EditorPrefs.DeleteKey(manifest.name);
        }

        public static string GetReleaseBranchName(string projectPath, PackageManifest manifest)
        {
            // var cmd = "git branch --show-current";
            var cmd = "git rev-parse --abbrev-ref HEAD";
            var devBranch = ShellUtils.ProcessCommandSync(cmd, projectPath);

            devBranch = devBranch.TrimEnd(s_trimChars);
            if (devBranch == BRANCH_MASTER || devBranch == BRANCH_DEV)
            {
                devBranch = null;
            }

            var releaseBranch = manifest.displayName.ToLower();
            if (!string.IsNullOrEmpty(devBranch))
            {
                releaseBranch = $"{releaseBranch}-{devBranch}";
            }

            return releaseBranch;
        }

        public static async Task<ProjectInfo> PullProjectInfoWithRemote(PackageManifest manifest)
        {
            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            
            var cmd = $"git rev-parse --show-toplevel";
             
            var rootPath = ShellUtils.ProcessCommandSync(cmd, projectPath).Trim(s_trimChars);
            
            var currentBranch = PackageDevUtils.GetReleaseBranchName(rootPath, manifest);

            var projectInfo = await MTPManager.MTPManager.PullProjectInfoWithGitlab(manifest, currentBranch);

            return projectInfo;
        } 

        public static PackageManifest GetSelectionPackageManifest(out string filePath)
        {
            filePath = GetSelectionPath();
            if (string.IsNullOrEmpty(filePath))
                return null;

            return GetPackageManifest(filePath);
        }

        public static PackageManifest GetPackageManifest(string filePath)
        {
            if (!AssetDatabase.IsValidFolder(filePath))
            {
                return null;
            }

            if (!filePath.StartsWith("Assets"))
            {
                return null;
            }

            var file = AssetDatabase.LoadAssetAtPath<TextAsset>($"{filePath}/package.json");
            if (file == null)
            {
                return null;
            }

            var manifest = JsonMapper.ToObject<PackageManifest>(file.text);
            if (manifest == null)
            {
                return null;
            }

            return manifest;
        }

        public static string GetSelectionPath()
        {
            if (Selection.assetGUIDs.Length == 0)
                return null;

            return AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
        }

        public static PackageManifest GetSelectionParentManifest(out string parentPath, out string folderName)
        {
            parentPath = null;
            folderName = null;
            if (Selection.assetGUIDs.Length == 0)
                return null;

            var filePath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);

            if (!AssetDatabase.IsValidFolder(filePath))
            {
                return null;
            }

            folderName = Path.GetFileName(filePath);

            var dirInfo = Directory.GetParent(filePath);
            if (dirInfo == null)
                return null;

            parentPath = dirInfo.ToString();

            var file = AssetDatabase.LoadAssetAtPath<TextAsset>($"{parentPath}/package.json");
            if (file == null)
            {
                return null;
            }

            var manifest = JsonMapper.ToObject<PackageManifest>(file.text);
            if (manifest == null)
            {
                return null;
            }

            return manifest;
        }

        public static string FormatUrl(string url)
        {
            if (url.ToLower().StartsWith("git@"))
                url = "ssh://" + url.Replace(':', '/');
            return url;
        }

        public static void AddDevPackageToTestProject(PackageManifest manifest, string projectPath)
        {
            var projectName = Path.GetFileName(projectPath);
            projectPath = Path.Combine(projectPath, "Packages", "manifest.json");
            dynamic temp = JsonConvert.DeserializeObject<ExpandoObject>(File.ReadAllText(projectPath));
            var dependencies = (IDictionary<string, object>) temp!.dependencies;
            var localPath = Path.Combine(Environment.CurrentDirectory, GetSelectionPath());
            if (dependencies.ContainsKey(GetSelectionPackageManifest(out string _).name))
            {
                if (dependencies[manifest.name].ToString().Contains("file:"))
                {
                    Debug.Log($"[MTPDevToolKit]项目工程:{projectName}测试包替换测试包,包名:{manifest.name}");
                }
                else
                {
                    var msgs = JsonUtils.LoadFromFile<List<DevReplaceInfo>>(LOCAL_DEV_REPLACE_PATH);
                    msgs.Add(new DevReplaceInfo(manifest.name, dependencies[manifest.name].ToString(), projectPath));
                    JsonUtils.SaveToFile(LOCAL_DEV_REPLACE_PATH, msgs);
                    Debug.Log($"[MTPDevToolKit]项目工程:{projectName}测试包替换原有包,包名:{manifest.name}");
                }

                dependencies[manifest.name] = "file:" + localPath;
            }
            else
            {
                Debug.Log($"[MTPDevToolKit]项目工程:{projectName}添加测试包,包名:{manifest.name}");
                dependencies.Add(manifest.name, "file:" + localPath);
            }

            File.WriteAllText(projectPath, JsonConvert.SerializeObject(temp, Formatting.Indented));
        }

        public static void DelDevPackageFromTestProject(PackageManifest manifest, string projectPath)
        {
            var projectName = Path.GetFileName(projectPath);
            projectPath = Path.Combine(projectPath, "Packages", "manifest.json");
            dynamic temp = JsonConvert.DeserializeObject<ExpandoObject>(File.ReadAllText(projectPath));
            var dependencies = (IDictionary<string, object>) temp!.dependencies;
            //var temp = JsonUtils.LoadFromFile<Dictionary<string, Dictionary<string, string>>>(projectPath);

            if (dependencies.ContainsKey(manifest.name))
            {
                if (dependencies[manifest.name].ToString().Contains("file:"))
                {
                    dependencies.Remove(manifest.name);
                    Debug.Log($"[MTPDevToolKit]项目工程:{projectName}已移除测试包,包名:{manifest.name}");
                    //添加原有包信息
                    var msgs = JsonUtils.LoadFromFile<List<DevReplaceInfo>>(LOCAL_DEV_REPLACE_PATH);
                    for (var i = 0; i < msgs.Count; i++)
                    {
                        if (msgs[i].packageName.Equals(manifest.name) && msgs[i].projectPath.Equals(projectPath))
                        {
                            dependencies.Add(manifest.name, msgs[i].originalInfo);
                            msgs.Remove(msgs[i]);
                            Debug.Log($"[MTPDevToolKit]项目工程:{projectName}已添加原有包,包名:{manifest.name}");
                            break;
                        }
                    }

                    JsonUtils.SaveToFile(LOCAL_DEV_REPLACE_PATH, msgs);
                }
                else
                {
                    Debug.LogError($"[MTPDevToolKit]项目工程:{projectName}没有使用测试包,包名:{manifest.name}");
                }
            }
            else
            {
                Debug.LogError($"[MTPDevToolKit]项目工程:{projectName}没有此包,包名:{manifest.name}");
            }

            File.WriteAllText(projectPath, JsonConvert.SerializeObject(temp, Formatting.Indented));
        }

        // 开发标记功能
        // /// <summary>
        // /// 获取当前开发包所在git分支名
        // /// </summary>
        // /// <param name="projectPath">开发包的所在路径</param>
        // /// <returns>git分支名</returns>
        // private static string GetGitBranchName(string projectPath)
        // {
        //     var cmd = "git rev-parse --abbrev-ref HEAD";
        //     var devBranch = ShellUtils.ProcessCommandSync(cmd, projectPath);
        //
        //     devBranch = devBranch.TrimEnd(s_trimChars);
        //     return devBranch;
        // }
        // /// <summary>
        // /// 添加开发标记
        // /// </summary>
        // /// <param name="manifest">package.json文件</param>
        // /// <returns>添加成功返回true，已有标记则返回false</returns>
        // public static bool AddDevTag(PackageManifest manifest)
        // {
        //     var tags = JsonUtils.LoadFromFile<List<DevTagInfo>>(LOCAL_DEV_TAG_PATH);
        //     foreach (var info in tags)
        //     {
        //         if (info.packageName.Equals(manifest.displayName))
        //             return false;
        //     }
        //
        //     var path = GetSelectionPath();
        //     var branch = GetGitBranchName(path);
        //     tags.Add(new DevTagInfo(manifest.displayName, branch, path));
        //     JsonUtils.SaveToFile(LOCAL_DEV_TAG_PATH, tags);
        //     SceneView.RepaintAll();
        //     return true;
        // }
        //
        // /// <summary>
        // /// 删除开发标记
        // /// </summary>
        // /// <param name="manifest">package.json文件</param>
        // /// <returns>删除成功返回true，没有标记则返回false</returns>
        // public static bool DelDevTag(PackageManifest manifest)
        // {
        //     var tags = JsonUtils.LoadFromFile<List<DevTagInfo>>(LOCAL_DEV_TAG_PATH);
        //     for (var i = 0; i < tags.Count; i++)
        //     {
        //         var info = tags[i];
        //         if (info.packageName.Equals(manifest.displayName))
        //         {
        //             tags.Remove(info);
        //             JsonUtils.SaveToFile(LOCAL_DEV_TAG_PATH, tags);
        //             SceneView.RepaintAll();
        //             return true;
        //         }
        //     }
        //
        //     return false;
        // }
        //
        // /// <summary>
        // /// SceneView插入UI-Label,展示开发包名，开发版本
        // /// </summary>
        // private static void ShowDevTag()
        // {
        //     //ShowMsg
        //     var tags = JsonUtils.LoadFromFile<List<DevTagInfo>>(LOCAL_DEV_TAG_PATH);
        //     string msg="";
        //     if (tags.Count > 0)
        //     {
        //         msg = $"当前路径:{Environment.CurrentDirectory}\n";
        //         foreach (var info in tags) msg += ($"MT Package:{info.packageName} Branch:{info.branch} 开发版\n");
        //     }
        //
        //     if (msg.Length > 0)
        //     {
        //         //GameView添加Label
        //         var assembly = typeof(Editor).Assembly;
        //         var type = assembly.GetType("UnityEditor.GameView");
        //         var gameView = EditorWindow.GetWindow(type, false, "Game", false);
        //         var lb=new Label("\n"+msg)
        //         {
        //             style =
        //             {
        //                 fontSize = 24,
        //                 color = Color.white,
        //                 unityFontStyleAndWeight = FontStyle.Bold
        //             }
        //         };
        //         gameView.rootVisualElement.Add(lb);
        //         gameView.Repaint();
        //         //SceneView添加Label
        //         SceneView.duringSceneGui += (sceneView) =>
        //         {
        //             Handles.BeginGUI();
        //             var a = new GUIStyle
        //             {
        //                 fontSize = 24,
        //                 fontStyle = FontStyle.Bold,
        //                 normal = {textColor = Color.white}
        //             };
        //        
        //             GUILayout.Label(msg, a);
        //             Handles.EndGUI();
        //         };
        //     }
        // }
        //
        // [DidReloadScripts]
        // private static void UpdateDevTag()
        // {
        //     var tags = JsonUtils.LoadFromFile<List<DevTagInfo>>(LOCAL_DEV_TAG_PATH);
        //     foreach (DevTagInfo t in tags)
        //     {
        //         t.branch = GetGitBranchName(t.packagePath);
        //     }
        //
        //     JsonUtils.SaveToFile(LOCAL_DEV_TAG_PATH, tags);
        //     ShowDevTag();
        // }

        // [UnityEditor.Callbacks.DidReloadScripts]
        // private static void TryToInstallPackage()
        // {
        //     Debug.Log("TryToInstallPackage");
        //     // AssetDatabase.ImportPackage();
        //     if (!EditorPrefs.GetBool("com.magictavern.mtdevtoolkit", false))
        //     {
        //         EditorPrefs.SetBool("com.magictavern.mtdevtoolkit",true);
        //     }
        //     AssetDatabase.ImportPackage(EditorDefine.GetBasePath("Plugins/testdep.unitypackage"),true);
        //
        // }
        public static void DeleteLocalSubtreeBranch(PackageManifest manifest)
        {
            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            // subtree 发布分支
            var cmd = $"git rev-parse --show-toplevel";
            var rootPath = ShellUtils.ProcessCommandSync(cmd, projectPath).Trim(s_trimChars);
            // var tagVersion = manifest.version;
            var releaseBranch = GetReleaseBranchName(rootPath, manifest);
            // 清理本地分支 本地分支无效
            cmd = $"git branch -D {releaseBranch}";
            ShellUtils.ProcessCommandSync(cmd, rootPath);
        }

        public static bool HaveUnpushedCommits(string filePath, out string message)
        {
            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            
            // Get current branch name
            string cmd = "git rev-parse --abbrev-ref HEAD";
            var currentBranch = ShellUtils.ProcessCommandSync(cmd, projectPath).Trim(s_trimChars);
            
            // Check if branch exists on remote
            cmd = $"git ls-remote --heads origin {currentBranch}";
            var remoteBranchExists = !string.IsNullOrEmpty(ShellUtils.ProcessCommandSync(cmd, projectPath).Trim(s_trimChars));
            
            if (!remoteBranchExists)
            {
                // If branch doesn't exist on remote, all commits are considered unpushed
                cmd = $"git log -- {filePath} --pretty=format:\"%h %s\"";
                message = ShellUtils.ProcessCommandSync(cmd, projectPath).Trim(s_trimChars);
                return !string.IsNullOrEmpty(message);
            }
            
            // Check for any unpushed commits
            cmd = $"git log origin/{currentBranch}..HEAD --pretty=format:\"%h %s\"";
            message = ShellUtils.ProcessCommandSync(cmd, projectPath).Trim(s_trimChars);
            
            return !string.IsNullOrEmpty(message);
        }

        public static bool PushToRemote(string filePath)
        {
            var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
            char[] trimChars = {'\n', '\r'};
            
            // Get current branch
            string cmd = "git rev-parse --abbrev-ref HEAD";
            var currBranchName = ShellUtils.ProcessCommandSync(cmd, projectPath).TrimEnd(trimChars);
            
            // Push to remote
            cmd = $"git push origin refs/heads/{currBranchName} --verbose";
            var result = ShellUtils.ProcessCommandSync(cmd, projectPath);
            
            return !result.Contains("fatal");
        }
    }

    //class DevTagInfo
    // {
    //     public readonly string packageName;
    //     public string branch;
    //     public readonly string packagePath;
    //
    //     public DevTagInfo()
    //     {
    //     }
    //
    //     public DevTagInfo(string packageName, string branch, string packagePath)
    //     {
    //         this.packageName = packageName;
    //         this.branch = branch;
    //         this.packagePath = packagePath;
    //     }
    // }
    class DevReplaceInfo
    {
        public readonly string packageName;
        public string originalInfo;
        public readonly string projectPath;

        public DevReplaceInfo()
        {
        }

        public DevReplaceInfo(string packageName, string originalInfo, string projectPath)
        {
            this.packageName = packageName;
            this.originalInfo = originalInfo;
            this.projectPath = projectPath;
        }
    }
}