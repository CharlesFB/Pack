#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;

namespace ServiceBase.UnityBridge
{
    internal static class MtpReflection
    {
        internal const string Prefix = "MagicTavern.FrontEngine.MTPManager.";
        /// <summary>
        /// 在已加载程序集中查找 MTP 类型，避免直接编译依赖。
        /// </summary>
        internal static Type Find(
            string shortName // MTP 类型短名称
        )
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(Prefix + shortName, false);
                if (type != null) return type;
            }
            return null;
        }

        /// <summary>
        /// 反射读取指定字段。
        /// </summary>
        internal static object Field(
            object instance, // 包含目标字段的对象
            string name // 目标字段名称
        )
        {
            if (instance == null) throw new InvalidOperationException("Null MTP object");
            var field = instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(name);
            return field.GetValue(instance);
        }

        /// <summary>
        /// 复制新旧 MTP 依赖数据；旧版 Revision 作为 tag，依赖地址仅保留标签。
        /// </summary>
        internal static void CopyPackages(
            object snapshot, // 当前工程快照
            MtpSnapshot result, // 用于写入读取结果的快照
            bool legacy // 是否按旧版字段语义读取
        )
        {
            var packages = Field(snapshot, "Dependencies") as IDictionary;
            if (packages == null) throw new InvalidOperationException("Invalid dependency dictionary");
            foreach (DictionaryEntry entry in packages)
            {
                if (entry.Value == null) continue;
                object info = entry.Value;
                var package = new MtpPackageSnapshot
                {
                    name = (string)Field(info, "PackageName"),
                    // Legacy Revision is a tag, not a commit SHA.
                    tag = (string)Field(info, legacy ? "Revision" : "Tag") ?? "",
                    revision = legacy ? "" : (string)Field(info, "Revision") ?? "",
                    depth = (int)Field(info, "Depth"),
                    is_local = (bool)Field(info, "isLocal")
                };
                if (string.IsNullOrEmpty(package.name)) package.name = (string)entry.Key;
                var dependencies = Field(info, "Dependencies") as IDictionary;
                if (dependencies != null)
                    foreach (DictionaryEntry dependency in dependencies)
                    {
                        // Discard URLs entirely; they can include credentials.
                        string reference = dependency.Value as string ?? "";
                        int hash = reference.LastIndexOf('#');
                        package.dependencies.Add(new MtpDependencySnapshot
                        {
                            name = (string)dependency.Key,
                            requested_tag = hash >= 0 ? reference.Substring(hash + 1) : ""
                        });
                    }
                package.dependencies.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                result.packages.Add(package);
            }
            result.packages.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            result.state = "ready";
            result.detail = (legacy ? "旧版 MTP" : "MTP") + " 内存依赖快照；不代表逐项磁盘安装校验";
        }
    }
}
#endif
