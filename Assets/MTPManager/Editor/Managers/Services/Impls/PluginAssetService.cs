// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 插件资源条目列表内存状态服务（对应 PluginAssetList / Library/plugin-assets.json）
// ==================================================

using System;
using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PluginAssetService : IPluginAssetService
    {
        private readonly Dictionary<string, List<string>> _content =
            new Dictionary<string, List<string>>();

        public List<string> Get(string pluginLocalPath)
        {
            string storedKey = ResolveContentKey(pluginLocalPath, createIfMissing: false);
            if (string.IsNullOrEmpty(storedKey))
            {
                return null;
            }

            return _content.TryGetValue(storedKey, out var paths)
                ? new List<string>(paths)
                : null;
        }

        public void Set(string pluginLocalPath, List<string> assetPaths)
        {
            string storedKey = ResolveContentKey(pluginLocalPath, createIfMissing: true);
            if (string.IsNullOrEmpty(storedKey))
            {
                return;
            }

            _content[storedKey] = assetPaths != null
                ? new List<string>(assetPaths)
                : new List<string>();
        }

        public void Append(string pluginLocalPath, List<string> assetPaths)
        {
            string storedKey = ResolveContentKey(pluginLocalPath, createIfMissing: true);
            if (string.IsNullOrEmpty(storedKey))
            {
                return;
            }

            var merged = _content.TryGetValue(storedKey, out var existing) && existing != null
                ? new List<string>(existing)
                : new List<string>();

            if (assetPaths != null)
            {
                merged.AddRange(assetPaths);
            }

            var deduped = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in merged)
            {
                if (!string.IsNullOrEmpty(path) && seen.Add(path))
                {
                    deduped.Add(path);
                }
            }

            _content[storedKey] = deduped;
        }

        public bool Remove(string pluginLocalPath)
        {
            string storedKey = ResolveContentKey(pluginLocalPath, createIfMissing: false);
            return !string.IsNullOrEmpty(storedKey) && _content.Remove(storedKey);
        }

        public Dictionary<string, List<string>> GetAll()
        {
            var clone = new Dictionary<string, List<string>>(_content.Count);
            foreach (var kvp in _content)
            {
                clone[kvp.Key] = kvp.Value != null ? new List<string>(kvp.Value) : new List<string>();
            }

            return clone;
        }

        public PluginAssetList ExportToPluginAssetList()
        {
            return new PluginAssetList
            {
                Content = GetAll()
            };
        }

        public void LoadFromPluginAssetList(PluginAssetList list)
        {
            _content.Clear();
            if (list?.Content == null)
            {
                return;
            }

            foreach (var kvp in list.Content)
            {
                string key = PathUtils.NormalizePluginKey(kvp.Key);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                _content[key] = kvp.Value != null ? new List<string>(kvp.Value) : new List<string>();
            }
        }

        public void SaveToLocal()
        {
            JsonUtils.SaveToFile(MTPPath.PLUGIN_ASSET_LIST_PATH, ExportToPluginAssetList());
        }

        public void LoadFromLocal(bool createIfNotExist = true)
        {
            var list = JsonUtils.LoadFromFile<PluginAssetList>(MTPPath.PLUGIN_ASSET_LIST_PATH, createIfNotExist);
            LoadFromPluginAssetList(list);
        }

        public List<string> GetCoveredList(List<string> addList, string excludePluginKey = null)
        {
            var covered = new List<string>();
            if (addList == null || addList.Count == 0 || _content.Count == 0)
            {
                return covered;
            }

            var recorded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kvp in _content)
            {
                if (!string.IsNullOrEmpty(excludePluginKey)
                    && string.Equals(
                        PathUtils.NormalizePluginKey(kvp.Key),
                        PathUtils.NormalizePluginKey(excludePluginKey),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pathList = kvp.Value;
                if (pathList == null)
                {
                    continue;
                }

                foreach (var path in pathList)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        recorded.Add(path);
                    }
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in addList)
            {
                if (string.IsNullOrEmpty(path) || !recorded.Contains(path) || !seen.Add(path))
                {
                    continue;
                }

                covered.Add(path);
            }

            return covered;
        }

        public List<string> GetUncoveredListToRemove(string pluginLocalPath)
        {
            var result = new List<string>();
            string storedKey = ResolveContentKey(pluginLocalPath, createIfMissing: false);
            if (string.IsNullOrEmpty(storedKey) || !_content.TryGetValue(storedKey, out var pluginAssets)
                || pluginAssets == null || pluginAssets.Count == 0)
            {
                return result;
            }

            var sharedByOthers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kvp in _content)
            {
                if (string.Equals(kvp.Key, storedKey, StringComparison.Ordinal) || kvp.Value == null)
                {
                    continue;
                }

                foreach (var path in kvp.Value)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        sharedByOthers.Add(path);
                    }
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in pluginAssets)
            {
                if (string.IsNullOrEmpty(path) || sharedByOthers.Contains(path) || !seen.Add(path))
                {
                    continue;
                }

                result.Add(path);
            }

            return result;
        }

        private string ResolveContentKey(string pluginLocalPath, bool createIfMissing)
        {
            if (string.IsNullOrEmpty(pluginLocalPath))
            {
                return null;
            }

            if (_content.ContainsKey(pluginLocalPath))
            {
                return pluginLocalPath;
            }

            string normalized = PathUtils.NormalizePluginKey(pluginLocalPath);
            foreach (var key in _content.Keys)
            {
                if (string.Equals(PathUtils.NormalizePluginKey(key), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            return createIfMissing ? normalized : null;
        }
    }
}
