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