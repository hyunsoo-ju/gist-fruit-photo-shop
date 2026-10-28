using Unity.InferenceEngine;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Keeps a person mask (R = 0 background … 1 person) for the webcam feed in Mask, aligned 1:1 with
    /// WebcamSource.Output's UVs (Docs/DESIGN.md §9). Everything stays on the GPU.
    /// Preferred: Robust Video Matting (RVM), a video matting model that carries four recurrent state tensors
    /// from frame to frame, so hair and clothing edges stay stable; runs on the full 540×720 frame and outputs a
    /// soft alpha matte at that resolution (rounded up to RVM-friendly sizes, e.g. 576×768). Fallback: MediaPipe Selfie Segmentation at 256×256.
    /// </summary>
    public class PersonSegmenter : MonoBehaviour
    {
        [SerializeField] WebcamSource source;
        [Tooltip("Robust Video Matting ONNX (GPL-3.0). Used when set.")]
        [SerializeField] ModelAsset rvmModel;
        [Tooltip("RVM's internal downsample ratio: lower = faster, higher = finer detail. ~0.4–0.6 suits 540×720.")]
        [SerializeField, Range(0.1f, 1f)] float rvmDownsample = 0.5f;
        [Tooltip("MediaPipe Selfie Segmentation ONNX, used only if no RVM model is set.")]
        [SerializeField] ModelAsset model;
        [SerializeField] BackendType backend = BackendType.GPUCompute;
        [Tooltip("Frame rate the game should keep; RVM gets whatever time is left each frame.")]
        [SerializeField] float targetFrameRate = 60f;

        const int MediaPipeSize = 256;

        public RenderTexture Mask { get; private set; }
        /// <summary>True once at least one mask has been produced.</summary>
        public bool HasMask { get; private set; }
        public float Rate { get; private set; }
        public string Status { get; private set; } = "Idle";
        public bool UsingRvm { get; private set; }

        Worker worker;
        Tensor<float> input;
        readonly Tensor[] states = new Tensor[4];
        readonly Tensor[] rvmInputs = new Tensor[5];
        TextureTransform outputTransform;
        System.Collections.IEnumerator run;
        float layersPerFrame = 50f;
        bool outputChecked;
        int frames;
        float rateWindowStart;

        void Start()
        {
            if (source == null || (rvmModel == null && model == null))
            {
                Status = "Missing model or webcam source";
                enabled = false;
                return;
            }

            UsingRvm = rvmModel != null;
            worker = new Worker(UsingRvm ? BuildRvm() : ModelLoader.Load(model), backend);
            outputTransform = new TextureTransform();
            rateWindowStart = Time.unscaledTime;
            Status = "Waiting for camera";
        }

        /// <summary>RVM with downsample_ratio baked in as a constant; outputs pha, r1o … r4o.</summary>
        Model BuildRvm()
        {
            var rvm = ModelLoader.Load(rvmModel);
            var graph = new FunctionalGraph();
            var src = graph.AddInput(rvm, 0);
            var r1 = graph.AddInput(rvm, 1);
            var r2 = graph.AddInput(rvm, 2);
            var r3 = graph.AddInput(rvm, 3);
            var r4 = graph.AddInput(rvm, 4);
            var ratio = Functional.Constant(new TensorShape(1), new[] { rvmDownsample });
            var outs = Functional.Forward(rvm, src, r1, r2, r3, r4, ratio);
            ResetStates();
            return graph.Compile(outs[1], outs[2], outs[3], outs[4], outs[5]);
        }

        /// <summary>RVM starts from zero states (shape 1×1×1×1, broadcast by the model).</summary>
        void ResetStates()
        {
            for (int i = 0; i < states.Length; i++)
            {
                states[i]?.Dispose();
                states[i] = new Tensor<float>(new TensorShape(1, 1, 1, 1), new[] { 0f });
            }
        }

        void Update()
        {
            if (worker == null || source == null || !source.HasFrames || source.Output == null) return;

            if (UsingRvm) StepRvm();
            else RunMediaPipe();
        }

        /// <summary>
        /// Advances RVM by a slice of layers each frame instead of running it whole, so a heavy model doesn't stall
        /// the game: the slice size adapts to keep the frame time near targetFrameRate, and the cut-out keeps using
        /// the last finished matte (the camera colour itself still updates every frame).
        /// </summary>
        void StepRvm()
        {
            if (run == null)
            {
                var frame = source.Output;
                int w = RvmSize(frame.width), h = RvmSize(frame.height);
                EnsureBuffers(w, h);
                // Pixel values 0..1, NCHW. The input and states aren't touched again until this run finishes.
                TextureConverter.ToTensor(frame, input, new TextureTransform());
                rvmInputs[0] = input;
                for (int i = 0; i < 4; i++) rvmInputs[i + 1] = states[i];
                run = worker.ScheduleIterable(rvmInputs);
            }

            AdaptBudget();
            int budget = Mathf.Max(1, Mathf.RoundToInt(layersPerFrame));
            bool finished = false;
            for (int i = 0; i < budget; i++)
            {
                if (!run.MoveNext()) { finished = true; break; }
            }
            if (!finished) return;
            run = null;

            // Carry the recurrent state into the next run (copied, since outputs are reused by the worker).
            // CopyOutput can only reuse a tensor that's big enough, so reallocate when the shape changes
            // (once: from the 1×1×1×1 seed to the real state size after the first run).
            for (int i = 0; i < 4; i++)
            {
                var produced = worker.PeekOutput(i + 1);
                if (states[i] != null && states[i].shape != produced.shape)
                {
                    states[i].Dispose();
                    states[i] = null;
                }
                worker.CopyOutput(i + 1, ref states[i]);
            }
            Publish(worker.PeekOutput(0) as Tensor<float>);
        }

        /// <summary>Grows the per-frame layer budget while frames are fast and shrinks it when they're slow.</summary>
        void AdaptBudget()
        {
            float target = 1f / Mathf.Max(10f, targetFrameRate);
            float dt = Time.unscaledDeltaTime;
            if (dt > target * 1.1f) layersPerFrame *= 0.85f;
            else if (dt < target * 0.95f) layersPerFrame *= 1.05f;
            layersPerFrame = Mathf.Clamp(layersPerFrame, 2f, 5000f);
        }

        void RunMediaPipe()
        {
            EnsureBuffers(MediaPipeSize, MediaPipeSize);
            TextureConverter.ToTensor(source.Output, input, new TextureTransform());
            worker.Schedule(input);
            Publish(worker.PeekOutput() as Tensor<float>);
        }

        void Publish(Tensor<float> alpha)
        {
            if (!outputChecked)
            {
                var s = alpha.shape;
                bool channelLast = s.rank == 4 && s[3] == 1 && s[1] != 1;
                outputTransform = new TextureTransform().SetTensorLayout(channelLast ? TensorLayout.NHWC : TensorLayout.NCHW);
                outputChecked = true;
                Debug.Log($"[FruitGame] Segmentation: {(UsingRvm ? $"RVM (downsample {rvmDownsample}, time-sliced)" : "MediaPipe")}, " +
                          $"input {input.shape}, alpha {s}");
            }

            TextureConverter.RenderToTexture(alpha, Mask, outputTransform);
            HasMask = true;
            Status = UsingRvm ? $"RVM matte live ({layersPerFrame:0} layers/frame)" : "Mask live";
            CountRate();
        }

        /// <summary>
        /// RVM's encoder/decoder concatenates features at 1/2 … 1/16 of the downsampled size, so each side times
        /// the downsample ratio must be a multiple of 32. Round the input up (e.g. 540×720 at 0.5 → 576×768);
        /// the matte is stretched back over the same frame, so UVs still line up.
        /// </summary>
        int RvmSize(int frameSide)
        {
            int unit = Mathf.Max(1, Mathf.RoundToInt(32f / rvmDownsample));
            return Mathf.CeilToInt(frameSide / (float)unit) * unit;
        }

        void EnsureBuffers(int w, int h)
        {
            if (input == null || input.shape[3] != w || input.shape[2] != h)
            {
                input?.Dispose();
                input = new Tensor<float>(new TensorShape(1, 3, h, w));
                if (UsingRvm) ResetStates();
            }
            // The mask covers the whole webcam frame, so its UVs line up 1:1 with the frame's.
            if (Mask == null || Mask.width != w || Mask.height != h)
            {
                if (Mask != null) { Mask.Release(); Destroy(Mask); }
                Mask = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "Person Mask" };
                Mask.Create();
            }
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
            worker?.Dispose();
            input?.Dispose();
            foreach (var s in states) s?.Dispose();
            if (Mask != null)
            {
                Mask.Release();
                Destroy(Mask);
            }
        }
    }
}
