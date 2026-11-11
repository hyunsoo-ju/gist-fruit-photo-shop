using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The staff screen's performance meter (Docs/DESIGN.md §15), under the guest mirror. Left: the frame rate, large,
    /// its 1% low (the average of the slowest 1% of the last ~1000 frames, i.e. how bad the hitches are), how often the
    /// camera, background removal, face finding and face mesh update, and the computer's CPU and GPU load, temperatures
    /// and power (SystemMonitor, from macmon). Right: what a frame costs as one segmented bar (each big part's
    /// main-thread time from PerfMeter, the rest of the game's main thread, and the wait for the GPU, from Unity's frame
    /// timing), with marks at 60 and 40 fps; and a graph of the last minute: fps, 1% low, CPU and GPU load (%) and
    /// temperatures (°C), all on one 0–120 scale. Built at runtime by StaffScreen, two-screen mode only.
    /// </summary>
    public class PerfPanel : MonoBehaviour
    {
        const float Scale = 1000f / 30f;    // the bar runs from 0 to 33.3 ms (30 fps)
        const float Left = 330f, MarkLabelH = 18f, BarH = 28f;
        const int LowWindow = 1000;         // frames the 1% low is taken over
        const float GraphStep = 0.5f;       // seconds per graph sample (120 samples: a minute)

        static readonly Color CardColor = new Color(0.12f, 0.13f, 0.18f, 1f);
        static readonly Color HeadingColor = new Color(1f, 0.79f, 0.24f, 1f);
        static readonly Color Track = new Color(0.08f, 0.09f, 0.12f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.66f, 0.74f, 1f);
        static readonly Color MarkColor = new Color(1f, 1f, 1f, 0.35f);

        static readonly Color FpsColour = new Color(0.95f, 0.96f, 0.98f);
        static readonly Color LowColour = new Color(0.55f, 0.58f, 0.66f);
        static readonly Color CpuColour = new Color(0.36f, 0.62f, 0.98f);
        static readonly Color GpuColour = new Color(0.66f, 0.52f, 0.98f);
        static readonly Color CpuTempColour = new Color(1f, 0.6f, 0.25f);
        static readonly Color GpuTempColour = new Color(1f, 0.36f, 0.42f);

        struct Segment { public string label; public Color color; public RectTransform bar; public float ms; }

        Segment[] segments;
        TMP_Text heading, fps, low, rates, legend, graphLegend;
        RectTransform barTrack;
        PerfGraph graph;
        PersonSegmenter segmenter;
        WebcamSource source;
        FaceTracker tracker;
        FaceMesh mesh;
        TMP_Text style;

        float frameMs, cpuMs, gpuMs, lowFps, nextLog = 15f;
        bool hasTiming;
        int lastCamFrames;
        float camRate, camWindowStart, nextText, nextLow, nextSample;
        int sampleFrames;
        float sampleTime;
        readonly FrameTiming[] timing = new FrameTiming[1];
        readonly float[] recent = new float[LowWindow];
        readonly float[] sorted = new float[LowWindow];
        int recentCount, recentHead;
        readonly List<(RectTransform mark, float ms)> marks = new List<(RectTransform, float)>();

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

        void OnEnable() => SystemMonitor.Start();
        void OnDisable() => SystemMonitor.Stop();

        void Create(RectTransform card)
        {
            segmenter = FindFirstObjectByType<PersonSegmenter>();
            source = FindFirstObjectByType<WebcamSource>();
            tracker = FindFirstObjectByType<FaceTracker>();
            mesh = FindFirstObjectByType<FaceMesh>();

            // Left: the frame rate, its 1% low, the update rates and the computer's load.
            heading = Text("Heading", card, 32f, HeadingColor);
            Place(heading.rectTransform, card, new Vector2(20f, -10f), new Vector2(Left - 30f, 40f));
            fps = Text("FPS", card, 68f, Color.white);
            Place(fps.rectTransform, card, new Vector2(20f, -46f), new Vector2(Left - 30f, 76f));
            low = Text("Low", card, 22f, Color.white);
            Place(low.rectTransform, card, new Vector2(20f, -122f), new Vector2(Left - 30f, 28f));
            rates = Text("Rates", card, 17f, Dim);
            rates.textWrappingMode = TextWrappingModes.Normal;
            rates.overflowMode = TextOverflowModes.Ellipsis;
            rates.lineSpacing = 4f;
            Place(rates.rectTransform, card, new Vector2(20f, -154f), new Vector2(Left - 30f, 112f));

            // Right, top: what a frame costs, as one bar of segments, with the 60 and 40 fps marks.
            var right = new GameObject("Frame and Graph", typeof(RectTransform)).GetComponent<RectTransform>();
            right.SetParent(card, false);
            right.anchorMin = Vector2.zero; right.anchorMax = Vector2.one;
            right.offsetMin = new Vector2(Left + 10f, 12f); right.offsetMax = new Vector2(-20f, -10f);

            barTrack = Box("Frame Cost", right, Track);
            barTrack.anchorMin = new Vector2(0f, 1f); barTrack.anchorMax = Vector2.one; barTrack.pivot = new Vector2(0.5f, 1f);
            barTrack.offsetMin = new Vector2(0f, -MarkLabelH - BarH); barTrack.offsetMax = new Vector2(0f, -MarkLabelH);
            segments = new[]
            {
                new Segment { label = "Background removal", color = new Color32(236, 92, 150, 255) },
                new Segment { label = "Face finding", color = new Color32(242, 140, 38, 255) },
                new Segment { label = "Face mesh", color = new Color32(255, 205, 60, 255) },
                new Segment { label = "Cut-out & beauty", color = new Color32(120, 200, 110, 255) },
                new Segment { label = "Lighting", color = new Color32(90, 200, 210, 255) },
                new Segment { label = "Rest of the game", color = CpuColour },
                new Segment { label = "Waiting for the GPU", color = GpuColour },
            };
            for (int i = 0; i < segments.Length; i++)
            {
                var b = Box(segments[i].label, barTrack, segments[i].color);
                b.anchorMin = Vector2.zero; b.anchorMax = new Vector2(0f, 1f); b.pivot = new Vector2(0f, 0.5f);
                b.offsetMin = new Vector2(0f, 3f); b.offsetMax = new Vector2(0f, -3f);
                segments[i].bar = b;
            }
            foreach (var (ms, label) in new[] { (1000f / 60f, "60 fps"), (1000f / 40f, "40 fps") })
            {
                // The line runs down through the bar; its label sits above it.
                var mark = Box(label, right, MarkColor);
                mark.anchorMin = mark.anchorMax = new Vector2(0f, 1f); mark.pivot = new Vector2(0.5f, 1f);
                mark.sizeDelta = new Vector2(2f, BarH);
                marks.Add((mark, ms));
                var t = Text(label, mark, 14f, Dim);
                t.text = label;
                t.alignment = TextAlignmentOptions.Bottom;
                var r = t.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f); r.pivot = new Vector2(0.5f, 0f);
                r.anchoredPosition = new Vector2(0f, 1f); r.sizeDelta = new Vector2(80f, MarkLabelH - 2f);
            }
            legend = Text("Legend", right, 15f, Dim);
            legend.textWrappingMode = TextWrappingModes.Normal;
            legend.lineSpacing = 2f;
            Place(legend.rectTransform, right, new Vector2(0f, -MarkLabelH - BarH - 6f), new Vector2(-1f, 44f));

            // Right, below: the last minute.
            float graphTop = MarkLabelH + BarH + 6f + 44f + 4f;
            graphLegend = Text("Graph Legend", right, 15f, Dim);
            Place(graphLegend.rectTransform, right, new Vector2(0f, -graphTop), new Vector2(-1f, 20f));
            var back = Box("Graph Back", right, Track);
            back.anchorMin = Vector2.zero; back.anchorMax = Vector2.one;
            back.offsetMin = Vector2.zero; back.offsetMax = new Vector2(0f, -graphTop - 22f);
            graph = new GameObject("Graph", typeof(RectTransform), typeof(CanvasRenderer), typeof(PerfGraph)).GetComponent<PerfGraph>();
            graph.transform.SetParent(back, false);
            graph.raycastTarget = false;
            var gr = graph.rectTransform;
            gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one;
            gr.offsetMin = new Vector2(34f, 4f); gr.offsetMax = new Vector2(-6f, -4f);
            graph.AddSeries(FpsColour);
            graph.AddSeries(LowColour);
            graph.AddSeries(CpuColour);
            graph.AddSeries(GpuColour);
            graph.AddSeries(CpuTempColour);
            graph.AddSeries(GpuTempColour);
            // The scale (fps, % and °C alike), beside the guide lines.
            foreach (float v in new[] { 0f, 30f, 60f, 90f, 120f })
            {
                var t = Text("Axis " + v, graph.transform, 12f, Dim);
                t.text = v.ToString("0");
                t.alignment = TextAlignmentOptions.Right;
                var r = t.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0f, v / graph.Max); r.pivot = new Vector2(1f, 0.5f);
                r.anchoredPosition = new Vector2(-4f, 0f); r.sizeDelta = new Vector2(30f, 14f);
            }

            camWindowStart = Time.unscaledTime;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            frameMs = Mathf.Lerp(frameMs <= 0f ? dt * 1000f : frameMs, dt * 1000f, 1f / 30f);
            recent[recentHead] = Time.unscaledDeltaTime;
            recentHead = (recentHead + 1) % LowWindow;
            recentCount = Mathf.Min(recentCount + 1, LowWindow);
            // Builds: a line in Player.log every 10 s, so a slow booth can be looked into afterwards.
            if (!Application.isEditor && Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 10f;
                Debug.Log($"[FruitGame] Perf: {1000f / Mathf.Max(0.1f, frameMs):0} fps, 1% low {lowFps:0}, frame {frameMs:0.0} ms, main thread {cpuMs:0.0} ms, GPU {gpuMs:0.0} ms, " +
                          $"screen {Screen.width}x{Screen.height}, standby {Standby.Active}, target fps {Application.targetFrameRate}, drawing every {UnityEngine.Rendering.OnDemandRendering.renderFrameInterval} frame(s), vsync {QualitySettings.vSyncCount}, refresh {Screen.currentResolution.refreshRateRatio.value:0}");
            }
            sampleFrames++;
            sampleTime += Time.unscaledDeltaTime;

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

            UpdateBar();

            if (Time.unscaledTime >= nextLow) { nextLow = Time.unscaledTime + 0.25f; lowFps = OnePercentLow(); }
            if (Time.unscaledTime >= nextSample && sampleTime > 0f)
            {
                nextSample = Time.unscaledTime + GraphStep;
                float cpu = SystemMonitor.CpuLoad, gpu = SystemMonitor.GpuLoad;
                graph.Push(sampleFrames / sampleTime, lowFps, cpu < 0f ? -1f : cpu * 100f, gpu < 0f ? -1f : gpu * 100f,
                    SystemMonitor.CpuTemp, SystemMonitor.GpuTemp);
                sampleFrames = 0;
                sampleTime = 0f;
            }

            if (Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.25f;
            UpdateText();
        }

        /// <summary>
        /// The frame's segments: each big part's main-thread time, then the rest of the main thread, then the time the
        /// frame took beyond the main thread (mostly waiting for the GPU). Clipped at the bar's end (30 fps).
        /// </summary>
        void UpdateBar()
        {
            float parts = 0f;
            for (int p = 0; p < PerfMeter.Count; p++) { segments[p].ms = PerfMeter.Ms((PerfMeter.Part)p); parts += segments[p].ms; }
            float main = hasTiming ? Mathf.Max(cpuMs, parts) : frameMs;
            segments[PerfMeter.Count].ms = Mathf.Max(0f, Mathf.Min(main, frameMs) - parts);
            segments[PerfMeter.Count + 1].ms = Mathf.Max(0f, frameMs - Mathf.Max(main, parts));

            float width = Mathf.Max(1f, barTrack.rect.width), x = 0f;
            for (int i = 0; i < segments.Length; i++)
            {
                float w = Mathf.Min(segments[i].ms / Scale * width, width - x);
                segments[i].bar.anchoredPosition = new Vector2(x, 0f);
                segments[i].bar.sizeDelta = new Vector2(Mathf.Max(0f, w), segments[i].bar.sizeDelta.y);
                x += Mathf.Max(0f, w);
            }
            foreach (var (mark, ms) in marks) mark.anchoredPosition = new Vector2(ms / Scale * width, -MarkLabelH);
        }

        /// <summary>The frame rate the slowest 1% of the recent frames ran at (their average).</summary>
        float OnePercentLow()
        {
            if (recentCount < 20) return -1f;
            System.Array.Copy(recent, sorted, recentCount);
            System.Array.Sort(sorted, 0, recentCount);
            int n = Mathf.Max(1, recentCount / 100);
            float sum = 0f;
            for (int i = recentCount - n; i < recentCount; i++) sum += sorted[i];
            return n / Mathf.Max(1e-4f, sum);
        }

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        void UpdateText()
        {
            string T(string s) => StaffText.T(s);
            heading.text = T("Performance");
            fps.text = $"{1000f / Mathf.Max(0.1f, frameMs):0}<size=30> fps</size>";
            string lowColour = lowFps >= 55f ? "#7CFC9A" : lowFps >= 30f ? "#FFC857" : "#FF7A6E";
            low.text = lowFps < 0f ? $"{T("1% low")}  –" : $"{T("1% low")}  <color={lowColour}>{lowFps:0}</color><size=16> fps</size>";

            string matte = segmenter == null ? "–" : $"{segmenter.Rate:0}/s" + (segmenter.UsingRvm ? (segmenter.Whole ? "" : $" ({T("spread")})") : "");
            string cpu = SystemMonitor.CpuLoad < 0f ? "–" : $"{SystemMonitor.CpuLoad * 100f:0}%" + (SystemMonitor.CpuIsWhole ? "" : $" ({T("this app")})");
            string Temp(float t) => t < 0f ? "" : $" · {t:0} °C";
            string gpu = SystemMonitor.GpuLoad < 0f ? "–" : $"{SystemMonitor.GpuLoad * 100f:0}%";
            string power = SystemMonitor.Power < 0f ? "" : $" · {SystemMonitor.Power:0.0} W";
            rates.text = $"{T("Camera")} {camRate:0}/s · {T("Mattes")} {matte}\n" +
                         $"{T("Face")} {(tracker != null ? tracker.Rate : 0f):0}/s · {T("Mesh")} {(mesh != null && mesh.HasMesh ? mesh.Rate : 0f):0}/s\n" +
                         $"<color={Hex(CpuColour)}>CPU</color> {cpu}{Temp(SystemMonitor.CpuTemp)}\n" +
                         $"<color={Hex(GpuColour)}>GPU</color> {gpu}{Temp(SystemMonitor.GpuTemp)}{power}" +
                         (SystemMonitor.Problem.Length > 0 ? $"\n<size=14>{T(SystemMonitor.Problem)}</size>" : "");

            var sb = new System.Text.StringBuilder();
            sb.Append(T("Frame cost")).Append(' ').Append($"{frameMs:0.0} ms").Append(":  ");
            foreach (var s in segments)
                sb.Append($"<color={Hex(s.color)}>{T(s.label)} {s.ms:0.0}</color>   ");
            legend.text = sb.ToString();

            graphLegend.text = $"{T("last minute")}:   <color={Hex(FpsColour)}>fps</color>   <color={Hex(LowColour)}>{T("1% low")}</color>   " +
                               $"<color={Hex(CpuColour)}>CPU %</color>   <color={Hex(GpuColour)}>GPU %</color>   " +
                               $"<color={Hex(CpuTempColour)}>CPU °C</color>   <color={Hex(GpuTempColour)}>GPU °C</color>";
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
            t.alignment = TextAlignmentOptions.TopLeft;
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

        /// <summary>Top-left anchored at `pos` (y down from the top); a width of -1 stretches across the parent.</summary>
        static void Place(RectTransform r, RectTransform parent, Vector2 pos, Vector2 size)
        {
            if (size.x < 0f)
            {
                r.anchorMin = new Vector2(0f, 1f); r.anchorMax = Vector2.one; r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = pos; r.sizeDelta = new Vector2(0f, size.y);
                return;
            }
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }
    }
}
