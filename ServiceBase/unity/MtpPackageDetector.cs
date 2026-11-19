#if UNITY_EDITOR
using System;
using System.Text.RegularExpressions;
using UnityEditor.PackageManager;

namespace ServiceBase.UnityBridge
{
    internal sealed class MtpInstallation
    {
        public bool installed, versionKnown, legacy;
        public string version = "";
    }

    internal static class MtpPackageDetector
    {
        /// <summary>
        /// 读取 Unity 已注册包信息，识别 MTP 安装版本。
        /// </summary>
        internal static MtpInstallation Detect()
        {
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name != "com.magictavern.mtpmanager") continue;
                // Registered package metadata only: no UPM request or package resolution.
                return Classify(package.version, package.git != null ? package.git.revision : "");
            }
            return new MtpInstallation();
        }

        /// <summary>
        /// 结合版本号和 Git 引用判断是否使用旧版 MTP 适配器。
        /// </summary>
        internal static MtpInstallation Classify(
            string packageVersion, // UPM 包版本字符串
            string gitReference // 包的 Git 引用
        )
        {
            var info = new MtpInstallation { installed = true };
            // Prefer an explicit MTP git tag over potentially stale package.json.
            var match = Regex.Match(gitReference ?? "", @"^mtpmanager-(\d+)\.(\d+)(?:\.(\d+))?(?:[-+][A-Za-z0-9._-]+)?$");
            if (!match.Success)
                match = Regex.Match(packageVersion ?? "", @"^(\d+)\.(\d+)(?:\.(\d+))?(?:[-+][A-Za-z0-9._-]+)?$");
            if (!match.Success) return info;
            int major, minor, patch = 0;
            if (!int.TryParse(match.Groups[1].Value, out major) || !int.TryParse(match.Groups[2].Value, out minor)) return info;
            if (match.Groups[3].Success && !int.TryParse(match.Groups[3].Value, out patch)) return info;
            info.versionKnown = true;
            info.version = match.Value;
            info.legacy = major < 1 || (major == 1 && (minor < 3 || (minor == 3 && patch <= 19)));
            return info;
        }
    }
}
#endif
