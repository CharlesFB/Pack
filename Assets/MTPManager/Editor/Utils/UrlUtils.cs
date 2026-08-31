// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 提供URL拼接/拆解相关的方法
// ==================================================

using System;

namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class UrlUtils
    {
        /// <summary>
        /// 生成带有 Token 和 Revision 的最终安装链接。
        /// ssh:// / git@ 会先转成 https，再写入 oauth2:{token}（缺省用 DEFAULT_TOKEN）。
        /// </summary>
        public static string GetInstallUrl(string baseUrl, string revision, string privateToken)
        {
            if (string.IsNullOrEmpty(baseUrl)) return string.Empty;

            var token = string.IsNullOrEmpty(privateToken) ? MTPGit.DEFAULT_TOKEN : privateToken;
            var url = ToHttpsWithoutToken(baseUrl);

            if (!string.IsNullOrEmpty(token)
                && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && url.IndexOf("://oauth2", StringComparison.OrdinalIgnoreCase) < 0)
            {
                url = "https://oauth2:" + token + "@" + url.Substring("https://".Length);
            }

            return string.IsNullOrEmpty(revision) ? url : $"{url}#{revision}";
        }

        /// <summary>
        /// 去掉 URL 中的 oauth2 private token，保留 #tag / #revision。
        /// </summary>
        public static string StripPrivateToken(string url)
        {
            return FormatUrl(url);
        }

        /// <summary>
        /// 把 package.json dependenciesGit 的 "仓库链接#tag" 转成可安装链接（携带 DEFAULT_TOKEN）。
        /// </summary>
        public static string ToInstallGitUrl(string gitDependencyUrl, string privateToken = null)
        {
            if (string.IsNullOrEmpty(gitDependencyUrl))
            {
                return gitDependencyUrl;
            }

            var token = string.IsNullOrEmpty(privateToken) ? MTPGit.DEFAULT_TOKEN : privateToken;
            var splitIndex = gitDependencyUrl.LastIndexOf('#');
            if (splitIndex <= 0 || splitIndex == gitDependencyUrl.Length - 1)
            {
                return GetInstallUrl(gitDependencyUrl, string.Empty, token);
            }

            return GetInstallUrl(
                gitDependencyUrl.Substring(0, splitIndex),
                gitDependencyUrl.Substring(splitIndex + 1),
                token);
        }

        public static string ToHttpsWithoutToken(string url)
        {
            return SshToHttpsURLWithoutToken(FormatUrl(url));
        }

        public static string FormatUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.ToLower().StartsWith("git@"))
            {
                url = "ssh://" + url.Replace(':', '/');
            }
            
            // 处理从 manifest 反向取得的 url
            if (url.ToLower().StartsWith("https://oauth2"))
            {
                var removeOauth2 = url.Split('@');
                url = "https://" + removeOauth2[removeOauth2.Length - 1];
            }

            return url;
        }

        /// <summary>
        /// 下载链接 / dependenciesGit 链接 -> 仓库地址：去掉 #tag，再 FormatUrl 去掉 oauth2 token / 统一 git@。
        /// </summary>
        public static string GetRepoUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            var splitIndex = url.LastIndexOf('#');
            if (splitIndex > 0)
            {
                url = url.Substring(0, splitIndex);
            }

            return FormatUrl(url);
        }

        /// <summary>
        /// Tag / 依赖 revision 的前缀：第一个 '-' 之前。与旧工程 Revision.Split('-')[0] 一致。
        /// </summary>
        public static string GetTagPrefix(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return tag;
            }

            int dash = tag.IndexOf('-');
            return dash < 0 ? tag : tag.Substring(0, dash);
        }

        /// <summary>
        /// 同一仓库下用 tag 前缀区分包：prefix == ProjectInfo.PackageName。
        /// </summary>
        public static bool MatchesProjectTagPrefix(ProjectInfo project, string tag)
        {
            if (project == null || string.IsNullOrEmpty(project.PackageName) || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            return string.Equals(GetTagPrefix(tag), project.PackageName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 仓库链接是否对得上工程：Format 后的输入与 HttpUrl / SshUrl 任一相等即成功。
        /// </summary>
        public static bool MatchesProjectRepo(ProjectInfo project, string url)
        {
            if (project == null || string.IsNullOrEmpty(url))
            {
                return false;
            }

            var repo = GetRepoUrl(url);
            if (string.IsNullOrEmpty(repo))
            {
                return false;
            }

            return string.Equals(repo, GetRepoUrl(project.HttpUrl), StringComparison.Ordinal)
                   || string.Equals(repo, GetRepoUrl(project.SshUrl), StringComparison.Ordinal);
        }

        /// <summary>
        /// 配对工程：仓库链接（HttpUrl 或 SshUrl）命中，且 tag 前缀等于 PackageName。
        /// </summary>
        public static bool MatchesProject(ProjectInfo project, string url, string tag)
        {
            return MatchesProjectRepo(project, url) && MatchesProjectTagPrefix(project, tag);
        }

        public static string SshToHttpsURLWithToken(string url, string privateToken)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.Contains("ssh://")) url = url.Replace("ssh://", "");
            // 依赖传入的 token，而不是直接读全局变量
            url = url.Replace("git@", $"https://oauth2:{privateToken}@");
            return url;
        }

        public static string SshToHttpsURLWithoutToken(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            if (url.Contains("ssh://")) url = url.Replace("ssh://", "");
            if (url.ToLower().StartsWith("git@"))
            {
                url = url.Replace(':', '/');
                url = url.Replace("git@", "https://");
            }

            return url;
        }
    }
}