// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 命令行工具，用于运行命令行指令
// ==================================================

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 命令行工具，用于运行命令行指令
    /// </summary>
    public static class ShellUtils
    {
        private static string ShellApp
        {
            get
            {
#if UNITY_EDITOR_WIN
			    string app = "cmd.exe";
#elif UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                string app = "bash";
#endif
                return app;
            }
        }

        private static readonly string[] s_errorFlagStrings = new string[] { "error", "fatal", "exception", "null", "fail","not " };

        public static string ProcessCommandSync(string cmd, string workDirectory, Dictionary<string, string> envVars = null)
        {
            var start = new ProcessStartInfo(ShellApp);

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            start.Arguments = "-c";
#elif UNITY_EDITOR_WIN
		    start.Arguments = "/c";
#endif

            if (envVars != null)
            {
                foreach (var kv in envVars)
                {
                    if (start.EnvironmentVariables.ContainsKey(kv.Key))
                    {
                        start.EnvironmentVariables[kv.Key] = kv.Value;
                    }
                    else
                    {
                        start.EnvironmentVariables.Add(kv.Key, kv.Value);
                    }
                }
            }

            start.Arguments += (" \"" + cmd + " \"");
            start.CreateNoWindow = true;
            start.ErrorDialog = true;
            start.UseShellExecute = false;
            start.WorkingDirectory = workDirectory;
            start.RedirectStandardOutput = true;
            // CODE-10: 使用局部变量而非静态字段，避免并发命令互相干扰
            var output = new StringBuilder();
            start.RedirectStandardError = true;
            start.RedirectStandardInput = false;
            start.StandardOutputEncoding = Encoding.UTF8;

            var p = new Process();
            p.StartInfo = start;
            p.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.AppendLine(e.Data);
                    UnityEngine.Debug.Log(e.Data);
                }
            };
            p.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    bool isErrorLog = false;
                    foreach (var flag in s_errorFlagStrings)
                    {
                        if (e.Data.ToLower().Contains(flag))
                        {
                            isErrorLog = true;
                            break;
                        }
                    }

                    if (isErrorLog)
                        UnityEngine.Debug.LogError(e.Data);
                    else
                        UnityEngine.Debug.Log(e.Data);
                }
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            UnityEngine.Debug.LogFormat("Finish running : {0}", cmd);
            return output.ToString();
        }
    }
}
