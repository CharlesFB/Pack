using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    public static class MTPManager
    {
        private static IPackageJsonService s_packageJsonService => ServiceLocator.Get<IPackageJsonService>();
        private static IGitLabService s_gitLabService => ServiceLocator.Get<IGitLabService>();
        private static IMTPackageInfoService s_mtPackageInfoService => ServiceLocator.Get<IMTPackageInfoService>();
        private static IProjectInfoService s_projectInfoService => ServiceLocator.Get<IProjectInfoService>();

        public static async Task<PackageManifest> GetPackageJson(ProjectInfo projectInfo, string revision,
            bool force = false)
        {
            if (projectInfo == null || projectInfo.ProjectId <= 0)
            {
                Debug.LogError("[MTPManager] GetPackageJson: projectInfo / ProjectId 无效");
                return null;
            }

            if (string.IsNullOrEmpty(revision))
            {
                Debug.LogError("[MTPManager] GetPackageJson: revision 为空");
                return null;
            }

            // debug tag 禁用缓存，每次从 GitLab 拉取（作者可能 force-push 同一 tag）
            bool isDebugRevision = revision.EndsWith("-debug");
            string projectId = projectInfo.ProjectId.ToString();

            await s_packageJsonService.EnsureInitializedAsync();

            PackageManifest manifest = null;
            if (!isDebugRevision && !force)
            {
                manifest = ToPackageManifest(s_packageJsonService.Get(projectId, revision));
            }

            if (manifest == null)
            {
                manifest = await s_gitLabService.GetJsonFileAsync<PackageManifest>(
                    projectInfo.ProjectId, "package.json", revision);
                if (manifest != null && !isDebugRevision)
                {
                    s_packageJsonService.Add(projectId, revision, ToPackageJson(manifest));
                    s_packageJsonService.SaveToLocal();
                }
            }

            return manifest;
        }

        public static async Task<List<PackageInstallInfo>> GetPackageInstallList(bool forceRefresh)
        {
            var config = ConfigManager.Instance.Current;

            await s_projectInfoService.EnsureInitializedAsync();
            await s_packageJsonService.EnsureInitializedAsync();
            if (forceRefresh || s_projectInfoService.NeedUpdate)
            {
                // 对齐旧 GetProjectList(forceRefresh)：强制或 TTL 到期才拉 upm-list
                await s_projectInfoService.PullFromGitAsync(force: forceRefresh);
            }

            var projectList = s_projectInfoService.GetAll();
            var installedPackages = s_mtPackageInfoService.GetAllPackages() ?? new List<MTPackageInfo>();

            var installList = new List<PackageInstallInfo>();
            foreach (var projectInfo in projectList)
            {
                if (projectInfo == null)
                {
                    continue;
                }

                var matched = installedPackages.FindAll(dep => MatchesProject(dep, projectInfo));
                if (matched.Count > 0)
                {
                    foreach (var dep in matched)
                    {
                        installList.Add(new PackageInstallInfo
                        {
                            ProjectInfo = projectInfo,
                            DepInfo = ToDepInfo(dep),
                        });
                    }
                }
                else
                {
                    installList.Add(new PackageInstallInfo
                    {
                        ProjectInfo = projectInfo,
                    });
                }
            }

            installList.Sort((x, y) =>
            {
                if (x.DepInfo != null && y.DepInfo == null) return -1;
                if (x.DepInfo == null && y.DepInfo != null) return 1;
                return string.CompareOrdinal(
                    x.ProjectInfo?.PackageName, y.ProjectInfo?.PackageName);
            });

            if (!string.IsNullOrEmpty(config?.ProjectTag))
            {
                for (int k = 0; k < installList.Count; k++)
                {
                    installList[k].ProjectInfo = installList[k].ProjectInfo.CloneForDisplayTemp();
                }

                string projectName = config.ProjectTag.ToLower();
                for (int i = 0; i < installList.Count; i++)
                {
                    var package = installList[i];
                    var versions = package.ProjectInfo.Versions;
                    if (versions == null)
                    {
                        installList.RemoveAt(i);
                        i--;
                        continue;
                    }

                    for (int j = 0; j < versions.Count; j++)
                    {
                        var version = versions[j];
                        var parts = version.Split('-');
                        if (parts.Length > 2 &&
                            !parts[1].ToLower().Contains(projectName))
                        {
                            // 已安装的那个 tag 即使不属于当前项目组也要保留，和旧逻辑一致
                            if (package.DepInfo != null &&
                                string.Equals(package.DepInfo.Revision, version, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            versions.RemoveAt(j);
                            j--;
                        }
                    }

                    if (versions.Count == 0)
                    {
                        installList.RemoveAt(i);
                        i--;
                    }
                }
            }

            if (config != null && !config.DebugMode && config.BlackList != null && config.BlackList.Count > 0)
            {
                for (int i = 0; i < installList.Count; i++)
                {
                    var id = installList[i].ProjectInfo?.ProjectId.ToString();
                    if (!string.IsNullOrEmpty(id) &&
                        config.BlackList.Contains(id) &&
                        installList[i].DepInfo == null)
                    {
                        installList.RemoveAt(i);
                        i--;
                    }
                }
            }

            return installList;
        }

        private static bool MatchesProject(MTPackageInfo dep, ProjectInfo project)
        {
            if (dep == null || project == null)
            {
                return false;
            }

            return UrlUtils.MatchesProject(project, dep.Url, dep.Tag);
        }

        private static PackageDepInfo ToDepInfo(MTPackageInfo mt)
        {
            if (mt == null)
            {
                return null;
            }

            string tag = mt.Tag;
            string version = tag;
            if (!string.IsNullOrEmpty(tag))
            {
                var parts = tag.Split('-');
                version = parts[parts.Length - 1];
            }

            return new PackageDepInfo
            {
                PackageName = mt.PackageName,
                Revision = tag, // 兼容层：Revision == Tag
                Version = version,
                Url = mt.Url,
                Depth = mt.Depth,
                isLocal = mt.isLocal,
                ScriptDefineSymbols = mt.ScriptDefineSymbols,
                Dependencies = mt.Dependencies != null
                    ? new Dictionary<string, string>(mt.Dependencies)
                    : null,
            };
        }

        public static async Task<ProjectInfo> PullProjectInfoWithGitlab(
            PackageManifest manifest, string currentBranch)
        {
            if (manifest == null || string.IsNullOrEmpty(manifest.name))
            {
                Debug.LogError("[MTPManager] PullProjectInfoWithGitlab: manifest.name 为空");
                return null;
            }

            if (!s_gitLabService.EnsureClient())
            {
                Debug.LogError("未获取到GitLabClient");
                return null;
            }

            var splitName = manifest.name.Split('.');
            var packageName = splitName[splitName.Length - 1].ToLower();

            await s_projectInfoService.EnsureInitializedAsync();
            int projectId = GetProjectIdFromPackageName(packageName);

            var projectInfo = new ProjectInfo
            {
                PackageName = packageName,
                ProjectId = projectId,
            };

            if (projectId <= 0)
            {
                Debug.Log($"未找到{packageName}的projectID");
                return projectInfo; // 与旧行为一致：找不到 id 仍返回对象，Tags 为空
            }

            string loadMessage = "finished";
            EditorUtility.DisplayProgressBar("pull projectInfo", "processing...", 0.5f);
            try
            {
                projectInfo.Tags = await PullTagList(projectId, packageName, currentBranch);
            }
            catch (Exception e)
            {
                projectInfo = null;
                loadMessage = "network error";
                EditorUtility.DisplayDialog("网络异常", $"拉取工程版本信息错误: \n{e.Message}", "ok");
            }

            EditorUtility.DisplayProgressBar("pull projectInfo", loadMessage, 1f);
            EditorUtility.ClearProgressBar();
            return projectInfo;
        }

        private static int GetProjectIdFromPackageName(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return -1;
            }

            // 短名精确匹配（upm-list 里 PackageName 就是 git 工程短名）
            var byName = s_projectInfoService.Get(packageName);
            if (byName != null && byName.ProjectId > 0)
            {
                return byName.ProjectId;
            }

            // 回退：有的缓存可能存的是 com.xxx.xxx，用最后一段再扫一遍
            foreach (var project in s_projectInfoService.GetAll())
            {
                if (project == null || string.IsNullOrEmpty(project.PackageName))
                {
                    continue;
                }

                if (string.Equals(project.PackageName, packageName, StringComparison.OrdinalIgnoreCase))
                {
                    return project.ProjectId;
                }

                var parts = project.PackageName.Split('.');
                var shortName = parts[parts.Length - 1];
                if (string.Equals(shortName, packageName, StringComparison.OrdinalIgnoreCase))
                {
                    return project.ProjectId;
                }
            }

            Debug.LogError("projectInfoList.Packages为空，请更新本地缓存");
            return -1;
        }

        private static async Task<List<string>> PullTagList(
            int projectId, string packageName, string currentBranch)
        {
            var tags = await s_gitLabService.GetTagsAsync(projectId);
            if (tags == null)
            {
                return null;
            }

            var tagList = new List<string>();
            var splitBranch = string.IsNullOrEmpty(currentBranch)
                ? Array.Empty<string>()
                : currentBranch.Split('-');
            int splitBranchLength = splitBranch.Length;

            foreach (var tag in tags)
            {
                if (tag == null || string.IsNullOrEmpty(tag.Name))
                {
                    continue;
                }

                var splitTag = tag.Name.Split('-');
                if (splitTag.Length <= 1)
                {
                    continue;
                }

                var head = splitTag[0];
                var version = splitTag[splitTag.Length - 1];
                if (IsInValidPackage(head, version))
                {
                    continue;
                }

                if (splitBranchLength <= 1)
                {
                    // 无项目组前缀：只要 package-x.y.z（两段）
                    if (head == packageName && splitTag[1] == version)
                    {
                        tagList.Add(tag.Name);
                    }
                }
                else
                {
                    // 当前分支是 package-project-...，tag 前缀必须与分支前缀一致
                    bool isSame = true;
                    for (int i = 0; i < splitBranchLength; i++)
                    {
                        if (i >= splitTag.Length || splitBranch[i] != splitTag[i])
                        {
                            isSame = false;
                            break;
                        }
                    }

                    if (isSame)
                    {
                        tagList.Add(tag.Name);
                    }
                }
            }

            VersionUtils.SortTags(tagList);
            return tagList;
        }

        private static bool IsInValidPackage(string head, string version)
        {
            if (head == "dev") return true;
            if (head.Contains(".")) return true;
            if (head.Contains("_")) return true;
            if (version == "debug") return false;
            return !Version.TryParse(version, out _);
        }

        private static PackageManifest ToPackageManifest(PackageJson src)
        {
            if (src == null)
            {
                return null;
            }

            return new PackageManifest
            {
                name = src.name,
                version = src.version,
                description = src.description,
                displayName = src.displayName,
                unity = src.unity,
                unityRelease = src.unityRelease,
                author = src.author == null
                    ? new PackageManifest.Author()
                    : new PackageManifest.Author
                    {
                        name = src.author.name,
                        email = src.author.email,
                        url = src.author.url,
                    },
                dependenciesGit = src.dependenciesGit != null
                    ? new Dictionary<string, string>(src.dependenciesGit)
                    : new Dictionary<string, string>(),
                changelogUrl = src.changelogUrl,
                dependencies = src.dependencies != null
                    ? new Dictionary<string, string>(src.dependencies)
                    : new Dictionary<string, string>(),
                documentationUrl = src.documentationUrl,
                hideInEditor = src.hideInEditor,
                keywords = src.keywords != null ? new List<string>(src.keywords) : null,
                license = src.license,
                licenseUrl = src.licenseUrl,
                samples = CopySamplesFromPackageJson(src.samples),
                type = src.type,
                scriptDefineSymbols = src.scriptDefineSymbols,
            };
        }

        private static PackageJson ToPackageJson(PackageManifest src)
        {
            if (src == null)
            {
                return null;
            }

            return new PackageJson
            {
                name = src.name,
                version = src.version,
                description = src.description,
                displayName = src.displayName,
                unity = src.unity,
                unityRelease = src.unityRelease,
                author = src.author == null
                    ? new PackageJson.Author()
                    : new PackageJson.Author
                    {
                        name = src.author.name,
                        email = src.author.email,
                        url = src.author.url,
                    },
                dependenciesGit = src.dependenciesGit != null
                    ? new Dictionary<string, string>(src.dependenciesGit)
                    : new Dictionary<string, string>(),
                changelogUrl = src.changelogUrl,
                dependencies = src.dependencies != null
                    ? new Dictionary<string, string>(src.dependencies)
                    : new Dictionary<string, string>(),
                documentationUrl = src.documentationUrl,
                hideInEditor = src.hideInEditor,
                keywords = src.keywords != null ? new List<string>(src.keywords) : null,
                license = src.license,
                licenseUrl = src.licenseUrl,
                samples = CopySamplesFromManifest(src.samples),
                type = src.type,
                scriptDefineSymbols = src.scriptDefineSymbols,
            };
        }

        private static List<PackageManifest.Sample> CopySamplesFromPackageJson(List<PackageJson.Sample> src)
        {
            if (src == null)
            {
                return null;
            }

            var list = new List<PackageManifest.Sample>(src.Count);
            foreach (var sample in src)
            {
                if (sample == null)
                {
                    continue;
                }

                list.Add(new PackageManifest.Sample
                {
                    displayName = sample.displayName,
                    description = sample.description,
                    path = sample.path,
                });
            }

            return list;
        }

        private static List<PackageJson.Sample> CopySamplesFromManifest(List<PackageManifest.Sample> src)
        {
            if (src == null)
            {
                return null;
            }

            var list = new List<PackageJson.Sample>(src.Count);
            foreach (var sample in src)
            {
                if (sample == null)
                {
                    continue;
                }

                list.Add(new PackageJson.Sample
                {
                    displayName = sample.displayName,
                    description = sample.description,
                    path = sample.path,
                });
            }

            return list;
        }
    }
}