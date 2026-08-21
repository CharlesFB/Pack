// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 赵杰阳
// @Maintainer: 赵杰阳
// @Description: 预编译条件变量工具
// ==================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    public static class ScriptDefineUtils
    {
        private static readonly List<BuildTargetGroup> s_buildTargetGroups = new List<BuildTargetGroup>
        {
            BuildTargetGroup.Android,
            BuildTargetGroup.iOS,
            BuildTargetGroup.Standalone,
            BuildTargetGroup.WebGL,
        };

        public static void SetScriptDefineSymbol(string symbols)
        {
            foreach (BuildTargetGroup item in s_buildTargetGroups)
            {
#if UNITY_2021_2_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(
                    UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(item), symbols);
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(item, symbols);
#endif
            }

            Debug.LogFormat("Set ScriptingDefineSymbols:{0}", symbols);
        }

        [MenuItem("Assets/TestAddScriptDefineSymbol")]
        public static void TestAddScriptDefineSymbol()
        {
            TryToAddScriptDefineSymbol("TEST");
        }

        [MenuItem("Assets/TestRemoveScriptDefineSymbol")]
        public static void TestRemoveScriptDefineSymbol()
        {
            TryToRemoveScriptDefineSymbol("TEST");
        }

        public static bool TryToAddScriptDefineSymbol(BuildTargetGroup group, string symbol)
        {
#if UNITY_2021_2_OR_NEWER
            var buildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            var old = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
#else
            var old = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
            var oldSymbols = old.Split(';');
            if (oldSymbols.Contains(symbol))
            {
                return false;
            }

            if (!old.EndsWith(";"))
                old += ";";
            var newSymbols = old + symbol;
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(buildTarget, newSymbols);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, newSymbols);
#endif
            return true;
        }

        public static void TryToRemoveScriptDefineSymbol(BuildTargetGroup group, string newSymbol)
        {
#if UNITY_2021_2_OR_NEWER
            var buildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group);
            var old = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
#else
            var old = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
            var oldSymbols = old.Split(';');
            var newSymbols = string.Empty;
            foreach (var symbol in oldSymbols)
            {
                if (symbol == newSymbol)
                    continue;
                newSymbols += symbol + ";";
            }

#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(buildTarget, newSymbols);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, newSymbols);
#endif
        }

        public static void TryToAddScriptDefineSymbol(string symbol)
        {
            var changedGroups = new List<BuildTargetGroup>();
            foreach (BuildTargetGroup item in s_buildTargetGroups)
            {
                if (TryToAddScriptDefineSymbol(item, symbol))
                {
                    changedGroups.Add(item);
                }
            }

            AssetDatabase.SaveAssets();
            if (changedGroups.Count > 0)
            {
                Debug.LogFormat("[MTPManager] Added script define symbol:{0} groups:{1}", symbol,
                    string.Join(",", changedGroups));
            }
        }

        public static void TryToRemoveScriptDefineSymbol(string symbol)
        {
            foreach (BuildTargetGroup item in s_buildTargetGroups)
            {
                TryToRemoveScriptDefineSymbol(item, symbol);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
