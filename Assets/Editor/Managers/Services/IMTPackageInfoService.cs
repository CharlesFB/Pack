// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MT 包依赖信息内存服务接口
// ==================================================

using System.Collections.Generic;
using Editor.Data.Models;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IMTPackageInfoService
    {
        MTPackageInfo AddDependency(MTPackageInfo newPackage);
        MTPackageInfo AddDependency(PackageInfo rawPackage, string url, string tag, string revision, int depth, bool isLocal);
        void RemoveDependency(string packageName);
        MTPackageInfo GetPackage(string packageName);
        List<MTPackageInfo> GetAllPackages();

        /// <summary>
        /// 写入/更新某包 Plugins 字典中的一条：插件本地路径 -> 是否已安装。
        /// 包不存在时返回 false。
        /// </summary>
        bool SetPlugin(string packageName, string pluginLocalPath, bool installed);

        /// <summary>
        /// 获取某包 Plugins 字典副本；包不存在返回 null。
        /// </summary>
        Dictionary<string, bool> GetPlugins(string packageName);

        MTPackageInfoList ExportToMTPackageInfoList();
        void LoadFromMTPackageInfoList(MTPackageInfoList list);
        void LoadDependenciesFromLocal(bool createIfNotExist = true);
        void SaveDependenciesToLocal();
    }
}
