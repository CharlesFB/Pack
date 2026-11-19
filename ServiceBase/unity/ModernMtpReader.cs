#if UNITY_EDITOR
using System;
using System.Reflection;
namespace ServiceBase.UnityBridge
{
    internal static class ModernMtpReader
    {
        /// <summary>
        /// 从新版 MTP 服务导出工程依赖记录并补充已缓存元数据。
        /// </summary>
        internal static void Read(
            MtpSnapshot result // 用于写入读取结果的快照
        )
        {
            result.detected = true;
            result.adapter = "modern";
            result.source = "IMTPackageInfoService.ExportToMTPackageInfoList";
            var contract = MtpReflection.Find("IMTPackageInfoService");
            var locator = MtpReflection.Find("ServiceLocator");
            var tryGet = locator?.GetMethod("TryGet", BindingFlags.Public | BindingFlags.Static);
            var export = contract?.GetMethod("ExportToMTPackageInfoList", Type.EmptyTypes);
            if (tryGet == null || !tryGet.IsGenericMethodDefinition || export == null)
            {
                result.state = "incompatible";
                result.detail = "新版 MTP 快照接口不兼容";
                return;
            }
            var args = new object[] { null };
            if (!(bool)tryGet.MakeGenericMethod(contract).Invoke(null, args) || args[0] == null)
            {
                result.state = "initializing";
                result.detail = "MTP 服务尚未注册，请稍后刷新";
                return;
            }
            MtpReflection.CopyPackages(export.Invoke(args[0], null), result, false);
            var metadata = contract.GetMethod("GetPackageJson", new[] { typeof(string) });
            if (metadata != null)
                foreach (var package in result.packages)
                    try { PackageMetadataReader.CopyCached(metadata.Invoke(args[0], new object[] {package.name}), package); }
                    catch { /* Optional metadata must not prevent dependency display. */ }
        }
    }
}
#endif
