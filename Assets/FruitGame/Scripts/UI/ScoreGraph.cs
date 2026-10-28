using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Score over the round as a stepped line with a soft fill, for the game-over panel.
    /// x runs from 0 to the round length and y from 0 to the final score (at least a few points so a
    /// short run doesn't fill the box). Adapted from Photo Booth 2's ScoreGraphGraphic.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class ScoreGraph : MaskableGraphic
    {
        [SerializeField] Color lineColor = new Color(0.76f, 0.09f, 0.36f);
        [SerializeField] Color fillColor = new Color(0.76f, 0.09f, 0.36f, 0.15f);
        [SerializeField] Color gridColor = new Color(0.2f, 0.25f, 0.31f, 0.15f);
        [SerializeField] float lineWidth = 6f;
        [SerializeField] int minYRange = 5;

        readonly List<Vector2> points = new List<Vector2>();
        float duration = 60f;

        /// <summary>history: (seconds into the round, score) after each change, starting at (0, 0).</summary>
        public void SetData(IReadOnlyList<Vector2> history, float roundDuration)
        {
            points.Clear();
            if (history != null) points.AddRange(history);
            duration = Mathf.Max(1f, roundDuration);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            for (int i = 0; i <= 3; i++)
            {
                float y = r.yMin + r.height * i / 3f;
                Quad(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), 2f, gridColor);
            }
            if (points.Count == 0) return;

            float max = minYRange;
            foreach (var p in points) max = Mathf.Max(max, p.y);
            float last = Mathf.Max(duration, points[points.Count - 1].x);
            Vector2 Map(float t, float s) => new Vector2(
                r.xMin + r.width * Mathf.Clamp01(t / last),
                r.yMin + lineWidth + (r.height - 2f * lineWidth) * (s / max));

            // Steps: hold each score flat until the next catch, then jump; the last one runs to the end.
            var corners = new List<Vector2>();
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) corners.Add(Map(points[i].x, points[i - 1].y));
                corners.Add(Map(points[i].x, points[i].y));
            }
            corners.Add(Map(last, points[points.Count - 1].y));

            for (int i = 1; i < corners.Count; i++)
                Fill(vh, corners[i - 1], corners[i], r.yMin, fillColor);
            for (int i = 1; i < corners.Count; i++)
                Quad(vh, corners[i - 1], corners[i], lineWidth, lineColor, extend: lineWidth * 0.5f);
        }

        /// <summary>A thick segment from a to b, optionally lengthened at both ends so step corners join cleanly.</summary>
        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color, float extend = 0f)
        {
            var dir = (b - a).normalized;
            if (dir == Vector2.zero) return;
            a -= dir * extend;
            b += dir * extend;
            var n = new Vector2(-dir.y, dir.x) * width * 0.5f;
            int s = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        /// <summary>The area under segment a→b down to the bottom of the box.</summary>
        static void Fill(VertexHelper vh, Vector2 a, Vector2 b, float bottom, Color color)
        {
            if (Mathf.Approximately(a.x, b.x)) return;
            int s = vh.currentVertCount;
            vh.AddVert(new Vector2(a.x, bottom), color, Vector2.zero);
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(new Vector2(b.x, bottom), color, Vector2.zero);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }
    }
}
