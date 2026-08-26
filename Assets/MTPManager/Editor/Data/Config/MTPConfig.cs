// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: MTP包管理器主界面的菜单入口类
// ==================================================

using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class MTPConfig
    {
        public List<string> BlackList;
        public string Url;
        public string PrivateToken;
        public string GroupId;
        public string ProjectId;
        public string ProjectTag;
        public RefreshTime refreshFrequency;
        
        public bool LogOnConsole;
        public bool DebugMode; 
        public bool ShowDevTag; 
        public bool InstallToLocal; 

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(Url)) return false;
            if (string.IsNullOrEmpty(PrivateToken)) return false;
            if (string.IsNullOrEmpty(GroupId) && string.IsNullOrEmpty(ProjectId)) return false;
            return true;
        }

        // 向前兼容
        public void EnsureForwardCompatibility()
        {
            if (BlackList == null)
            {
                BlackList = new List<string>(MTPGit.DefaultBlacklist);
            }
        }

        public void ResetToDefault()
        {
            Url = MTPGit.DEFAULT_URL;
            PrivateToken = MTPGit.DEFAULT_TOKEN;
            GroupId = MTPGit.DEFAULT_GROUP;
            
            LogOnConsole = true;
            ProjectId = string.Empty;
            ProjectTag = string.Empty;
            DebugMode = false;
            ShowDevTag = false;
            InstallToLocal = false;
            BlackList = new List<string>(MTPGit.DefaultBlacklist);
            
            refreshFrequency = RefreshTime.OneDay;
        }
    }
}