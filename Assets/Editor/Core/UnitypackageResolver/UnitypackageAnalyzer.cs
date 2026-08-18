// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 针对引入的插件包，提供其内部Unity资源清单的解析方法
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    public static class UnitypackageAnalyzer
    {
        /// <summary>
        /// 解析.unitypackage里的包内容，返回每一个资源（包括文件夹资源）的基本信息
        /// </summary>
        /// <param name="packagePath"> 待解析包路径 </param>
        /// <param name="computeHash"> 是否计算Hash校验码，默认计算 </param>
        /// <returns>
        /// 资源包内容List
        /// </returns>
        public static List<UnitypackageAssetInfo> GetPackageInfos(string packagePath, bool computeHash = true)
        {
            Dictionary<string, UnitypackageAssetInfo> entryMap = new Dictionary<string, UnitypackageAssetInfo>();

            if (!File.Exists(packagePath))
            {
                Debug.LogError($"[PackageAnalyzer] 找不到文件: {packagePath}");
                return new List<UnitypackageAssetInfo>();
            }

            try
            {
                using (FileStream fs = File.OpenRead(packagePath))
                using (GZipInputStream gzipStream = new GZipInputStream(fs))
                using (TarInputStream tarStream = new TarInputStream(gzipStream, Encoding.UTF8))
                {
                    TarEntry entry;

                    while ((entry = tarStream.GetNextEntry()) != null)
                    {
                        /*
                         * 正常的entry.Name内会包含如下几种类型的文件路径信息（建立在标准unitypackage情况下，部分第三方打包可能不会有"./"开头）
                         * 1. "./" 当前文件夹内索引
                         * 2. “./{guid}/” 某条Unity资源存放目录
                         * 3. "./{guid}/asset" 资源文件（不一定存在，如果Unity下是路径资源，则没有这个文件）
                         * 4. “./{guid}/pathname” 资源文件在Unity工程需要存放的路径
                         * 5. "./{guid}/asset.meta" 通常是隐藏文件，标识Unity资源信息
                         */
                        if (entry.IsDirectory) continue; // 用来排除 1. & 2. 两种情况，不需要解析目录资源
                        
                        string[] pathParts = entry.Name.Split('/');
                        string guid;
                        string fileName;
                        // 正常目录，标准unitypackage进入，会以"./"开头，因此分割会有三部分
                        if (pathParts.Length == 3 && pathParts[0] == ".")
                        {
                            guid = pathParts[1]; //guid
                            fileName = pathParts[2]; //asset / pathname / asset.meta
                        }
                        // 第三方工具手动打包的unitypackage可能是 {guid}/{file} 的格式，split后只有两部分
                        else if (pathParts.Length == 2)
                        {
                            guid = pathParts[0]; //guid
                            fileName = pathParts[1]; //asset / pathname / asset.meta
                        }
                        else continue; // 非法目录结构

                        if (!entryMap.TryGetValue(guid, out UnitypackageAssetInfo info))
                        {
                            info = new UnitypackageAssetInfo { guid = guid };
                            entryMap[guid] = info;
                        }
                        /*
                         * .unitypackage资源解包后，针对每一个guid目录下的文件都必定会有一个"pathname"文件用于标识这个资源在工程里的存放目录
                         * 以及可能会附带一个名称为"asset"文件，即为当前资源编号代表的asset资源；如果没有这个文件，那基本上就是目录资源了（文件夹）
                         *
                         * tips：这个判断目录资源的方法也可以采取按行读取asset.meta文件里的内容，如果其中存在一行信息"folderAsset: yes"，也就说明这是个目录资源
                         */
                        // 读到pathname文件
                        if (fileName.Equals("pathname", StringComparison.OrdinalIgnoreCase))
                        {
                            string pathContent = ReadEntryText(tarStream);
                            info.pathname = pathContent.TrimEnd('/', '\\');
                            // 传统判断path后是否跟斜线的方式行不通，检查包信息很多目录结尾都没有斜线，代码直接用tips方式，检查asset.meta中是否有记录目录资源信息的方式判断
                            // info.isDirectory = pathContent.EndsWith("/") || pathContent.EndsWith("\\"); 

                            if (!info.isDirectory)
                            {
                                info.extension = Path.GetExtension(info.pathname).ToLower();
                            }
                            else
                            {
                                info.extension = string.Empty;
                            }
                        }
                        // 读到了资源文件asset
                        else if (fileName.Equals("asset", StringComparison.OrdinalIgnoreCase))
                        {
                            info.fileSize = entry.Size;

                            if (computeHash && entry.Size > 0)
                            {
                                using (MD5 md5 = MD5.Create())
                                {
                                    byte[] hashBytes = md5.ComputeHash(tarStream);
                                    info.hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                                }
                            }
                        }
                        // 读到meta文件
                        else if (fileName.Equals("asset.meta", StringComparison.OrdinalIgnoreCase))
                        {
                            //如果文件包括"folderAsset: yes"字样，则认定为info.isDirectory = true，否则为false
                            string metaContent = ReadEntryText(tarStream);
                    
                            // 检查是否包含目录标识
                            if (metaContent.Contains("folderAsset: yes"))
                            {
                                info.isDirectory = true;
                                info.extension = string.Empty; //确认是目录，强制清空可能在pathname阶段误判的扩展名
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageAnalyzer] 解析失败: {ex.Message}");
            }

            return entryMap.Values.Where(v => !string.IsNullOrEmpty(v.pathname)).ToList(); 
        }
        
        /// <summary>
        /// 补充可能不在unitypackage里管理的父目录
        /// 升序排序，用hashset判重，递归尾插需要补充的父目录
        /// </summary>
        /// <param name="infos"></param>
        /// <returns></returns>
        public static List<UnitypackageAssetInfo> FixMissingDirectories(List<UnitypackageAssetInfo> infos)
        {
            infos.Sort((a, b) => String.CompareOrdinal(a.pathname, b.pathname)); //升序
            HashSet<string> set = new HashSet<string>();
            int len = infos.Count;
            for (int i = 0; i < len; ++i)
            {
                if (string.IsNullOrEmpty(infos[i].pathname))
                    continue;
                string cur = infos[i].pathname.Replace("\\", "/").Trim().TrimEnd('/');
                set.Add(cur);
                int lastIndex = cur.LastIndexOf('/');
                string parent = lastIndex > 0 ? cur.Substring(0, lastIndex) : string.Empty;
                while (!string.IsNullOrEmpty(parent) && parent != "Assets" && !set.Contains(parent)) //父目录不在list中
                {
                    UnitypackageAssetInfo newInfo = new UnitypackageAssetInfo
                    {
                        isDirectory = true,
                        pathname = parent,
                        extension = string.Empty,
                        fileSize = 0,
                        hash = string.Empty,
                        guid = string.Empty
                    };
                    infos.Insert(infos.Count, newInfo);
                    set.Add(parent);
                    lastIndex = parent.LastIndexOf('/');
                    parent = lastIndex > 0 ? parent.Substring(0, lastIndex) : string.Empty;
                }
            }
            return infos;
        }

        // 供安装时持久化、卸载时清理unitypackage资源
        public static List<string> GetAssetPathnames(string packagePath, bool assetsOnly = true)
        {
            var infos = FixMissingDirectories(GetPackageInfos(packagePath, false));
            return infos
                .Where(info => !assetsOnly || info.pathname.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .Select(info => info.pathname.Replace("\\", "/").Trim().TrimEnd('/'))
                .Where(pathname => !string.IsNullOrEmpty(pathname))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public static Dictionary<string, List<string>> CollectUnityPackageAssetPaths(string pluginPackageDirectory)
        {
            var result = new Dictionary<string, List<string>>();
            if (string.IsNullOrEmpty(pluginPackageDirectory) || !Directory.Exists(pluginPackageDirectory))
            {
                return result;
            }

            string[] pluginPackages = Directory.GetFiles(pluginPackageDirectory, "*.unitypackage", SearchOption.AllDirectories);
            foreach (string pluginPackagePath in pluginPackages)
            {
                string fileName = Path.GetFileName(pluginPackagePath);
                result[fileName] = GetAssetPathnames(pluginPackagePath);
            }

            return result;
        }

        /// <summary>
        /// 【弃用】统一用上面的API拿全部信息
        /// 解析.unitypackage返回它将释放到Assets下的所有文件路径
        /// </summary>
        /// <param name="packagePath"> 包路径 </param>
        /// <returns>
        /// 路径名称List
        /// </returns>
        public static List<string> GetPackageAssetPaths(string packagePath)
        {
            List<string> targetPaths = new List<string>();

            if (!File.Exists(packagePath))
            {
                Debug.LogError($"[PackageAnalyzer] 找不到文件: {packagePath}");
                return targetPaths;
            }

            try
            {
                using (FileStream fs = File.OpenRead(packagePath))
                using (GZipInputStream gzipStream = new GZipInputStream(fs))
                using (TarInputStream tarStream = new TarInputStream(gzipStream, Encoding.UTF8))
                {
                    TarEntry entry;

                    while ((entry = tarStream.GetNextEntry()) != null)
                    {
                        // if (entry.IsDirectory) continue;
                        if (entry.Name.EndsWith("pathname", StringComparison.OrdinalIgnoreCase))
                        {
                            string pathContent = ReadEntryText(tarStream);
                            if (!string.IsNullOrEmpty(pathContent))
                            {
                                targetPaths.Add(pathContent);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageAnalyzer] 解析 {packagePath} 失败: {ex.Message}");
            }

            return targetPaths;
        }

        /// <summary>
        /// 解析每一个Stream数据
        /// </summary>
        /// <param name="tarStream"></param>
        /// <returns></returns>
        private static string ReadEntryText(TarInputStream tarStream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buffer = new byte[2048]; //字节流buffer
                int size;

                while ((size = tarStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, size);
                }

                return Encoding.UTF8.GetString(ms.ToArray()).Trim(); //转译成Utf-8输出为string类型数据
            }
        }
    }
}