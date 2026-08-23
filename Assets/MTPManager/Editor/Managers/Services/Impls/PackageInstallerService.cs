using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    using MTPRequest = IPackageInstallerService.MTPackageInstallerRequest;
    using LM = LogManager;
    
    public class PackageInstallerService : IPackageInstallerService
    {
        private IPluginPackageService PluginPackage => ServiceLocator.Get<IPluginPackageService>();
        private IMTPackageInfoService MTPackage => ServiceLocator.Get<IMTPackageInfoService>();
        private IPluginAssetService PluginAsset => ServiceLocator.Get<IPluginAssetService>();
        private IPackageJsonService PackageJson => ServiceLocator.Get<IPackageJsonService>();

        public List<MTPRequest> LoadInstallerRequest(List<MTPackageInfo> addList,
            List<MTPackageInfo> removeList)
        {
            var requests = new List<MTPRequest>();
            addList ??= new List<MTPackageInfo>();
            removeList ??= new List<MTPackageInfo>();

            var addProjectIds = new HashSet<int>();
            foreach (var package in addList)
            {
                if (package != null && package.ProjectId > 0)
                {
                    addProjectIds.Add(package.ProjectId);
                }
            }

            var removeProjectIds = new HashSet<int>();
            foreach (var package in removeList)
            {
                if (package != null && package.ProjectId > 0)
                {
                    removeProjectIds.Add(package.ProjectId);
                }
            }

            // 先 Remove / UpdateRemove，再 Add / UpdateAdd，避免同包先装新版再被卸掉。
            // 同 ProjectId 且同 Tag：只发 UpdateAdd（按新 hash 再 Add），不卸旧包。
            foreach (var package in removeList)
            {
                if (package == null)
                {
                    continue;
                }

                if (string.Equals(package.PackageName, MTPGit.SELF_PACKAGE_NAME, StringComparison.Ordinal))
                {
                    continue;
                }

                bool isUpdate = package.ProjectId > 0 && addProjectIds.Contains(package.ProjectId);
                if (isUpdate)
                {
                    var matchingAdd = FindByProjectId(addList, package.ProjectId);
                    if (matchingAdd != null
                        && string.Equals(matchingAdd.Tag, package.Tag, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                requests.Add(new MTPRequest
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

                bool isUpdate = package.ProjectId > 0 && removeProjectIds.Contains(package.ProjectId);
                requests.Add(new MTPRequest
                {
                    RequestType = isUpdate ? RequestType.UpdateAdd : RequestType.Add,
                    RequestPackageInfo = package,
                });
            }

            return requests;
        }

        private static MTPackageInfo FindByProjectId(List<MTPackageInfo> packages, int projectId)
        {
            if (packages == null || projectId <= 0)
            {
                return null;
            }

            foreach (var package in packages)
            {
                if (package != null && package.ProjectId == projectId)
                {
                    return package;
                }
            }

            return null;
        }
        
        public async Task StartHandlingInstallerRequest(List<MTPRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                await Task.CompletedTask;
                return;
            }
            int totalCount = requests.Count;
            int finishedCount = 0;
            
            // 执行编译锁，并 Finally 解锁
            EditorApplication.LockReloadAssemblies();
            try
            {
                // 执行 UPM 请求
                foreach (var request in requests)
                {
                    EditorUtility.DisplayProgressBar(
                        title: $"MTP Installer",
                        info: $"Now handling No.{finishedCount+1} of {totalCount} requests in total...",
                        progress: (float)finishedCount / totalCount);
                    
                    // 如果为 Add 请求，则先添加assets
                    if (request.RequestType == RequestType.Add)
                    {
                        // 处理插件导入：PreparePluginsAsync 负责下载/发现插件并写入 Plugins 字典，
                        // PluginImportWindow 内部已经包含冲突检测/勾选 UI/实际导入/记录，这里不用再重复展示
                        bool pluginReady = await HandlePluginImportForAdd(request.RequestPackageInfo);
                        if (!pluginReady)
                        {
                            // 用户取消视为“安装 0 个插件”，不会走到这里；这里只会是真正的准备/导入出错，
                            // 已经在 HandlePluginImportForAdd 内静默回滚过，直接跳过本次 UPM 请求
                            request.RequestState = RequestState.Failure;
                            finishedCount++;
                            continue;
                        }
                    }
                    
                    // 根据 Add / Remove 请求类型，分发给UPM不同的request
                    int timeoutCount = 0;
                    bool timedOut = false;
                    var req = request.StartRequest();
                    while (!req.IsCompleted)
                    {
                        if (timeoutCount >= MTPTime.INSTALLATION_TIMEOUT)
                        {
                            // 安装超时
                            timedOut = true;
                            break;
                        }
                        await Task.Delay(MTPTime.INSTALLATION_CHECK_TIME);
                        timeoutCount += MTPTime.INSTALLATION_CHECK_TIME;
                    }

                    request.RequestState = !timedOut && req.Status == StatusCode.Success
                        ? RequestState.Success
                        : RequestState.Failure;

                    // 安装错误提示
                    if (request.RequestState == RequestState.Failure)
                    {
                        var reason = timedOut ? "超时" : req.Error?.message ?? "未知错误";
                        LM.LogError(
                            $"[PackageInstallerService] {request.RequestType} 请求失败: " +
                            $"{request.RequestPackageInfo?.PackageName}, 原因: {reason}");
                    }

                    // Add：UPM 成功了才写入本地持久化本次插件准备/导入的结果；失败则静默回滚，不留下半安装状态
                    if (request.RequestType == RequestType.Add)
                    {
                        if (request.RequestState == RequestState.Success)
                        {
                            HandlePackageAddSucceeded(request.RequestPackageInfo);
                        }
                        else
                        {
                            RollbackPluginImport(request.RequestPackageInfo);
                        }
                    }

                    // 处理宏定义：按 taginfo（projectId + tag）上的 scriptDefineSymbols，逻辑同旧工程
                    if (request.RequestState == RequestState.Success)
                    {
                        if (request.RequestType == RequestType.Add || request.RequestType == RequestType.UpdateAdd)
                        {
                            ApplyScriptDefineSymbols(request.RequestPackageInfo, add: true);
                        }
                        else if (request.RequestType == RequestType.Remove || request.RequestType == RequestType.UpdateRemove)
                        {
                            ApplyScriptDefineSymbols(request.RequestPackageInfo, add: false);
                        }
                    }
                    
                    // Remove：只有 UPM 卸载成功才处理插件卸载；失败则包还在装着，不能动插件
                    if (request.RequestType == RequestType.Remove && request.RequestState == RequestState.Success)
                    {
                        // 与旧工程一致：汇总本次会删除的全部资源路径，弹一次 DisplayDialog 整体确认
                        PluginPackage.RemoveImportedPlugins(request.RequestPackageInfo);
                    }
                    
                    finishedCount++;
                    EditorUtility.ClearProgressBar(); // 清理当前一轮的安装进度
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar(); // 清理本次try内可能产生的进度条
                EditorApplication.UnlockReloadAssemblies(); // 解锁编译锁
            }
        }

        /// <summary>
        /// Add 请求里处理插件导入的完整步骤：准备（下载/发现） -> 弹窗勾选并导入。
        /// 期间不持久化到本地（persist:false），写盘工作统一交给 UPM 请求成功后的后续方法
        /// 用户取消 / 未勾选任何插件视为"安装 0 个插件"，返回 true，不影响后续 UPM 安装；
        /// 只有真正的准备失败或导入失败才返回 false，且已经在内部静默回滚（不弹窗）。
        /// </summary>
        private async Task<bool> HandlePluginImportForAdd(MTPackageInfo package)
        {
            bool prepared = await PluginPackage.PreparePluginsAsync(package, persist: false);
            if (!prepared)
            {
                LM.LogError($"[PackageInstallerService] 插件准备失败，回滚: {package?.PackageName}");
                PluginPackage.RemoveImportedPlugins(package, showConfirmDialog: false);
                return false;
            }

            if (package.Plugins == null || package.Plugins.Count == 0)
            {
                return true; // 该包没有插件，视为“安装 0 个插件”
            }

            bool imported = await PluginImportWindow.ShowWindowAsync(package, persist: false);
            if (!imported)
            {
                LM.LogError($"[PackageInstallerService] 插件导入失败，回滚: {package?.PackageName}");
                PluginPackage.RemoveImportedPlugins(package, showConfirmDialog: false);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 包安装成功的后续处理：此时才把本次准备/导入的插件状态统一落盘。
        /// </summary>
        private void HandlePackageAddSucceeded(MTPackageInfo package)
        {
            MTPackage.SaveDependenciesToLocal();
            PluginAsset.SaveToLocal();
            LM.Log($"[PackageInstallerService] 包安装成功，插件状态已落盘: {package?.PackageName}");
        }

        /// <summary>
        /// 按 package 的 ProjectId + Tag 取 taginfo，把 scriptDefineSymbols（分号分隔）写入 / 移出 PlayerSettings。
        /// taginfo 缺失或字段为空则跳过，与旧 OnAdd/OnRemoveScriptDefineSymbols 一致。
        /// </summary>
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
        /// UPM 添加失败后的回滚：静默移除本次已导入的插件（不弹窗），避免遗留一份没有对应包的插件资源。
        /// 该包没有导入过任何插件时，RemoveImportedPlugins 内部会直接判空返回，调用无副作用。
        /// </summary>
        private void RollbackPluginImport(MTPackageInfo package)
        {
            PluginPackage.RemoveImportedPlugins(package, showConfirmDialog: false);
        }
    }
}
