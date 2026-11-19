#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;
namespace ServiceBase.Upm
{
    [Serializable] internal class Target { public string migration_id, migration_digest; public string[] migration_entries; public PackageGoal previous; public string previous_build_id; public int previous_file_count; public string[] absent_packages, removed_records; public string plan_digest; public PackageGoal[] packages, protected_packages; public PluginGoal[] plugins; public string[] expected_direct; public string name, identifier, source, version, commit; public string record_key, record_digest; public string operation, plugin_id, build_id, format, recipe_id, bundle_sha256; public bool requires_destination; public string[] preview_paths; public long bundle_size; public int file_count; }
    [Serializable] internal class Work { public string job_id, attempt_id, destination_root, expected_fingerprint; public Target target; public bool recover_only; public int recheck, sequence; }
    [Serializable] internal class Evidence {
        public string migration_id, migration_digest; public string name = "", identifier = "", source = "", version = "", commit = "";
        public bool verified, removed, operation_finished; public string update_digest; public string plan_digest, project_fingerprint; public bool direct, manifest_match, lock_match; public string record_digest, recipe_id, build_id;
    }
    [Serializable] internal class Report {
        public string attempt_id, state, message = "", failure_kind = "";
        public int sequence; public Evidence evidence = new Evidence();
    }
    [Serializable] internal class Journal {
        public string migration_record_json;
        public int schema = 1;
        public int intent_protocol;
        public string server, project, manifest_before, lock_before, started_at;
        // Raw UPM diagnostics are local recovery data, like manifest_before. Never upload this field.
        public string upm_error_code = "", upm_error_message = "";
        public Work work; public Report report; public int ack, recheck;
        public string destination_root = ""; public bool destination_saved;
        public bool intent, add_finished, add_failed; public PluginState plugin; public RemovalState removal; public PluginUpdateState update;
    }
    internal static class InstallJournal
    {
        internal static void Save(string path, Journal value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(value, true));
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        internal static Journal Load(string path)
        {
            if (!File.Exists(path)) return null;
            var value = JsonUtility.FromJson<Journal>(File.ReadAllText(path));
            if (value == null || value.schema != 1 || value.work == null || value.report == null || value.work.target == null || value.report.attempt_id != value.work.attempt_id) throw new FormatException("Invalid installation journal");
            Guid id; if (!Guid.TryParse(value.work.attempt_id, out id)) throw new FormatException("Invalid journal identity");
            return value;
        }
    }
}
#endif
