// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 陈梓霄
// @Description: Unity Package Manager 的 manifest.json 持久化数据模型
// ==================================================

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 映射目录见 MTPPath.UNITY_PACKAGE_LIST_PATH
    /// Unity Package Manager 项目清单
    /// 相关服务见 IPackageManifestService
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class PackageManifest
    {
        public Dictionary<string, string> dependencies = new Dictionary<string, string>();
    }
}
