// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 包的安装信息数据类
// ==================================================

using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 包的安装信息数据类
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class PackageInstallInfo
    {
        public ProjectInfo ProjectInfo;
        public PackageDepInfo DepInfo;

        public string Revision
        {
            get
            {
                if (DepInfo == null)
                    return LatestVersion;
                return DepInfo.Revision;
            }
        }

        private string LatestVersion
        {
            get
            {
                if (ProjectInfo == null || ProjectInfo.Versions == null || !ProjectInfo.Versions.Any())
                {
                    return "N/A";
                }

                string ver = ConfigManager.Instance.Current.DebugMode ? ProjectInfo.Versions.FirstOrDefault() : ProjectInfo.Versions.FirstOrDefault(item => !item.EndsWith("-debug"));

                if (string.IsNullOrEmpty(ver))
                {
                    return "N/A";
                }

                return ver;
            }
        }

        public string Version
        {
            get
            {
                if (DepInfo == null)
                {
                    var revisions = LatestVersion.Split('-');
                    return revisions[revisions.Length - 1];
                }

                return DepInfo.Version;
            }
        }
    }
}