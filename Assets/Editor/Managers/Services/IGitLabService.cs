// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: GitLab API 基础设施服务接口（Client 管理 + 文件 / Tag / Tree / Group 拉取）
// ==================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using GitLabApiClient.Models.Groups.Responses;
using GitLabApiClient.Models.Tags.Responses;
using GitTree = GitLabApiClient.Models.Trees.Responses.Tree;

namespace MagicTavern.FrontEngine.MTPManager
{
    public interface IGitLabService
    {
        /// <summary>
        /// 当前是否已持有可用的 GitLabClient。
        /// </summary>
        bool HasClient { get; }

        /// <summary>
        /// 尝试按当前配置确保 Client 可用；配置无效时返回 false。
        /// </summary>
        bool EnsureClient();

        /// <summary>
        /// 丢弃内存中的 Client（配置变更后调用，下次拉取会重新懒加载）。
        /// </summary>
        void ResetClient();

        /// <summary>
        /// 拉取仓库文件原文（UTF-8 解码后的文本）。
        /// </summary>
        Task<string> GetFileTextAsync(int projectId, string filePath, string reference = "master");

        /// <summary>
        /// 拉取仓库文件原始字节（GitLab base64 Content 解码）。
        /// </summary>
        Task<byte[]> GetFileBytesAsync(int projectId, string filePath, string reference = "master");

        /// <summary>
        /// 拉取仓库文件并写入本地路径；成功返回 true。
        /// </summary>
        Task<bool> DownloadFileAsync(int projectId, string filePath, string destPath, string reference = "master");

        /// <summary>
        /// 拉取仓库 JSON 文件并反序列化为 T。
        /// </summary>
        Task<T> GetJsonFileAsync<T>(int projectId, string filePath, string reference = "master") where T : class;

        /// <summary>
        /// 拉取工程全部 Tag 列表。
        /// </summary>
        Task<IList<Tag>> GetTagsAsync(int projectId);

        /// <summary>
        /// 按名称拉取单个 Tag。
        /// </summary>
        Task<Tag> GetTagAsync(int projectId, string tagName);

        /// <summary>
        /// 拉取仓库目录树节点（不下载文件内容）。
        /// </summary>
        /// <param name="projectId">工程 Id</param>
        /// <param name="path">子目录路径，可空表示仓库根</param>
        /// <param name="reference">分支 / tag / commit</param>
        /// <param name="recursive">是否递归</param>
        Task<IList<GitTree>> GetTreesAsync(int projectId, string path = null, string reference = null,
            bool recursive = false);

        /// <summary>
        /// 按 Group Id / path 拉取 Group 详情（含 Projects）。
        /// </summary>
        Task<Group> GetGroupAsync(string groupId);
    }
}
