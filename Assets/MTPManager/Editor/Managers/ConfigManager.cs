using System;
using System.IO;
using System.Text;
using MagicTavern.FrontEngine.MTPManager.LitJson;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    public sealed class ConfigManager
    {
        private static ConfigManager s_instance;

        public static ConfigManager Instance => s_instance ??= new ConfigManager();

        // 缓存当前配置，避免频繁读取磁盘
        private MTPConfig _currentConfig;

        private string ProjectRootPath => Directory.GetParent(Application.dataPath).FullName;
        private string ConfigFilePath => Path.Combine(ProjectRootPath, MTPPath.CONFIG_PATH);

        private ConfigManager()
        {
        }

        /// <summary>
        /// 已缓存配置；未 Load 过时为 null（不读盘）。供 LogManager 等避免与 Load 循环。
        /// </summary>
        public static MTPConfig CachedOrNull => s_instance?._currentConfig;

        /// <summary>
        /// 当前配置（懒加载）。用法：ConfigManager.Instance.Current
        /// </summary>
        public MTPConfig Current
        {
            get
            {
                if (_currentConfig == null)
                {
                    _currentConfig = Load();
                }

                return _currentConfig;
            }
        }

        public MTPConfig GetDefault()
        {
            MTPConfig config = new MTPConfig();
            config.ResetToDefault();
            return config;
        }

        public double GetRefreshSecond()
        {
            switch (_currentConfig.refreshFrequency)
            {
                case RefreshTime.HalfHour :
                    return 1800;
                case RefreshTime.OneHour :
                    return 3600;
                case RefreshTime.OneDay :
                    return 86400;
                case RefreshTime.ThreeDay:
                    return 259200;
                case RefreshTime.OneWeek :
                    return 604800;
                case RefreshTime.OneMonth :
                    return 2592000;
                default:
                    return 1000;
            }
        }

        private MTPConfig Load()
        {
            if (!File.Exists(ConfigFilePath))
            {
                return CreateDefaultAndSave();
            }

            try
            {
                var bytes = Convert.FromBase64String(File.ReadAllText(ConfigFilePath));
                var jsonStr = Encoding.UTF8.GetString(bytes);
                var config = JsonMapper.ToObject<MTPConfig>(jsonStr);

                config.EnsureForwardCompatibility();
                return config;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConfigManager] 读取配置文件失败，将重置为默认值。Error: {e.Message}");
                return CreateDefaultAndSave();
            }
        }

        public void Save()
        {
            if (_currentConfig == null) return;

            var jsonStr = JsonMapper.ToJson(_currentConfig, true);
            var bytes = Encoding.UTF8.GetBytes(jsonStr);
            var base64 = Convert.ToBase64String(bytes);

            File.WriteAllText(ConfigFilePath, base64);
        }

        private MTPConfig CreateDefaultAndSave()
        {
            var config = new MTPConfig();
            config.ResetToDefault();
            _currentConfig = config;
            Save();
            return _currentConfig;
        }
    }
}
