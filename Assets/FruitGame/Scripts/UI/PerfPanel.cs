using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The staff screen's performance meter (Docs/DESIGN.md §15), under the guest mirror: the frame rate and how often
    /// the camera, background removal, face finding and face mesh update, and a bar graph of milliseconds per frame:
    /// the whole frame, the GPU's and the CPU's share of it (Unity's frame timing), and each big part's main-thread
    /// time (PerfMeter). Marks at 16.7 ms (60 fps) and 25 ms (40 fps, where background removal starts spreading its
    /// runs over frames). Built at runtime by StaffScreen, two-screen mode only.
    /// </summary>
    public class PerfPanel : MonoBehaviour
    {
        const float Scale = 1000f / 30f;    // the bars run from 0 to 33.3 ms (30 fps)
        const float LabelW = 250f, ValueW = 96f, RowH = 25f, RowGap = 3f, MarkLabelH = 20f;

        static readonly Color CardColor = new Color(0.12f, 0.13f, 0.18f, 1f);
        static readonly Color HeadingColor = new Color(1f, 0.79f, 0.24f, 1f);
        static readonly Color Track = new Color(0.08f, 0.09f, 0.12f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.66f, 0.74f, 1f);
        static readonly Color MarkColor = new Color(1f, 1f, 1f, 0.22f);

        struct Row
        {
            public string label;
            public Color color;
            public TMP_Text name, value;
            public RectTransform bar;
            public float ms;
        }

        Row[] rows;
        TMP_Text heading, fps, rates;
        RectTransform graph;
        PersonSegmenter segmenter;
        WebcamSource source;
        FaceTracker tracker;
        FaceMesh mesh;
        TMP_Text style;

        float frameMs, cpuMs, gpuMs;
        bool hasTiming;
        int lastCamFrames;
        float camRate, camWindowStart, nextText;
        readonly FrameTiming[] timing = new FrameTiming[1];

        /// <summary>Builds the meter filling `area`.</summary>
        public static PerfPanel Build(RectTransform area, TMP_Text style)
        {
            var card = new GameObject("Performance", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            card.SetParent(area, false);
            card.anchorMin = Vector2.zero; card.anchorMax = Vector2.one; card.offsetMin = card.offsetMax = Vector2.zero;
            var img = card.GetComponent<Image>();
            img.color = CardColor;
            img.raycastTarget = false;
            var panel = card.gameObject.AddComponent<PerfPanel>();
            panel.style = style;
            panel.Create(card);
            return panel;
        }

        void Create(RectTransform card)
        {
            segmenter = FindFirstObjectByType<PersonSegmenter>();
            source = FindFirstObjectByType<WebcamSource>();
            tracker = FindFirstObjectByType<FaceTracker>();
            mesh = FindFirstObjectByType<FaceMesh>();

            heading = Text("Heading", card, 32f, HeadingColor);
            Place(heading.rectTransform, new Vector2(20f, -10f), new Vector2(300f, 40f));

            // Left: the frame rate, large, and the update rates under it.
            fps = Text("FPS", card, 72f, Color.white);
            Place(fps.rectTransform, new Vector2(20f, -50f), new Vector2(300f, 84f));
            rates = Text("Rates", card, 20f, Dim);
            rates.textWrappingMode = TextWrappingModes.Normal;
            rates.lineSpacing = 8f;
            Place(rates.rectTransform, new Vector2(20f, -138f), new Vector2(300f, 130f));

            // Right: the bar graph.
            graph = new GameObject("Graph", typeof(RectTransform)).GetComponent<RectTransform>();
            graph.SetParent(card, false);
            graph.anchorMin = Vector2.zero; graph.anchorMax = Vector2.one;
            graph.offsetMin = new Vector2(340f, 16f); graph.offsetMax = new Vector2(-20f, -16f);

            rows = new[]
            {
                new Row { label = "Whole frame", color = new Color(0.92f, 0.93f, 0.96f) },
                new Row { label = "GPU", color = new Color(0.62f, 0.5f, 0.95f) },
                new Row { label = "CPU (main thread)", color = new Color(0.36f, 0.62f, 0.98f) },
                new Row { label = "Background removal", color = new Color32(236, 92, 150, 255) },
                new Row { label = "Face finding", color = new Color32(242, 140, 38, 255) },
                new Row { label = "Face mesh", color = new Color32(255, 205, 60, 255) },
                new Row { label = "Cut-out & beauty", color = new Color32(120, 200, 110, 255) },
                new Row { label = "Lighting", color = new Color32(90, 200, 210, 255) },
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float y = -MarkLabelH - i * (RowH + RowGap);
                rows[i].name = Text(rows[i].label, graph, 18f, Color.white);
                Place(rows[i].name.rectTransform, new Vector2(0f, y), new Vector2(LabelW, RowH));
                var track = Box("Track", graph, Track);
                track.anchorMin = new Vector2(0f, 1f); track.anchorMax = Vector2.one; track.pivot = new Vector2(0f, 1f);
                track.offsetMin = new Vector2(LabelW, y - RowH); track.offsetMax = new Vector2(-ValueW, y);
                rows[i].bar = Box("Bar", track, rows[i].color);
                rows[i].bar.anchorMin = Vector2.zero; rows[i].bar.anchorMax = new Vector2(0f, 1f); rows[i].bar.pivot = new Vector2(0f, 0.5f);
                rows[i].bar.offsetMin = new Vector2(0f, 3f); rows[i].bar.offsetMax = new Vector2(0f, -3f);
                rows[i].value = Text("Value", graph, 18f, Dim);
                rows[i].value.alignment = TextAlignmentOptions.Right;
                var v = rows[i].value.rectTransform;
                v.anchorMin = v.anchorMax = v.pivot = new Vector2(1f, 1f);
                v.anchoredPosition = new Vector2(0f, y); v.sizeDelta = new Vector2(ValueW - 8f, RowH);
            }

            // Marks at 60 and 40 fps down through the bars, labelled above them.
            float graphH = rows.Length * (RowH + RowGap);
            foreach (var (ms, label) in new[] { (1000f / 60f, "60 fps"), (1000f / 40f, "40 fps") })
            {
                var mark = Box(label, graph, MarkColor);
                mark.anchorMin = new Vector2(0f, 1f); mark.anchorMax = new Vector2(0f, 1f); mark.pivot = new Vector2(0.5f, 1f);
                mark.sizeDelta = new Vector2(2f, graphH);
                marks.Add((mark, ms));
                var t = Text(label, graph, 15f, Dim);
                t.text = label;
                t.alignment = TextAlignmentOptions.Center;
                var r = t.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0.5f, 1f);
                r.sizeDelta = new Vector2(80f, MarkLabelH);
                markLabels.Add((r, ms));
            }
            camWindowStart = Time.unscaledTime;
        }

        readonly System.Collections.Generic.List<(RectTransform mark, float ms)> marks = new System.Collections.Generic.List<(RectTransform, float)>();
        readonly System.Collections.Generic.List<(RectTransform label, float ms)> markLabels = new System.Collections.Generic.List<(RectTransform, float)>();

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            frameMs = Mathf.Lerp(frameMs <= 0f ? dt * 1000f : frameMs, dt * 1000f, 1f / 30f);

            // Unity's frame timing (Player Settings > Frame Timing Stats): the CPU's and the GPU's time per frame.
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].cpuMainThreadFrameTime > 0)
            {
                hasTiming = true;
                cpuMs = Mathf.Lerp(cpuMs, (float)timing[0].cpuMainThreadFrameTime, 1f / 30f);
                gpuMs = Mathf.Lerp(gpuMs, (float)timing[0].gpuFrameTime, 1f / 30f);
            }

            if (source != null && Time.unscaledTime - camWindowStart >= 1f)
            {
                camRate = (source.FrameCount - lastCamFrames) / (Time.unscaledTime - camWindowStart);
                lastCamFrames = source.FrameCount;
                camWindowStart = Time.unscaledTime;
            }

            float trackW = Mathf.Max(1f, graph.rect.width - LabelW - ValueW);
            rows[0].ms = frameMs;
            rows[1].ms = hasTiming ? gpuMs : -1f;
            rows[2].ms = hasTiming ? cpuMs : -1f;
            for (int p = 0; p < PerfMeter.Count; p++) rows[3 + p].ms = PerfMeter.Ms((PerfMeter.Part)p);
            for (int i = 0; i < rows.Length; i++)
                rows[i].bar.sizeDelta = new Vector2(Mathf.Clamp01(Mathf.Max(0f, rows[i].ms) / Scale) * trackW, rows[i].bar.sizeDelta.y);
            foreach (var (mark, ms) in marks) mark.anchoredPosition = new Vector2(LabelW + ms / Scale * trackW, -MarkLabelH);
            foreach (var (label, ms) in markLabels) label.anchoredPosition = new Vector2(LabelW + ms / Scale * trackW, 0f);

            if (Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.25f;
            heading.text = StaffText.T("Performance");
            fps.text = $"{1000f / Mathf.Max(0.1f, frameMs):0}<size=32> fps</size>";
            string matte = segmenter == null ? "–" : $"{segmenter.Rate:0}/s" + (segmenter.UsingRvm ? (segmenter.Whole ? "" : $" ({StaffText.T("spread")})") : "");
            rates.text = $"{StaffText.T("Camera")}  {camRate:0}/s\n{StaffText.T("Mattes")}  {matte}\n" +
                         $"{StaffText.T("Face")}  {(tracker != null ? tracker.Rate : 0f):0}/s  ·  {StaffText.T("Mesh")}  {(mesh != null && mesh.HasMesh ? mesh.Rate : 0f):0}/s";
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].name.text = StaffText.T(rows[i].label);
                rows[i].value.text = rows[i].ms < 0f ? "n/a" : $"{rows[i].ms:0.0} ms";
            }
        }

        TMP_Text Text(string name, Transform parent, float size, Color color)
        {
            var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            if (style != null) { t.font = style.font; t.fontSharedMaterial = style.fontSharedMaterial; }
            t.fontSize = size;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.Left;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform Box(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        /// <summary>Top-left anchored at `pos` (y down from the top), `size`.</summary>
        static void Place(RectTransform r, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }
    }
}
