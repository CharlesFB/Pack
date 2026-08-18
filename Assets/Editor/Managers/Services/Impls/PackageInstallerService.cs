using System.Collections.Generic;
using System.Threading.Tasks;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PackageInstallerService : IPackageInstallerService
    {
        public MTPackageInstallerRequest LoadInstallerRequest(List<MTPackageInfo> addList,
            List<MTPackageInfo> removeList)
        {
            return new MTPackageInstallerRequest();
        }
        
        public async Task StartHandlingInstallerRequest(List<MTPackageInfo> addList, List<MTPackageInfo> removeList)
        {
            // 先处理卸载，再处理安装
            await Task.CompletedTask;
        }
    }
}