using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// A line graph for the performance meter (PerfPanel): a few series sharing one scale (0 to `Max`), the newest sample
    /// on the right, with faint guide lines. Drawn as UI quads; values below 0 (unknown) leave gaps.
    /// </summary>
    public class PerfGraph : MaskableGraphic
    {
        public float Max = 120f;
        public int Capacity = 120;
        public float LineWidth = 2f;
        /// <summary>Values the faint horizontal guide lines are drawn at.</summary>
        public float[] Guides = { 30f, 60f, 90f };

        sealed class Series { public Color colour; public float[] values; }

        readonly List<Series> series = new List<Series>();
        int count, head;

        /// <summary>Adds a series; returns its index for Push.</summary>
        public int AddSeries(Color colour)
        {
            series.Add(new Series { colour = colour, values = new float[Capacity] });
            return series.Count - 1;
        }

        /// <summary>Adds one sample to every series (in AddSeries order) and redraws.</summary>
        public void Push(params float[] sample)
        {
            for (int i = 0; i < series.Count; i++) series[i].values[head] = i < sample.Length ? sample[i] : -1f;
            head = (head + 1) % Capacity;
            count = Mathf.Min(count + 1, Capacity);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;
            float Y(float v) => r.yMin + Mathf.Clamp01(v / Max) * r.height;
            foreach (var g in Guides)
                Line(vh, new Vector2(r.xMin, Y(g)), new Vector2(r.xMax, Y(g)), 1f, new Color(1f, 1f, 1f, 0.1f));
            if (count < 2) return;
            float step = r.width / (Capacity - 1);
            foreach (var s in series)
            {
                for (int i = 1; i < count; i++)
                {
                    // Oldest first: sample k of the last `count` sits at the right edge minus its age.
                    float a = s.values[(head - count + i - 1 + Capacity) % Capacity];
                    float b = s.values[(head - count + i + Capacity) % Capacity];
                    if (a < 0f || b < 0f) continue;
                    float x1 = r.xMax - (count - i) * step, x0 = x1 - step;
                    Line(vh, new Vector2(x0, Y(a)), new Vector2(x1, Y(b)), LineWidth, s.colour);
                }
            }
        }

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * (width * 0.5f);
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c;
            v.position = a - n; vh.AddVert(v);
            v.position = a + n; vh.AddVert(v);
            v.position = b + n; vh.AddVert(v);
            v.position = b - n; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
