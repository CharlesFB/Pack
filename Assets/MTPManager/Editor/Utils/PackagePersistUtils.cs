// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 依赖树 / 根包 / 嵌入包 持久化时去掉 URL 中的 private token，读回内存再写回
// ==================================================

using System.Collections.Generic;
using Editor.Data.Models;

namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class PackagePersistUtils
    {
        /// <summary>
        /// 拷贝一份用于写盘：Dependencies 下的 url 去掉 oauth2 token，不改内存对象。
        /// </summary>
        public static MTPackageInfoList CloneWithoutPrivateTokens(MTPackageInfoList source)
        {
            var result = new MTPackageInfoList();
            if (source?.Dependencies == null)
            {
                return result;
            }

            foreach (var kvp in source.Dependencies)
            {
                result.Dependencies[kvp.Key] = ClonePackageWithoutPrivateTokens(kvp.Value);
            }

            return result;
        }

        /// <summary>
        /// 从磁盘读入后写入内存前：按当前配置把 private token 填回 Dependencies 的 url。
        /// </summary>
        public static void InjectPrivateTokens(MTPackageInfoList list)
        {
            if (list?.Dependencies == null)
            {
                return;
            }

            string token = ResolveToken();
            foreach (var package in list.Dependencies.Values)
            {
                InjectPrivateTokens(package, token);
            }
        }

        private static MTPackageInfo ClonePackageWithoutPrivateTokens(MTPackageInfo source)
        {
            if (source == null)
            {
                return null;
            }

            return new MTPackageInfo
            {
                PackageName = source.PackageName,
                Tag = source.Tag,
                Revision = source.Revision,
                Url = UrlUtils.StripPrivateToken(source.Url),
                ScriptDefineSymbols = source.ScriptDefineSymbols,
                ProjectId = source.ProjectId,
                Depth = source.Depth,
                isLocal = source.isLocal,
                Plugins = source.Plugins != null
                    ? new Dictionary<string, bool>(source.Plugins)
                    : new Dictionary<string, bool>(),
                Dependencies = StripDependencyUrls(source.Dependencies),
            };
        }

        private static Dictionary<string, string> StripDependencyUrls(Dictionary<string, string> dependencies)
        {
            if (dependencies == null)
            {
                return null;
            }

            var result = new Dictionary<string, string>(dependencies.Count);
            foreach (var kvp in dependencies)
            {
                result[kvp.Key] = UrlUtils.StripPrivateToken(kvp.Value);
            }

            return result;
        }

        private static void InjectPrivateTokens(MTPackageInfo package, string token)
        {
            if (package?.Dependencies == null || package.Dependencies.Count == 0)
            {
                return;
            }

            var keys = new List<string>(package.Dependencies.Keys);
            foreach (var key in keys)
            {
                package.Dependencies[key] = UrlUtils.ToInstallGitUrl(package.Dependencies[key], token);
            }
        }

        private static string ResolveToken()
        {
            var token = ConfigManager.Instance.Current?.PrivateToken;
            return string.IsNullOrEmpty(token) ? MTPGit.DEFAULT_TOKEN : token;
        }
    }
}
