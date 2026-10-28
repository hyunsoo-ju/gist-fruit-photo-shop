using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// MediaPipe Face Mesh (478 landmarks incl. irises; ONNX from astaileyyoung/FaceMeshONNX, MIT) for the photo
    /// booth's face reshaping and makeup (Docs/DESIGN.md §10). Crops a square around the BlazeFace box, runs the mesh on
    /// the GPU, and maps the points back into webcam-output UV (the same space as FaceTracker / the cut-out).
    /// Only runs while Active, so gameplay doesn't pay for it.
    /// </summary>
    public class FaceMesh : MonoBehaviour
    {
        [SerializeField] WebcamSource source;
        [SerializeField] FaceTracker tracker;
        [SerializeField] ModelAsset model;
        [SerializeField] BackendType backend = BackendType.GPUCompute;
        [Tooltip("Crop side as a multiple of the BlazeFace box (the mesh wants the whole head with some margin).")]
        [SerializeField] float cropScale = 1.8f;
        [Tooltip("Per-update smoothing of the points (0 = none, closer to 1 = steadier but laggier).")]
        [SerializeField, Range(0f, 0.95f)] float smoothing = 0.5f;

        const int Size = 256;
        public const int Count = 478;

        /// <summary>Set by whoever needs the mesh (the photo booth); off during gameplay.</summary>
        public bool Active { get; set; }
        public bool HasMesh { get; private set; }
        /// <summary>Landmarks in webcam-output UV (0..1, origin bottom-left).</summary>
        public Vector2[] Points { get; } = new Vector2[Count];

        Worker worker;
        Tensor<float> input;
        RenderTexture crop;
        bool busy;
        bool disposed;
        bool primed;

        void Start()
        {
            if (model == null || source == null || tracker == null) { enabled = false; return; }
            worker = new Worker(ModelLoader.Load(model), backend);
            input = new Tensor<float>(new TensorShape(1, Size, Size, 3));
            crop = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32) { name = "Face Mesh Crop" };
            crop.Create();
        }

        void Update()
        {
            if (!Active) { HasMesh = false; primed = false; return; }
            if (worker == null || busy || !source.HasFrames || !tracker.HasFace) return;
            RunAsync();
        }

        async void RunAsync()
        {
            busy = true;
            try
            {
                var frame = source.Output;
                var face = tracker.FaceRect;
                // Square crop in pixels, centred on the face box.
                float side = Mathf.Max(face.width * frame.width, face.height * frame.height) * cropScale;
                float cw = side / frame.width, ch = side / frame.height;
                var cropRect = new Rect(face.center.x - cw * 0.5f, face.center.y - ch * 0.5f, cw, ch);
                Graphics.Blit(frame, crop, new Vector2(cw, ch), new Vector2(cropRect.x, cropRect.y));

                // Pixels 0..1, NHWC, row 0 = top of the crop.
                TextureConverter.ToTensor(crop, input, new TextureTransform().SetTensorLayout(TensorLayout.NHWC));
                worker.Schedule(input);
                var landmarksTask = (worker.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync();
                var presenceTask = (worker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
                using var landmarks = await landmarksTask;
                using var presence = await presenceTask;
                if (disposed || !Active) return;

                float score = 1f / (1f + Mathf.Exp(-presence[0]));
                if (score < 0.5f) { HasMesh = false; return; }

                float keep = primed ? smoothing : 0f;
                for (int i = 0; i < Count; i++)
                {
                    float x = landmarks[i * 3] / Size, y = landmarks[i * 3 + 1] / Size;
                    var uv = new Vector2(cropRect.x + x * cropRect.width, cropRect.y + (1f - y) * cropRect.height);
                    Points[i] = Vector2.Lerp(uv, Points[i], keep);
                }
                primed = true;
                HasMesh = true;
            }
            catch (Exception e)
            {
                if (!disposed) Debug.LogWarning($"[FruitGame] Face mesh failed: {e.Message}");
            }
            finally
            {
                busy = false;
            }
        }

        void OnDestroy()
        {
            disposed = true;
            worker?.Dispose();
            input?.Dispose();
            if (crop != null) { crop.Release(); Destroy(crop); }
        }
    }
}
