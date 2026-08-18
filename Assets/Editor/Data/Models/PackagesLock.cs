using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// DTO: Unity packages-lock.json（Packages/packages-lock.json）
    /// 本地文件由 UPM 管理，MTP 只读取；相关服务见 IPackageLockService
    /// </summary>
    public class PackagesLock
    {
        public class LockInfo
        {
            public List<Dictionary<string, string>> dependencies = new List<Dictionary<string, string>>();
            public string version;
            public string source;
            public string hash;
            public int depth;
        }
        public Dictionary<string, LockInfo> dependencies;
    }
}