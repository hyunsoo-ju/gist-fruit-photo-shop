using System;
using Unity.InferenceEngine;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// MediaPipe Face Mesh (478 landmarks incl. irises; ONNX from astaileyyoung/FaceMeshONNX, MIT) for the photo
    /// booth's face reshaping and makeup (Docs/DESIGN.md §14). Crops a square around the BlazeFace box, runs the mesh on
    /// the GPU, and maps the points back into webcam-output UV (the same space as FaceTracker / the cut-out).
    /// Only runs while Active (today: whenever the beauty filter is on, which is always). Also gives the head's
    /// rotation from the 3D points (M6 2.3), for trying rotation steering.
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
        /// <summary>Set while the basket is steered by head turn (FaceToBasketMapper), which needs the mesh too.</summary>
        public static bool NeededForSteering { get; set; }
        public bool HasMesh { get; private set; }
        /// <summary>Landmarks in webcam-output UV (0..1, origin bottom-left).</summary>
        public Vector2[] Points { get; } = new Vector2[Count];
        /// <summary>Head rotation in degrees as seen on screen: yaw + = turned toward screen right, pitch + = tilted up,
        /// roll + = tilted clockwise. From the face's own axes (eye corners across, forehead to chin down).</summary>
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float Roll { get; private set; }
        /// <summary>Meshes per second actually achieved (debug overlay).</summary>
        public float Rate { get; private set; }

        int frames;
        float rateWindowStart;

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
            if (!Active && !NeededForSteering) { HasMesh = false; primed = false; return; }
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
                if (disposed || (!Active && !NeededForSteering)) return;

                float score = 1f / (1f + Mathf.Exp(-presence[0]));
                if (score < 0.5f) { HasMesh = false; return; }

                float keep = primed ? smoothing : 0f;
                UpdatePose(landmarks, keep);
                CountRate();
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

        /// <summary>
        /// Head rotation from three landmark pairs in the crop's own pixel space (x right, y down, z away from the
        /// camera; the crop is square, so all three axes share a scale): across = outer eye corners 33 → 263,
        /// down = forehead 10 → chin 152, forward = across × down.
        /// </summary>
        void UpdatePose(Tensor<float> l, float keep)
        {
            Vector3 P(int i) => new Vector3(l[i * 3], l[i * 3 + 1], l[i * 3 + 2]);
            // Screen space: the frame is mirrored, so take the eye axis pointing screen-right whichever eye that is.
            var across = (P(263) - P(33)).normalized;
            if (across.x < 0f) across = -across;
            var down = P(152) - P(10);
            down = (down - Vector3.Dot(down, across) * across).normalized;
            // Points into the head, away from the camera; turning to face screen-right swings it screen-left.
            var forward = Vector3.Cross(across, down);
            float yaw = -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(across.y, across.x) * Mathf.Rad2Deg;
            Yaw = Mathf.Lerp(yaw, Yaw, keep);
            Pitch = Mathf.Lerp(pitch, Pitch, keep);
            Roll = Mathf.Lerp(roll, Roll, keep);
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
            if (crop != null) { crop.Release(); Destroy(crop); }
        }
    }
}
