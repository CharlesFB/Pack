// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 依赖树重建 + UPM/本地 diff + 安装编排；Reset 卸载全部 MTP 包
// ==================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Editor.Data.Models;
using UnityEditor;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PackageSyncService : IPackageSyncService
    {
        private IMTPackageInfoService MTPackage => ServiceLocator.Get<IMTPackageInfoService>();
        private ILocalPackageService LocalPackage => ServiceLocator.Get<ILocalPackageService>();
        private IRootPackageService RootPackage => ServiceLocator.Get<IRootPackageService>();
        private IManifestJsonService Manifest => ServiceLocator.Get<IManifestJsonService>();
        private IPackageInstallerService Installer => ServiceLocator.Get<IPackageInstallerService>();
        private IProjectInfoService ProjectInfo => ServiceLocator.Get<IProjectInfoService>();
        private IPackageJsonService PackageJson => ServiceLocator.Get<IPackageJsonService>();
        private IPluginAssetService PluginAsset => ServiceLocator.Get<IPluginAssetService>();

        public async Task<bool> SyncManifestAsync(bool? installToLocal = null)
        {
            bool toLocal = installToLocal ?? ConfigManager.Instance.Current?.InstallToLocal ?? false;
            LM.Log($"[PackageSyncService] 开始同步清单并收集安装器安装卸载请求. installToLocal:{toLocal}");

            var previousTree = MTPackage.ExportToMTPackageInfoList();

            try
            {
                await MTPackage.RebuildFromRootsAsync(toLocal);

                Manifest.LoadFromLocal();
                LocalPackage.LoadFromLocal();

                var addList = new List<MTPackageInfo>();
                var removeList = new List<MTPackageInfo>();
                if (toLocal)
                {
                    CollectLocalAddPackage(addList);
                    CollectLocalRemovePackage(removeList);
                    CollectLocalUpdatePackage(removeList);
                }
                else
                {
                    CollectUnityAddPackage(addList);
                    await CollectUnityRemovePackage(previousTree, removeList);
                    CollectUnityUpdatePackage(previousTree, removeList);
                }

                LM.Log(
                    $"[PackageSyncService] 同步清单、收集整理依赖完成，installToLocal:{toLocal}, " +
                    $"depTree:{MTPackage.GetAllPackages().Count}, add:{addList.Count}, remove:{removeList.Count}");

                var requests = Installer.LoadInstallerRequest(addList, removeList);
                if (requests != null && requests.Count > 0)
                {
                    await Installer.StartHandlingInstallerRequest(requests);
                    if (!AllRequestsSucceeded(requests))
                    {
                        LM.LogWarning("[PackageSyncService] SyncManifest 安装器未全部成功，回滚依赖树快照");
                        MTPackage.LoadFromMTPackageInfoList(previousTree);
                        return false;
                    }
                }

                MTPackage.SaveDependenciesToLocal();
                if (toLocal)
                {
                    CommitLocalSnapshot(addList, removeList);
                }

                Manifest.LoadFromLocal();
                AssetDatabase.Refresh();
                LM.Log("[PackageSyncService] 同步清单完成");
                return true;
            }
            catch (Exception e)
            {
                LM.LogError($"[PackageSyncService] SyncManifest 异常，回滚依赖树快照: {e}");
                MTPackage.LoadFromMTPackageInfoList(previousTree);
                return false;
            }
        }

        public async Task<bool> ResetAsync(bool changeInstallMode = false)
        {
            Installer.ClearInterruptedProgress();

            // 只清根包并落盘。依赖树与 upm-local 留给 SyncManifest 做卸载快照；
            // 先清它们会导致本地模式卸不掉已内嵌包，UPM 模式也无法用旧树还原插件信息。
            RootPackage.Clear();
            RootPackage.SaveToLocal();

            bool currentLocal = ConfigManager.Instance.Current?.InstallToLocal ?? false;
            bool installToLocal = changeInstallMode ? !currentLocal : currentLocal;
            bool ok = await SyncManifestAsync(installToLocal);
            if (!ok)
            {
                LM.LogWarning("[PackageSyncService] Reset 同步失败，缓存状态可能需要手动刷新");
                return false;
            }

            ProjectInfo.Clear();
            ProjectInfo.SaveToLocal();
            PackageJson.Clear();
            PackageJson.SaveToLocal();
            LocalPackage.Clear();
            LocalPackage.SaveToLocal();
            PluginAsset.Clear();
            PluginAsset.SaveToLocal();
            Installer.ClearInterruptedProgress();

            await ProjectInfo.PullFromGitAsync(force: true);
            await PackageJson.PullFromGitAsync(force: true);
            return true;
        }

        private void CollectUnityAddPackage(List<MTPackageInfo> addList)
        {
            foreach (var package in MTPackage.GetAllPackages())
            {
                if (package == null || string.IsNullOrEmpty(package.PackageName))
                {
                    continue;
                }

                if (!Manifest.Contains(package.PackageName) || !IsSameUnityInstall(package))
                {
                    addList.Add(package);
                }
            }
        }

        private async Task CollectUnityRemovePackage(MTPackageInfoList previousTree, List<MTPackageInfo> removeList)
        {
            foreach (var kvp in Manifest.GetGitPackages())
            {
                var packageName = kvp.Key;
                var packageUrl = kvp.Value;
                if (MTPackage.GetPackage(packageName) != null)
                {
                    continue;
                }

                if (string.Equals(packageName, MTPGit.SELF_PACKAGE_NAME, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsMagicTavernPackage(packageUrl))
                {
                    continue;
                }

                var depInfo = FindInTree(previousTree, packageName);
                if (depInfo == null)
                {
                    if (!Manifest.GetGitPackageInfo(packageName, out var url, out var revision))
                    {
                        LM.LogError($"[PackageSyncService] {packageName} 无法从 manifest 解析 url#revision");
                        continue;
                    }

                    depInfo = await MTPackage.FetchPackageInfoAsync(url, revision);
                    if (depInfo == null)
                    {
                        LM.LogError($"[PackageSyncService] {packageName} 无法还原卸载信息: {url}#{revision}");
                        continue;
                    }
                }

                removeList.Add(depInfo);
            }
        }

        /// <summary>
        /// 已在依赖树和 manifest 中、但 url/revision 不同：把旧节点放入 removeList，
        /// 与 addList 按 ProjectId 配对成 Update。同 Tag 时安装器只会发 UpdateAdd。
        /// </summary>
        private void CollectUnityUpdatePackage(MTPackageInfoList previousTree, List<MTPackageInfo> removeList)
        {
            var removeNames = new HashSet<string>();
            foreach (var existing in removeList)
            {
                if (existing != null && !string.IsNullOrEmpty(existing.PackageName))
                {
                    removeNames.Add(existing.PackageName);
                }
            }

            foreach (var package in MTPackage.GetAllPackages())
            {
                if (package == null || string.IsNullOrEmpty(package.PackageName))
                {
                    continue;
                }

                if (!Manifest.Contains(package.PackageName) || IsSameUnityInstall(package))
                {
                    continue;
                }

                if (removeNames.Contains(package.PackageName))
                {
                    continue;
                }

                var previous = FindInTree(previousTree, package.PackageName);
                if (previous == null)
                {
                    if (!Manifest.GetGitPackageInfo(package.PackageName, out var url, out var revision))
                    {
                        continue;
                    }

                    previous = new MTPackageInfo
                    {
                        PackageName = package.PackageName,
                        Url = UrlUtils.FormatUrl(url),
                        Revision = revision,
                        Tag = package.Tag,
                        ProjectId = package.ProjectId,
                        Depth = package.Depth,
                    };
                }

                // 本包也会进入 removeList 仅用于配对 UpdateAdd；LoadInstallerRequest 不会发出卸载请求。
                removeList.Add(previous);
                removeNames.Add(package.PackageName);
            }
        }

        private void CollectLocalAddPackage(List<MTPackageInfo> addList)
        {
            foreach (var package in MTPackage.GetAllPackages())
            {
                if (package == null || string.IsNullOrEmpty(package.PackageName))
                {
                    continue;
                }

                package.isLocal = true;
                var existing = LocalPackage.GetLocal(package.PackageName);
                if (existing == null || !IsSameLocalRevision(existing, package))
                {
                    addList.Add(package);
                }
            }
        }

        private void CollectLocalRemovePackage(List<MTPackageInfo> removeList)
        {
            foreach (var local in LocalPackage.GetAllLocals())
            {
                if (local == null || string.IsNullOrEmpty(local.PackageName))
                {
                    continue;
                }

                if (string.Equals(local.PackageName, MTPGit.SELF_PACKAGE_NAME, StringComparison.Ordinal))
                {
                    continue;
                }

                if (MTPackage.GetPackage(local.PackageName) == null)
                {
                    local.isLocal = true;
                    removeList.Add(local);
                }
            }
        }

        /// <summary>
        /// 本地内嵌包已存在但 revision/tag 不同：把旧快照放入 removeList，与 addList 配对成 Update。
        /// </summary>
        private void CollectLocalUpdatePackage(List<MTPackageInfo> removeList)
        {
            var removeNames = new HashSet<string>();
            foreach (var existing in removeList)
            {
                if (existing != null && !string.IsNullOrEmpty(existing.PackageName))
                {
                    removeNames.Add(existing.PackageName);
                }
            }

            foreach (var package in MTPackage.GetAllPackages())
            {
                if (package == null || string.IsNullOrEmpty(package.PackageName))
                {
                    continue;
                }

                var existing = LocalPackage.GetLocal(package.PackageName);
                if (existing == null || IsSameLocalRevision(existing, package))
                {
                    continue;
                }

                if (removeNames.Contains(package.PackageName))
                {
                    continue;
                }

                existing.isLocal = true;
                removeList.Add(existing);
                removeNames.Add(package.PackageName);
            }
        }

        private void CommitLocalSnapshot(List<MTPackageInfo> addList, List<MTPackageInfo> removeList)
        {
            foreach (var removed in removeList)
            {
                if (removed != null)
                {
                    LocalPackage.RemoveLocal(removed.PackageName);
                }
            }

            foreach (var added in addList)
            {
                if (added != null)
                {
                    LocalPackage.AddOrUpdateLocal(added);
                }
            }

            LocalPackage.SaveToLocal();
        }

        private static bool AllRequestsSucceeded(List<MTPackageInstallerRequest> requests)
        {
            foreach (var request in requests)
            {
                if (request == null || request.RequestState != RequestState.Success)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSameUnityInstall(MTPackageInfo package)
        {
            if (!Manifest.GetGitPackageInfo(package.PackageName, out var url, out var revision))
            {
                return false;
            }

            return string.Equals(UrlUtils.FormatUrl(url), package.Url, StringComparison.Ordinal)
                   && (string.Equals(revision, package.Tag, StringComparison.Ordinal)
                       || string.Equals(revision, package.Revision, StringComparison.Ordinal));
        }

        private static bool IsSameLocalRevision(MTPackageInfo existing, MTPackageInfo target)
        {
            if (!string.IsNullOrEmpty(existing.Revision) && !string.IsNullOrEmpty(target.Revision)
                && string.Equals(existing.Revision, target.Revision, StringComparison.Ordinal))
            {
                return true;
            }

            return !string.IsNullOrEmpty(existing.Tag)
                   && string.Equals(existing.Tag, target.Tag, StringComparison.Ordinal);
        }

        private static MTPackageInfo FindInTree(MTPackageInfoList tree, string packageName)
        {
            if (tree?.Dependencies == null || string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            return tree.Dependencies.TryGetValue(packageName, out var package) ? package : null;
        }

        private static bool IsMagicTavernPackage(string packageUrl)
        {
            return !string.IsNullOrEmpty(packageUrl)
                   && packageUrl.IndexOf(MTPGit.MTP_GIT_PACKAGE_MARKER, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
