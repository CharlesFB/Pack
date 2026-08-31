// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: DevTag 叠加层：在 Game / Scene 视图显示工程路径与 git 分支
// ==================================================

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 配置项 ShowDevTag 在 Settings 点 Save 后生效，于 GameView / SceneView 叠加开发环境信息。
    /// </summary>
    [InitializeOnLoad]
    public static class DevTagOverlay
    {
        private const string GameLabelName = "mtp-dev-tag";
        private static readonly char[] s_trimChars = { '\n', '\r', ' ' };

        private static string s_message;
        private static bool s_sceneHooked;
        private static bool s_updateHooked;
        private static double s_nextGameViewCheck;

        static DevTagOverlay()
        {
            EditorApplication.delayCall += Refresh;
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += Refresh;
        }

        /// <summary>
        /// 按当前已保存配置刷新叠加层。Settings 只有点 Save 后才会改到磁盘/内存配置。
        /// </summary>
        public static void Refresh()
        {
            bool show = ConfigManager.Instance.Current?.ShowDevTag ?? false;
            if (show)
            {
                s_message = BuildMessage();
                HookScene();
                HookUpdate();
                ApplyGameViewLabels();
            }
            else
            {
                s_message = null;
                UnhookScene();
                UnhookUpdate();
                RemoveGameViewLabels();
            }

            SceneView.RepaintAll();
            RepaintGameViews();
        }

        private static string BuildMessage()
        {
            string path = Environment.CurrentDirectory;
            string project = Path.GetFileNameWithoutExtension(path);
            string branch = GetGitBranchName(path);
            return $"当前路径:{path}\n当前项目:{project}\n当前分支:{branch}";
        }

        private static string GetGitBranchName(string projectPath)
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    WorkingDirectory = projectPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
#if UNITY_2020_2_OR_NEWER
                using var process = Process.Start(start);
                if (process == null)
                {
                    return "N/A";
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                output = output.TrimEnd(s_trimChars);
                return string.IsNullOrEmpty(output) ? "N/A" : output;
#else
                using (var process = Process.Start(start))
                {
                    if (process == null)
                    {
                        return "N/A";
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    output = output.TrimEnd(s_trimChars);
                    return string.IsNullOrEmpty(output) ? "N/A" : output;
                }
#endif
            }
            catch
            {
                return "N/A";
            }
        }

        private static void HookScene()
        {
            if (s_sceneHooked)
            {
                return;
            }

            SceneView.duringSceneGui += OnSceneGUI;
            s_sceneHooked = true;
        }

        private static void UnhookScene()
        {
            if (!s_sceneHooked)
            {
                return;
            }

            SceneView.duringSceneGui -= OnSceneGUI;
            s_sceneHooked = false;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (string.IsNullOrEmpty(s_message))
            {
                return;
            }

            Handles.BeginGUI();
            var style = new GUIStyle
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUILayout.Label(s_message, style);
            Handles.EndGUI();
        }

        private static void HookUpdate()
        {
            if (s_updateHooked)
            {
                return;
            }

            EditorApplication.update += OnEditorUpdate;
            s_updateHooked = true;
        }

        private static void UnhookUpdate()
        {
            if (!s_updateHooked)
            {
                return;
            }

            EditorApplication.update -= OnEditorUpdate;
            s_updateHooked = false;
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < s_nextGameViewCheck)
            {
                return;
            }

            s_nextGameViewCheck = EditorApplication.timeSinceStartup + 1d;
            ApplyGameViewLabels();
        }

        private static void ApplyGameViewLabels()
        {
            if (string.IsNullOrEmpty(s_message))
            {
                return;
            }

            foreach (var window in EnumerateGameViews())
            {
                var root = window.rootVisualElement;
                if (root == null)
                {
                    continue;
                }

                var label = root.Q<Label>(GameLabelName);
                if (label == null)
                {
                    label = new Label
                    {
                        name = GameLabelName,
                        pickingMode = PickingMode.Ignore,
                    };
                    label.style.position = Position.Absolute;
                    label.style.left = 8;
                    label.style.top = 8;
                    label.style.fontSize = 24;
                    label.style.color = Color.white;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.whiteSpace = WhiteSpace.Normal;
                    root.Add(label);
                }

                label.text = s_message;
            }
        }

        private static void RemoveGameViewLabels()
        {
            foreach (var window in EnumerateGameViews())
            {
                var label = window.rootVisualElement?.Q<Label>(GameLabelName);
                label?.RemoveFromHierarchy();
            }
        }

        private static void RepaintGameViews()
        {
            foreach (var window in EnumerateGameViews())
            {
                window.Repaint();
            }
        }

        private static System.Collections.Generic.IEnumerable<EditorWindow> EnumerateGameViews()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (type == null)
            {
                yield break;
            }

            var windows = Resources.FindObjectsOfTypeAll(type);
            if (windows == null)
            {
                yield break;
            }

            foreach (var obj in windows)
            {
                if (obj is EditorWindow window)
                {
                    yield return window;
                }
            }
        }
    }
}
