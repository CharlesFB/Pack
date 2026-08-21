// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: PackageJson的缓存对象
// ==================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// DTO: 解析 / 映射Git远端工程（647号Project）的管理全部MTP包的Cache文件 upm-cache.json，本地映射 /Library/upm-cache.json
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class PackageJsonCache
    {
        public DateTime RefreshTime;
        public Dictionary<string, Dictionary<string, PackageManifest>> PackageCache;

        /// <summary>
        /// 新增属性RefreshTime，添加NeedToUpdate
        /// 反序列化后为默认值 DateTime.MinValue，会被判定为已过期，从而触发一次重新拉取。
        /// </summary>
        public bool NeedToUpdate => (DateTime.UtcNow - RefreshTime).TotalSeconds > 600
                                     || PackageCache == null
                                     || PackageCache.Count == 0;

        public void Add(string projectId, string revision, PackageManifest packageJson)
        {
            if (PackageCache == null)
            {
                PackageCache = new Dictionary<string, Dictionary<string, PackageManifest>>();
            }

            if (!PackageCache.ContainsKey(projectId))
            {
                PackageCache.Add(projectId, new Dictionary<string, PackageManifest>());
            }

            var packDic = PackageCache[projectId];
            if (!packDic.ContainsKey(revision))
            {
                packDic.Add(revision, packageJson);
            }
            else
            {
                packDic[revision] = packageJson;
            }
        }

        public PackageManifest Get(string projectId, string revision)
        {
            if (PackageCache == null)
            {
                PackageCache = new Dictionary<string, Dictionary<string, PackageManifest>>();
            }

            if (!PackageCache.TryGetValue(projectId, out var packDic))
            {
                return null;
            }

            if (!packDic.TryGetValue(revision, out var packageJson))
            {
                return null;
            }

            return packageJson;
        }
    }
}