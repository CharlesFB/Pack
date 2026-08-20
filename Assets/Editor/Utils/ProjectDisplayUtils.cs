// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 工程列表展示辅助：Tag 过滤排序、按 Tag 检索 package.json 缓存（taginfo）
// ==================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// lvProjectList / InfoView 共用的“工程 -> 展示用 Tag -> taginfo”解析逻辑，
    /// 保证列表条目与详情面板对同一个工程给出一致的结果。
    /// </summary>
    public static class ProjectDisplayUtils
    {
        private const string DEBUG_TAG_SUFFIX = "-debug";

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

            return VersionUtils.SortedTags(filtered);
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
            return visibleTags.Count > 0 ? visibleTags[0] : null;
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
        /// 便捷方法：直接拿 taginfo 的 displayName，缺失统一返回 "N/A"。
        /// </summary>
        public static string GetDisplayName(ProjectInfo project, string tag)
        {
            return GetTagInfo(project, tag)?.displayName ?? "N/A";
        }
    }
}
