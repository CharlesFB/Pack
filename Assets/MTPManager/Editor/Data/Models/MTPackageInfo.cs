using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 记录 MTP 开发的包的属性，目前不直接参与Json映射
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class MTPackageInfo
    {
        public Dictionary<string, string> Dependencies;
        public Dictionary<string, bool> Plugins = new Dictionary<string, bool>();

        public string PackageName; // "com.xxx.xxx"
        public string Tag; // 发布 Tag，如 package-1.2.3
        public string Revision; // Hash Commit
        public string Url;
        public int ProjectId; // 所属远端工程 id（对应 ProjectInfo.ProjectId）
        public string ScriptDefineSymbols;

        public int Depth;
        public bool isLocal;

        private bool IsModifyVersion
        {
            get
            {
                if (string.IsNullOrEmpty(Tag))
                {
                    return false;
                }

                return Tag.Split('-').Length >= 3;
            }
        }

        public bool IsLaterTran(MTPackageInfo newMTPackageInfo)
        {
            return IsLaterTran(newMTPackageInfo.Revision, newMTPackageInfo.Tag);
        }

        public bool IsLaterTran(string revision, string tag)
        {
            var strArr = string.IsNullOrEmpty(tag) ? Array.Empty<string>() : tag.Split('-');
            var newIsModifyVersion = strArr.Length >= 3;
            if (IsModifyVersion != newIsModifyVersion)
            {
                return IsModifyVersion;
            }

            try
            {
                var v1 = new Version(VersionUtils.GetVersionSegment(Tag));
                var v2 = new Version(VersionUtils.GetVersionSegment(tag));
                return v1.CompareTo(v2) >= 0;
            }
            catch (Exception)
            {
                LM.LogError($"Version Cast Error:{PackageName}-{Revision}-{Tag}");
                throw;
            }
        }

        /// <summary>
        /// 提供一个便捷方法，内部调用工具类。
        /// </summary>
        public string GetInstallUrl(string privateToken)
        {
            return UrlUtils.GetInstallUrl(Url, Revision, privateToken);
        }
    }
}
