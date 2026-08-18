namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class MTPPath
    {
        // ----- 不可恢复文件，存放在Packages目录下，随git同步 -----
        public const string PACKAGE_MANIFEST_PATH = "Packages/manifest.json";               //unity manifest.json 路径
        public const string PACKAGE_LOCK_PATH = "Packages/packages-lock.json";              //unity package-lock.json 路径
        
        public const string CONFIG_PATH = "Packages/MTPConfig.byte";                        //MTP配置文件持久化路径
        public const string INSTALLED_MTP_INFO_PATH = "Packages/upm-install.json";          //依赖树根节点，随git项目走，用于初始化构建依赖树
        public const string LOCAL_MTP_INFO_PATH = "Packages/upm-local.json";                //内嵌包信息管理
        
        // ----- 可恢复文件，用户可以删除Library目录重构，以同步内容 -----
        public const string PROJECT_INFO_LIST_PATH = "Library/MTPCache/upm-list.json";      //工程信息路径
        public const string PACKAGE_INFO_CACHE_PATH = "Library/MTPCache/upm-cache.json";    //全部MTP工程的每一个版本package.json信息
        public const string PLUGIN_ASSET_LIST_PATH = "Library/MTPCache/plugin-assets.json"; //插件资源条目记录
        public const string MT_PACKAGE_INFO_LIST_PATH = "Library/MTPCache/upm-dependencies.json";   //MTP依赖树持久化路径
        public const string PLUGIN_STAGING_ROOT = "Library/MTPCache/MTPPluginStaging";      //UPM 安装前插件暂存根目录
        
        public const string PACKAGE_CACHE_ROOT = "Library/PackageCache";                    //Unity存放项目依赖包的位置
        
        // ----- MTP日志缓存 -----
        public const string LOG_PATH = "Library/MTPCache/MTPLog.log";                       //MTP日志
        
        // ----- 临时持久化路径，MTP记录状态与进度用 -----
        
        // ----- 约定目录段 -----
        public const string PLUGINS_PACKAGE_DIR = "Editor/PluginsPackage";                  //包内约定插件根目录
    }
}
