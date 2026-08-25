using System.Collections.Generic;
using System.Threading.Tasks;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPackageInstallerService
    {
        /// <summary>
        /// 装载初始化安装器请求
        /// </summary>
        List<MTPackageInstallerRequest> LoadInstallerRequest(List<MTPackageInfo> addList, List<MTPackageInfo> removeList);

        /// <summary>
        /// 处理一个批次的安装器请求。会写入中断恢复进度；全部完成后删除进度文件。
        /// </summary>
        Task StartHandlingInstallerRequest(List<MTPackageInstallerRequest> requests);

        /// <summary>
        /// 从本地进度文件续跑被域重载打断的批次。无进度文件时直接返回。
        /// </summary>
        Task ResumeInterruptedInstallAsync();
    }
}
