// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 远端 MT 工程信息列表持久化数据模型
// ==================================================

using System;
using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 映射目录见 MTPPath.PROJECT_INFO_LIST_PATH（Library/upm-list.json）
    /// 从远端仓库拉取，并在本地缓存的全部 MT 工程信息
    /// 相关服务见 IProjectInfoService
    /// </summary>
    [Serializable]
    public class ProjectInfoList
    {
        public DateTime RefreshTime;
        public List<ProjectInfo> Packages = new List<ProjectInfo>();
    }
}
