// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 工程列表展示辅助：Tag 过滤排序、按 Tag 检索 package.json 缓存（taginfo）
// ==================================================

using System;
using System.Collections.Generic;
using System.Linq;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// lvProjectList / InfoView 共用的“工程 -> 展示用 Tag -> taginfo”解析逻辑，
    /// 保证列表条目与详情面板对同一个工程给出一致的结果。
    /// </summary>
    public static class ProjectDisplayUtils
    {
        private const string DEBUG_TAG_SUFFIX = "debug";

        /// <summary>
        /// 非 DebugMode 下过滤掉以 "-debug" 结尾的 Tag，再按 VersionUtils 规则排序
        /// （debug 优先，其余按 SemVer 降序，故 [0] 即当前可见的最高 Tag）。
        /// </summary>
        public static List<string> GetVisibleSortedTags(ProjectInfo project)
        {
            if (project?.Tags == null || project.Tags.Count == 0)
            {
                return new List<string>();
            }

            bool debugMode = ConfigManager.Instance.Current?.DebugMode ?? false;
            IEnumerable<string> filtered = debugMode
                ? project.Tags
                : project.Tags.Where(tag => !string.IsNullOrEmpty(tag)
                    && !tag.EndsWith(DEBUG_TAG_SUFFIX, StringComparison.OrdinalIgnoreCase));

            List<string> res = VersionUtils.SortedTags(filtered);
            // for (int i = 0; i < res.Count(); i++)
            // {
            //     if (GetTagInfo(project, res[i]) == null)
            //         res.RemoveAt(i);
            // }
            return res;
        }

        /// <summary>
        /// 展示用 Tag 解析：已安装（在依赖树 _dependencies 中）则用安装的 Tag；
        /// 否则默认取（受 DebugMode 过滤后）最高 Tag；两者皆无返回 null。
        /// </summary>
        public static string ResolveDisplayTag(ProjectInfo project)
        {
            if (project == null)
            {
                return null;
            }

            var installed = ServiceLocator.Get<IMTPackageInfoService>().GetPackage(project.PackageName);
            if (installed != null && !string.IsNullOrEmpty(installed.Tag))
            {
                return installed.Tag;
            }

            var visibleTags = GetVisibleSortedTags(project);
            if (visibleTags.Count == 0)
            {
                LM.Log($"[ProjectDisplayUtils] No tags found in {project.PackageName}");
                return null;
            }

            foreach (var tag in visibleTags)
            {
                if (GetTagInfo(project, tag) == null) continue;
                return tag;
            }
            
            return null;
        }

        /// <summary>
        /// 按 ProjectId + Tag 检索 package.json 缓存（即 "taginfo"）。
        /// 工程 / Tag / 缓存任一缺失均返回 null，调用方应统一以 "N/A" 兜底展示。
        /// </summary>
        public static PackageJson GetTagInfo(ProjectInfo project, string tag)
        {
            if (project == null || string.IsNullOrEmpty(tag))
            {
                return null;
            }

            return ServiceLocator.Get<IPackageJsonService>().Get(project.ProjectId.ToString(), tag);
        }

        /// <summary>
        /// 展示名解析：优先用 taginfo 的 displayName；
        /// 如果按 tag 取不到 taginfo（包括 project 的 taglist 本身为空导致 tag 都解析不出来），
        /// 但 project 与 project.PackageName 本身不为空，则退化展示 "PackageName 首字母大写"，
        /// 而不是直接显示 "N/A"；project 为空或 PackageName 为空时才返回 "N/A"。
        /// </summary>
        public static string GetDisplayName(ProjectInfo project, string tag)
        {
            var displayName = GetTagInfo(project, tag)?.displayName;
            if (!string.IsNullOrEmpty(displayName))
            {
                return displayName;
            }

            if (project != null && !string.IsNullOrEmpty(project.PackageName))
            {
                return CapitalizeFirstLetter(project.PackageName);
            }

            return "N/A";
        }

        /// <summary>
        /// 只把首字母大写，其余字符原样保留（不同于 TitleCase 逐词大写）。
        /// </summary>
        private static string CapitalizeFirstLetter(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        public static string GetTagFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                LM.Log($"[ProjectDisplayUtils] Empty url.");
                return null;
            }
            
            var parts = url.Split('#');
            if (parts.Length < 2)
            {
                LM.Log("[ProjectDisplayUtils] No tag available in url.");
                return null;
            }
            // LM.Log($"[ProjectDisplayUtils] tag : {parts[url.Length - 1]}");
            return parts[url.Length - 1];
        }
    }
}
