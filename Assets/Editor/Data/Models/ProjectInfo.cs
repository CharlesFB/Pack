using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// MT 包远端工程信息结构
    /// </summary>
    public class ProjectInfo
    {
        public List<string> Tags;
        public List<string> PluginFileNames;
        public List<string> Versions => Tags;
        public string PackageName;
        public string SshUrl;
        public string HttpUrl;
        
        public PackageType PackageType = PackageType.Normal;
        public int ProjectId;

        /// <summary>
        /// 创建一个浅拷贝对象，其中 Tags 使用独立的新列表，用于过滤显示时不污染缓存。
        /// </summary>
        public ProjectInfo CloneForDisplayTemp()
        {
            var clone = (ProjectInfo)MemberwiseClone();
            clone.Tags = new List<string>(Tags);
            return clone;
        }
    }
}