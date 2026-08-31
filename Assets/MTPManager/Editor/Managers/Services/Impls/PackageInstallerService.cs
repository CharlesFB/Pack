using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PackageInstallerService : IPackageInstallerService
    {
        private IPluginPackageService PluginPackage => ServiceLocator.Get<IPluginPackageService>();
        private IPackageJsonService PackageJson => ServiceLocator.Get<IPackageJsonService>();

        public List<MTPackageInstallerRequest> LoadInstallerRequest(List<MTPackageInfo> addList,
            List<MTPackageInfo> removeList)
        {
            var requests = new List<MTPackageInstallerRequest>();
#if UNITY_2020_2_OR_NEWER
            addList ??= new List<MTPackageInfo>();
            removeList ??= new List<MTPackageInfo>();
#else
            if (addList == null)
            {
                addList = new List<MTPackageInfo>();
            }

            if (removeList == null)
            {
                removeList = new List<MTPackageInfo>();
            }
#endif

            var addNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var package in addList)
            {
                if (package != null && !string.IsNullOrEmpty(package.PackageName))
                {
                    addNames.Add(package.PackageName);
                }
            }

            var removeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var package in removeList)
            {
                if (package != null && !string.IsNullOrEmpty(package.PackageName))
                {
                    removeNames.Add(package.PackageName);
                }
            }

            // 先 Remove / UpdateRemove，再 Add / UpdateAdd，避免同包先装新版再被卸掉。
            // 同一 PackageName 且同 Tag，只发 UpdateAdd（按新 hash 再 Add），不卸旧包。
            // 同一 GitLab 工程可以有多个包。
            foreach (var package in removeList)
            {
                if (package == null)
                {
                    continue;
                }

#if !PACKAGE_DEV
                if (string.Equals(package.PackageName, MTPGit.SELF_PACKAGE_NAME, StringComparison.Ordinal))
                {
                    continue;
                }
#endif

                bool isUpdate = !string.IsNullOrEmpty(package.PackageName)
                                && addNames.Contains(package.PackageName);
                if (isUpdate)
                {
                    var matchingAdd = FindByPackageName(addList, package.PackageName);
                    if (matchingAdd != null
                        && string.Equals(matchingAdd.Tag, package.Tag, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                requests.Add(new MTPackageInstallerRequest
                {
                    RequestType = isUpdate ? RequestType.UpdateRemove : RequestType.Remove,
                    RequestPackageInfo = package,
                });
            }

            foreach (var package in addList)
            {
                if (package == null)
                {
                    continue;
                }

                bool isUpdate = !string.IsNullOrEmpty(package.PackageName)
                                && removeNames.Contains(package.PackageName);
                requests.Add(new MTPackageInstallerRequest
                {
                    RequestType = isUpdate ? RequestType.UpdateAdd : RequestType.Add,
                    RequestPackageInfo = package,
                });
            }

            return requests;
        }

        private static MTPackageInfo FindByPackageName(List<MTPackageInfo> packages, string packageName)
        {
            if (packages == null || string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            foreach (var package in packages)
            {
                if (package != null
                    && string.Equals(package.PackageName, packageName, StringComparison.Ordinal))
                {
                    return package;
                }
            }

            return null;
        }

        public async Task StartHandlingInstallerRequest(List<MTPackageInstallerRequest> requests)
        {
            EditorUtility.ClearProgressBar(); // 必要的进度条清理
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            var state = new InstallerProgressState
            {
                RestRequests = new List<MTPackageInstallerRequest>(requests),
                CompletedCount = 0,
                CurrentUpmDone = false,
            };
            SaveProgress(state);
            await ProcessProgressStateAsync(state);
        }

        public void ClearInterruptedProgress()
        {
            DeleteProgress();
        }

        public async Task ResumeInterruptedInstallAsync()
        {
            EditorUtility.ClearProgressBar(); // 必要的进度条清理
            var state = LoadProgress();
            if (state == null || state.RestRequests == null || state.RestRequests.Count == 0)
            {
                LM.TimeLog("[PackageInstallerService] 恢复中断安装： 检查已经全部安装完成，正在清理安装缓存");
                DeleteProgress();
                return;
            }

            LM.TimeLog(
                $"[PackageInstallerService] 恢复中断安装. completed:{state.CompletedCount}, " +
                $"rest:{state.RestRequests.Count}, currentUpmDone:{state.CurrentUpmDone}" + 
                $"upcomingPackage:{state.RestRequests[0].RequestPackageInfo.PackageName}");
            await ProcessProgressStateAsync(state);
        }

        private async Task ProcessProgressStateAsync(InstallerProgressState state)
        {
            bool lockEntire = ShouldLockEntireProcess(state.RestRequests);
            if (lockEntire)
            {
                EditorApplication.LockReloadAssemblies();
            }

            try
            {
                while (state.RestRequests != null && state.RestRequests.Count > 0)
                {
                    var request = state.RestRequests[0];
                    if (request == null)
                    {
                        AdvanceToNextRequest(state);
                        continue;
                    }

                    UpdateProgressBar(state);

                    if (!state.CurrentUpmDone)
                    {
                        bool upmOk = await WaitUpmRequestAsync(request);
                        request.RequestState = upmOk ? RequestState.Success : RequestState.Failure;
                        if (!upmOk)
                        {
                            LM.LogError(
                                $"[PackageInstallerService] {request.RequestType} 失败: " +
                                $"{request.RequestPackageInfo?.PackageName}");
                            AdvanceToNextRequest(state);
                            continue;
                        }

                        // 节点1: 当前包 UPM 已完成，资源处理尚未开始
                        state.CurrentUpmDone = true;
                        SaveProgress(state);
                        UpdateProgressBar(state);
                    }
                    else
                    {
                        // 从进度恢复，节点 1 已写入，视为 UPM 成功
                        request.RequestState = RequestState.Success;
                    }

                    await HandleResourcesAsync(request, state);
                    AdvanceToNextRequest(state);
                }

                DeleteProgress();
                MTPackageManagerWindow.RefreshOpenWindows();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (lockEntire)
                {
                    EditorApplication.UnlockReloadAssemblies();
                }
            }
        }

        /// <summary>
        /// 节点2: 资源处理结束（或 UPM 失败跳过资源）。弹出 rest[0]，CompletedCount++，CurrentUpmDone=false。
        /// </summary>
        private static void AdvanceToNextRequest(InstallerProgressState state)
        {
            if (state.RestRequests != null && state.RestRequests.Count > 0)
            {
                state.RestRequests.RemoveAt(0);
            }

            state.CurrentUpmDone = false;
            // ================ 控制插件恢复选择 ==================
            state.PluginSelectionDone = false;
            state.PluginSelectionConfirmed = false;
            state.SelectedPluginKeys = new List<string>();
            state.ZipExtractDirs = new Dictionary<string, string>();
            // =================================================
            state.PluginsImportDone = false;
            state.CompletedCount++;
            SaveProgress(state);
        }

        private static async Task<bool> WaitUpmRequestAsync(MTPackageInstallerRequest request)
        {
            var req = request?.StartRequest();
            if (req == null)
            {
                return false;
            }

            int timeoutCount = 0;
            while (!req.IsCompleted)
            {
                if (timeoutCount >= MTPTime.INSTALLATION_TIMEOUT)
                {
                    return false;
                }

                await Task.Delay(MTPTime.INSTALLATION_CHECK_TIME);
                timeoutCount += MTPTime.INSTALLATION_CHECK_TIME;
            }

            return req.Status == StatusCode.Success;
        }

        /// <summary>
        /// UPM 成功后资源处理。
        /// Add：插件选择/导入 → 加宏 → 空闲。
        /// Remove：去宏 → 卸插件资源 → 空闲。
        /// UpdateAdd 只加宏；UpdateRemove 不处理插件/宏。
        /// </summary>
        private async Task HandleResourcesAsync(
            MTPackageInstallerRequest request, InstallerProgressState state)
        {
            if (request == null || request.RequestState != RequestState.Success)
            {
                return;
            }

            var package = request.RequestPackageInfo;
            // await EditorBusyWait.WaitUntilIdleAsync();
            
            if (request.RequestType == RequestType.Add)
            {
                await ImportPluginsAsync(package, state);
                // await EditorBusyWait.WaitUntilIdleAsync();
            }

            bool applyDefines = request.RequestType == RequestType.Add
                                || request.RequestType == RequestType.UpdateAdd
                                || request.RequestType == RequestType.Remove;
            if (applyDefines)
            {
                bool addDefines = request.RequestType != RequestType.Remove;
                ApplyScriptDefineSymbols(package, add: addDefines);
                // await EditorBusyWait.WaitUntilIdleAsync();
            }

            if (request.RequestType == RequestType.Remove)
            {
                PluginPackage.RemoveImportedPlugins(package);
                // await EditorBusyWait.WaitUntilIdleAsync();
            }
            await EditorBusyWait.WaitUntilIdleAsync();
        }

        private async Task ImportPluginsAsync(MTPackageInfo package, InstallerProgressState state)
        {
            if (state != null && state.PluginsImportDone)
            {
                LM.Log($"[PackageInstallerService] 插件阶段已完成，跳过: {package?.PackageName}");
                return;
            }

            bool prepared = await PluginPackage.PreparePluginsAsync(package, persist: true);
            if (!prepared)
            {
                LM.LogError($"[PackageInstallerService] 插件准备失败（包已安装，不回滚 UPM）: {package?.PackageName}");
                MarkPluginsImportDone(state);
                return;
            }

            if (package?.Plugins == null || package.Plugins.Count == 0)
            {
                MarkPluginsImportDone(state);
                return;
            }
            
            LM.Log($"[PackageInstallerService] 插件准备成功，已写入依赖树。package: {package?.PackageName}");

            // ===========================  控制插件选择恢复 ================================
            PluginImportWindow.PluginImportSelection selection;
            if (state != null && state.PluginSelectionDone)
            {
                LM.Log($"[PackageInstallerService] 恢复已保存的插件选择，不再弹窗: {package.PackageName}");
                selection = new PluginImportWindow.PluginImportSelection
                {
                    Confirmed = state.PluginSelectionConfirmed,
                    SelectedKeys = state.SelectedPluginKeys ?? new List<string>(),
                    ZipExtractDirs = state.ZipExtractDirs ?? new Dictionary<string, string>(),
                };
            }
            else
            {
                var selectionTask = PluginImportWindow.ShowWindowAsync(package);
                await EditorBusyWait.WaitUntilTaskAsync(selectionTask);
                selection = selectionTask.Status == TaskStatus.RanToCompletion
                    ? selectionTask.Result
                    : null;
                PersistPluginSelection(state, selection);
            }
            // ============================================================================
            
            if (selection == null || !selection.Confirmed
                                  || selection.SelectedKeys == null || selection.SelectedKeys.Count == 0)
            {
                LM.Log($"[PackageInstallerService] 用户未导入插件（取消或未勾选）: {package.PackageName}");
                MarkPluginsImportDone(state);
                return;
            }

            var importTask = PluginPackage.ImportPreparedPluginsAsync(
                package, selection.SelectedKeys, selection.ZipExtractDirs,
                persist: true, skipRecorded: true);
            await EditorBusyWait.WaitUntilTaskAsync(importTask);
            
            bool imported = importTask.Status == TaskStatus.RanToCompletion && importTask.Result;
            if (!imported)
            {
                LM.LogError($"[PackageInstallerService] 插件导入失败: {package?.PackageName}");
            }

            MarkPluginsImportDone(state);
        }

        private static void PersistPluginSelection(
            InstallerProgressState state, PluginImportWindow.PluginImportSelection selection)
        {
            if (state == null)
            {
                return;
            }
        
            state.PluginSelectionDone = true;
            state.PluginSelectionConfirmed = selection != null && selection.Confirmed;
            state.SelectedPluginKeys = selection?.SelectedKeys != null
                ? new List<string>(selection.SelectedKeys)
                : new List<string>();
            state.ZipExtractDirs = selection?.ZipExtractDirs != null
                ? new Dictionary<string, string>(selection.ZipExtractDirs)
                : new Dictionary<string, string>();
            SaveProgress(state);
        }

        private static void MarkPluginsImportDone(InstallerProgressState state)
        {
            if (state == null)
            {
                return;
            }

            state.PluginsImportDone = true;
            SaveProgress(state);
        }

        private void ApplyScriptDefineSymbols(MTPackageInfo package, bool add)
        {
            if (package == null || package.ProjectId <= 0 || string.IsNullOrEmpty(package.Tag))
            {
                return;
            }

            var tagInfo = PackageJson.Get(package.ProjectId.ToString(), package.Tag);
            var symbols = tagInfo?.scriptDefineSymbols;
            if (string.IsNullOrEmpty(symbols))
            {
                return;
            }

            foreach (var symbol in symbols.Split(';'))
            {
                if (string.IsNullOrEmpty(symbol))
                {
                    continue;
                }

                if (add)
                {
                    ScriptDefineUtils.TryToAddScriptDefineSymbol(symbol);
                }
                else
                {
                    ScriptDefineUtils.TryToRemoveScriptDefineSymbol(symbol);
                }
            }

            LM.Log(add
                ? $"[PackageInstallerService] 添加 scriptDefineSymbols: {package.PackageName} -> {symbols}"
                : $"[PackageInstallerService] 移除 scriptDefineSymbols: {package.PackageName} -> {symbols}");
        }

        /// <summary>
        /// 与旧工程一致：仅当本批次全部为本地内嵌包时，才对安装循环全程加锁。
        /// UPM Client.Add/Remove 无法被这把锁拦住域重载。
        /// </summary>
        private static bool ShouldLockEntireProcess(List<MTPackageInstallerRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                return false;
            }

            foreach (var request in requests)
            {
                if (request?.RequestPackageInfo == null || !request.RequestPackageInfo.isLocal)
                {
                    return false;
                }
            }

            return true;
        }

        private static void UpdateProgressBar(InstallerProgressState state)
        {
            int rest = state.RestRequests?.Count ?? 0;
            int done = state.CompletedCount;
            int total = done + rest;
            float progress = total > 0 ? (float) done / total : 1f;
            string name = rest > 0
                ? state.RestRequests[0]?.RequestPackageInfo?.PackageName ?? "package"
                : "package";
            string stage = state.CurrentUpmDone ? "macros/plugins" : "UPM";
            EditorUtility.DisplayProgressBar(
                "MTP Installer",
                $"{name} [{stage}] {done}/{total}",
                progress);
        }

        private static void SaveProgress(InstallerProgressState state)
        {
            JsonUtils.SaveToFile(MTPPath.INSTALLER_PROGRESS_PATH, state);
        }

        private static InstallerProgressState LoadProgress()
        {
            return JsonUtils.LoadFromFile<InstallerProgressState>(MTPPath.INSTALLER_PROGRESS_PATH,
                createIfNotExist: false);
        }

        private static void DeleteProgress()
        {
            if (File.Exists(MTPPath.INSTALLER_PROGRESS_PATH))
            {
                File.Delete(MTPPath.INSTALLER_PROGRESS_PATH);
            }
        }
    }
}