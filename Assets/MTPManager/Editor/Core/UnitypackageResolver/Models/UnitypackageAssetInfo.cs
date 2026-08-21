// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: .unitypackage 内单条资源条目信息
// ==================================================

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// .unitypackage 内单条资源条目信息
    /// </summary>
    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class UnitypackageAssetInfo
    {
        public string pathname; // 资源路径名，形如 "Assets/XXX/XXXX"
        public string guid; // Unity资源的GUID
        public string hash; // 文件哈希校验码
        public string extension; // 扩展名，目录的扩展名为 string.Empty
        public long fileSize; // 文件大小（对文件夹来讲，值为0）
        public bool isDirectory; // 是否为文件夹

        public UnitypackageAssetInfo()
        {
            pathname = string.Empty;
            guid = string.Empty;
            hash = string.Empty;
            extension = string.Empty;
            fileSize = 0;
            // isDirectory 务必初始化为 false：获取 unitypackage 的资源顺序不确定，
            // 是否为目录资源只有读到 asset.meta 后才会确定。
            // 默认按文件处理，先 pathname 后 meta 可纠正误判；若默认目录则会漏掉文件资源。
            isDirectory = false;
        }

        public UnitypackageAssetInfo(string pathname, bool isDirectory, string guid, string hash, string extension,
            long fileSize = 0)
        {
            this.pathname = pathname;
            this.isDirectory = isDirectory;
            this.guid = guid;
            this.hash = hash;
            this.extension = extension;
            this.fileSize = fileSize;
        }

        public string AbsolutePath
        {
            get
            {
                if (string.IsNullOrEmpty(pathname))
                {
                    return string.Empty;
                }

                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (projectRoot != null)
                {
                    return Path.Combine(projectRoot, pathname).Replace("\\", "/");
                }

                return string.Empty;
            }
        }

        public override string ToString()
        {
            return
                $"[{(isDirectory ? "Dir" : "File")}] {pathname} (GUID: {guid}, Hash: {hash}, Extension: {extension}, Size: {fileSize} bytes)";
        }
    }
}
