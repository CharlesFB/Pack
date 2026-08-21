// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 包依赖信息数据类
// ==================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 包依赖信息数据类
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class PackageDepInfo
    {
        public string PackageName;

        public string Version;
        public string Revision;

        public int RefCount;
        public Dictionary<string, string> Dependencies;
        public Dictionary<string, string> UnZipPath;
        // 映射unitypackage文件名至导入到工程的Assets相对路径列表
        
        public string Url;
        public int Depth;
        public string ScriptDefineSymbols;
        public bool isLocal;

        private bool IsModifyVersion
        {
            get { return Revision.Split('-').Length >= 3; }
        }

        public bool IsLaterTran(PackageDepInfo newDepInfo)
        {
            if (IsModifyVersion != newDepInfo.IsModifyVersion)
            {
                if (IsModifyVersion)
                    return true;
                return false;
            }

            try
            {
                var v1 = new Version(Version);
                var v2 = new Version(newDepInfo.Version);
                if (v1.CompareTo(v2) >= 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                Debug.LogError($"Version Cast Error:{PackageName}-{Revision}-{Version}");
                throw;
            }

            return false;
        }

        public bool IsLaterTran(string revision, string version)
        {
            var strArr = revision.Split('-');
            // var newVersion = strArr[strArr.Length - 1];
            var newIsModifyVersion = strArr.Length >= 3;
            if (IsModifyVersion != newIsModifyVersion)
            {
                if (IsModifyVersion)
                    return true;
                return false;
            }

            try
            {
                var v1 = new Version(Version);
                var v2 = new Version(version);
                if (v1.CompareTo(v2) >= 0)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Version Cast Error:{PackageName}-{Revision}-{Version};{e.Message}");
                throw;
            }

            return false;
        }

        private static string ResolvePrivateToken()
        {
            var token = ConfigManager.Instance.Current?.PrivateToken;
            return string.IsNullOrEmpty(token) ? MTPGit.DEFAULT_TOKEN : token;
        }

        public string GetInstallUrl()
        {
            var url = Url;
            var urlToLower = url.ToLower();
            if (urlToLower.StartsWith("https:"))
            {
                if (!urlToLower.StartsWith("https://oauth2"))
                    url = url.Replace("https://", $"https://oauth2:{ResolvePrivateToken()}@");
            }

            return $"{url}#{Revision}";
        }

        public static string FormatUrl(string url)
        {
            if (url.ToLower().StartsWith("git@"))
            {
                url = "ssh://" + url.Replace(':', '/');
            }
            
            //处理从manifest反向取得的url
            if (url.ToLower().StartsWith("https://oauth2"))
            {
                var removeOauth2 = url.Split('@');
                url = "https://" + removeOauth2[removeOauth2.Length - 1];
            }

            return url;
        }

        public static string SSHToHttpsURLWithToken(string url)
        {
            if (url.Contains("ssh://")) url = url.Replace("ssh://", "");
            url = url.Replace("git@", $"https://oauth2:{ResolvePrivateToken()}@");
            return url;
        }

        public static string SSHToHttpsURLWithoutToken(string url)
        {
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