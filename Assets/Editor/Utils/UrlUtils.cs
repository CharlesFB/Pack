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
        /// 生成带有 Token 和 Revision 的最终安装链接
        /// </summary>
        public static string GetInstallUrl(string baseUrl, string revision, string privateToken)
        {
            if (string.IsNullOrEmpty(baseUrl)) return string.Empty;

            var urlToLower = baseUrl.ToLower();
            var finalUrl = baseUrl;

            if (urlToLower.StartsWith("https:"))
            {
                if (!urlToLower.StartsWith("https://oauth2") && !string.IsNullOrEmpty(privateToken))
                {
                    finalUrl = baseUrl.Replace("https://", $"https://oauth2:{privateToken}@");
                }
            }

            return $"{finalUrl}#{revision}";
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