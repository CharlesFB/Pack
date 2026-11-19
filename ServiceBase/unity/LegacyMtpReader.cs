#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
namespace ServiceBase.UnityBridge
{
    internal static class LegacyMtpReader
    {
        /// <summary>
        /// 定位旧版类型并委托只读缓存适配，避免触发管理器初始化。
        /// </summary>
        internal static void Read(
            MtpSnapshot result // 用于写入读取结果的快照
        )
        {
            ReadCache(MtpReflection.Find("MTPManager"), MtpReflection.Find("PackageDepInfoList"),
                MtpReflection.Find("JsonUtils"), Path.GetDirectoryName(Application.dataPath), result);
        }

        // Read a literal path without running MTPManager's static initializer.
        // LoadFromFile(path, false) only reads/deserializes; never initialize or sync MTP.
        /// <summary>
        /// 只读取旧版依赖缓存，不初始化 MTP 管理器或触发远端请求。
        /// </summary>
        internal static void ReadCache(
            Type manager, // 已检测到的旧版管理器类型
            Type model, // 旧版依赖记录类型
            Type jsonUtils, // 旧版 JSON 文件工具类型
            string projectRoot, // Unity 工程根目录
            MtpSnapshot result // 用于写入读取结果的快照
        )
        {
            result.detected = true;
            result.adapter = "legacy";
            var pathField = manager?.GetField("DEPENDENCY_INFO_LIST_PATH", BindingFlags.NonPublic | BindingFlags.Static);
            var dependencies = model?.GetField("Dependencies", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo loader = null;
            if (jsonUtils != null)
                foreach (var method in jsonUtils.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var parameters = method.GetParameters();
                    if (method.Name == "LoadFromFile" && method.IsGenericMethodDefinition &&
                        method.GetGenericArguments().Length == 1 && parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(bool))
                        loader = method;
                }
            if (pathField == null || !pathField.IsLiteral || pathField.FieldType != typeof(string) ||
                dependencies == null || !typeof(IDictionary).IsAssignableFrom(dependencies.FieldType) || loader == null)
            {
                result.state = "incompatible";
                result.detail = "旧版 MTP 缺少已知的独立缓存读取接口，已停止读取；需要单独评估";
                return;
            }
            var relativePath = (string)pathField.GetRawConstantValue();
            if (relativePath != "Library/upm-dependencies.json")
            {
                result.state = "incompatible";
                result.detail = "旧版 MTP 缓存路径与已验证结构不一致，已停止读取";
                return;
            }
            result.source = relativePath;
            var path = Path.Combine(projectRoot, relativePath);
            if (!File.Exists(path))
            {
                result.state = "error";
                result.detail = "旧版 MTP 依赖缓存不存在（Library/upm-dependencies.json）；桥接层不会创建或重建缓存";
                return;
            }
            try
            {
                var snapshot = loader.MakeGenericMethod(model).Invoke(null, new object[] { path, false });
                if (snapshot == null || dependencies.GetValue(snapshot) == null)
                    throw new InvalidDataException("Missing dependency table");
                MtpReflection.CopyPackages(snapshot, result, true);
                result.detail = "旧版 MTP 文件缓存快照；可能滞后于实际安装状态，未逐项校验磁盘。缓存修改时间：" +
                    File.GetLastWriteTimeUtc(path).ToString("o");
            }
            catch (Exception)
            {
                result.packages.Clear();
                result.state = "error";
                result.detail = "旧版 MTP 依赖缓存读取失败，文件可能损坏、正在写入或结构不兼容；未修改缓存，可稍后刷新重试";
            }
        }
    }
}
#endif
