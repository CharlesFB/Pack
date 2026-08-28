// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 工程列表展示辅助：Tag 过滤排序、按 Tag 检索 package.json 缓存（taginfo）
// ==================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        /// 可见 Tag：非 DebugMode 去掉 *-debug；设置了 ProjectTag 时只保留两段通用版
        /// （xxx-semver / xxx-debug）以及 3 段及以上且中间段包含 ProjectTag 的 Tag。
        /// 已安装 Tag 即使不属于当前项目组也保留（与旧工程一致）。
        /// 再按 VersionUtils 排序（debug 优先，其余 SemVer 降序）。
        /// </summary>
        public static List<string> GetVisibleSortedTags(ProjectInfo project)
        {
            if (project?.Tags == null || project.Tags.Count == 0)
            {
                return new List<string>();
            }

            var config = ConfigManager.Instance.Current;
            bool debugMode = config?.DebugMode ?? false;
            string projectTag = config?.ProjectTag;
            string keepInstalledTag = ServiceLocator.Get<IMTPackageInfoService>()
                ?.GetPackageByProject(project)?.Tag;

            IEnumerable<string> filtered = project.Tags.Where(tag =>
            {
                if (string.IsNullOrEmpty(tag))
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(keepInstalledTag)
                    && string.Equals(tag, keepInstalledTag, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!debugMode && tag.EndsWith(DEBUG_TAG_SUFFIX, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return PassesProjectTagFilter(tag, projectTag);
            });

            return VersionUtils.SortedTags(filtered);
        }

        /// <summary>
        /// 两段 Tag 一律保留；3 段及以上要求 parts[1] 包含 projectTag（忽略大小写）。
        /// </summary>
        internal static bool PassesProjectTagFilter(string tag, string projectTag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            if (string.IsNullOrEmpty(projectTag))
            {
                return true;
            }

            var parts = tag.Split('-');
            if (parts.Length <= 2)
            {
                return true;
            }

            return parts[1].IndexOf(projectTag, StringComparison.OrdinalIgnoreCase) >= 0;
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

            var installed = FindInstalled(project);
            if (installed != null && !string.IsNullOrEmpty(installed.Tag))
            {
                return installed.Tag;
            }

            var visibleTags = GetVisibleSortedTags(project);
            if (visibleTags.Count == 0)
            {
                // LM.Log($"[ProjectDisplayUtils] No tags found in {project.PackageName}");
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
        /// 工程是否已安装：仓库链接命中，且已装 Tag 前缀等于 PackageName（同一仓库多包）。
        /// </summary>
        public static MTPackageInfo FindInstalled(ProjectInfo project)
        {
            if (project == null)
            {
                return null;
            }

            var mtPackage = ServiceLocator.Get<IMTPackageInfoService>();
            var byUrl = mtPackage.GetPackageByProject(project);
            if (byUrl != null)
            {
                return byUrl;
            }

            foreach (var tag in GetVisibleSortedTags(project))
            {
                var tagInfo = GetTagInfo(project, tag);
                if (tagInfo == null || string.IsNullOrEmpty(tagInfo.name))
                {
                    continue;
                }

                var byName = mtPackage.GetPackage(tagInfo.name);
                if (byName != null)
                {
                    return byName;
                }
            }

            return null;
        }

        /// <summary>
        /// 在给定依赖树（线上或草稿）中查找与工程匹配的节点，规则与 <see cref="FindInstalled"/> 相同。
        /// </summary>
        public static MTPackageInfo FindInTree(ProjectInfo project, IReadOnlyDictionary<string, MTPackageInfo> tree)
        {
            if (project == null || tree == null || tree.Count == 0)
            {
                return null;
            }

            foreach (var package in tree.Values)
            {
                if (package != null && UrlUtils.MatchesProject(project, package.Url, package.Tag))
                {
                    return package;
                }
            }

            foreach (var tag in GetVisibleSortedTags(project))
            {
                var tagInfo = GetTagInfo(project, tag);
                if (tagInfo == null || string.IsNullOrEmpty(tagInfo.name))
                {
                    continue;
                }

                if (tree.TryGetValue(tagInfo.name, out var byName) && byName != null)
                {
                    return byName;
                }
            }

            return null;
        }

        /// <summary>
        /// 按 ProjectId + Tag 检索 package.json 缓存（即 "taginfo"）。
        /// 同一仓库多包时，Tag 前缀必须等于 ProjectInfo.PackageName，否则视为不属于当前列表项。
        /// </summary>
        public static PackageJson GetTagInfo(ProjectInfo project, string tag)
        {
            if (project == null || string.IsNullOrEmpty(tag))
            {
                return null;
            }

            if (!UrlUtils.MatchesProjectTagPrefix(project, tag))
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
        /// 非 DebugMode 下过滤黑名单；已安装的包即使在黑名单里也保留。DebugMode 开放黑名单。
        /// </summary>
        public static bool PassesBlackListFilter(ProjectInfo project, List<string> blackList, bool debugMode)
        {
            if (debugMode || blackList == null || blackList.Count == 0 || project == null)
            {
                return true;
            }

            if (!blackList.Contains(project.ProjectId.ToString()))
            {
                return true;
            }

            return FindInstalled(project) != null;
        }
        
        public static List<ProjectInfo> BuildFilteredPackageList(List<ProjectInfo> initialList, bool descending = false, string searchText = null)
        {
            var config = ConfigManager.Instance.Current;
            var blackList = config?.BlackList ?? new List<string>();
            bool debugMode = config?.DebugMode ?? false;
            var rowCache = new Dictionary<ProjectInfo, (MTPackageInfo installed, string displayName, bool hasVisibleTags)>();

            (MTPackageInfo installed, string displayName, bool hasVisibleTags) Row(ProjectInfo project)
            {
                if (rowCache.TryGetValue(project, out var cached))
                {
                    return cached;
                }

                var installed = FindInstalled(project);
                string displayTag = ResolveDisplayTag(project);
                cached = (installed, GetDisplayName(project, displayTag), GetVisibleSortedTags(project).Count > 0);
                rowCache[project] = cached;
                return cached;
            }

            var candidates = initialList
                .Where(project => project != null)
                .Where(project => project.PackageType == PackageType.Normal)
                .Where(project => PassesBlackListFilter(project, blackList, debugMode))
                .Where(project => Row(project).hasVisibleTags)
                .ToList();

            // 先搜，后排序，提高展示性能
            if (!string.IsNullOrEmpty(searchText))
            {
                candidates = candidates
                    .Where(project => Row(project).displayName
                        .IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            candidates.Sort((a, b) =>
            {
                var rowA = Row(a);
                var rowB = Row(b);
                return CompareProjectListOrder(
                    rowA.installed,
                    rowA.displayName,
                    rowB.installed,
                    rowB.displayName,
                    descending);
            });

            return candidates;
        }

        /// <summary>
        /// 工程列表排序：已安装按 Depth 升序（0 最先），再未安装；同档按 displayName 忽略大小写。
        /// installed 为 null 视为未安装。nameDescending 只翻转名称比较，不影响安装/深度档位。
        /// 调用方自行决定 installed 来自线上树还是草稿树。
        /// </summary>
        public static int CompareProjectListOrder(
            MTPackageInfo installedA, string displayNameA,
            MTPackageInfo installedB, string displayNameB,
            bool nameDescending = false)
        {
            bool aInstalled = installedA != null;
            bool bInstalled = installedB != null;

            if (aInstalled && bInstalled)
            {
                if (installedA.Depth < installedB.Depth)
                {
                    return -1;
                }

                if (installedA.Depth > installedB.Depth)
                {
                    return 1;
                }
            }

            if (aInstalled != bInstalled)
            {
                return aInstalled ? -1 : 1;
            }

            int cmp = string.Compare(displayNameA, displayNameB, StringComparison.OrdinalIgnoreCase);
            return nameDescending ? -cmp : cmp;
        }

        /// <summary>
        /// 只把首字母大写，其余字符原样保留（不同于 TitleCase 逐词大写）。
        /// </summary>
        public static string CapitalizeFirstLetter(string text)
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
