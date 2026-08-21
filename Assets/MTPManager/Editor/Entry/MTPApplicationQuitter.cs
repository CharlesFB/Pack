using UnityEditor;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    [InitializeOnLoad]
    public class MTPApplicationQuitter
    {
        static MTPApplicationQuitter()
        {
            EditorApplication.quitting += OnEditorQuitting;
        }

        /// <summary>
        /// 订阅关闭编辑器时的同步事件
        /// 快、同步、轻量
        /// </summary>
        private static void OnEditorQuitting()
        {
            LM.TimeLog("[MTPApplicationQuitter] 正在关闭 Unity Editor，处理清理工作...");
            
        }
    }
}