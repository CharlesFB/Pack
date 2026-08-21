// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: Git 相关工具类
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MagicTavern.FrontEngine.MTPManager;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    public static class GitUtils
    {
        private static readonly char[] s_trimChars = {'\n', '\r'};

        public static string GetLastReleaseTag(string projectPath)
        {
            // 获取当前选中的包路径
            var selectionPath = PackageDevUtils.GetSelectionPath();
            if (string.IsNullOrEmpty(selectionPath))
            {
                Debug.LogError("[GitUtils] No package selected");
                return null;
            }

            // 读取 package.json 获取包名
            var packageJsonPath = Path.Combine(projectPath, selectionPath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                Debug.LogError("[GitUtils] package.json not found");
                return null;
            }

            var packageJson = File.ReadAllText(packageJsonPath);
            var packageName = "";
            try
            {
                var json = JsonUtility.FromJson<PackageManifest>(packageJson);
                packageName = json.name.Replace("com.magictavern.", "");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GitUtils] Failed to parse package.json: {e.Message}");
                return null;
            }
            packageName = "dev-" + packageName;

            // 根据包名查找对应的标签
            var cmd = $"git tag --sort=-v:refname | grep -E '^{packageName}-[0-9]+\\.[0-9]+\\.[0-9]+$' | head -n 1";
            var result = ShellUtils.ProcessCommandSync(cmd, projectPath);
            return result.Trim(s_trimChars);
        }

        public static List<string> GetCommitsSinceTag(string projectPath, string tag)
        {
            var cmd = $"git log {tag}..HEAD --pretty=format:\"%s\"";
            var result = ShellUtils.ProcessCommandSync(cmd, projectPath);
            var commits = new List<string>();
            
            using (var reader = new StringReader(result))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim(s_trimChars);
                    if (!string.IsNullOrEmpty(line))
                    {
                        commits.Add(line);
                    }
                }
            }

            return commits;
        }

        public static string GenerateChangelog(string projectPath)
        {
            var lastTag = GetLastReleaseTag(projectPath);
            if (string.IsNullOrEmpty(lastTag))
            {
                Debug.LogWarning("[GitUtils] No previous release tag found");
                return null;
            }

            var commits = GetCommitsSinceTag(projectPath, lastTag);
            if (commits.Count == 0)
            {
                Debug.LogWarning("[GitUtils] No commits found since last release");
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Changes since {lastTag}:");
            sb.AppendLine();

            foreach (var commit in commits)
            {
                sb.AppendLine($"- {commit}");
            }

            return sb.ToString();
        }
    }
} 