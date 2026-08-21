// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MTP Tag / 版本号比较与排序工具
// ==================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// Tag 命名约定形如 package-1.2.3 / package-project-1.2.3 / xxx-debug，
    /// 比较时取 '-' 分割后的末段作为版本段。
    /// </summary>
    internal static class VersionUtils
    {
        /// <summary>
        /// 取出 tag 名称中的版本段（最后一个 '-' 之后；无 '-' 则返回自身）。
        /// </summary>
        public static string GetVersionSegment(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return string.Empty;
            }

            var parts = tag.Split('-');
            return parts[parts.Length - 1];
        }

        /// <summary>
        /// Tag 比较器：debug 优先；其余按 Version 降序（新版本在前）。
        /// 供 List.Sort 使用。
        /// </summary>
        public static int CompareTags(string tag1, string tag2)
        {
            var ver1 = GetVersionSegment(tag1);
            var ver2 = GetVersionSegment(tag2);

            if (ver1 == "debug")
            {
                if (ver2 == "debug")
                {
                    return string.Compare(tag2, tag1, StringComparison.Ordinal);
                }

                return -1;
            }

            if (ver2 == "debug")
            {
                return 1;
            }

            try
            {
                var version1 = Version.Parse(ver1);
                var version2 = Version.Parse(ver2);
                return version2.CompareTo(version1);
            }
            catch (Exception ex)
            {
                LM.LogError($"[VersionUtils] CompareTags 解析失败: {tag1} vs {tag2}\n{ex.Message}");
                return string.Compare(tag2, tag1, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// 原地按 CompareTags 规则排序；列表为空或 null 时直接返回。
        /// </summary>
        public static void SortTags(List<string> tags)
        {
            if (tags == null || tags.Count <= 1)
            {
                return;
            }

            try
            {
                tags.Sort(CompareTags);
            }
            catch (Exception ex)
            {
                LM.LogWarning($"[VersionUtils] SortTags 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 返回按 CompareTags 排序后的新列表（不修改入参）。
        /// </summary>
        public static List<string> SortedTags(IEnumerable<string> tags)
        {
            var result = tags != null ? new List<string>(tags) : new List<string>();
            SortTags(result);
            return result;
        }
    }
}
