// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳、商祥鹏
// @Description: MTP开发编辑器入口，MTP包的创建、发布相关菜单命令在这里
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using MagicTavern.FrontEngine.MTPManager.LitJson;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    public static class MTPDevToolkit
    {
        // 为了组件包菜单名字区分开
        private const string BACK_MENU_PATH = "Assets/MTTools/Package Tools";

        [MenuItem(BACK_MENU_PATH + "/1. 创建Package")]
        public static void CreateEx()
        {
            if (Selection.assetGUIDs.Length == 0)
                return;

            var filePath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
            Debug.Log(filePath);

            if (!AssetDatabase.IsValidFolder(filePath))
            {
                Debug.LogError("[Create Package] is not valid folder!");
                return;
            }

            var packageName = Path.GetFileName(filePath);
            Debug.Log(packageName);

            var manifest = new PackageManifest
            {
                name = "com.magictavern." + packageName.ToLower(),
                displayName = packageName,
                version = "1.0.0",
                description = "a package for...",
                unity = "2019.4",
                author = new PackageManifest.Author
                {
                    name = "Magic Tavern",
                    email = "developer@magictavern.com",
                    url = "www.magictavern.com"
                }
            };

            PackageCreateWindow.Load(manifest,
                manifestNew => { PackageDevUtils.CreatePackageFolder(manifestNew, false); });
            //OpenDevTag();
        }

        [MenuItem(BACK_MENU_PATH + "/1. 创建Package", true)]
        public static bool DontHavePackageJson()
        {
            var selectPath = PackageDevUtils.GetSelectionPath();
            if (selectPath == null)
                return false;
            if (!selectPath.StartsWith("Assets"))
                return false;

            return PackageDevUtils.GetPackageManifest(selectPath) == null;
        }

        [MenuItem(BACK_MENU_PATH + "/2. 编辑Package配置")]
        public static void PublishCheck()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;

            var packagePath = PackageDevUtils.GetSelectionPath();
            PackagePublishWindow.Load(manifest, m => SaveManifest(m, packagePath), "OK");
        }

        private static void SaveManifest(PackageManifest manifestNew, string packagePath)
        {
            if (manifestNew.dependencies == null)
            {
                manifestNew.dependencies = new Dictionary<string, string>();
            }

            // save package.json
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var path = Path.Combine(projectRoot, packagePath, "package.json");
            File.WriteAllText(path, JsonMapper.ToJson(manifestNew, true));

            // Save changelog from PackageManifestView
            var changelog = PackagePublishWindow.GetCurrentChangelog();
            if (!string.IsNullOrEmpty(changelog))
            {
                var changelogPath = Path.Combine(projectRoot, packagePath, "CHANGELOG.md");
                File.WriteAllText(changelogPath, changelog);
            }

            AssetDatabase.Refresh();
        }

        [MenuItem(BACK_MENU_PATH + "/3. 提交package.json")]
        public static void PushYourCode()
        {
            if (EditorUtility.DisplayDialog("提交代码", "本次提交将提交package.json和CHANGELOG.md(如果有变动),如果提交内容很多建议手动提交", "自动提交", "手动提交"))
            {
                string projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
                string filePath = PackageDevUtils.GetSelectionPath();
                string packageJsonPath = $"{filePath}/package.json";
                string changelogPath = $"{filePath}/CHANGELOG.md";

                char[] trimChars = {'\n', '\r'};
                string cmd = "git rev-parse --abbrev-ref HEAD";
                var currBranchName = ShellUtils.ProcessCommandSync(cmd, projectPath).TrimEnd(trimChars);

                cmd = $"git pull --rebase=true --autostash origin {currBranchName} --verbose";
                ShellUtils.ProcessCommandSync(cmd, projectPath);

                // Add package.json
                cmd = $"git add {packageJsonPath}";
                var result = ShellUtils.ProcessCommandSync(cmd, projectPath);
                if (result.Contains("fatal"))
                {
                    return;
                }

                // Check if CHANGELOG.md has changes and add it if it does
                cmd = $"git status --porcelain {changelogPath}";
                var status = ShellUtils.ProcessCommandSync(cmd, projectPath);
                if (!string.IsNullOrEmpty(status))
                {
                    cmd = $"git add {changelogPath}";
                    ShellUtils.ProcessCommandSync(cmd, projectPath);
                }

                cmd = $"git commit -m '[MOD] auto-commit package.json and CHANGELOG.md at{DateTime.Now}!'";
                //兼容window处理
                if (Application.platform == RuntimePlatform.WindowsEditor)
                    cmd = $"git commit -m \"[MOD] auto-commit package.json and CHANGELOG.md at{DateTime.Now}!\"";
                ShellUtils.ProcessCommandSync(cmd, projectPath);

                cmd = $"git pull --rebase=true --autostash origin {currBranchName} --verbos";
                ShellUtils.ProcessCommandSync(cmd, projectPath);

                cmd = $"git push origin refs/heads/{currBranchName} --verbose";
                ShellUtils.ProcessCommandSync(cmd, projectPath);
            }
        }

        [MenuItem(BACK_MENU_PATH + "/3.5. 发布Debug Package")]
        public static void PublishForDebug()
        {
            PackageManifest manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null) return;

            var packagePath = PackageDevUtils.GetSelectionPath();
            manifest.samples = PackageSampleUtils.SyncAndGetSamplesInfo(manifest.displayName);
            SaveManifest(manifest, packagePath);

            string packageJsonPath = $"{PackageDevUtils.GetSelectionPath()}/package.json";
            if (PackageDevUtils.HaveUnpushedCommits(packageJsonPath, out string unpushedCommits))
            {
                if (EditorUtility.DisplayDialog("未推送的改动", 
                    $"存在未推送的提交，是否要自动推送这些改动？", 
                    "是，自动推送", "否，取消发布"))
                {
                    if (!PackageDevUtils.PushToRemote(packageJsonPath))
                    {
                        EditorUtility.DisplayDialog("Error", "推送失败，请手动推送后再试", "ok");
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            if (PackageDevUtils.HaveChangeToCommit(out string _))
            {
                if (!EditorUtility.DisplayDialog("未提交改动", $"有改动未提交(Sample改动也需要提交)，请检查是否需要先提交改动！", "继续发布", "取消发布"))
                {
                    return;
                }
            }

            //if (PackageDevUtils.IsManifestHasError(manifest, out var errors, out var __, true))
            if (PackageManifestCheckUtils.CheckManifest(manifest, null, out var errors, out var __, true))
            {
                var sb = new StringBuilder();
                foreach (var error in errors)
                {
                    sb.AppendLine(error);
                }

                EditorUtility.DisplayDialog("Error", "package.json 文件有错误:\n" + sb, "ok");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("publishing package", "clear currentVersionTags", 0.2f);
                PackageDevUtils.ClearCurrentVersionTags(true);
                EditorUtility.DisplayProgressBar("publishing package", "pushing package", 0.5f);
                PackageDevUtils.PublishInternal(true);
                EditorUtility.ClearProgressBar();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(BACK_MENU_PATH + "/4. 发布Package")]
        public static async void Publish()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null) return;

            var packagePath = PackageDevUtils.GetSelectionPath();
            manifest.samples = PackageSampleUtils.SyncAndGetSamplesInfo(manifest.displayName);
            SaveManifest(manifest, packagePath);

            string packageJsonPath = $"{PackageDevUtils.GetSelectionPath()}/package.json";
            if (PackageDevUtils.HaveFileChangeToCommit(packageJsonPath, out string filesName))
            {
                EditorUtility.DisplayDialog("Error", "当前package有改动的package.json 文件未提交:\n" + filesName, "ok");
                return;
            }

            // Check for unpushed commits
            if (PackageDevUtils.HaveUnpushedCommits(packageJsonPath, out string unpushedCommits))
            {
                if (EditorUtility.DisplayDialog("未推送的改动", 
                    $"存在未推送的提交，是否要自动推送这些改动？", 
                    "是，自动推送", "否，取消发布"))
                {
                    if (!PackageDevUtils.PushToRemote(packageJsonPath))
                    {
                        EditorUtility.DisplayDialog("Error", "推送失败，请手动推送后再试", "ok");
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            if (PackageDevUtils.HaveChangeToCommit(out string _))
            {
                if (!EditorUtility.DisplayDialog("未提交改动", $"有改动未提交(Sample改动也需要提交)，请检查是否需要先提交改动！", "继续发布", "取消发布"))
                {
                    return;
                }
            }

            var projectInfo = await PackageDevUtils.PullProjectInfoWithRemote(manifest);

            if (PackageManifestCheckUtils.CheckManifest(manifest, projectInfo, out var errors, out var __))
            {
                var sb = new StringBuilder();
                foreach (var error in errors)
                {
                    sb.AppendLine(error);
                }

                EditorUtility.DisplayDialog("Error", "package.json 文件有错误:\n" + sb, "ok");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("publishing package", "processing...", 0.5f);
                PackageDevUtils.PublishInternal();
                EditorUtility.DisplayProgressBar("publishing package", "finished", 1f);
                EditorUtility.ClearProgressBar();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.ClearProgressBar();
            }

            await MTPManager.MTPManager.GetPackageInstallList(true); //自动刷新
        }

        [MenuItem(BACK_MENU_PATH + "/5. 更新Package列表")]
        public static async void RefreshPackageList()
        {
            var url = "https://jenkins-engine.magictavern.net/generic-webhook-trigger/invoke?token=magictavern.packagelist.refresh.BUILD";
            using var client = new HttpClient();
            var result = await client.GetAsync(url);
            Debug.Log(await result.Content.ReadAsStringAsync());
        }

        // [MenuItem(BACK_MENU_PATH + "/[Tools]Create Dev Branch")]
        // public static void CreateDevBranch()
        // {
        //     // PackageDevUtils.PublishInternal(false);
        // }

        [MenuItem(BACK_MENU_PATH + "/[本地测试] 发布到本地工程")]
        public static void AddDevPackageToTestProject()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null) return;
            var path = EditorUtility.OpenFolderPanel("添加本地测试包的项目工程位置", Environment.CurrentDirectory, "");
            PackageDevUtils.AddDevPackageToTestProject(manifest, path);
        }

        [MenuItem(BACK_MENU_PATH + "/[本地测试] 从本地工程移除")]
        public static void DelDevPackageToTestProject()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null) return;
            var path = EditorUtility.OpenFolderPanel("移除本地测试包的项目工程位置", Environment.CurrentDirectory, "");
            PackageDevUtils.DelDevPackageFromTestProject(manifest, path);
        }

        [MenuItem(BACK_MENU_PATH + "/[开发包预编译变量] 添加")]
        public static void AddScriptingDefineSymbols()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;
            // 规定测试编译变量必须带有MTP前缀和DEV后缀
            ScriptDefineUtils.TryToAddScriptDefineSymbol("MTP_" + manifest.displayName.ToUpper() + "_DEV");
        }

        [MenuItem(BACK_MENU_PATH + "/[开发包预编译变量] 清除")]
        public static void RemoveScriptingDefineSymbols()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;
            ScriptDefineUtils.TryToRemoveScriptDefineSymbol("MTP_" + manifest.displayName.ToUpper() + "_DEV");
        }

        [MenuItem(BACK_MENU_PATH + "/[版本号缓存] 同步")]
        public static void SyncVersionCache()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;
            EditorPrefs.SetString(manifest.name, manifest.version);
        }

        [MenuItem(BACK_MENU_PATH + "/[版本号缓存] 清理")]
        public static void RemoveVersionCache()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;

            EditorPrefs.DeleteKey(manifest.name);
        }

        [MenuItem(BACK_MENU_PATH + "/[Assembly文件] 创建")]
        public static void CreateAssemblyDefineFileByFolder()
        {
            var manifest = PackageDevUtils.GetSelectionParentManifest(out string _, out var folder);
            if (manifest == null)
                return;

            var fakeAssembly = new FakeAssemblyDefinition()
            {
                name = $"MagicTavern.FrontEngine.{manifest.displayName}.{folder}"
            };
            var fullPath =
                Path.GetFullPath(
                    $"Assets/{manifest.displayName}/{folder}/MagicTavern.FrontEngine.{manifest.displayName}.{folder}.asmdef");
            File.WriteAllText(fullPath, JsonMapper.ToJson(fakeAssembly));
            AssetDatabase.Refresh();
            // Debug.Log($"MagicTavern.FrontEngine.{manifest.displayName}.{folder}");
            // Debug.Log($"Assets/{manifest.displayName}/{folder}/MagicTavern.FrontEngine.{manifest.displayName}.{folder}.asmdef");
        }

        //[MenuItem(BACK_MENU_PATH + "/[Tools] 开启开发标记", true)]
        //[MenuItem(BACK_MENU_PATH + "/[Tools] 关闭开发标记", true)]
        [MenuItem(BACK_MENU_PATH + "/4. 发布Package", true)]
        [MenuItem(BACK_MENU_PATH + "/3.5. 发布Debug Package", true)]
        [MenuItem(BACK_MENU_PATH + "/3. 提交代码", true)]
        [MenuItem(BACK_MENU_PATH + "/2. 编辑Package配置", true)]
        [MenuItem(BACK_MENU_PATH + "/[本地测试] 发布到本地工程", true)]
        [MenuItem(BACK_MENU_PATH + "/[本地测试] 从本地工程移除", true)]
        [MenuItem(BACK_MENU_PATH + "/[开发包预编译变量] 添加", true)]
        [MenuItem(BACK_MENU_PATH + "/[开发包预编译变量] 清除", true)]
        [MenuItem(BACK_MENU_PATH + "/[版本号缓存] 同步", true)]
        [MenuItem(BACK_MENU_PATH + "/[版本号缓存] 清理", true)]
        [MenuItem(BACK_MENU_PATH + "/[FIX] 清理当前的版本号Tag", true)]
        [MenuItem(BACK_MENU_PATH + "/[FIX] 修复无法发布错误", true)]
        [MenuItem(BACK_MENU_PATH + "/[Samples] 创建", true)]
        [MenuItem(BACK_MENU_PATH + "/[Samples] 同步", true)]
        public static bool HavePackageJson()
        {
            return PackageDevUtils.GetSelectionPackageManifest(out var _) != null;
        }

        //取消正式版清理tag功能
        [MenuItem(BACK_MENU_PATH + "/[FIX] 清理当前的版本号Tag")]
        public static void RemoveCurrentVersionTag()
        {
            PackageDevUtils.ClearCurrentVersionTags(false);
        }

        [MenuItem(BACK_MENU_PATH + "/[FIX] 修复无法发布错误")]
        public static void FixPublish()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;

            PackageDevUtils.DeleteLocalSubtreeBranch(manifest);
        }

        [MenuItem(BACK_MENU_PATH + "/[Samples] 创建")]
        public static void CreateSamples()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;

            PackageSampleUtils.CreateSamplesFolder(manifest.displayName);
        }

        [MenuItem(BACK_MENU_PATH + "/[Samples] 同步")]
        public static void SyncSamples()
        {
            var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
            if (manifest == null)
                return;
            // PackageSampleUtils.SyncSamplesFolder(manifest.displayName);
            manifest.samples = PackageSampleUtils.SyncAndGetSamplesInfo(manifest.displayName);
            var syncSamplesPath = PackageDevUtils.GetSelectionPath();
            SaveManifest(manifest, syncSamplesPath);
        }

        // 开发标记功能
        // [MenuItem(BACK_MENU_PATH + "/[Tools] 开启开发标记")]
        // public static void OpenDevTag()
        // {
        //     var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
        //     if (manifest == null) return;
        //     if (PackageDevUtils.AddDevTag(manifest))
        //     {
        //         Debug.Log($"[MTPDevToolKit] {manifest.displayName}开发标记开启");
        //     }
        //     else
        //     {
        //         Debug.LogWarning($"[MTPDevToolKit] {manifest.displayName}开发标记已经开启,无需开启");
        //     }
        // }
        //
        // [MenuItem(BACK_MENU_PATH + "/[Tools] 关闭开发标记")]
        // public static void CloseDevTag()
        // {
        //     var manifest = PackageDevUtils.GetSelectionPackageManifest(out string _);
        //     if (manifest == null) return;
        //     if (PackageDevUtils.DelDevTag(manifest))
        //     {
        //         Debug.Log($"[MTPDevToolKit] {manifest.displayName}开发标记关闭");
        //     }
        //     else
        //     {
        //         Debug.LogWarning($"[MTPDevToolKit] {manifest.displayName}开发标记已经关闭,无需关闭");
        //     }
        // }

        // [MenuItem(BACK_MENU_PATH + "/Test")]
        // public static void TryToGetCurrentBranchName()
        // {
        //     // git branch --show-current
        //     var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
        //     // var branchName = manifest.displayName.ToLower();
        //     // var tagVersion = manifest.version;
        //
        //     var cmd = $"git branch --show-current";
        //     // var cmd = $"git rev-parse --abbrev-ref HEAD";
        //     var branchName = ShellHelper.ProcessCommandSync(cmd, projectPath).TrimEnd('\n');
        //     Debug.Log($"get branch name:{branchName} from cmd!");
        // }

        // [MenuItem(BACK_MENU_PATH + "/Create")]
        // public static void Create()
        // {
        //     if (Selection.assetGUIDs.Length == 0)
        //         return;
        //
        //     var filePath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
        //     Debug.Log(filePath);
        //
        //     if (!AssetDatabase.IsValidFolder(filePath))
        //     {
        //         Debug.LogError("[Create Package] is not valid folder!");
        //         return;
        //     }
        //
        //     var packageName = Path.GetFileName(filePath);
        //     Debug.Log(packageName);
        //
        //     var manifest = new PackageManifest
        //     {
        //         name = "com.magictavern." + packageName.ToLower(),
        //         displayName = packageName,
        //         version = "1.0.0",
        //         description = "a package for...",
        //         unity = "2019.4",
        //         author = new PackageManifest.Author
        //         {
        //             name = "Magic Tavern",
        //             email = "developer@magictavern.com",
        //             url = "www.magictavern.com"
        //         }
        //     };
        //
        //     PackageDevUtils.CreatePackageFolder(manifest, false);
        // }

        // [MenuItem(BACK_MENU_PATH + "/Publish to upm")]
        // public static void Publish()
        // {
        //     var manifest = PackageDevUtils.GetSelectionPackageManifest(out var filePath);
        //     if (manifest == null)
        //         return;
        //
        //     var projectPath = Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length);
        //     // Debug.Log(projectPath);
        //     var cmd = $"git subtree split --prefix={filePath} --branch upm";
        //     ShellHelper.ProcessCommandSync(cmd, projectPath);
        //     // cmd = "git push -u origin upm";
        //     // ShellHelper.ProcessCommandSync(cmd, projectPath);
        //
        //     cmd = $"git tag {manifest.version} upm";
        //     ShellHelper.ProcessCommandSync(cmd, projectPath);
        //     cmd = "git push origin upm --tags";
        //     ShellHelper.ProcessCommandSync(cmd, projectPath);
        // }

        // [MenuItem(BACK_MENU_PATH + "/Test")]
        // public static void TestCommand()
        // {
        //     unsafe
        //     {
        //         // var cmd = $"git tag --delete {branchName}_{tagVersion}";
        //         // ShellHelper.ProcessCommandSync(cmd, projectPath);
        //
        //         var numbers = stackalloc int[2048 * 1024];
        //         for (var i = 0; i < 1024 * 1024; i++)
        //         {
        //             numbers[i] = i;
        //         }
        //
        //         for (var i = 0; i < 1024; i++)
        //         {
        //             Debug.Log($"int array:{i}:{numbers[i]}");
        //         }
        //     }
        // }
        //
    }
}