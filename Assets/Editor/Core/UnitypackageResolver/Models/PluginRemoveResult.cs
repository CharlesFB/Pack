// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: unitypackage 资源移除操作结果
// ==================================================

using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PluginRemoveResult
    {
        public int RemovedCount;
        public int SkippedCount;
        public List<string> FailedPaths = new List<string>();
        public List<string> SkippedPaths = new List<string>();

        public void Merge(PluginRemoveResult other)
        {
            if (other == null)
            {
                return;
            }

            RemovedCount += other.RemovedCount;
            SkippedCount += other.SkippedCount;
            if (other.FailedPaths != null)
            {
                FailedPaths.AddRange(other.FailedPaths);
            }

            if (other.SkippedPaths != null)
            {
                SkippedPaths.AddRange(other.SkippedPaths);
            }
        }
    }
}
