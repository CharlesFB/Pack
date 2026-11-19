#if UNITY_EDITOR
using System;
using UnityEditor;
namespace ServiceBase.UnityBridge
{
    internal static class MtpSnapshotReader
    {
        /// <summary>
        /// 在 Unity 主线程采集工程快照；编译或资源刷新时返回初始化状态。
        /// </summary>
        internal static MtpSnapshot Capture()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return new MtpSnapshot { state = "initializing", detail = "Unity 正在编译或刷新，请稍后读取 MTP" };
            try
            {
                var result = CaptureForInstallation(MtpPackageDetector.Detect());
                // Complete catalog is fetched and cached by the Python service.
                PackageMetadataReader.EnrichInstalled(result);
                return result;
            }
            catch (Exception) { return new MtpSnapshot { state = "error", detail = "无法读取 UPM 已加载包信息，请稍后重试" }; }
        }

        // Separate selection from Unity metadata for deterministic compatibility tests.
        /// <summary>
        /// 根据检测结果选择新旧 MTP 读取路径，将不兼容或读取异常转换为快照状态。
        /// </summary>
        internal static MtpSnapshot CaptureForInstallation(
            MtpInstallation installation // MTP 安装与版本检测结果
        )
        {
            var result = new MtpSnapshot { mtp_version = installation.version };
            try
            {
                if (installation.installed)
                {
                    result.detected = true;
                    if (!installation.versionKnown)
                    {
                        result.state = "incompatible";
                        result.detail = "无法识别已安装 MTP 版本，已停止读取";
                    }
                    else if (installation.legacy) LegacyMtpReader.Read(result);
                    else ModernMtpReader.Read(result);
                }
                else if (MtpReflection.Find("IMTPackageInfoService") != null)
                    ModernMtpReader.Read(result); // Current Assets development version.
                else if (MtpReflection.Find("MTPManager") != null)
                {
                    result.detected = true;
                    result.state = "incompatible";
                    result.detail = "检测到 MTP 类型，但缺少 UPM 版本或新版接口，已停止读取";
                }
            }
            catch (Exception)
            {
                result.state = "error";
                result.detail = "MTP 反射读取失败，已停止；需要检查该版本的数据结构";
                result.packages.Clear();
            }
            return result;
        }
    }
}
#endif
