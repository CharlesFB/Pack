// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 陈梓霄
// @Description: PackageJson数据类
// ==================================================

using System;
using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 解析 / 映射包的标识文件 package.json
    /// </summary>
    [Serializable]
    public class PackageInfo
    {
        public string name = "com.magictavern.package";
        public string version = "0.0.1";
        public string description = "a package for...";
        public string displayName;
        public string unity = "2019.4";
        public string unityRelease;

        [Serializable]
        public class Author
        {
            public string name = "Magic Tavern";
            public string email = "developer@magictavern.com";
            public string url = "https://www.magictavern.com/";
        }

        public Author author = new Author();
        public Dictionary<string, string> dependenciesGit = new Dictionary<string, string>();

        public string changelogUrl;
        public Dictionary<string, string> dependencies = new Dictionary<string, string>();
        public string documentationUrl;
        public bool hideInEditor; // 无用
        public List<string> keywords;
        public string license;
        public string licenseUrl;

        [Serializable]
        public class Sample
        {
            public string displayName;
            public string description;
            public string path;
        }

        public List<Sample> samples;
        public string type; //无用
        public string scriptDefineSymbols;
    }
}