using System;
using UnityEditor;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager.Test
{
    public class RawTest
    {
        [MenuItem("MTPTest/ClearBar")]
        public static void CloseProgressBar()
        {
            EditorUtility.ClearProgressBar();
        }

        [MenuItem("MTPTest/Pull Extra Plugins List")]
        public static async void PullExtraPluginsList()
        {
            try
            {
                var service = ServiceLocator.Get<IPluginPackageService>();
                var list = await service.PullExtraPluginsListAsync();
                LM.Log($"[RawTest] PullExtraPluginsList 完成，{list?.Count ?? 0} 个插件，RefreshTime={service.ExtraPluginsRefreshTime:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception e)
            {
                LM.LogError($"[RawTest] PullExtraPluginsList 失败: {e}");
            }
        }
    }
}
