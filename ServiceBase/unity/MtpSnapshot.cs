#if UNITY_EDITOR
using System;
using System.Collections.Generic;
namespace ServiceBase.UnityBridge
{
    [Serializable]
    internal sealed class MtpSnapshot
    {
        public int schema_version = 1;
        public string instance_id, session_id;
        public bool detected;
        public string state = "absent";
        public string detail = "未检测到已加载的 MTP 服务";
        public string source = "";
        public string catalog_state = "unavailable";
        public int catalog_count;
        public string adapter = "", mtp_version = "";
        public string captured_at = DateTime.UtcNow.ToString("o");
        public List<MtpPackageSnapshot> packages = new List<MtpPackageSnapshot>();
    }

    [Serializable]
    internal sealed class MtpPackageSnapshot
    {
        public string name, tag, revision;
        public string catalog_id = "";
        public List<string> available_tags = new List<string>();
        public string display_name = "", version = "", description = "", unity = "";
        public string author = "", license = "", changelog = "";
        public string installation_status = "unknown", installation_source = "", installed_version = "";
        public int depth;
        public bool is_local;
        public List<MtpDependencySnapshot> dependencies = new List<MtpDependencySnapshot>();
    }

    [Serializable]
    internal sealed class MtpDependencySnapshot
    {
        public string name, requested_tag;
    }

}
#endif
