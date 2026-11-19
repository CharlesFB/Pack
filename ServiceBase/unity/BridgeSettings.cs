#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
namespace ServiceBase.UnityBridge
{
    // Local preferences/environment only; never put device credentials in project Git.
    internal class BridgeSettings : EditorWindow
    {
        internal static string Url { get { return (Environment.GetEnvironmentVariable("SERVICEBASE_URL") ?? EditorPrefs.GetString("ServiceBase.Url", "http://127.0.0.1:8000")).TrimEnd('/'); } }
        internal static string Token { get { return Environment.GetEnvironmentVariable("SERVICEBASE_TOKEN") ?? EditorPrefs.GetString("ServiceBase.Token", ""); } }
        private string url, token;
        [MenuItem("Tools/ServiceBase/Settings")]
        /// <summary>
        /// 打开 ServiceBase 连接设置窗口。
        /// </summary>
        private static void Open() { GetWindow<BridgeSettings>("ServiceBase"); }
        /// <summary>
        /// 载入当前有效地址和设备凭据，作为窗口初始值。
        /// </summary>
        private void OnEnable() { url = Url; token = Token; }
        /// <summary>
        /// 绘制连接表单；保存前验证地址，保存后重新连接服务。
        /// </summary>
        private void OnGUI()
        {
            GUILayout.Label("服务器连接");
            url = EditorGUILayout.TextField("服务器 URL", url);
            token = EditorGUILayout.PasswordField("设备 Token", token);
            EditorGUILayout.HelpBox("远端需要 HTTPS 和设备 Token。环境变量 SERVICEBASE_URL / SERVICEBASE_TOKEN 优先于本机设置。", MessageType.Info);
            if (GUILayout.Button("保存并重新连接"))
            {
                Uri parsed;
                if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || (parsed.Scheme != "https" && !(parsed.Scheme == "http" && parsed.IsLoopback)))
                { Debug.LogError("[ServiceBase] 只允许 HTTPS 或本机 HTTP 地址。"); return; }
                if(url.TrimEnd('/')!=Url && System.IO.File.Exists(System.IO.Path.Combine(Application.dataPath,"../.servicebase/upm/active.json")))
                { Debug.LogError("[ServiceBase] 工程有未确认任务，请完成当前任务后再切换服务器。"); return; }
                ServiceBaseBridge.Stop();
                EditorPrefs.SetString("ServiceBase.Url", url.TrimEnd('/'));
                EditorPrefs.SetString("ServiceBase.Token", token.Trim());
                ServiceBaseBridge.Start();
            }
        }
    }
}
#endif
