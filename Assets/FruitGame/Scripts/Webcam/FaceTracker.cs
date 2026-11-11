using System;
using System.Globalization;
using Unity.InferenceEngine;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Runs BlazeFace (short range) on the webcam feed with Unity Inference Engine and tracks one face (Docs/DESIGN.md §13).
    /// The webcam frame (9:16 or 16:9) is letterboxed into the model's 128×128 input. The model's raw output (a score
    /// and a box for each of its 896 anchors, always the same size) is read back without waiting, and the boxes are
    /// picked on the CPU (score threshold, then non-maximum suppression): with NMS in the graph, its output's size
    /// wasn't known until the GPU had run, so scheduling stopped the main thread until the GPU had finished everything
    /// queued before it (4–12 ms a detection behind background removal). Runs once per new camera frame. Results are in
    /// the webcam Output's UV space (0..1, origin bottom-left, already mirrored), so they line up with what's on screen.
    /// </summary>
    // Right after the camera copy (WebcamSource, -500) and before background removal (PersonSegmenter, -400): the face
    // finding goes onto the GPU first, so its result isn't held up behind a whole RVM run.
    [DefaultExecutionOrder(-450)]
    public class FaceTracker : MonoBehaviour
    {
        [SerializeField] WebcamSource source;
        [SerializeField] ModelAsset faceModel;
        [SerializeField] TextAsset anchorsCsv;
        [SerializeField, Range(0f, 1f)] float scoreThreshold = 0.6f;
        [SerializeField, Range(0f, 1f)] float iouThreshold = 0.3f;
        [Tooltip("Stick with the previous face while it still overlaps it this much (Docs/DESIGN.md §8: several faces).")]
        [SerializeField, Range(0f, 1f)] float stickyIou = 0.3f;
        [SerializeField] BackendType backend = BackendType.GPUCompute;

        const int InputSize = 128;
        const int NumAnchors = 896;

        /// <summary>True while a face was found in the latest processed frame.</summary>
        public bool HasFace { get; private set; }
        /// <summary>Face box in webcam-output UV (0..1, origin bottom-left, mirrored like the screen).</summary>
        public Rect FaceRect { get; private set; }
        public Vector2 FaceCenter => FaceRect.center;
        /// <summary>The face box relative to the 3:4 play area (CameraFrame.ToPlay): what calibration and the basket use,
        /// so head movement means the same whatever the camera's or the screen's shape.</summary>
        public Rect PlayFaceRect => CameraFrame.ToPlay(FaceRect);
        public Vector2 PlayFaceCenter => PlayFaceRect.center;
        public float Score { get; private set; }
        /// <summary>
        /// BlazeFace's six keypoints in the same UV space as FaceRect: 0 right eye, 1 left eye, 2 nose tip,
        /// 3 mouth centre, 4 right ear, 5 left ear (the subject's own left/right).
        /// </summary>
        public Vector2[] Keypoints { get; } = new Vector2[6];
        public float LastSeenTime { get; private set; } = -999f;
        /// <summary>Detections per second actually achieved (for the debug overlay).</summary>
        public float Rate { get; private set; }
        public string Status { get; private set; } = "Idle";

        float[,] anchors;
        // One worker (and input) per detection in flight: a worker recycles its output buffers when it's scheduled
        // again, which broke a readback still waiting on the previous run.
        Worker[] workers;
        Tensor<float>[] inputs;
        bool[] workerBusy;
        RenderTexture letterbox;
        /// <summary>Detections may overlap: one starts on every new camera frame without waiting for the last one's
        /// result (each takes about three frames to come back), up to this many at once.</summary>
        const int MaxInFlight = 3;
        int inFlight, started, latestDone = -1;
        bool disposed;
        int frames;
        float rateWindowStart;

        void Start()
        {
            if (faceModel == null || anchorsCsv == null || source == null)
            {
                Status = "Missing model, anchors or webcam source";
                enabled = false;
                return;
            }

            anchors = LoadAnchors(anchorsCsv.text);
            var model = ModelLoader.Load(faceModel);

            // Bake the input normalisation (0..1 → −1..1) into the graph; keep the raw outputs (fixed size).
            var graph = new FunctionalGraph();
            var x = graph.AddInput(model, 0);
            var outputs = Functional.Forward(model, 2 * x - 1);
            model = graph.Compile(outputs[0], outputs[1]); // boxes (1, 896, 16), scores (1, 896, 1)

            workers = new Worker[MaxInFlight];
            inputs = new Tensor<float>[MaxInFlight];
            workerBusy = new bool[MaxInFlight];
            for (int i = 0; i < MaxInFlight; i++)
            {
                workers[i] = new Worker(model, backend);
                inputs[i] = new Tensor<float>(new TensorShape(1, InputSize, InputSize, 3));
            }
            letterbox = new RenderTexture(InputSize, InputSize, 0, RenderTextureFormat.ARGB32) { name = "Face Input 128" };
            letterbox.Create();
            rateWindowStart = Time.unscaledTime;
            Status = "Waiting for camera";
        }

        void Update()
        {
            // Camera turned off from the staff panel: no one's in front of it as far as the game is concerned.
            if (source != null && source.Idle) { HasFace = false; return; }
            if (workers == null || inFlight >= MaxInFlight || source == null || !source.HasFrames || source.Output == null) return;
            // Nothing new to look at: the same picture would give the same faces.
            if (source.FrameCount == lastFrame) return;
            lastFrame = source.FrameCount;
            DetectAsync();
        }

        async void DetectAsync()
        {
            int slot = System.Array.IndexOf(workerBusy, false);
            if (slot < 0) return;
            workerBusy[slot] = true;
            var worker = workers[slot];
            var input = inputs[slot];
            inFlight++;
            int id = started++;
            try
            {
                Awaitable<Tensor<float>> boxTask, scoreTask;
                using (PerfMeter.Measure(PerfMeter.Part.FaceFinding))
                {
                    Letterbox(source.Output);
                    TextureConverter.ToTensor(letterbox, input, new TextureTransform().SetTensorLayout(TensorLayout.NHWC));
                    worker.Schedule(input);
                    boxTask = (worker.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync();
                    scoreTask = (worker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
                }
                using var boxes = await boxTask;
                using var scores = await scoreTask;
                // The GPU finishes them in order, but drop a result older than one already used, just in case.
                if (disposed || id < latestDone) return;
                latestDone = id;

                using (PerfMeter.Measure(PerfMeter.Part.FaceFinding))
                {
                    boxes.DownloadToNativeArray().CopyTo(rawBoxes);
                    scores.DownloadToNativeArray().CopyTo(rawScores);
                    ReadFaces();
                }
                CountRate();
            }
            catch (Exception e)
            {
                if (!disposed) Debug.LogWarning($"[FruitGame] Face detection failed: {e.Message}");
            }
            finally
            {
                inFlight--;
                workerBusy[slot] = false;
            }
        }

        /// <summary>Draws the frame centred in a black 128×128 square (pillar- or letterboxed) for the model.</summary>
        void Letterbox(Texture src)
        {
            float aspect = (float)src.width / src.height;
            float w = InputSize * Mathf.Min(1f, aspect);
            float h = InputSize * Mathf.Min(1f, 1f / aspect);
            var prev = RenderTexture.active;
            RenderTexture.active = letterbox;
            GL.Clear(false, true, Color.black);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, InputSize, InputSize, 0);
            Graphics.DrawTexture(new Rect((InputSize - w) * 0.5f, (InputSize - h) * 0.5f, w, h), src);
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        readonly float[] rawBoxes = new float[NumAnchors * 16];
        readonly float[] rawScores = new float[NumAnchors];
        readonly System.Collections.Generic.List<int> candidates = new System.Collections.Generic.List<int>();
        readonly System.Collections.Generic.List<int> kept = new System.Collections.Generic.List<int>();
        readonly System.Collections.Generic.List<Rect> keptRects = new System.Collections.Generic.List<Rect>();
        int lastFrame = -1;

        /// <summary>Box of anchor a in model pixels (x right, y down): centre and size.</summary>
        Rect ModelBox(int a)
        {
            float cx = anchors[a, 0] * InputSize + rawBoxes[a * 16], cy = anchors[a, 1] * InputSize + rawBoxes[a * 16 + 1];
            float w = rawBoxes[a * 16 + 2], h = rawBoxes[a * 16 + 3];
            return new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
        }

        /// <summary>
        /// Picks the faces from the raw output: anchors scoring at least scoreThreshold, best first, dropping any that
        /// overlap a better one by more than iouThreshold (non-maximum suppression); then the face to follow.
        /// </summary>
        void ReadFaces()
        {
            candidates.Clear();
            float minLogit = Mathf.Log(scoreThreshold / (1f - scoreThreshold)); // sigmoid(x) ≥ threshold
            for (int a = 0; a < NumAnchors; a++)
                if (rawScores[a] >= minLogit) candidates.Add(a);
            candidates.Sort((p, q) => rawScores[q].CompareTo(rawScores[p]));
            kept.Clear();
            keptRects.Clear();
            foreach (int a in candidates)
            {
                var box = ModelBox(a);
                bool overlaps = false;
                foreach (var k in keptRects)
                    if (Iou(box, k) > iouThreshold) { overlaps = true; break; }
                if (overlaps) continue;
                kept.Add(a);
                keptRects.Add(box);
            }

            Rect best = default;
            int bestAnchor = -1;
            float bestScore = 0f, bestArea = 0f, bestOverlap = 0f;
            bool found = false;

            float aspect = (float)source.Output.width / source.Output.height;
            float contentW = Mathf.Min(1f, aspect), contentH = Mathf.Min(1f, 1f / aspect);

            for (int i = 0; i < kept.Count; i++)
            {
                int a = kept[i];
                var m = keptRects[i];
                // Model pixels → letterbox UV (y up) → webcam-output UV.
                float u = ((m.center.x / InputSize) - (1f - contentW) * 0.5f) / contentW;
                float v = ((1f - m.center.y / InputSize) - (1f - contentH) * 0.5f) / contentH;
                float uw = m.width / InputSize / contentW, vh = m.height / InputSize / contentH;
                var rect = new Rect(u - uw * 0.5f, v - vh * 0.5f, uw, vh);

                float score = Sigmoid(rawScores[a]);
                float area = uw * vh;
                float overlap = HasFace ? Iou(rect, FaceRect) : 0f;

                // Prefer the face we were already tracking; otherwise the biggest one.
                bool better = !found
                              || (overlap >= stickyIou && overlap > bestOverlap)
                              || (bestOverlap < stickyIou && overlap < stickyIou && area > bestArea);
                if (better)
                {
                    best = rect; bestAnchor = a; bestScore = score; bestArea = area; bestOverlap = overlap; found = true;
                }
            }

            HasFace = found;
            if (found)
            {
                FaceRect = best;
                Score = bestScore;
                for (int k = 0; k < Keypoints.Length; k++)
                {
                    float kx = anchors[bestAnchor, 0] * InputSize + rawBoxes[bestAnchor * 16 + 4 + 2 * k];
                    float ky = anchors[bestAnchor, 1] * InputSize + rawBoxes[bestAnchor * 16 + 5 + 2 * k];
                    Keypoints[k] = new Vector2(((kx / InputSize) - (1f - contentW) * 0.5f) / contentW,
                                               ((1f - ky / InputSize) - (1f - contentH) * 0.5f) / contentH);
                }
                LastSeenTime = Time.unscaledTime;
                Status = $"Face {bestScore:P0}";
            }
            else Status = "No face";
        }

        void CountRate()
        {
            frames++;
            float elapsed = Time.unscaledTime - rateWindowStart;
            if (elapsed >= 1f)
            {
                Rate = frames / elapsed;
                frames = 0;
                rateWindowStart = Time.unscaledTime;
            }
        }

        void OnDestroy()
        {
            disposed = true;
            if (workers != null) foreach (var w in workers) w?.Dispose();
            if (inputs != null) foreach (var t in inputs) t?.Dispose();
            if (letterbox != null)
            {
                letterbox.Release();
                Destroy(letterbox);
            }
        }

        static float[,] LoadAnchors(string csv)
        {
            var result = new float[NumAnchors, 4];
            var lines = csv.Split('\n');
            for (int i = 0; i < NumAnchors; i++)
            {
                var values = lines[i].Split(',');
                for (int j = 0; j < 4; j++)
                    result[i, j] = float.Parse(values[j], CultureInfo.InvariantCulture);
            }
            return result;
        }

        static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));

        static float Iou(Rect a, Rect b)
        {
            float ix = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float iy = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float inter = ix * iy;
            float union = a.width * a.height + b.width * b.height - inter;
            return union > 0f ? inter / union : 0f;
        }
    }
}
