// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: Unity packages-lock.json 读取服务
// ==================================================

using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PackagesLockService : IPackagesLockService
    {
        private readonly Dictionary<string, PackagesLock.LockInfo> _dependencies =
            new Dictionary<string, PackagesLock.LockInfo>();

        public PackagesLock.LockInfo Get(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            return _dependencies.GetValueOrDefault(packageName);
        }

        public Dictionary<string, PackagesLock.LockInfo> GetAll()
        {
            return new Dictionary<string, PackagesLock.LockInfo>(_dependencies);
        }

        public void LoadFromLocal()
        {
            LoadFromLocalFile();
        }

        public PackagesLock LoadFromLocalFile()
        {
            _dependencies.Clear();

            // createIfNotExist=false：本地文件由 UPM 维护，不存在时不由 MTP 创建空文件
            var lockData = JsonUtils.LoadFromFile<PackagesLock>(MTPPath.PACKAGE_LOCK_PATH, false);
            if (lockData?.dependencies == null)
            {
                return lockData;
            }

            foreach (var kvp in lockData.dependencies)
            {
                _dependencies[kvp.Key] = kvp.Value;
            }

            return lockData;
        }
    }
}
