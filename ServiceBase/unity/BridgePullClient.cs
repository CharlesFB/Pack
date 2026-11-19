#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
namespace ServiceBase.UnityBridge
{
    // One outbound request at a time; no public Unity port or worker-thread Unity API calls.
    internal static class BridgePullClient
    {
        internal static bool Active { get; private set; }  // 当前编辑器会话是否应继续推进网络状态机。
        private static BridgeRegistration project;  // 本次连接使用的稳定工程身份和编辑器会话身份。
        private static UnityWebRequest request;  // 唯一在途请求；完成或停止时必须释放。
        private static string operation, url, token, storageKey;  // 当前请求类型、连接凭据与回执存储键。
        private static bool registered, warned;  // 注册完成及错误日志已提示标记。
        private static int failures;  // 连续失败次数，用于计算重试退避。
        private static double nextPoll, retryAt;  // 基于编辑器运行时间的轮询与重试截止点。
        private static Receipt pendingReceipt;  // 需要发送或重发的任务结果。
        private static Receipts receipts;  // 当前会话有限长度的已执行任务记录，支持重复投递去重。
        private static readonly System.Random Random = new System.Random();  // 打散多工程的轮询时刻，减少集中请求。
        [Serializable] private class Identity { public string instance_id, session_id; }
        [Serializable] private class BridgeTask { public string id = "", kind = "", method = "", message = ""; }
        [Serializable] private class PollReply { public BridgeTask[] tasks = new BridgeTask[0]; }
        [Serializable] private class Result { public bool ok = true; public string method, message, feedback, received_at; }
        [Serializable] private class Receipt { public string instance_id, session_id, task_id; public Result result; }
        [Serializable] private class Receipts { public List<Receipt> items = new List<Receipt>(); public Receipt pending; }
        /// <summary>
        /// 初始化当前连接状态并开始注册与轮询。
        /// </summary>
        internal static void Start(
            BridgeRegistration registration // 工程与编辑器会话身份
        )
        {
            url = BridgeSettings.Url; token = BridgeSettings.Token;
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || (parsed.Scheme != "https" && !(parsed.Scheme == "http" && parsed.IsLoopback)))
            { Debug.LogError("[ServiceBase] 服务器地址必须使用 HTTPS（本机可用 HTTP）。"); return; }
            if (!parsed.IsLoopback && string.IsNullOrEmpty(token))
            { Debug.LogError("[ServiceBase] 请先在 Settings 配置设备 Token。"); return; }
            project = registration;
            storageKey = "ServiceBase.Receipts." + ServiceBaseBridge.Hash(url + project.session_id);
            try { receipts = JsonUtility.FromJson<Receipts>(SessionState.GetString(storageKey, "")); } catch { receipts = null; }
            if (receipts == null) receipts = new Receipts();
            pendingReceipt = receipts.pending;
            registered = false; warned = false; failures = 0; retryAt = 0;
            nextPoll = 0;
            Active = true;
        }
        /// <summary>
        /// 停止 Bridge 并释放尚未完成的网络请求。
        /// </summary>
        internal static void Stop()
        {
            Active = false;
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            pendingReceipt = null;
        }
        /// <summary>
        /// 构造当前工程 ID 与编辑器会话 ID。
        /// </summary>
        private static Identity Id() { return new Identity { instance_id = project.instance_id, session_id = project.session_id }; }
        /// <summary>
        /// 发送一个带设备身份的 JSON 请求，禁止重定向并设置超时。
        /// </summary>
        private static void Send(
            string action, // Bridge API 路由名称
            object data // 待序列化的请求对象
        )
        {
            operation = action;
            request = new UnityWebRequest(url + "/api/bridge/" + action + "/", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-ServiceBase-Client", "unity-bridge-v2");
            if (!string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", "Bearer " + token);
            request.redirectLimit = 0;
            request.timeout = 10;
            request.SendWebRequest();
        }
        /// <summary>
        /// 由编辑器帧循环驱动请求完成、失败退避和消息任务处理。
        /// </summary>
        internal static void Update()
        {
            if (!Active) return;
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (request != null)
                {
                    if (!request.isDone) return;
                    long status = request.responseCode;
                    string body = request.downloadHandler.text;
                    request.Dispose(); request = null;
                    if (status != 200)
                    {
                        if (status == 409) registered = false;
                        if (operation == "result" && (status == 404 || status == 410)) { pendingReceipt = null; SaveReceipts(); }
                        if (operation == "result" && status == 400) { pendingReceipt = null; SaveReceipts(); }
                        failures = Math.Min(failures + 1, 6);
                        retryAt = now + Math.Min(60, Math.Pow(2, failures)) * (.8 + Random.NextDouble() * .4);
                        if (!warned) Debug.LogWarning("[ServiceBase] 连接或请求失败（HTTP " + status + "），将退避重试。请检查工作台和凭据：" + url);
                        warned = true;
                        return;
                    }
                    failures = 0; retryAt = 0; warned = false;
                    if (operation == "register")
                    { registered = true; Debug.Log("[ServiceBase] 已连接工作台 " + url); }
                    else if (operation == "poll")
                    {
                        nextPoll = now + 5 * (.8 + Random.NextDouble() * .4);
                        var reply = JsonUtility.FromJson<PollReply>(body);
                        if (reply.tasks != null && reply.tasks.Length > 0) Handle(reply.tasks[0]);
                    }
                    else if (operation == "result") { pendingReceipt = null; SaveReceipts(); nextPoll = 0; }
                }
                if (now < retryAt) return;
                if (!registered) { Send("register", project); return; }
                if (pendingReceipt != null) { Send("result", pendingReceipt); return; }
                if (now >= nextPoll) { Send("poll", Id()); return; }
            }
            catch (Exception error)
            {
                if (request != null) { request.Abort(); request.Dispose(); request = null; }
                retryAt = now + 10;
                if (!warned) Debug.LogWarning("[ServiceBase] Bridge 请求处理失败：" + error.GetType().Name);
                warned = true;
            }
        }
        /// <summary>
        /// 处理收到的任务；已完成任务重发回执，旧快照任务返回停用说明。
        /// </summary>
        private static void Handle(
            BridgeTask task // 服务器下发的任务
        )
        {
            var previous = receipts.items.Find(item => item.task_id == task.id);
            if (previous != null) { pendingReceipt = previous; SaveReceipts(); return; }
            if (task.kind == "snapshot") { Complete(task, "包状态由独立执行器自动上报；旧 MTP 读取已停用，请连接 v9 执行器。", false); return; }
            if (task.kind != "message" || (task.method != "GET" && task.method != "POST") || string.IsNullOrEmpty(task.message) || task.message.Length > 2000)
            { Complete(task, "不支持的任务", false); return; }
            Debug.Log("[ServiceBase][" + task.method + "] " + task.message);
            Complete(task, "Mock: Unity 已收到消息并输出到 Console，未执行其他操作。");
        }
        /// <summary>
        /// 构造任务反馈并保存待发送回执，以支持域重载后的重试。
        /// </summary>
        private static void Complete(
            BridgeTask task, // 服务器下发的任务
            string feedback, // 向服务器返回的反馈文字
            bool ok = true // 任务是否成功
        )
        {
            pendingReceipt = new Receipt { instance_id=project.instance_id, session_id=project.session_id, task_id=task.id,
                result=new Result { ok=ok, method=task.method, message=task.message, feedback=feedback, received_at=DateTime.UtcNow.ToString("o") } };
            receipts.items.Add(pendingReceipt);
            if (receipts.items.Count > 64) receipts.items.RemoveAt(0);
            SaveReceipts();
        }
        /// <summary>
        /// 将有限数量的任务回执持久化到编辑器会话状态。
        /// </summary>
        private static void SaveReceipts()
        {
            receipts.pending = pendingReceipt;
            SessionState.SetString(storageKey, JsonUtility.ToJson(receipts));
        }
    }
}
#endif
