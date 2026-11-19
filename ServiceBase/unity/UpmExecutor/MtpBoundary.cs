#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
namespace ServiceBase.Upm
{
    internal static class MtpBoundary
    {
        internal static string Check(string root)
        {
            if (new[] { "mtp-install-recovery.json", "mtp-install-progress.json", "mtp-pending-manifest.json" }
                .Any(file => File.Exists(Path.Combine(root, "Library/" + file))))
                return "检测到 MTP 恢复记录；请先处理原操作，服务不会接管或删除该记录。";
            // Observe identity only. Never load MTP types, invoke its APIs or suppress its callbacks.
            if (UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(p => p.name == "com.magictavern.mtpmanager")
                || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Magictavern.FrontEngine.MTPManager"))
                return "检测到 MTP；该组合尚无经过验收的共存协议，服务安装暂被阻止。";
            return "";
        }
    }
}
#endif
