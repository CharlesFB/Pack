using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// "接口-实现"服务注册工具
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>
        /// 注册服务
        /// </summary>
        /// <typeparam name="T">服务的接口类型</typeparam>
        /// <param name="serviceInstance">实现该接口的具体实例</param>
        public static void Register<T>(T serviceInstance)
        {
            var type = typeof(T);
            
            // 防止注册空对象
            if (serviceInstance == null)
            {
                LM.LogError($"[ServiceLocator] 尝试注册的服务 {type.Name} 实例为空！");
                return;
            }

            // 防止重复注册
            if (_services.ContainsKey(type))
            {
                LM.LogWarning($"[ServiceLocator] 服务 {type.Name} 已经被注册过，正在覆盖。请检查初始化逻辑是否重复执行！");
            }

            _services[type] = serviceInstance;
        }

        /// <summary>
        /// 获取服务 (严格模式)
        /// 如果找不到服务，会抛出异常，强制暴露初始化顺序问题
        /// </summary>
        public static T Get<T>()
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var service))
            {
                return (T)service;
            }

            // 注册顺序导致的依赖错误提示，方便定位 Bug
            throw new InvalidOperationException($"[ServiceLocator] 找不到服务 {type.Name}！请确认它是否在项目启动时被正确注册。");
        }

        /// <summary>
        /// 获取服务 (安全模式)
        /// 找不到时返回 false，不抛异常
        /// </summary>
        public static bool TryGet<T>(out T service)
        {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var obj))
            {
                service = (T)obj;
                return true;
            }

            service = default;
            return false;
        }

        /// <summary>
        /// 注销所有服务 (主要用于 Unity 域重载或退出时清理)
        /// </summary>
        public static void Clear()
        {
            // 如果 Service 实现了 IDisposable 接口，可以在这里统一调用 Dispose() 释放资源
            foreach (var service in _services.Values)
            {
                if (service is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _services.Clear();
        }
    }
}