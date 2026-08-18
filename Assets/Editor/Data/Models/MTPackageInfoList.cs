using System.Collections.Generic;
using MagicTavern.FrontEngine.MTPManager;

namespace Editor.Data.Models
{
    /// <summary>
    /// 映射目录见 MTPPath.MT_PACKAGE_INFO_LIST_PATH
    /// 保存所有已安装的 MT 包的依赖图
    /// 相关服务见 IMTPackageInfoService
    /// </summary>
    [System.Serializable]
    public class MTPackageInfoList
    {
        public Dictionary<string, MTPackageInfo> Dependencies = new Dictionary<string, MTPackageInfo>();
    }
}