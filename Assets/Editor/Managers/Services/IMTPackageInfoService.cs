// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MT 包依赖信息内存服务接口
// ==================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using Editor.Data.Models;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IMTPackageInfoService
    {
        MTPackageInfo AddDependency(MTPackageInfo newPackage);
        MTPackageInfo AddDependency(PackageJson rawPackage, string url, string tag, string revision, int depth, bool isLocal);
        void RemoveDependency(string packageName);
        MTPackageInfo GetPackage(string packageName);
        List<MTPackageInfo> GetAllPackages();

        /// <summary>
        /// 按 "仓库链接#tag"（与 package.json 中 dependenciesGit 的写法一致）从远端拉取单条工程的
        /// package.json，并解析出 tag 对应的 40 位 commit sha（revision），转换为 MTPackageInfo 返回。
        /// 纯拉取 + 转换，不会写入内部依赖树状态；失败（找不到工程 / 拉取失败 / revision 解析失败）返回 null。
        /// </summary>
        /// <param name="gitDependencyUrl">形如 "https://xxx/xxx.git#package-1.2.3" 的仓库链接#tag 字符串</param>
        Task<MTPackageInfo> FetchPackageInfoAsync(string gitDependencyUrl, int depth = 0, bool isLocal = false);

        /// <summary>
        /// 同上，url 与 tag 分别传入。
        /// </summary>
        Task<MTPackageInfo> FetchPackageInfoAsync(string url, string tag, int depth = 0, bool isLocal = false);

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
