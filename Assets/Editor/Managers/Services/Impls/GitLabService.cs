// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: GitLab API 基础设施服务（Client 懒加载 + 文件 / Tag / Tree / Group 拉取）
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GitLabApiClient;
using GitLabApiClient.Models.Commits.Responses;
using GitLabApiClient.Models.Groups.Responses;
using GitLabApiClient.Models.Tags.Responses;
using MagicTavern.FrontEngine.MTPManager.LitJson;
using UnityEngine;
using GitTree = GitLabApiClient.Models.Trees.Responses.Tree;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class GitLabService : IGitLabService
    {
        private GitLabClient _client;

        public bool HasClient => _client != null;

        /// <summary>
        /// 懒加载获取内存中的 GitLabClient；配置无效时返回 null。
        /// </summary>
        private GitLabClient Client()
        {
            if (_client != null)
            {
                return _client;
            }

            var config = ConfigManager.Instance.Current;
            if (config == null || !config.IsValid())
            {
                LM.LogError("[GitLabService] MTPConfig 无效，无法创建 GitLabClient。请检查 Url / PrivateToken / GroupId。");
                return null;
            }

            try
            {
                _client = new GitLabClient(config.Url, config.PrivateToken);
            }
            catch (Exception ex)
            {
                LM.LogError($"[GitLabService] 创建 GitLabClient 失败: {ex.Message}");
                _client = null;
            }

            return _client;
        }

        public bool EnsureClient()
        {
            return Client() != null;
        }

        public void ResetClient()
        {
            _client = null;
        }

        public async Task<string> GetFileTextAsync(int projectId, string filePath, string reference = "master",
            bool logOnFailure = true)
        {
            if (projectId <= 0 || string.IsNullOrEmpty(filePath))
            {
                if (logOnFailure)
                {
                    LM.LogWarning(
                        $"[GitLabService] GetFileTextAsync 参数无效: projectId={projectId}, filePath={filePath}");
                }

                return null;
            }

            var client = Client();
            if (client == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(reference))
            {
                reference = "master";
            }

            try
            {
                var file = await client.Files.GetAsync(projectId, filePath, reference);
                if (file == null || string.IsNullOrEmpty(file.Content))
                {
                    if (logOnFailure)
                    {
                        LM.LogError(
                            $"[GitLabService] 拉取文件失败: projectId={projectId}, path={filePath}, ref={reference}");
                    }

                    return null;
                }

                return file.ContentDecoded;
            }
            catch (Exception ex)
            {
                if (logOnFailure)
                {
                    LM.Log(
                        $"[GitLabService] GetFileTextAsync 异常: projectId={projectId}, path={filePath}, ref={reference}\n{ex.Message}");
                }

                return null;
            }
        }

        public async Task<byte[]> GetFileBytesAsync(int projectId, string filePath, string reference = "master")
        {
            if (projectId <= 0 || string.IsNullOrEmpty(filePath))
            {
                LM.LogWarning($"[GitLabService] GetFileBytesAsync 参数无效: projectId={projectId}, filePath={filePath}");
                return null;
            }

            var client = Client();
            if (client == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(reference))
            {
                reference = "master";
            }

            try
            {
                var file = await client.Files.GetAsync(projectId, filePath, reference);
                if (file == null || string.IsNullOrEmpty(file.Content))
                {
                    LM.LogError(
                        $"[GitLabService] 拉取文件字节失败: projectId={projectId}, path={filePath}, ref={reference}");
                    return null;
                }

                return Convert.FromBase64String(file.Content);
            }
            catch (Exception ex)
            {
                LM.LogError(
                    $"[GitLabService] GetFileBytesAsync 异常: projectId={projectId}, path={filePath}, ref={reference}\n{ex.Message}");
                return null;
            }
        }

        public async Task<bool> DownloadFileAsync(int projectId, string filePath, string destPath,
            string reference = "master")
        {
            if (string.IsNullOrEmpty(destPath))
            {
                LM.LogWarning("[GitLabService] DownloadFileAsync 参数无效: destPath 为空");
                return false;
            }

            var bytes = await GetFileBytesAsync(projectId, filePath, reference);
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(destPath, bytes);
                return true;
            }
            catch (Exception ex)
            {
                LM.LogError(
                    $"[GitLabService] DownloadFileAsync 写盘失败: dest={destPath}\n{ex.Message}");
                return false;
            }
        }

        public async Task<T> GetJsonFileAsync<T>(int projectId, string filePath, string reference = "master")
            where T : class
        {
            var text = await GetFileTextAsync(projectId, filePath, reference);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            try
            {
                return JsonMapper.ToObject<T>(text);
            }
            catch (Exception ex)
            {
                LM.LogError(
                    $"[GitLabService] JSON 反序列化失败: type={typeof(T).Name}, path={filePath}\n{ex.Message}");
                return null;
            }
        }

        public async Task<IList<Tag>> GetTagsAsync(int projectId)
        {
            if (projectId <= 0)
            {
                LM.LogWarning($"[GitLabService] GetTagsAsync 参数无效: projectId={projectId}");
                return null;
            }

            var client = Client();
            if (client == null)
            {
                return null;
            }

            try
            {
                return await client.Tags.GetAsync(projectId);
            }
            catch (Exception ex)
            {
                LM.LogError($"[GitLabService] GetTagsAsync 异常: projectId={projectId}\n{ex.Message}");
                return null;
            }
        }

        public async Task<Tag> GetTagAsync(int projectId, string tagName)
        {
            if (projectId <= 0 || string.IsNullOrEmpty(tagName))
            {
                LM.LogWarning(
                    $"[GitLabService] GetTagAsync 参数无效: projectId={projectId}, tagName={tagName}");
                return null;
            }

            var client = Client();
            if (client == null)
            {
                return null;
            }

            try
            {
                return await client.Tags.GetAsync(projectId, tagName);
            }
            catch (Exception ex)
            {
                LM.LogError(
                    $"[GitLabService] GetTagAsync 异常: projectId={projectId}, tagName={tagName}\n{ex.Message}");
                return null;
            }
        }

        public async Task<Commit> GetCommitAsync(int projectId, string sha)
        {
            if (projectId <= 0 || string.IsNullOrEmpty(sha))
            {
                LM.LogWarning($"[GitLabService] GetCommitAsync 参数无效: projectId={projectId}, sha={sha}");
                return null;
            }

            var client = Client();
            if (client == null)
            {
                return null;
            }

            try
            {
                return await client.Commits.GetAsync(projectId, sha);
            }
            catch (Exception ex)
            {
                LM.LogError($"[GitLabService] GetCommitAsync 异常: projectId={projectId}, sha={sha}\n{ex.Message}");
                return null;
            }
        }

        public async Task<string> ResolveRevisionAsync(int projectId, string reference)
        {
            if (projectId <= 0 || string.IsNullOrEmpty(reference))
            {
                LM.LogWarning(
                    $"[GitLabService] ResolveRevisionAsync 参数无效: projectId={projectId}, reference={reference}");
                return null;
            }

            var tag = await GetTagAsync(projectId, reference);
            if (!string.IsNullOrEmpty(tag?.Commit?.Id))
            {
                return tag.Commit.Id;
            }

            // reference 可能本身就是分支名或已经是 commit sha，回退用 Commits 接口解析。
            var commit = await GetCommitAsync(projectId, reference);
            if (commit == null)
            {
                LM.LogError(
                    $"[GitLabService] ResolveRevisionAsync 无法解析: projectId={projectId}, reference={reference}");
                return null;
            }

            return commit.Id;
        }

        public async Task<IList<GitTree>> GetTreesAsync(int projectId, string path = null, string reference = null,
            bool recursive = false)
        {
            if (projectId <= 0)
            {
                LM.LogWarning($"[GitLabService] GetTreesAsync 参数无效: projectId={projectId}");
                return null;
            }

            var client = Client();
            if (client == null)
            {
                return null;
            }

            try
            {
                return await client.Trees.GetAsync(projectId, o =>
                {
                    o.Path = path;
                    o.Reference = reference;
                    o.Recursive = recursive;
                });
            }
            catch (Exception ex)
            {
                LM.LogError(
                    $"[GitLabService] GetTreesAsync 异常: projectId={projectId}, path={path}, ref={reference}\n{ex.Message}");
                return null;
            }
        }

        public async Task<Group> GetGroupAsync(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                LM.LogWarning("[GitLabService] GetGroupAsync 参数无效: groupId 为空");
                return null;
            }

            var client = Client();
            if (client == null)
            {
                return null;
            }

            try
            {
                return await client.Groups.GetAsync(groupId);
            }
            catch (Exception ex)
            {
                LM.LogError($"[GitLabService] GetGroupAsync 异常: groupId={groupId}\n{ex.Message}");
                return null;
            }
        }
    }
}
