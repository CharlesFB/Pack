using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 可持久化，映射插件身份 Key（packageName#pluginFileName）与该插件已落盘的资源路径。
    /// 映射目录见 MTPPath.PLUGIN_ASSET_LIST_PATH。
    /// </summary>
    [System.Serializable]
    public class PluginAssetList
    {
        public Dictionary<string, List<string>> Content = new Dictionary<string, List<string>>();
    }
}