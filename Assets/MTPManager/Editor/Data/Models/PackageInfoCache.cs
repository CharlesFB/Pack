// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 陈梓霄
// @Description: PackageJson 缓存持久化数据模型
// ==================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 映射目录见 MTPPath.PACKAGE_JSON_CACHE_PATH
    /// 远端工程（647）管理的全部 MTP 包 package.json 缓存
    /// 相关服务见 IPackageJsonService
    /// </summary>
    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class PackageInfoCache
    {
        public DateTime RefreshTime;
        public Dictionary<string, Dictionary<string, PackageJson>> PackageCache =
            new Dictionary<string, Dictionary<string, PackageJson>>();
    }
}
