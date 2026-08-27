using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    internal class DependencyGraphLine
    {
        public string StartPackageName;
        public string EndPackageName;
        public Vector2 Start;
        public Vector2 End;
        public bool IsSelected;
        public float[] SamplePoints;
    }

    internal static class DependencyGraphLineDrawer
    {
        public const float LineWidth = 3f;
        public const float HoverLineWidth = 5f;
        public const float HoverDistanceThreshold = 15f;
        public const int CurveSamples = 20;
        public const float ArrowSize = 10f;
        public const float ArrowT = 0.92f;

        public static void DrawAll(IEnumerable<DependencyGraphLine> lines, float zoom, bool showArrows)
        {
            if (lines == null) return;

            var lineList = lines as IList<DependencyGraphLine> ?? lines.ToList();

            foreach (var line in lineList.Where(l => !l.IsSelected))
            {
                Draw(line, zoom, showArrows, false);
            }

            foreach (var line in lineList.Where(l => l.IsSelected))
            {
                Draw(line, zoom, showArrows, true);
            }
        }

        public static void Draw(DependencyGraphLine line, float zoom, bool showArrows, bool isSelected)
        {
            Vector2 start = line.Start;
            Vector2 end = line.End;
            GetBezierControlPoints(start, end, out Vector2 startTangent, out Vector2 endTangent);

            Handles.BeginGUI();
            Handles.color = isSelected
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(0.8f, 0.8f, 0.8f, 0.8f);

            float scaledLineWidth = (isSelected ? HoverLineWidth : LineWidth) * zoom;
            Handles.DrawBezier(
                new Vector3(start.x, start.y, 0),
                new Vector3(end.x, end.y, 0),
                new Vector3(startTangent.x, startTangent.y, 0),
                new Vector3(endTangent.x, endTangent.y, 0),
                Handles.color,
                null,
                scaledLineWidth
            );

            if (showArrows)
            {
                DrawArrowOnCurve(start, startTangent, endTangent, end, zoom, isSelected);
            }

            // Flow dots: DependencyGraphFlowDots.Draw(start, startTangent, endTangent, end, zoom, isSelected);

            Handles.EndGUI();
        }

        public static float[] PreCalculateSamplePoints(Vector2 start, Vector2 end)
        {
            float[] points = new float[CurveSamples * 2];
            GetBezierControlPoints(start, end, out Vector2 startTangent, out Vector2 endTangent);

            for (int i = 0; i < CurveSamples; i++)
            {
                float t = i / (float)(CurveSamples - 1);
                Vector2 point = CalculateBezierPoint(t, start, startTangent, endTangent, end);
                points[i * 2] = point.x;
                points[i * 2 + 1] = point.y;
            }

            return points;
        }

        public static bool IsMouseNearLine(Vector2 mousePos, float[] samplePoints)
        {
            float minDistance = float.MaxValue;
            float thresholdSquared = HoverDistanceThreshold * HoverDistanceThreshold;

            for (int i = 0; i < samplePoints.Length - 2; i += 2)
            {
                Vector2 p1 = new Vector2(samplePoints[i], samplePoints[i + 1]);
                Vector2 p2 = new Vector2(samplePoints[i + 2], samplePoints[i + 3]);

                float distance = DistanceToLineSegment(mousePos, p1, p2);
                minDistance = Mathf.Min(minDistance, distance);

                if (minDistance < thresholdSquared)
                {
                    return true;
                }
            }

            return minDistance < thresholdSquared;
        }

        public static DependencyGraphLine HitTest(IEnumerable<DependencyGraphLine> lines, Vector2 mouse)
        {
            if (lines == null) return null;

            DependencyGraphLine closest = null;
            float minDistance = float.MaxValue;
            foreach (var line in lines)
            {
                if (!IsMouseNearLine(mouse, line.SamplePoints)) continue;

                float distance = float.MaxValue;
                for (int i = 0; i < line.SamplePoints.Length - 2; i += 2)
                {
                    Vector2 p1 = new Vector2(line.SamplePoints[i], line.SamplePoints[i + 1]);
                    Vector2 p2 = new Vector2(line.SamplePoints[i + 2], line.SamplePoints[i + 3]);
                    distance = Mathf.Min(distance, DistanceToLineSegment(mouse, p1, p2));
                }

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closest = line;
                }
            }

            return closest;
        }

        public static void GetBezierControlPoints(Vector2 start, Vector2 end, out Vector2 startTangent, out Vector2 endTangent)
        {
            float controlPointOffset = Mathf.Abs(end.y - start.y) * 0.25f;
            startTangent = new Vector2(start.x, start.y + controlPointOffset);
            endTangent = new Vector2(end.x, end.y - controlPointOffset);
        }

        public static Vector2 CalculateBezierPoint(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            Vector2 p = uuu * p0;
            p += 3 * uu * t * p1;
            p += 3 * u * tt * p2;
            p += ttt * p3;
            return p;
        }

        private static void DrawArrowOnCurve(
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float zoom, bool isSelected)
        {
            Vector2 tip = CalculateBezierPoint(ArrowT, p0, p1, p2, p3);
            Vector2 before = CalculateBezierPoint(ArrowT - 0.02f, p0, p1, p2, p3);
            Vector2 dir = (tip - before).normalized;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = (p3 - p0).normalized;
            }

            float size = ArrowSize * zoom;
            Vector2 right = new Vector2(-dir.y, dir.x);
            Vector3[] points =
            {
                new Vector3(tip.x, tip.y, 0),
                new Vector3(tip.x - dir.x * size + right.x * size * 0.5f,
                            tip.y - dir.y * size + right.y * size * 0.5f, 0),
                new Vector3(tip.x - dir.x * size - right.x * size * 0.5f,
                            tip.y - dir.y * size - right.y * size * 0.5f, 0)
            };

            Handles.color = isSelected
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(0.9f, 0.9f, 0.9f, 0.95f);
            Handles.DrawAAConvexPolygon(points);
        }

        private static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float lineLengthSquared = line.sqrMagnitude;

            if (lineLengthSquared == 0)
            {
                return (point - lineStart).sqrMagnitude;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lineLengthSquared);
            Vector2 projection = lineStart + t * line;
            return (point - projection).sqrMagnitude;
        }
    }
}
