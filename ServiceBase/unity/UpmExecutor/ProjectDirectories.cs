#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
namespace ServiceBase.Upm
{
    [Serializable] internal class DirectoryQuery { public string request_id, path; }
    [Serializable] internal class DirectoryReply {
        public string request_id, path, error = "";
        public string[] folders = new string[0];
        public bool truncated;
    }
    internal static class ProjectDirectories
    {
        // Read one level only; never enumerate project files or follow links.
        internal static DirectoryReply Read(string root, DirectoryQuery query) {
            var reply = new DirectoryReply { request_id=query.request_id, path=query.path };
            try {
                Guid id;
                if (!Guid.TryParse(query.request_id,out id) || string.IsNullOrEmpty(query.path)
                    || query.path.Contains("\\") || query.path.Contains(":")) throw new IOException();
                var absolute=Path.Combine(root,query.path);
                if (PluginImporter.ValidateDestination(root,absolute)!=query.path) throw new IOException();
                var folders=new List<string>(); int scanned=0;
                foreach (var path in Directory.EnumerateDirectories(absolute)) {
                    if (++scanned>2000 || folders.Count>=200) { reply.truncated=true; break; }
                    try { folders.Add(PluginImporter.ValidateDestination(root,path)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                folders.Sort(StringComparer.OrdinalIgnoreCase); reply.folders=folders.ToArray();
            } catch (Exception) {
                reply.error="无法读取目录：请选择当前工程 Assets 内已存在、无符号链接的资源目录。";
            }
            return reply;
        }
    }
}
#endif
