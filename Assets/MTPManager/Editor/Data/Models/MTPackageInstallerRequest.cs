using System;
using System.Diagnostics.CodeAnalysis;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 安装器单条请求。字段可被 LitJson 写入中断恢复进度文件。
    /// </summary>
    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class MTPackageInstallerRequest
    {
        public RequestType RequestType;
        public MTPackageInfo RequestPackageInfo;
        public RequestState RequestState;

        public Request StartRequest()
        {
            if (RequestPackageInfo == null)
            {
                return null;
            }

            if (RequestType == RequestType.Add || RequestType == RequestType.UpdateAdd)
            {
                return Client.Add(RequestPackageInfo.GetInstallUrl(MTPGit.DEFAULT_TOKEN));
            }

            if (RequestType == RequestType.Remove || RequestType == RequestType.UpdateRemove)
            {
                return Client.Remove(RequestPackageInfo.PackageName);
            }

            return null;
        }
    }
}
