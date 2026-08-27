// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: 插件资源条目列表内存状态服务（对应 PluginAssetList / Library/plugin-assets.json）
// ==================================================

using System;
using System.Collections.Generic;
using System.IO;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PluginAssetService : IPluginAssetService
    {
        private readonly Dictionary<string, List<string>> _content =
            new Dictionary<string, List<string>>();

        public List<string> Get(string pluginKey)
        {
            string storedKey = ResolveContentKey(pluginKey, createIfMissing: false);
            if (string.IsNullOrEmpty(storedKey))
            {
                return null;
            }

            return _content.TryGetValue(storedKey, out var paths)
                ? new List<string>(paths)
                : null;
        }

        public void Set(string pluginKey, List<string> assetPaths)
        {
            string storedKey = ResolveContentKey(pluginKey, createIfMissing: true);
            if (string.IsNullOrEmpty(storedKey))
            {
                return;
            }

            _content[storedKey] = assetPaths != null
                ? new List<string>(assetPaths)
                : new List<string>();
        }

        public void Append(string pluginKey, List<string> assetPaths)
        {
            string storedKey = ResolveContentKey(pluginKey, createIfMissing: true);
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

        public bool Remove(string pluginKey)
        {
            string storedKey = ResolveContentKey(pluginKey, createIfMissing: false);
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
                string key = kvp.Key;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                _content[key] = kvp.Value != null ? new List<string>(kvp.Value) : new List<string>();
            }
        }

        public void Clear()
        {
            _content.Clear();
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
                    && PathUtils.PluginKeysEqual(kvp.Key, excludePluginKey))
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

        public List<string> GetUncoveredListToRemove(string pluginKey)
        {
            var result = new List<string>();
            string storedKey = ResolveContentKey(pluginKey, createIfMissing: false);
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

        public int PruneMissingPlugins(string packageName, IEnumerable<string> keepFileNames)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return 0;
            }

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (keepFileNames != null)
            {
                foreach (var name in keepFileNames)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        keep.Add(Path.GetFileName(name));
                    }
                }
            }

            var toRemove = new List<string>();
            foreach (var key in _content.Keys)
            {
                if (!PathUtils.TryParsePluginKey(key, out var pkg, out var fileName))
                {
                    continue;
                }

                if (!string.Equals(pkg, packageName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!keep.Contains(fileName))
                {
                    toRemove.Add(key);
                }
            }

            foreach (var key in toRemove)
            {
                _content.Remove(key);
            }

            return toRemove.Count;
        }

        private string ResolveContentKey(string pluginKey, bool createIfMissing)
        {
            if (string.IsNullOrEmpty(pluginKey))
            {
                return null;
            }

            if (_content.ContainsKey(pluginKey))
            {
                return pluginKey;
            }

            foreach (var key in _content.Keys)
            {
                if (PathUtils.PluginKeysEqual(key, pluginKey))
                {
                    return key;
                }
            }

            return createIfMissing ? pluginKey : null;
        }
    }
}
