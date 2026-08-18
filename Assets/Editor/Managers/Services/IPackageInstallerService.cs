using System.Collections.Generic;
using System.Threading.Tasks;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPackageInstallerService
    {
        /// <summary>
        /// 装载初始化安装器请求
        /// </summary>
        public MTPackageInstallerRequest LoadInstallerRequest(List<MTPackageInfo> addList, List<MTPackageInfo> removeList);
        
        /// <summary>
        /// 处理一个批次的安装器请求
        /// </summary>
        public Task StartHandlingInstallerRequest(List<MTPackageInfo> addList, List<MTPackageInfo> removeList);
    }
}