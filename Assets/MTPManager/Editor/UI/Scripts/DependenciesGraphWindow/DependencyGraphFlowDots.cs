using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 拓展类
    /// 有需要的话，可以把连线加入流动点，本类已经调整好一些初始速度/点大小、颜色等信息
    /// 使用方法：面板导航栏添加开关，开启后绘制面板时执行Draw，关闭后停止Draw即可。绘制路线沿连线的贝塞尔曲线走
    /// </summary>
    internal static class DependencyGraphFlowDots
    {
        private const int DotCount = 3;
        private const float DotRadius = 3.5f;
        private const float Speed = 0.35f;
        private const float TMin = 0.08f;
        private const float TMax = 0.88f;

        public static void Draw(
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            float zoom,
            bool isSelected)
        {
            float time = Time.realtimeSinceStartup * Speed;
            float radius = DotRadius * zoom;

            for (int i = 0; i < DotCount; i++)
            {
                float t = (time + i / (float)DotCount) % 1f;
                t = Mathf.Lerp(TMin, TMax, t);
                Vector2 point = EvaluateCubicBezier(t, p0, p1, p2, p3);

                float alpha = isSelected ? 1f : 0.75f;
                Handles.color = new Color(1f, 0.85f, 0.2f, alpha);
                Handles.DrawSolidDisc(new Vector3(point.x, point.y, 0), Vector3.forward, radius);
            }
        }

        private static Vector2 EvaluateCubicBezier(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
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
    }
}
