// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 商祥鹏
// @Maintainer: 商祥鹏
// @Description: MTP包的manifest检查工具类-检查manifest内容是否合规
// ==================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    /// <summary>
    /// MTP包的manifest检查工具类-检查manifest内容是否合规
    /// </summary>
    public static class PackageManifestCheckUtils
    {
        private static readonly Regex s_packageNameRegex =
            new Regex(@"[a-z]+[0-9a-z_]*(\.[a-z]+[0-9a-z_]*)(\.[a-z]+[0-9a-z_]*)+",
                RegexOptions.Compiled);

        private static readonly Regex s_packageUnityVersionRegex =
            new Regex(@"20(\d){2}\.(\d){1,2}", RegexOptions.Compiled);

        private static readonly Regex s_packageVersionRegex =
            new Regex(@"^([0-9]\d|[0-9])(\.([1-9]\d|\d)){2}$", RegexOptions.Compiled);

        private static readonly Regex s_packageURLRegex =
            new Regex(
                @"((https?|ftp|file)://)?[-A-Za-z0-9+&@#/%?=~_|!:,.;]+[-A-Za-z0-9+&@#/%=~_|]",
                RegexOptions.Compiled);

        private static readonly Regex s_packageEmailRegex =
            new Regex(
                @"[\w!#$%&'*+/=?^_`{|}~-]+(?:\.[\w!#$%&'*+/=?^_`{|}~-]+)*@(?:[\w](?:[\w-]*[\w])?\.)+[\w](?:[\w-]*[\w])?",
                RegexOptions.Compiled);

        private const string UNITY_PACKAGE_DEPENDENCY_LIST_PATH = "Packages/manifest.json"; //依赖信息路径

        //检查包清单
        //返回信息
        //  err信息列表
        //  warning信息列表
        public static bool CheckManifest(PackageManifest manifest, ProjectInfo projectInfo, out List<string> err, out List<string> warning,
            bool forDebug = false)
        {
            err = new List<string>();
            warning = new List<string>();

            //检查 包名
            var nameError = CheckManifestName(manifest, err, warning);

            //检查 包的displayName
            CheckManifestDisplayName(manifest, err, warning);

            //检查 包的版本信息
            CheckManifestVersion(manifest, projectInfo, err, warning, nameError, forDebug);

            //检查 兼容的最低unity版本
            CheckManifestUnityVersion(manifest, err, warning);

            //检查 包的描述信息
            CheckManifestDescription(manifest, err, warning);

            //检查 包的官方依赖和git依赖
            CheckManifestAllDependencies(manifest, err, warning);

            //检查 包的作者信息
            CheckManifestAuthor(manifest, err, warning);

            //检查 包的documentURl信息
            CheckManifestDocumentURL(manifest, err, warning);

            return err.Count > 0;
        }

        public static bool CheckManifestName(PackageManifest manifest, List<string> err, List<string> warning)
        {
            var nameError = false;

            if (string.IsNullOrEmpty(manifest.name))
            {
                err.Add("[package name] 包名不能为空!");
                nameError = true;
            }

            if (manifest.name.Length > 50)
            {
                warning.Add("[package name] 包名太长会影响UI显示!");
                // nameError = true;
            }

            if (manifest.name.Length > 214)
            {
                err.Add("[package name] 包名过长超出214字符限制!");
                nameError = true;
            }

            if (!manifest.name.StartsWith("com.magictavern"))
            {
                err.Add("[package name] 包名不包含com.magictavern\n包名结构需要符合com.magictavern.xxx!");
                nameError = true;
            }

            var match = s_packageNameRegex.Match(manifest.name);
            if (!match.Success || match.Value != manifest.name)
            {
                err.Add("[package name] 包名无效，请注意检查:\n1.符合xx.xx.xx的形式.\n2.不能包含除数字、小写字母之外的字符!\n");
                nameError = true;
            }

            return nameError;
        }

        public static bool CheckManifestDisplayName(PackageManifest manifest, List<string> err, List<string> warning)
        {
            var displayNameError = false;

            if (string.IsNullOrEmpty(manifest.displayName))
            {
                err.Add("[package displayName] 显示名称不能为空!");
                displayNameError = true;
            }

            return displayNameError;
        }

        private static bool CheckManifestVersion(PackageManifest manifest, ProjectInfo projectInfo, List<string> err, List<string> warning,
            bool nameError = false, bool forDebug = false)
        {
            var versionError = false;

            if (string.IsNullOrEmpty(manifest.version))
            {
                err.Add("[package version] 版本号为空!");
                versionError = true;
            }

            var match = s_packageVersionRegex.Match(manifest.version);
            if (!match.Success || match.Value != manifest.version)
            {
                err.Add("[package version] 版本号不是合法形式，要符合A.B.C的形式!");
                versionError = true;
            }

            if (!nameError && !forDebug)
            {
                if (projectInfo == null)
                {
                    Debug.LogError("未拉取到项目信息，请检查网络连接");
                    err.Add("未拉取到项目信息，请检查网络连接");
                    versionError = true;
                }
                else if (projectInfo.Tags == null)
                {
                    Debug.LogWarning("未找到项目版本信息，无法检查更新版本号!");
                }
                else
                {
                    Version newestVersion = null;

                    foreach (var tag in projectInfo.Tags)
                    {
                        var splitTag = tag.Split('-');
                        if (splitTag.Length <= 1)
                            continue;

                        var version = splitTag[splitTag.Length - 1];
                        if (version != "debug")
                        {
                            newestVersion = new Version(version);
                            break;
                        }
                    }

                    var currentVersion = new Version(manifest.version);

                    if (newestVersion != null && newestVersion.CompareTo(currentVersion) > -1)
                    {
                        Debug.LogError("[package version] 发布新版请更新版本号! \n 当前版本号:" + manifest.version + " 不高于最新版本号:" + newestVersion);
                        err.Add("[package version] 发布新版请更新版本号! \n 当前版本号:" + manifest.version + " 不高于最新版本号:" + newestVersion);
                        versionError = true;
                    }
                }
            }

            return versionError;
        }

        public static bool CheckManifestUnityVersion(PackageManifest manifest, List<string> err, List<string> warning)
        {
            var unityVersionError = false;

            if (string.IsNullOrEmpty(manifest.unity))
            {
                err.Add("[unity version] Unity兼容版本不能为空!");
                unityVersionError = true;
            }
            else
            {
                var match = s_packageUnityVersionRegex.Match(manifest.unity);
                if (!match.Success || match.Value != manifest.unity)
                {
                    Debug.LogError($"[package Unity] Unity版本信息{manifest.unity}格式错误，请注意检查");
                    err.Add("[package Unity] Unity版本信息格式错误，请注意检查");
                    unityVersionError = true;
                }
            }

            return unityVersionError;
        }

        public static bool CheckManifestDescription(PackageManifest manifest, List<string> err, List<string> warning)
        {
            //var descriptionError = false;

            if (string.IsNullOrEmpty(manifest.description))
            {
                warning.Add("[package description] 描述尽量不要为空!");
            }

            return false;
        }

        //检查包的所有依赖关系，目前检测unity的官方依赖和git依赖
        public static bool CheckManifestAllDependencies(PackageManifest manifest, List<string> err,
            List<string> warning)
        {
            var dependencyError = false;

            var unityPackageList = JsonUtils.LoadFromFile<UnityPackageList>("Packages/manifest.json", false);

            if (manifest.dependencies != null)
            {
                foreach (KeyValuePair<string, string> depKvp in manifest.dependencies)
                {
                    //检测依赖是否属于官方依赖
                    var match = s_packageVersionRegex.Match(depKvp.Value);
                    if (!match.Success || match.Value != depKvp.Value)
                    {
                        Debug.LogError($"{depKvp.Key}: ref:{depKvp.Value}------非官方依赖包");
                        err.Add(
                            $"[unity dependencies] Package 依赖包非unity官方依赖！{depKvp.Key} ref:{depKvp.Value}");
                        dependencyError = true;
                    }

                    if (unityPackageList.dependencies.TryGetValue(depKvp.Key, out var version))
                    {
                        if (version != depKvp.Value)
                        {
                            Debug.LogError($"{depKvp.Key}: ref:{depKvp.Value}------in manifest:{version}");
                            err.Add(
                                $"[unity dependencies] Package 依赖包{depKvp.Key}版本不一致!配置：{depKvp.Value}，实际：{version}");
                            dependencyError = true;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[unity dependencies] Package 依赖包{depKvp.Key}并无实际依赖！");
                        warning.Add($"[unity dependencies] Package 依赖包{depKvp.Key}并无实际依赖！");
                    }
                }

                //本地manifest是否包含ssh的URL标志
                var reWrite = false;

                foreach (KeyValuePair<string, string> depKvp in manifest.dependenciesGit)
                {
                    //检测依赖是否属于官方依赖
                    var match = s_packageURLRegex.Match(depKvp.Value);
                    if (!match.Success || match.Value != depKvp.Value)
                    {
                        Debug.LogError($"{depKvp.Key}: ref:{depKvp.Value}------非git依赖包");
                        err.Add(
                            $"[unity dependencies Git] Package 依赖包非git依赖！{depKvp.Key} ref:{depKvp.Value}");
                        dependencyError = true;
                    }

                    if (unityPackageList.dependencies.TryGetValue(depKvp.Key, out var url))
                    {
                        var formatVersion = PackageDepInfo.FormatUrl(url);

                        //本地manifest的sshURL转变为httpsURL
                        if (formatVersion.Contains("ssh"))
                        {
                            formatVersion = PackageDepInfo.SSHToHttpsURLWithoutToken(url);
                            unityPackageList.dependencies[depKvp.Key] = PackageDepInfo.SSHToHttpsURLWithToken(url);
                            reWrite = true;
                        }

                        //用户输入URL也转变为https格式
                        var userInputURL = PackageDepInfo.SSHToHttpsURLWithoutToken(depKvp.Value);
                        if (formatVersion != userInputURL)
                        {
                            dependencyError = true;

                            var formStrings = formatVersion.Split('#');
                            var userStrings = userInputURL.Split('#');
                            if (formStrings[0].Equals(userStrings[0]) && !formStrings[1].Equals(userStrings[1]))
                            {
                                Debug.LogError($"版本不一致!配置：{userStrings[1]}，实际：{formStrings[1]}");
                                err.Add(
                                    $"[unity dependenciesGit] Package Git依赖包{depKvp.Key}版本不一致!配置：{userStrings[1]}，实际：{formStrings[1]}");
                            }
                            else
                            {
                                Debug.LogError($"选择的包：{depKvp.Key}:" +
                                               $"\n手动填写的git ref:{depKvp.Value}" +
                                               $"\nin manifest:{url}" +
                                               $"\nmanifest has been Changed To:{formatVersion}");
                                err.Add(
                                    $"[unity dependenciesGit] Package Git依赖包{depKvp.Key}信息不匹配!\n配置：{depKvp.Value}，\n实际：{formatVersion}");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[unity dependenciesGit] Package Git依赖包{depKvp.Key}并无实际依赖！");
                        warning.Add($"[unity dependenciesGit] Package Git依赖包{depKvp.Key}并无实际依赖！");
                        // dependencyError = true;
                    }
                }

                //本地sshURL转换格式为https
                if (reWrite) JsonUtils.SaveToFile(UNITY_PACKAGE_DEPENDENCY_LIST_PATH, unityPackageList);
            }

            return dependencyError;
        }

        //检测 包作者信息
        public static bool CheckManifestAuthor(PackageManifest manifest, List<string> err, List<string> warning)
        {
            var authorError = false;

            if (manifest.author != null)
            {
                CheckManifestAuthorName(manifest, err, warning);
                CheckManifestAuthorEmail(manifest, err, warning);
                CheckManifestAuthorURL(manifest, err, warning);
            }
            else
            {
                Debug.LogError("[package author] author信息不能为空!");
                err.Add("[package author] author信息不能为空!");
                authorError = true;
            }

            return authorError;
        }

        public static bool CheckManifestAuthorName(PackageManifest manifest, List<string> err, List<string> warning)
        {
            var authorNameError = false;

            if (string.IsNullOrEmpty(manifest.author.name))
            {
                Debug.LogError("[package AuthorName] Author姓名不能为空!");
                err.Add("[package AuthorName] Author姓名不能为空!");
                authorNameError = true;
            }

            return authorNameError;
        }

        public static bool CheckManifestAuthorEmail(PackageManifest manifest, List<string> err, List<string> warning)
        {
            var authorEmailError = false;

            if (string.IsNullOrEmpty(manifest.author.email))
            {
                Debug.LogError("[package AuthorEmail] Author邮箱不能为空!");
                err.Add("[package AuthorEmail] Author邮箱不能为空!");
                authorEmailError = true;
            }
            else
            {
                var match = s_packageEmailRegex.Match(manifest.author.email);
                if (!match.Success || match.Value != manifest.author.email)
                {
                    err.Add($"[package AuthorEmail] author邮箱{manifest.author.email}格式无效，与正则表达式规则不同，请注意检查\n" +
                            @"正则规则为：[\w!#$%&'*+/=?^_`{|}~-]+(?:\.[\w!#$%&'*+/=?^_`{|}~-]+)*@(?:[\w](?:[\w-]*[\w])?\.)+[\w](?:[\w-]*[\w])?");
                    authorEmailError = true;
                }
            }

            return authorEmailError;
        }

        public static bool CheckManifestAuthorURL(PackageManifest manifest, List<string> err, List<string> warning)
        {
            var authorURLError = false;

            if (string.IsNullOrEmpty(manifest.author.url))
            {
                Debug.LogError("[package AuthorURL] Author网址不能为空!");
                err.Add("[package AuthorURL] Author网址不能为空!");
                authorURLError = true;
            }
            else
            {
                var match = s_packageURLRegex.Match(manifest.author.url);
                if (!match.Success || match.Value != manifest.author.url)
                {
                    err.Add($"[package authorURL] authorURL{manifest.author.url}无效，与正则表达式规则不同，请注意检查\n" +
                            $"正则规则为：((https?|ftp|file)://)?[-A-Za-z0-9+&@#/%?=~_|!:,.;]+[-A-Za-z0-9+&@#/%=~_|]");
                    authorURLError = true;
                }
            }

            return authorURLError;
        }

        //检测网址
        public static bool CheckManifestDocumentURL(PackageManifest manifest, List<string> err, List<string> warning)
        {
            var documentURLError = false;

            if (string.IsNullOrEmpty(manifest.documentationUrl))
            {
                Debug.LogError("[package documentURL] 文档URL不能为空!");
                err.Add("[package documentURL] 文档URL不能为空!");
                documentURLError = true;
            }
            else
            {
                var match = s_packageURLRegex.Match(manifest.documentationUrl);
                if (!match.Success || match.Value != manifest.documentationUrl)
                {
                    err.Add($"[package documentURL] documentURL{manifest.documentationUrl}无效，与正则表达式规则不同，请注意检查\n" +
                            "正则规则为：((https?|ftp|file)://)?[-A-Za-z0-9+&@#/%?=~_|!:,.;]+[-A-Za-z0-9+&@#/%=~_|]");
                    documentURLError = true;
                }
            }

            return documentURLError;
        }
    }
}