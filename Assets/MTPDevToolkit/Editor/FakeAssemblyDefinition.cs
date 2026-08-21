// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 用来修改AssemblyDef的数据对象，
// 暂时只用来创建空的asmdef文件，后续可能会添加设置
// ==================================================

using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    public class FakeAssemblyDefinition
    {
        public string name;

        // public string rootNamespace;
        public List<string> references;

        // ReSharper disable once CollectionNeverQueried.Global
        public List<string> includePlatforms = new List<string>();
        public List<string> excludePlatforms = new List<string>();
        public bool allowUnsafeCode;

        public bool overrideReferences = false;
        public List<string> precompiledReferences = new List<string>();
        public bool autoReferenced = true;
        public List<string> defineConstraints = new List<string>();
        public List<string> versionDefines = new List<string>();
        public bool noEngineReferences;
    }
}