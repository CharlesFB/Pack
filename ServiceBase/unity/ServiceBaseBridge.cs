#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ServiceBase.UnityBridge
{
    [InitializeOnLoad]
    public static class ServiceBaseBridge
    {
        public static string DashboardUrl { get { return BridgeSettings.Url; } }
        private const string EnabledKey = "ServiceBase.Bridge.Enabled";  // 域重载期间保留用户手动停止状态。
        private const string SessionKey = "ServiceBase.Bridge.Session";  // 同一次编辑器运行的会话键，域重载不会改变会话身份。
        /// <summary>
        /// 注册编辑器生命周期事件，在允许自动启动时延迟连接服务。
        /// </summary>
        static ServiceBaseBridge()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BridgePullClient.Stop;
            EditorApplication.quitting += BridgePullClient.Stop;
            EditorApplication.update += BridgePullClient.Update;
            EditorApplication.delayCall += () => { if (SessionState.GetBool(EnabledKey, true)) Start(); };
        }
        [MenuItem("Tools/ServiceBase/Start")]
        /// <summary>
        /// 生成稳定设备工程 ID，恢复编辑器会话 ID，并启动主动连接。
        /// </summary>
        public static void Start()
        {
            if (BridgePullClient.Active) return;
            SessionState.SetBool(EnabledKey, true);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            string machine = EditorPrefs.GetString("ServiceBase.MachineId", "");
            if (machine == "") { machine = Guid.NewGuid().ToString("N"); EditorPrefs.SetString("ServiceBase.MachineId", machine); }
            string session = SessionState.GetString(SessionKey, "");
            if (session == "") { session = Guid.NewGuid().ToString("N"); SessionState.SetString(SessionKey, session); }
            BridgePullClient.Start(new BridgeRegistration {
                instance_id = Hash(machine + "\n" + root), session_id = session, project_name = new DirectoryInfo(root).Name,
                project_path = root, machine_name = Environment.MachineName, unity_version = Application.unityVersion,
                process_id = System.Diagnostics.Process.GetCurrentProcess().Id
            });
        }
        /// <summary>
        /// 将输入文本计算为稳定的 SHA-256 十六进制标识。
        /// </summary>
        internal static string Hash(
            string value // 输入字段对象或待哈希文本
        )
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        [MenuItem("Tools/ServiceBase/Stop")]
        /// <summary>
        /// 禁用当前会话的自动启动并停止连接；服务器将在心跳过期后标记离线。
        /// </summary>
        public static void Stop()
        {
            SessionState.SetBool(EnabledKey, false); BridgePullClient.Stop();
            Debug.Log("[ServiceBase] 已停止，45 秒内显示离线。");
        }
        [MenuItem("Tools/ServiceBase/Open Dashboard")]
        /// <summary>
        /// 使用系统浏览器打开当前配置的工作台地址。
        /// </summary>
        public static void OpenDashboard() { Application.OpenURL(DashboardUrl); }
    }
    [Serializable] internal class BridgeRegistration
    {
        public string instance_id, session_id, project_name, project_path, machine_name, unity_version;
        public int process_id;
    }
}
#endif
