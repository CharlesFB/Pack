// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: AI 相关工具类
// ==================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using UnityEditor;
using System.Text.RegularExpressions;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    [System.Serializable]
    public class ChatCompletionRequest
    {
        public string model;
        public List<AIMessage> messages = new List<AIMessage>();
    }
    
    [System.Serializable]
    public class AIMessage
    {
        public string role;
        public string content;
    }

    public static class AIUtils
    {
        private const string OpenRouterBaseUrl = "https://llm-api.bgpsvc.io/v1";
        private const string ModelName = "gpt-4o";
        private static string GetApiKey() => EditorPrefs.GetString("MTPDevToolkit_AIApiKey", null);
        private static string _lastGeneratedChangelog;
        private static bool _isGenerating;
        private static System.Action<string> _onChangelogGenerated;

        public static void GenerateChangelog(List<string> commitMessages, System.Action<string> onChangelogGenerated = null, string version = null)
        {
            if (commitMessages == null || commitMessages.Count == 0)
            {
                Debug.LogError("[AIUtils] No commit messages found");
                return;
            }

            if (_isGenerating)
            {
                Debug.LogWarning("[AIUtils] Already generating a changelog, please wait...");
                return;
            }

            var apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("[AIUtils] API key not configured. Set it via EditorPrefs key 'MTPDevToolkit_AIApiKey'.");
                return;
            }

            _isGenerating = true;
            _lastGeneratedChangelog = null;
            _onChangelogGenerated = onChangelogGenerated;

            // Format commit messages for AI prompt
            var requestContent = FormatCommitMessagesForAI(commitMessages, version);

            // Start the coroutine
            EditorCoroutineUtility.StartCoroutine(GenerateAnswerFromAI(requestContent), null);
        }

        private static string FormatCommitMessagesForAI(List<string> commitMessages, string version = null)
        {
            if (version == null)
            {
                var window = PackagePublishWindow.s_instance;
                version = window?.packageManifestView?.GetVersion();
            }
            
            var sb = new StringBuilder();
            sb.AppendLine("请根据以下Git提交信息生成版本的ChangeLog,在回答中不用出现你的思考过程，只需要直接给出最终的ChangeLog内容。生成的格式如下：" +
                          "groups the same types of changes together, makes each version and section linkable, displays the release date for each version, and states whether the project follows Semantic Versioning. " +
                          "Use these change types: Added for new features, Changed for changes in existing functionality, Deprecated for soon-to-be removed features, Removed for features that have been removed, Fixed for bug fixes, and Security for vulnerabilities or security improvements." +
                          "and it is totally in Chinese even if some commits message are in english.");
            sb.AppendLine($"### [{version ?? "Unreleased"}] - {System.DateTime.Now.ToString("yyyy-MM-dd")}");
            foreach (var message in commitMessages)
            {
                sb.AppendLine($"- {message}");
            }

            return sb.ToString();
        }

        private static IEnumerator GenerateAnswerFromAI(string requestContent)
        {
            var messages = new List<AIMessage>();
            messages.Add(new AIMessage { role = "user", content = requestContent });
            
            var requestBody = new ChatCompletionRequest
            {
                model = ModelName,
                messages = messages
            };

            

            var jsonBody = JsonUtility.ToJson(requestBody);
            Debug.Log($"[AIUtils] Request body: {jsonBody}");

            var request = new UnityWebRequest(OpenRouterBaseUrl + "/chat/completions", "POST");
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {GetApiKey()}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("HTTP-Referer", "https://magictavern.com");
            request.SetRequestHeader("X-Title", "MagicTavern Package Manager");

            yield return request.SendWebRequest();

            while (request.result == UnityWebRequest.Result.InProgress)
            {
                yield return null;
            }

            try
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var responseText = request.downloadHandler.text;
                    // Remove all blank lines from the response
                    responseText = Regex.Replace(responseText, @"^\s*$\n|\r", "", RegexOptions.Multiline);
                    
                    // Parse the JSON response
                    var responseJson = JsonUtility.FromJson<ChatCompletionResponse>(responseText);
                    if (responseJson?.choices != null && responseJson.choices.Length > 0)
                    {
                        _lastGeneratedChangelog = responseJson.choices[0].message.content;
                        Debug.Log($"[AIUtils] Response: {_lastGeneratedChangelog}");
                        
                        // Update UI with AI-generated content
                        _onChangelogGenerated?.Invoke(_lastGeneratedChangelog);
                    }
                    else
                    {
                        Debug.LogError("[AIUtils] No choices found in the response");
                        _lastGeneratedChangelog = null;
                    }
                }
                else
                {
                    Debug.LogError($"[AIUtils] AI request failed: {request.responseCode}, Error: {request.error}");
                    Debug.LogError($"[AIUtils] Response: {request.downloadHandler.text}");
                    _lastGeneratedChangelog = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AIUtils] Exception during AI request: {e.Message}");
                _lastGeneratedChangelog = null;
            }
            finally
            {
                request.Dispose();
                _isGenerating = false;
            }
        }
    }

    [System.Serializable]
    public class ChatCompletionResponse
    {
        public Choice[] choices;
    }

    [System.Serializable]
    public class Choice
    {
        public Message message;
    }

    [System.Serializable]
    public class Message
    {
        public string content;
    }
} 