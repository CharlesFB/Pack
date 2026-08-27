using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class DependencyGraphNodeDrawer
    {
        private static readonly IMTPackageInfoService s_mtpInfoService = ServiceLocator.Get<IMTPackageInfoService>();

        private const float NODE_SIZE = 40f;

        public static void Draw(
            // IEnumerable<PackageInstallInfo> packages,
            Dictionary<string, Vector2> positions,
            float zoom,
            string draggingNode)
        {
            if (positions == null) return;

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(11 * zoom),
                stretchWidth = true,
                stretchHeight = true,
                wordWrap = false,
                fixedHeight = 40 * zoom
            };
            labelStyle.normal.textColor = Color.white;

            foreach (var package in s_mtpInfoService.GetAllPackages())
            {
                string packageName = package.PackageName;
                string curVersion = package.Tag;
                if (!positions.TryGetValue(packageName, out Vector2 pos)) continue;

                float scaledNodeSize = NODE_SIZE * zoom;
                Rect nodeRect = new Rect(
                    pos.x - scaledNodeSize / 2,
                    pos.y - scaledNodeSize / 2,
                    scaledNodeSize,
                    scaledNodeSize);

                bool isDragging = draggingNode == packageName;
                Color nodeColor = package.Depth == 0
                    ? new Color(0.0f, 0.4f, 1.0f, 1.0f)
                    : new Color(0.0f, 0.8f, 0.0f, 1.0f);
                if (isDragging)
                {
                    nodeColor = Color.Lerp(nodeColor, Color.white, 0.35f);
                }

                GUI.backgroundColor = nodeColor;
                GUI.Box(nodeRect, "");
                EditorGUIUtility.AddCursorRect(nodeRect, MouseCursor.MoveArrow);

                float labelOffset = scaledNodeSize / 2 + 5;
                Rect labelRect = new Rect(pos.x - 100 * zoom, pos.y + labelOffset, 200 * zoom, 20 * zoom);
                string displayName = (s_mtpInfoService.GetPackageJson(packageName).displayName ?? packageName.Substring(packageName.LastIndexOf('.')+1) )+ "\n" + curVersion;
                if (package.Depth > 0)
                {
                    displayName += "\n(dep)";
                }
                GUI.Label(labelRect, displayName, labelStyle);
            }

            GUI.backgroundColor = Color.white;
        }

        public static string HitTest(Dictionary<string, Vector2> positions, Vector2 mouse, float zoom)
        {
            if (positions == null) return null;

            float size = NODE_SIZE * zoom;
            string best = null;
            float bestDist = float.MaxValue;
            foreach (var kv in positions)
            {
                var rect = new Rect(kv.Value.x - size / 2, kv.Value.y - size / 2, size, size);
                if (!rect.Contains(mouse)) continue;

                float dist = (kv.Value - mouse).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = kv.Key;
                }
            }

            return best;
        }
    }
}
