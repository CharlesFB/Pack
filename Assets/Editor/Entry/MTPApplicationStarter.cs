using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 初始化 MTP 服务注册，初始化内存状态，恢复被意外中断的安装流程，检查清单项是否均成功安装
    /// 执行顺序：
    /// 1. [InitializeOnLoad] 注册服务
    /// 2. [InitializeOnLoadMethod] Editor 首次启动拉取 ProjectInfoList / PackageInfoCache（from project 647）
    /// 3. [DidReloadScripts(0)] 同步装载本地内存
    /// 4. [DidReloadScripts(1)] 冷启动->中断恢复->清单同步（串行 await）
    /// </summary>
    [InitializeOnLoad]
    public static class MTPApplicationStarter
    {
        static MTPApplicationStarter()
        {
            ServiceManager.Initialize();
        }

        /// <summary>
        /// Editor 第一次启动时执行：从 647 拉取 list/cache 文件到本地与内存（每会话一次）。
        /// </summary>
        [InitializeOnLoadMethod]
        public static async void EditorFirstStartup()
        {
            if (SessionState.GetBool(SessionKey.EDITOR_FIRST_STARTUP, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey.EDITOR_FIRST_STARTUP, true);

            try
            {
                var packageInfo = ServiceLocator.Get<IPackageInfoService>();
                var projectInfo = ServiceLocator.Get<IProjectInfoService>();

                await projectInfo.EnsureInitializedAsync();
                await packageInfo.EnsureInitializedAsync();

                if (projectInfo.NeedUpdate)
                {
                    await projectInfo.PullFromGitAsync();
                }

                if (packageInfo.NeedUpdate)
                {
                    await packageInfo.PullFromGitAsync();
                }

                LM.Log(
                    $"[MTPApplicationStarter] EditorFirstStartup done. " +
                    $"projects:{projectInfo.GetAll().Count}, " +
                    $"packageCacheLastUpdateDatetime:{packageInfo.RefreshTime}, " +
                    $"projectListLastUpdateDatetime:{projectInfo.RefreshTime}");
            }
            catch (Exception e)
            {
                LM.LogError($"[MTPApplicationStarter] EditorFirstStartup failed: {e}");
                SessionState.SetBool(SessionKey.EDITOR_FIRST_STARTUP, false);
            }
        }

        /// <summary>
        /// 纯静态地从本地持久化文件写入内存（不含需 Git 的远端缓存）。
        /// </summary>
        [DidReloadScripts(0)]
        public static void StaticMemoryLoader()
        {
            var mtPackageInfo = ServiceLocator.Get<IMTPackageInfoService>();
            var packageManifest = ServiceLocator.Get<IPackageManifestService>();
            var packagesLock = ServiceLocator.Get<IPackagesLockService>();
            var pluginAssetList = ServiceLocator.Get<IPluginAssetService>();
            var projectInfo = ServiceLocator.Get<IProjectInfoService>();
            var packageInfo = ServiceLocator.Get<IPackageInfoService>();
            var rootPackage = ServiceLocator.Get<IRootPackageService>();
            var localPackage = ServiceLocator.Get<ILocalPackageService>();

            packageManifest.LoadFromLocal();
            packagesLock.LoadFromLocal();
            pluginAssetList.LoadFromLocal();
            mtPackageInfo.LoadDependenciesFromLocal();
            projectInfo.LoadFromLocal();
            packageInfo.LoadFromLocal();
            rootPackage.LoadFromLocal();
            localPackage.LoadFromLocal();

            LM.Log(
                $"[MTPApplicationStarter] Memory loaded. " +
                $"deps:{mtPackageInfo.GetAllPackages().Count}, " +
                $"projects:{projectInfo.GetAll().Count}, " +
                $"pluginAssets:{pluginAssetList.GetAll().Count}");
        }

        /// <summary>
        /// 冷启动 -> 中断 Recovery -> SynchronizeManifest。
        /// </summary>
        [DidReloadScripts(1)]
        public static async void OnScriptsReloaded()
        {
            try
            {
                await ColdStartCheckAsync();
                await InterruptedRecoveryAsync();
                await SynchronizeManifestAsync();
            }
            catch (Exception e)
            {
                LM.LogError($"[MTPApplicationStarter] OnScriptReloaded failed: {e.Message}");
            }
        }

        /// <summary>
        /// 冷启动占位
        /// </summary>
        private static async Task ColdStartCheckAsync()
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// 中断恢复：续跑被域重载打断的安装流程。
        /// </summary>
        private static async Task InterruptedRecoveryAsync()
        {
            // TODO: 接入 PackageInstaller 中断恢复
            await Task.CompletedTask;
            LM.Log("[MTPApplicationStarter] InterruptedRecovery done.");
        }

        /// <summary>
        /// 清单同步：对齐 manifest / 依赖图 / 已安装列表。
        /// </summary>
        private static async Task SynchronizeManifestAsync()
        {
            // TODO: 接入 SyncManifest
            await Task.CompletedTask;
            LM.Log("[MTPApplicationStarter] SynchronizeManifest done.");
        }
    }
}
