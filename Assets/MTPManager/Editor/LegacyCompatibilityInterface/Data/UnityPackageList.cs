// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: unity package manager的manifest的数据类，为了便于操作和修改 manifest用
// ==================================================

using System.Collections.Generic;
using System.Linq;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// DTO：装载 / 持久化 项目清单 "/Packages/manifest.json" 
    /// unity package manager的manifest的数据类，为了便于操作和修改 manifest用
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class UnityPackageList
    {
        // ReSharper disable once UnassignedField.Global
        public Dictionary<string, string> dependencies;

        public Dictionary<string, string> GetGitPackages()
        {
            var result = new Dictionary<string, string>();
            foreach (var kvp in dependencies)
            {
                if (kvp.Value.Contains(".git"))
                {
                    result.Add(kvp.Key,kvp.Value);
                }
            }

            return result;
        }

        // public string GetGitPackageRevision(string packageName)
        // {
        //     if (dependencies.TryGetValue(packageName, out var url))
        //     {
        //         var result = url.Split('#');
        //         if (result.Length >= 2)
        //         {
        //             return result[result.Length - 1];
        //         }
        //     }
        //
        //     return null;
        // }

        public bool GetGitPackageInfo(string packageName, out string url, out string revision)
        {
            if (dependencies.TryGetValue(packageName, out var fullUrl))
            {
                var result = fullUrl.Split('#');
                if (result.Length >= 2)
                {
                    url = PackageDepInfo.FormatUrl(result[0]);
                    revision = result[result.Length - 1];
                    return true;
                }
            }

            url = null;
            revision = null;
            return false;
        }
    }
}