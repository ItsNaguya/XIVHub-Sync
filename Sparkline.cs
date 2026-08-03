using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace XIVHubCompanion
{
    internal static class Sparkline
    {
        public static void Draw(Vector2 areaMin, Vector2 areaMax, ReadOnlySpan<float> values, Vector4 line, Vector4 fill)
        {
            var areaWidth = areaMax.X - areaMin.X;
            var areaHeight = areaMax.Y - areaMin.Y;

            if (values.Length < 2 || areaWidth <= 2f || areaHeight <= 2f)
            {
                return;
            }

            var min = values[0];
            var max = values[0];
            for (var index = 1; index < values.Length; index++)
            {
                if (values[index] < min)
                {
                    min = values[index];
                }

                if (values[index] > max)
                {
                    max = values[index];
                }
            }

            var range = max - min;
            if (range <= 0f)
            {
                range = 1f;
            }

            var count = values.Length;
            var stepX = areaWidth / (count - 1);
            var usableHeight = areaHeight - 2f;
            var baseY = areaMax.Y;
            Span<Vector2> points = count <= 128 ? stackalloc Vector2[count] : new Vector2[count];
            for (var index = 0; index < count; index++)
            {
                var normalized = (values[index] - min) / range;
                points[index] = new Vector2(areaMin.X + stepX * index, baseY - normalized * usableHeight - 1f);
            }

            var drawList = ImGui.GetWindowDrawList();
            var fillColor = ImGui.GetColorU32(fill);
            var lineColor = ImGui.GetColorU32(line);
            for (var index = 0; index < count - 1; index++)
            {
                var leftBase = new Vector2(points[index].X, baseY);
                var rightBase = new Vector2(points[index + 1].X, baseY);
                drawList.AddQuadFilled(points[index], points[index + 1], rightBase, leftBase, fillColor);
            }

            for (var index = 0; index < count - 1; index++)
            {
                drawList.AddLine(points[index], points[index + 1], lineColor, 2f);
            }
        }
    }
}
