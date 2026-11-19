#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;
namespace ServiceBase.Upm
{
    [InitializeOnLoad]
    public static class UpmExecutor
    {
        [Serializable] class Registration { public string instance_id, session_id, project_name, project_path, machine_name, unity_version; public int process_id; }
        [Serializable] class Sync { public string instance_id, session_id, diagnostic; public int protocol = 1, executor_version = 10; public bool migration_capable, has_legacy_preview; public LegacyPreview legacy_preview; public bool has_report, has_directory_result; public Report report; public DirectoryReply directory_result; public bool has_plugin_inventory; public PluginSnapshot plugin_inventory; public bool has_package_state; public PackageState package_state; }
        [Serializable] class Reply { public LegacyQuery legacy_request; public string inventory_request; public int ack; public Work work; public DirectoryQuery directory_request; }
        static string root, path, url, token, action, fault = "";
        static DirectoryQuery directoryQuery; static DirectoryReply directoryResult;
        static Registration identity; static Journal journal; static UnityWebRequest http;
        static AddRequest add; static ListRequest preflight;
        static bool registered, fatal, loaded, synchronized, packageTurn; static double nextNetwork, quietSince, lastVerify;
        // Read-only busy state shared by independent package and plugin scans.
        public static bool Busy { get { return fatal || (journal != null && !Terminal(journal.report.state)); } }
        static bool Terminal(string state) { return state == "succeeded" || state == "failed" || state == "cancelled"; }
        [MenuItem("Tools/ServiceBase/UPM/Reconnect and Recheck")]
        public static void Reconnect()
        {
            // Recover persisted intent, never reset it to an unstarted operation.
            if (PackageRemover.Busy || (add != null && !add.IsCompleted) || (preflight != null && !preflight.IsCompleted)) return;
            add = null; preflight = null; PackageRemover.Reset();
            Close(); fatal = false; fault = ""; loaded = false; registered = false; synchronized = false; nextNetwork = 0;
        }
        static UpmExecutor()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Close;
            EditorApplication.quitting += Close;
        }
        static string Hash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        static void Initialize()
        {
            root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            path = Path.Combine(root, ".servicebase/upm/active.json");
            url = (Environment.GetEnvironmentVariable("SERVICEBASE_URL") ?? EditorPrefs.GetString("ServiceBase.Url", "http://127.0.0.1:8000")).TrimEnd('/');
            token = Environment.GetEnvironmentVariable("SERVICEBASE_TOKEN") ?? EditorPrefs.GetString("ServiceBase.Token", "");
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) || (!uri.IsLoopback && string.IsNullOrEmpty(token))) throw new InvalidOperationException("Invalid service configuration");
            string machine = EditorPrefs.GetString("ServiceBase.MachineId", "");
            if (machine == "") { machine = Guid.NewGuid().ToString("N"); EditorPrefs.SetString("ServiceBase.MachineId", machine); }
            string session = SessionState.GetString("ServiceBase.Bridge.Session", "");
            if (session == "") { session = Guid.NewGuid().ToString("N"); SessionState.SetString("ServiceBase.Bridge.Session", session); }
            identity = new Registration { instance_id = Hash(machine + "\n" + root.Replace('\\','/')), session_id = session,
                project_name = new DirectoryInfo(root).Name, project_path = root.Replace('\\','/'), machine_name = Environment.MachineName,
                unity_version = Application.unityVersion, process_id = System.Diagnostics.Process.GetCurrentProcess().Id };
            journal = InstallJournal.Load(path);
            if (journal != null && (journal.server != url || journal.project != identity.instance_id)) throw new InvalidOperationException("Installation journal belongs to another server or project");
            if (journal != null && (journal.intent_protocol != 1 || File.Exists(IntentPath(journal.work.attempt_id)))) journal.intent = true;
            synchronized = false;
            loaded = true; quietSince = EditorApplication.timeSinceStartup;
        }
        static void Close() { PluginImporter.Close(); PluginRemover.Close(); PluginUpdater.Close(); if (http != null) { http.Abort(); http.Dispose(); http = null; } }
        static void Send(string route, object body)
        {
            action = route;
            http = new UnityWebRequest(url + "/api/bridge/" + route + "/", "POST");
            http.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            http.downloadHandler = new DownloadHandlerBuffer(); http.timeout = 15; http.redirectLimit = 0;
            http.SetRequestHeader("Content-Type", "application/json"); http.SetRequestHeader("X-ServiceBase-Client", "unity-bridge-v2");
            if (!string.IsNullOrEmpty(token)) http.SetRequestHeader("Authorization", "Bearer " + token);
            http.SendWebRequest();
        }
        static void Tick()
        {
            if (fatal && identity == null) return;
            try {
                if (loaded && (url!=(Environment.GetEnvironmentVariable("SERVICEBASE_URL") ?? EditorPrefs.GetString("ServiceBase.Url", "http://127.0.0.1:8000")).TrimEnd('/') || token!=(Environment.GetEnvironmentVariable("SERVICEBASE_TOKEN") ?? EditorPrefs.GetString("ServiceBase.Token", "")))) {
                    Reconnect(); if(loaded)return;
                }
                if (!loaded) Initialize();
                bool enabled = SessionState.GetBool("ServiceBase.Bridge.Enabled", true);
                if (!fatal && journal != null) Advance(); // A stop pauses new starts, but preserves/verifies work already started.
                if (!enabled) { Close(); registered = false; return; }
                if (http != null) {
                    if (!http.isDone) return;
                    long status = http.responseCode; string body = http.downloadHandler.text;
                    http.Dispose(); http = null; nextNetwork = EditorApplication.timeSinceStartup + 5;
                    if (status != 200) {
                        if (status == 409) registered = false;
                        if (status == 400) { Debug.LogError("[ServiceBase UPM] 同步回执被拒绝，日志已保留，请检查服务端安装记录。"); nextNetwork += 25; }
                        return;
                    }
                    if (action == "register") registered = true;
                    else {
                        var reply = JsonUtility.FromJson<Reply>(body);
                        LegacyMigration.Reply=null; LegacyMigration.Request(root,reply.legacy_request);
                        directoryResult = null; directoryQuery = reply.directory_request; PluginInventory.Request(reply.inventory_request);
                        if (fatal) return; // A memory-only or corrupt journal must never be reported as durable progress.
                        if (journal != null && reply.ack > journal.ack) { journal.ack = reply.ack; Save(); }
                        if (journal != null && Terminal(journal.report.state) && journal.ack >= journal.report.sequence) {
                            InstallJournal.Save(Path.Combine(root, ".servicebase/upm/history/" + journal.work.attempt_id + ".json"), journal);
                            File.Delete(path); journal = null; synchronized = false;
                        }
                        if (reply.work != null && !string.IsNullOrEmpty(reply.work.attempt_id)) Accept(reply.work);
                        else if (journal != null && !Terminal(journal.report.state)) {
                            fatal = true; fault = "服务器未确认当前本地任务，已停止自动操作，请核对两端记录。";
                        }
                    }
                }
                LegacyMigration.Tick(root,Busy); PluginInventory.Tick(root, Busy); ProjectPackageState.Tick(root, Busy);
                if (EditorApplication.timeSinceStartup < nextNetwork) return;
                if (!registered) Send("register", identity);
                else {
                    if (directoryQuery != null && !string.IsNullOrEmpty(directoryQuery.request_id) && !EditorApplication.isCompiling && !EditorApplication.isUpdating) {
                        directoryResult = ProjectDirectories.Read(root, directoryQuery); directoryQuery = null;
                    }
                    packageTurn=!packageTurn;
                    Send("upm/sync", new Sync { instance_id=identity.instance_id, session_id=identity.session_id,
                    migration_capable=!fatal,has_legacy_preview=LegacyMigration.Reply!=null,legacy_preview=LegacyMigration.Reply,
                    diagnostic=fatal ? fault : MtpBoundary.Check(root), has_report=!fatal && journal != null,
                    report=fatal || journal == null ? null : journal.report,
                    has_directory_result=directoryResult != null, directory_result=directoryResult,
                    has_plugin_inventory=!Busy && !packageTurn && PluginInventory.Snapshot!=null, plugin_inventory=Busy || packageTurn ? null : PluginInventory.Snapshot,
                    has_package_state=!Busy && packageTurn && ProjectPackageState.Snapshot!=null, package_state=Busy || !packageTurn ? null : ProjectPackageState.Snapshot });
                }
            } catch (Exception error) {
                fatal = true; Close();
                loaded = identity != null;
                fault = "本地执行器已停止自动操作，保留日志等待核对：" + error.GetType().Name;
                nextNetwork = EditorApplication.timeSinceStartup + 5;
                Debug.LogError("[ServiceBase UPM] " + fault);
            }
        }
        static void Save() { InstallJournal.Save(path, journal); }
        static void Report(string state, string message, string failure = "")
        {
            if((state=="succeeded" || (state=="failed" && journal.work.target.operation=="plugin")) && !string.IsNullOrEmpty(journal.work.expected_fingerprint)) journal.report.evidence.project_fingerprint=ProjectPackageState.Fingerprint(root);
            if(state=="succeeded") ProjectPackageState.Dirty();
            journal.report = new Report { attempt_id=journal.work.attempt_id, sequence=journal.report.sequence + 1, state=state, message=message, failure_kind=failure, evidence=journal.report.evidence };
            Save(); nextNetwork = 0;
        }
        static void Accept(Work work)
        {
            if (journal != null) {
                if (journal.work.attempt_id != work.attempt_id || JsonUtility.ToJson(journal.work.target) != JsonUtility.ToJson(work.target)) throw new InvalidOperationException("Conflicting installation command");
                if (!string.IsNullOrEmpty(work.destination_root)) {
                    if (!string.IsNullOrEmpty(journal.destination_root) && journal.destination_root != work.destination_root) throw new InvalidOperationException("Conflicting ZIP destination");
                    journal.destination_root = work.destination_root; journal.destination_saved = true; Save();
                }
                if((journal.work.expected_fingerprint??"")!=(work.expected_fingerprint??"")) throw new InvalidOperationException("Conflicting project baseline");
                journal.work.destination_root = work.destination_root;
                synchronized = true;
                if (work.sequence > journal.report.sequence) {
                    journal.intent = true; journal.report.sequence = work.sequence;
                    Report("verifying", "本地进度落后于服务，只核对、不重装。");
                }
                if (work.recheck > journal.recheck) {
                    journal.recheck = work.recheck; Save();
                    if (!Terminal(journal.report.state) && add == null && preflight == null && !PackageRemover.Busy) Report("verifying", "按请求重新核对实际安装状态；不会重新发起安装。");
                }
                return;
            }
            Guid id; if (!Guid.TryParse(work.attempt_id, out id)) throw new FormatException("Invalid attempt ID");
            string archive = Path.Combine(root, ".servicebase/upm/history/" + work.attempt_id + ".json");
            journal = InstallJournal.Load(archive);
            if (journal != null) { Save(); synchronized = true; return; }
            journal = new Journal { destination_root=work.destination_root ?? "", destination_saved=!string.IsNullOrEmpty(work.destination_root), intent_protocol=1, server=url, project=identity.instance_id, work=work, recheck=work.recheck, started_at=DateTime.UtcNow.ToString("o"), intent=work.recover_only,
                report=new Report { attempt_id=work.attempt_id, sequence=work.sequence, state="accepted" } };
            Report(work.recover_only ? "verifying" : "accepted", work.recover_only ? "首次派发已有记录但本地日志缺失，只核对、不重装。" : "已保存单包安装任务，等待编辑器空闲。");
            if (File.Exists(IntentPath(work.attempt_id))) journal.intent = true;
            synchronized = true;
        }
        static string IntentPath(string attempt) { return Path.Combine(root, ".servicebase/upm/intents/" + attempt); }
        static void MarkIntent()
        {
            string intentPath = IntentPath(journal.work.attempt_id);
            Directory.CreateDirectory(Path.GetDirectoryName(intentPath));
            using (var marker = new FileStream(intentPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                byte[] fingerprint = Encoding.UTF8.GetBytes(Hash(url + identity.instance_id + JsonUtility.ToJson(journal.work.target)));
                marker.Write(fingerprint, 0, fingerprint.Length); marker.Flush(true);
            }
        }
        static void Advance()
        {
            if(!synchronized || Terminal(journal.report.state))return;
            if(journal.report.state=="needs_review") {
                if(journal.work.target.operation!="plugin")return;
                Report("verifying","按新版规则恢复插件执行记录，不重新导入。");
            }
            if(!EditorApplication.isCompiling && !EditorApplication.isUpdating && !ProjectPackageState.BeforeStart(journal,root,Report))return;
            if(journal.work.target.operation=="legacy_migrate") { LegacyMigration.Advance(journal,root,Save,Report,MarkIntent);return; }
            if(journal.work.target.operation=="upm_remove") {
                PackageRemover.Advance(journal,root,Save,Report,MarkIntent);return;
            }
            if(journal.work.target.operation=="plugin_update") {
                PluginUpdater.Advance(journal,root,url,token,identity.instance_id,identity.session_id,Save,Report,MarkIntent);return;
            }
            if(journal.work.target.operation=="plan_verify") {
                ProjectPackageState.VerifyPlan(journal,root,Report);return;
            }
            if (journal.work.target.operation == "plugin_remove") {
                if (synchronized && !Terminal(journal.report.state)) PluginRemover.Advance(journal, root, url, token,
                    identity.instance_id, identity.session_id, Save, Report, () => MarkIntent());
                return;
            }
            if (journal.work.target.operation == "plugin") {
                if (synchronized && !Terminal(journal.report.state)) PluginImporter.Advance(journal, root, url, token,
                    identity.instance_id, identity.session_id, Save, Report, () => MarkIntent());
                return;
            }
            if (!synchronized || Terminal(journal.report.state)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) { quietSince = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - quietSince < 3) return;
            if (add != null) {
                if (!add.IsCompleted) return;
                journal.add_finished = true; journal.add_failed = add.Status == StatusCode.Failure;
                if(journal.add_failed) UpmDiagnostics.Remember(journal,add.Error==null?"Unknown":add.Error.errorCode.ToString(),add.Error==null?null:add.Error.message);
                add = null;
                Report("verifying", "UPM 请求已结束，核对 manifest、lock 和已注册包。");
                quietSince = EditorApplication.timeSinceStartup; return;
            }
            if (journal.report.state == "accepted" && !journal.intent) {
                if (!SessionState.GetBool("ServiceBase.Bridge.Enabled", true)) return;
                if (AssetsPackages.Read(root).Any(p=>p.name==journal.work.target.name)) { Report("failed","Assets 中存在同名本地包，尚未调用安装。","preflight"); return; }
                string boundary = MtpBoundary.Check(root);
                if (boundary != "") { Report("failed", boundary, "preflight"); return; }
                if (preflight == null) { preflight = Client.List(true, true); return; }
                if (!preflight.IsCompleted) return;
                bool valid = preflight.Status == StatusCode.Success;
                if(!valid) UpmDiagnostics.Remember(journal,preflight.Error==null?"Unknown":preflight.Error.errorCode.ToString(),preflight.Error==null?null:preflight.Error.message);
                preflight = null;
                if (!valid) { Report("failed", "UPM 预检查失败，尚未调用安装。"+UpmDiagnostics.Detail(journal), "preflight"); return; }
                Evidence existing;
                if (Verify(out existing)) { journal.report.evidence = existing; Report("succeeded", "指定包版本已满足，未重复安装。"); return; }
                // Baseline may include authenticated dependency URLs: keep it local, never upload.
                journal.manifest_before = File.ReadAllText(Path.Combine(root,"Packages/manifest.json"));
                string lockPath = Path.Combine(root,"Packages/packages-lock.json");
                journal.lock_before = File.Exists(lockPath) ? File.ReadAllText(lockPath) : "";
                ValidateTarget(journal.work.target);
                // A separate immutable marker prevents an older restored active.json from reissuing Add.
                MarkIntent();
                journal.started_at = DateTime.UtcNow.ToString("o");
                journal.intent = true;
                Report("running", "已持久化安装意图，正在执行一次 UPM Add。");
                Debug.Log("[ServiceBase UPM] Start Add " + journal.work.attempt_id);
                add = Client.Add(journal.work.target.identifier);
                return;
            }
            if (journal.report.state == "needs_review") return;
            if (EditorApplication.timeSinceStartup - lastVerify < 5) return;
            lastVerify = EditorApplication.timeSinceStartup;
            Evidence evidence;
            if (Verify(out evidence)) { journal.report.evidence = evidence; Report("succeeded", "目标包的 manifest、lock 与注册信息一致。"); }
            else if (journal.add_finished && journal.add_failed) Report("failed", UpmDiagnostics.Failure(journal,root), "add_completed");
            else {
                DateTime started;
                if (DateTime.TryParse(journal.started_at, null, System.Globalization.DateTimeStyles.RoundtripKind, out started) && (DateTime.UtcNow - started.ToUniversalTime()).TotalMinutes < 10) {
                    if (journal.report.state != "verifying") Report("verifying", "等待注册信息与 lock 稳定，只核对、不重装。");
                } else Report("failed", "UPM 安装结束后仍未达到目标，停止本次安装计划；不会自动重装。", "package_verify");
            }
        }
        static void ValidateTarget(Target target)
        {
            if (target == null || string.IsNullOrEmpty(target.name) || string.IsNullOrEmpty(target.identifier)) throw new FormatException("Invalid target");
            if (target.name == "com.magictavern.mtpmanager") throw new InvalidOperationException("MTP self-update is not supported by this executor");
            if ((target.source == "registry" || target.source == "builtin") && target.identifier == target.name + "@" + target.version && !string.IsNullOrEmpty(target.version)) return;
            Uri uri;
            if (target.source == "git" && Uri.TryCreate(target.identifier, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.UserInfo == "" && uri.Fragment == "#" + target.commit && target.commit.Length == 40) return;
            throw new FormatException("Invalid fixed package target");
        }
        static bool Verify(out Evidence evidence)
        {
            evidence = new Evidence();
            try {
                Target target = journal.work.target;
                var manifest = JsonMap.Object(JsonMap.Read(File.ReadAllText(Path.Combine(root,"Packages/manifest.json"))), "dependencies");
                var dependencies = JsonMap.Object(JsonMap.Read(File.ReadAllText(Path.Combine(root,"Packages/packages-lock.json"))), "dependencies");
                var entry = JsonMap.Object(dependencies, target.name);
                var info = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == target.name);
                if (info == null) return false;
                string expected = target.source != "git" ? target.version : target.identifier;
                bool registry = target.source != "git";
                evidence = new Evidence { name=info.name, identifier=target.identifier, source=target.source, version=info.version,
                    commit=registry ? "" : (info.git == null ? "" : info.git.hash), direct=info.isDirectDependency,
                    manifest_match=JsonMap.String(manifest,target.name) == expected,
                    lock_match=JsonMap.String(entry,"source") == target.source && JsonMap.String(entry,"version") == expected && (registry || JsonMap.String(entry,"hash") == target.commit) };
                return evidence.direct && evidence.manifest_match && evidence.lock_match &&
                    (registry ? info.source == (target.source=="builtin"?PackageSource.BuiltIn:PackageSource.Registry) && info.version == target.version : info.source == PackageSource.Git && evidence.commit == target.commit);
            } catch (Exception) { return false; }
        }
    }
}
#endif
