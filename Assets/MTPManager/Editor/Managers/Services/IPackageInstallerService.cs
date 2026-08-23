using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IPackageInstallerService
    {
        public class MTPackageInstallerRequest
        {
            public RequestType RequestType { get; set; }
            public MTPackageInfo RequestPackageInfo { get; set; }
            public RequestState RequestState { get; set; }

            public Request StartRequest()
            {
                if (RequestType == RequestType.Add || RequestType == RequestType.UpdateAdd)
                    return Client.Add(RequestPackageInfo.GetInstallUrl(MTPGit.DEFAULT_TOKEN));
                if (RequestType == RequestType.Remove || RequestType == RequestType.UpdateRemove)
                    return Client.Remove(RequestPackageInfo.PackageName);
                return null;
            }
        }
        /// <summary>
        /// 装载初始化安装器请求
        /// </summary>
        public List<MTPackageInstallerRequest> LoadInstallerRequest(List<MTPackageInfo> addList, List<MTPackageInfo> removeList);
        
        /// <summary>
        /// 处理一个批次的安装器请求
        /// </summary>
        public Task StartHandlingInstallerRequest(List<MTPackageInstallerRequest> requests);
    }
}