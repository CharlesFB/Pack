using System;
using System.Globalization;
using System.IO;
using MagicTavern.FrontEngine.MTPManager.LitJson;
using UnityEditor;
using UnityEngine;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class JsonUtils
    {
        public static T LoadFromFile<T>(string filePath, bool createIfNotExist = true) where T : new()
        {
            if (File.Exists(filePath))
            {
                return JsonMapper.ToObject<T>(File.ReadAllText(filePath));
            }

            if (createIfNotExist)
            {
                return new T();
            }

            return default(T);
        }

        public static void SaveToFile<T>(string filePath, T data)
        {
            if (data == null)
            {
                LM.LogError($"[JsonUtils] SaveToFile {filePath} ,data == null!");
                return;
            }

            var content = JsonMapper.ToJson(data, true);
            // FIX: fix line ending with LF
            content = content.Replace("\r\n", "\n");

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, content);
            // using (StreamWriter sw = new StreamWriter(filePath))
            // {
            //     sw.NewLine = "\n";
            //     sw.Write(content);
            // }
            EditorPrefs.SetString(filePath, DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
        }
    }
}