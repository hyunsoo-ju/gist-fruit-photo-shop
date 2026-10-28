using System;
using System.Globalization;
using Unity.InferenceEngine;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Runs BlazeFace (short range) on the webcam feed with Unity Inference Engine and tracks one face (Docs/DESIGN.md §9).
    /// The 3:4 webcam frame is letterboxed into the model's 128×128 input; scores filtering + NMS are baked
    /// into the graph (as in Unity's BlazeDetectionSample). Results are in the webcam Output's UV space
    /// (0..1, origin bottom-left, already mirrored), so they line up with what's on screen.
    /// </summary>
    public class FaceTracker : MonoBehaviour
    {
        [SerializeField] WebcamSource source;
        [SerializeField] ModelAsset faceModel;
        [SerializeField] TextAsset anchorsCsv;
        [SerializeField, Range(0f, 1f)] float scoreThreshold = 0.6f;
        [SerializeField, Range(0f, 1f)] float iouThreshold = 0.3f;
        [Tooltip("Stick with the previous face while it still overlaps it this much (Docs/DESIGN.md §6: several faces).")]
        [SerializeField, Range(0f, 1f)] float stickyIou = 0.3f;
        [SerializeField] BackendType backend = BackendType.GPUCompute;

        const int InputSize = 128;
        const int NumAnchors = 896;

        /// <summary>True while a face was found in the latest processed frame.</summary>
        public bool HasFace { get; private set; }
        /// <summary>Face box in webcam-output UV (0..1, origin bottom-left, mirrored like the screen).</summary>
        public Rect FaceRect { get; private set; }
        public Vector2 FaceCenter => FaceRect.center;
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
        Worker worker;
        Tensor<float> input;
        RenderTexture letterbox;
        bool busy;
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

            // Bake input normalisation (0..1 → −1..1), score filtering and NMS into the graph.
            var graph = new FunctionalGraph();
            var x = graph.AddInput(model, 0);
            var outputs = Functional.Forward(model, 2 * x - 1);
            var rawBoxes = outputs[0];   // (1, 896, 16)
            var rawScores = outputs[1];  // (1, 896, 1)
            var anchorData = new float[NumAnchors * 4];
            Buffer.BlockCopy(anchors, 0, anchorData, 0, anchorData.Length * sizeof(float));
            var anchorTensor = Functional.Constant(new TensorShape(NumAnchors, 4), anchorData);
            var (indices, scores, boxes) = NmsFiltering(rawBoxes, rawScores, anchorTensor, iouThreshold, scoreThreshold);
            model = graph.Compile(indices, scores, boxes);

            worker = new Worker(model, backend);
            input = new Tensor<float>(new TensorShape(1, InputSize, InputSize, 3));
            letterbox = new RenderTexture(InputSize, InputSize, 0, RenderTextureFormat.ARGB32) { name = "Face Input 128" };
            letterbox.Create();
            rateWindowStart = Time.unscaledTime;
            Status = "Waiting for camera";
        }

        void Update()
        {
            if (worker == null || busy || source == null || !source.HasFrames || source.Output == null) return;
            DetectAsync();
        }

        async void DetectAsync()
        {
            busy = true;
            try
            {
                Letterbox(source.Output);
                TextureConverter.ToTensor(letterbox, input, new TextureTransform().SetTensorLayout(TensorLayout.NHWC));
                worker.Schedule(input);

                var idxTask = (worker.PeekOutput(0) as Tensor<int>).ReadbackAndCloneAsync();
                var scoreTask = (worker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
                var boxTask = (worker.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();
                using var idx = await idxTask;
                using var scores = await scoreTask;
                using var boxes = await boxTask;
                if (disposed) return;

                ReadFaces(idx, scores, boxes);
                CountRate();

                // With no faces the readbacks complete synchronously; yield so the main thread keeps running.
                if (idx.shape.length == 0) await Awaitable.NextFrameAsync();
            }
            catch (Exception e)
            {
                if (!disposed) Debug.LogWarning($"[FruitGame] Face detection failed: {e.Message}");
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>Draws the 3:4 frame centred in a black 128×128 square (pillarboxed) for the model.</summary>
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

        void ReadFaces(Tensor<int> idx, Tensor<float> scores, Tensor<float> boxes)
        {
            int n = idx.shape.length;
            Rect best = default;
            int bestIndex = -1;
            float bestScore = 0f, bestArea = 0f, bestOverlap = 0f;
            bool found = false;

            float aspect = (float)source.Output.width / source.Output.height;
            float contentW = Mathf.Min(1f, aspect), contentH = Mathf.Min(1f, 1f / aspect);

            for (int i = 0; i < n; i++)
            {
                int a = idx[i];
                // Box centre/size in model pixels (x right, y down), relative to its anchor.
                float cx = anchors[a, 0] * InputSize + boxes[0, i, 0];
                float cy = anchors[a, 1] * InputSize + boxes[0, i, 1];
                float bw = boxes[0, i, 2], bh = boxes[0, i, 3];

                // Model pixels → letterbox UV (y up) → webcam-output UV.
                float u = ((cx / InputSize) - (1f - contentW) * 0.5f) / contentW;
                float v = ((1f - cy / InputSize) - (1f - contentH) * 0.5f) / contentH;
                float uw = bw / InputSize / contentW, vh = bh / InputSize / contentH;
                var rect = new Rect(u - uw * 0.5f, v - vh * 0.5f, uw, vh);

                float score = Sigmoid(scores[0, i, 0]);
                float area = uw * vh;
                float overlap = HasFace ? Iou(rect, FaceRect) : 0f;

                // Prefer the face we were already tracking; otherwise the biggest one.
                bool better = !found
                              || (overlap >= stickyIou && overlap > bestOverlap)
                              || (bestOverlap < stickyIou && overlap < stickyIou && area > bestArea);
                if (better)
                {
                    best = rect; bestIndex = i; bestScore = score; bestArea = area; bestOverlap = overlap; found = true;
                }
            }

            HasFace = found;
            if (found)
            {
                FaceRect = best;
                Score = bestScore;
                int a = idx[bestIndex];
                for (int k = 0; k < Keypoints.Length; k++)
                {
                    float kx = anchors[a, 0] * InputSize + boxes[0, bestIndex, 4 + 2 * k];
                    float ky = anchors[a, 1] * InputSize + boxes[0, bestIndex, 5 + 2 * k];
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
            worker?.Dispose();
            input?.Dispose();
            if (letterbox != null)
            {
                letterbox.Release();
                Destroy(letterbox);
            }
        }

        /// <summary>Decodes box corners for NMS and keeps the surviving detections (thresholds are baked into the graph).</summary>
        static (FunctionalTensor, FunctionalTensor, FunctionalTensor) NmsFiltering(FunctionalTensor rawBoxes,
            FunctionalTensor rawScores, FunctionalTensor anchorTensor, float iou, float minScore)
        {
            var xCenter = rawBoxes[0, .., 0] + anchorTensor[.., 0] * InputSize;
            var yCenter = rawBoxes[0, .., 1] + anchorTensor[.., 1] * InputSize;
            var halfW = 0.5f * rawBoxes[0, .., 2];
            var halfH = 0.5f * rawBoxes[0, .., 3];
            var nmsBoxes = Functional.Stack(new[] { yCenter - halfH, xCenter - halfW, yCenter + halfH, xCenter + halfW }, 1);
            var nmsScores = Functional.Squeeze(Functional.Sigmoid(Functional.Clamp(rawScores, -100f, 100f)));
            var selected = Functional.NMS(nmsBoxes, nmsScores, iou, minScore);
            var boxes = Functional.IndexSelect(rawBoxes, 1, selected).Unsqueeze(0);
            var scores = Functional.IndexSelect(rawScores, 1, selected).Unsqueeze(0);
            return (selected, scores, boxes);
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
