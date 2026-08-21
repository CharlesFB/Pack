using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// DTO: 装载远端拉取的 package-lock.json 文件，本地的 package-lock.json 由UPM管理，这个DTO不需要管理持久化
    /// </summary>
    public class UnityPackagesGitLock
    {
        public class LockInfo
        {
            public string version;
            public int depth;
            public string source;
            public List<Dictionary<string, string>> dependencies = new List<Dictionary<string, string>>();
            public string hash;
        }
        public Dictionary<string, LockInfo> dependencies;
    }
}