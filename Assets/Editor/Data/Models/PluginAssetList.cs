using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 可持久化，映射项目记录导入的每一条 “资源包路径” 与 “该资源包内所有资源项目路径”
    /// 映射目录见 MTPPath.PLUGIN_ASSET_LIST_PATH（Library/plugin-assets.json）
    /// 相关服务见 IPluginAssetListService
    /// </summary>
    [System.Serializable]
    public class PluginAssetList
    {
        public Dictionary<string, List<string>> Content = new Dictionary<string, List<string>>();
    }
}