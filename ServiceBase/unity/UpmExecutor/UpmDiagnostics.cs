#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
namespace ServiceBase.Upm
{
    internal static class UpmDiagnostics
    {
        internal static void Remember(Journal journal,string code,string message) {
            journal.upm_error_code=code ?? "Unknown";
            journal.upm_error_message=message ?? "UPM 未提供具体错误";
        }
        internal static string PublicMessage(string message) {
            // UPM/Git errors can echo authenticated remotes and HTTP headers. Keep the
            // full diagnostic local; only this bounded, redacted text reaches the web.
            var text=message ?? "";
            text=Regex.Replace(text,@"(?i)\b(?:https?|ssh|git)://[^\s""'<>]+","[仓库地址已隐藏]");
            text=Regex.Replace(text,@"(?i)\bgit@[^\s""'<>]+","[仓库地址已隐藏]");
            text=Regex.Replace(text,@"(?im)\b(?:proxy-)?authorization\s*[:=][^\r\n]*","Authorization: [已隐藏]");
            text=Regex.Replace(text,@"(?i)\b(?:password|passwd|token|access_token|private_token|secret)\b\s*[=:]\s*(?:""[^""]*""|'[^']*'|[^\s,;]+)","[凭据已隐藏]");
            text=Regex.Replace(text,@"\bglpat-[A-Za-z0-9_-]+","[凭据已隐藏]");
            text=Regex.Replace(text,@"[\x00-\x1f\x7f]+"," ").Trim();
            return text.Length>1200 ? text.Substring(0,1200)+"…" : text;
        }
        internal static string Detail(Journal journal) {
            var message=PublicMessage(journal.upm_error_message);
            return string.IsNullOrEmpty(message) ? "旧执行器未保存 UPM 原始错误，请查 Unity Package Manager 日志。" :
                "UPM 错误（"+PublicMessage(journal.upm_error_code)+"）："+message;
        }
        internal static string Failure(Journal journal,string root) {
            var detail=Detail(journal);
            try {
                var manifest=File.ReadAllText(Path.Combine(root,"Packages/manifest.json"));
                var path=Path.Combine(root,"Packages/packages-lock.json");
                var locked=File.Exists(path)?File.ReadAllText(path):"";
                return detail+(manifest==journal.manifest_before && locked==journal.lock_before ?
                    "；目标未满足，manifest/lock 与安装前一致。" : "；目标未满足，manifest/lock 已有变化，请检查工程。");
            } catch(IOException) { return detail+"；无法读取 manifest/lock，请检查工程。"; }
            catch(UnauthorizedAccessException) { return detail+"；无法读取 manifest/lock，请检查文件权限。"; }
        }
    }
}
#endif
