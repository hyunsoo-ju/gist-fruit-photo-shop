using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Serialization;

namespace FruitGame
{
    /// <summary>
    /// Keeps a person mask (R = 0 background … 1 person) for the webcam feed in Mask, aligned 1:1 with
    /// WebcamSource.Output's UVs (Docs/DESIGN.md §13). Everything stays on the GPU.
    /// Preferred: Robust Video Matting (RVM), a video matting model that carries four recurrent state tensors
    /// from frame to frame, so hair and clothing edges stay stable; runs on the whole frame and outputs a soft alpha
    /// matte at that resolution (rounded to RVM-friendly sizes, e.g. 896×512), once for every new camera frame
    /// (StepRvm). Fallback: MediaPipe Selfie Segmentation at 256×256.
    /// Three options can be switched live for comparison (M6 2.2; F7 while the F1 debug view is open): RVM ResNet-50,
    /// the lighter RVM MobileNetV3 and MediaPipe. Model costs on this computer: FruitGame > ML > Benchmark Models.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public class PersonSegmenter : MonoBehaviour
    {
        [SerializeField] WebcamSource source;
        [Tooltip("Robust Video Matting ONNX (GPL-3.0). Used when set.")]
        [SerializeField] ModelAsset rvmModel;
        [Tooltip("RVM's internal downsample ratio: lower = faster, higher = finer detail. ~0.4–0.6 suits 540×720.")]
        [SerializeField, Range(0.1f, 1f)] float rvmDownsample = 0.5f;
        [Tooltip("RVM MobileNetV3 ONNX (GPL-3.0): about five times cheaper than ResNet-50 at downsample 0.25.")]
        [SerializeField] ModelAsset rvmLightModel;
        [SerializeField, Range(0.1f, 1f)] float rvmLightDownsample = 0.25f;
        [Tooltip("MediaPipe Selfie Segmentation ONNX, used only if no RVM model is set.")]
        [SerializeField] ModelAsset model;
        [Tooltip("Which model to start with (switchable live with F7 while the F1 debug view is open).")]
        [SerializeField] Option startWith = Option.RvmMobileNet;
        [SerializeField] BackendType backend = BackendType.GPUCompute;
        [Tooltip("The lowest frame rate RVM may bring the game down to. Above it, RVM runs whole on every new camera frame, so there's a fresh matte for every picture; only if that would drop the average frame rate below this is each run spread over a few frames.")]
        [SerializeField, FormerlySerializedAs("targetFrameRate")] float frameRateFloor = 40f;
        [Tooltip("The fewest mattes per second, whatever the frame time: the budget never shrinks below this. Without it a frame already slower than the floor (the realistic look) squeezed RVM to 2 layers a frame, about one matte a second.")]
        [SerializeField] float minMatteRate = 30f;

        const int MediaPipeSize = 256;

        public enum Option { RvmResNet50, RvmMobileNet, MediaPipe }
        /// <summary>The model to use: startWith, unless the staff Settings saved another (StaffPanel).</summary>
        public static Option Preferred { get; set; } = Option.RvmMobileNet;

        void Awake() => Preferred = startWith;
        public Option Current { get; private set; }
        float downsample;

        public RenderTexture Mask { get; private set; }
        /// <summary>True once at least one mask has been produced.</summary>
        public bool HasMask { get; private set; }
        public float Rate { get; private set; }
        /// <summary>Mattes (or masks) finished since the start, for measuring.</summary>
        public int MatteCount { get; private set; }
        public string Status { get; private set; } = "Idle";
        public bool UsingRvm { get; private set; }

        Worker worker;
        Tensor<float> input;
        readonly Tensor[] states = new Tensor[4];
        readonly Tensor[] rvmInputs = new Tensor[5];
        TextureTransform outputTransform;
        System.Collections.IEnumerator run;
        float layersPerFrame = 5000f;
        float smoothDt;
        int lastFedFrame = -1;
        bool outputChecked;
        int frames;
        float rateWindowStart;

        void Start()
        {
            if (source == null || (rvmModel == null && rvmLightModel == null && model == null))
            {
                Status = "Missing model or webcam source";
                enabled = false;
                return;
            }
            Use(Available(Preferred) ? Preferred : Next(Preferred));
        }

        bool Available(Option o) => o == Option.RvmResNet50 ? rvmModel != null : o == Option.RvmMobileNet ? rvmLightModel != null : model != null;

        Option Next(Option o)
        {
            for (int i = 1; i <= 3; i++)
            {
                var n = (Option)(((int)o + i) % 3);
                if (Available(n)) return n;
            }
            return o;
        }

        /// <summary>Switches to the next available model (staff comparison, F7).</summary>
        public void Cycle() => Use(Next(Current));

        public void Use(Option option)
        {
            if (!Available(option)) return;
            worker?.Dispose();
            run = null;
            runLayers = layersThisRun = 0; // a different model has a different length
            Current = option;
            UsingRvm = option != Option.MediaPipe;
            downsample = option == Option.RvmMobileNet ? rvmLightDownsample : rvmDownsample;
            worker = new Worker(UsingRvm ? BuildRvm(option == Option.RvmMobileNet ? rvmLightModel : rvmModel) : ModelLoader.Load(model), backend);
            input?.Dispose();
            input = null;
            outputChecked = false;
            outputTransform = new TextureTransform();
            frames = 0;
            rateWindowStart = Time.unscaledTime;
            layersPerFrame = 5000f;
            smoothDt = 0f;
            lastFedFrame = -1;
            Status = "Waiting for camera";
        }

        string Label => Current == Option.RvmResNet50 ? "RVM ResNet-50" : Current == Option.RvmMobileNet ? "RVM MobileNetV3" : "MediaPipe";

        /// <summary>RVM with downsample_ratio baked in as a constant; outputs pha, r1o … r4o.</summary>
        Model BuildRvm(ModelAsset asset)
        {
            var rvm = ModelLoader.Load(asset);
            var graph = new FunctionalGraph();
            var src = graph.AddInput(rvm, 0);
            var r1 = graph.AddInput(rvm, 1);
            var r2 = graph.AddInput(rvm, 2);
            var r3 = graph.AddInput(rvm, 3);
            var r4 = graph.AddInput(rvm, 4);
            var ratio = Functional.Constant(new TensorShape(1), new[] { downsample });
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
            // A photo is waiting for its own matte: don't start another live run until it has had its turn.
            if (photoWaiting > 0 && run == null) return;

            using (PerfMeter.Measure(PerfMeter.Part.Matting))
            {
                if (UsingRvm) StepRvm();
                else RunMediaPipe();
            }
        }

        /// <summary>
        /// Runs RVM once for every new camera frame, whole within the frame when the game can afford it, so there are as
        /// many mattes as pictures and each matte belongs to the picture shown with it (no edge trailing a moving
        /// player). If running whole would bring the average frame rate below frameRateFloor, each run is spread over a
        /// few frames instead (a slice of layers per frame), and the cut-out keeps the last finished matte meanwhile.
        /// </summary>
        void StepRvm()
        {
            AdaptBudget();
            if (run == null)
            {
                // Nothing new to matte: the same picture would give the same matte (and cost a whole run).
                if (source.FrameCount == lastFedFrame) return;
                lastFedFrame = source.FrameCount;
                var frame = source.Output;
                int w = RvmSize(frame.width), h = RvmSize(frame.height);
                EnsureBuffers(w, h);
                // Pixel values 0..1, NCHW. The input and states aren't touched again until this run finishes.
                TextureConverter.ToTensor(frame, input, new TextureTransform());
                rvmInputs[0] = input;
                for (int i = 0; i < 4; i++) rvmInputs[i + 1] = states[i];
                run = worker.ScheduleIterable(rvmInputs);
            }

            int budget = Mathf.Max(1, Mathf.RoundToInt(layersPerFrame));
            bool finished = false;
            // One call more than the layers: the iterator only reports the end on the call after the last layer, and
            // without it a whole-run budget left that call (and the matte) for the next frame.
            for (int i = 0; i <= budget; i++)
            {
                if (!run.MoveNext()) { finished = true; break; }
                layersThisRun++;
            }
            if (!finished) return;
            run = null;
            runLayers = layersThisRun;
            layersThisRun = 0;

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

        /// <summary>
        /// Grows the per-frame layer budget up to a whole run while the average frame rate stays above the floor, and
        /// shrinks it when it falls below. Judged on the frame time averaged over the last ~20 frames: single frames
        /// vary (a frame with a run in it is slower than one without), and reacting to each one kept cutting runs into
        /// a large slice and a small leftover, so every matte took two frames even with time to spare.
        /// </summary>
        void AdaptBudget()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            // A plain average over the last ~20 frames (1 / it = the frame rate). Weighting each frame by its own length
            // counted the slow ones more, and read near the floor while the game actually ran at 60.
            smoothDt = smoothDt <= 0f ? dt : Mathf.Lerp(smoothDt, dt, 0.05f);
            float limit = 1f / Mathf.Max(10f, frameRateFloor);
            if (smoothDt > limit * 1.03f) layersPerFrame *= 0.9f;
            else if (smoothDt < limit * 0.97f) layersPerFrame *= 1.1f;
            // Never fewer layers than finish minMatteRate runs a second at the current frame time; never more than a
            // whole run (so a slowdown is answered at once, not after shrinking from far above it).
            float least = runLayers > 0 ? runLayers * minMatteRate * smoothDt : 2f;
            float most = runLayers > 0 ? runLayers : 5000f;
            layersPerFrame = Mathf.Clamp(layersPerFrame, Mathf.Max(2f, Mathf.Min(least, most)), most);
        }

        /// <summary>True while every run finishes within its frame (a matte for every camera frame).</summary>
        public bool Whole => runLayers > 0 && layersPerFrame >= runLayers;

        int runLayers, layersThisRun;

        int photoWaiting;

        /// <summary>
        /// Background removal for a photo: the matte of exactly this (held) frame, written into `into` (resized to the
        /// mask's size if needed). The live matte is time-sliced over several frames, so it trails the picture a
        /// little; a photo waits for the run in progress to finish, then runs the model in one go on its own frame
        /// (one frame's worth of GPU work), with RVM's current recurrent state. False if it can't (no model, no time).
        /// </summary>
        public async Awaitable<bool> MatteAsync(Texture frame, RenderTexture into)
        {
            if (worker == null || frame == null || into == null || Mask == null || !HasMask) return false;
            float giveUp = Time.unscaledTime + 1f;
            photoWaiting++;
            try
            {
                while (run != null)
                {
                    if (this == null || Time.unscaledTime > giveUp) return false;
                    await Awaitable.NextFrameAsync();
                }
                if (this == null || worker == null) return false;
                if (UsingRvm)
                {
                    EnsureBuffers(RvmSize(frame.width), RvmSize(frame.height));
                    TextureConverter.ToTensor(frame, input, new TextureTransform());
                    rvmInputs[0] = input;
                    for (int i = 0; i < 4; i++) rvmInputs[i + 1] = states[i];
                    // A seed state (before the first live run) has the wrong shape for a whole run; wait for a live one.
                    if (states[0] == null || states[0].shape.rank != 4 || states[0].shape[1] == 1) return false;
                    worker.Schedule(rvmInputs);
                }
                else
                {
                    EnsureBuffers(MediaPipeSize, MediaPipeSize);
                    TextureConverter.ToTensor(frame, input, new TextureTransform());
                    worker.Schedule(input);
                }
                TextureConverter.RenderToTexture(worker.PeekOutput(0) as Tensor<float>, into, outputTransform);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FruitGame] Background removal for a photo failed: {e.Message}");
                return false;
            }
            finally { photoWaiting--; }
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
                Debug.Log($"[FruitGame] Segmentation: {Label}{(UsingRvm ? $" (downsample {downsample}, time-sliced)" : "")}, " +
                          $"input {input.shape}, alpha {s}");
            }

            TextureConverter.RenderToTexture(alpha, Mask, outputTransform);
            HasMask = true;
            MatteCount++;
            Status = !UsingRvm ? $"{Label} mask live"
                : Whole ? $"{Label} matte live (every camera frame)"
                : $"{Label} matte live (spread: {layersPerFrame:0} of {runLayers} layers/frame)";
            CountRate();
        }

        /// <summary>
        /// RVM's encoder/decoder concatenates features at 1/2 … 1/16 of the downsampled size, so each side times
        /// the downsample ratio must be a multiple of 32 (128 px at 0.25). Round each side down when that keeps at least
        /// 90 % of it, else up: a 960 × 540 frame goes in at 896 × 512, shrunk evenly by about 6 % (rounding up gave
        /// 1024 × 640, stretched 7 % one way and 19 % the other, and a run took 22 % longer). The matte is stretched
        /// back over the same frame, so UVs still line up.
        /// </summary>
        int RvmSize(int frameSide)
        {
            int unit = Mathf.Max(1, Mathf.RoundToInt(32f / downsample));
            int down = Mathf.Max(1, frameSide / unit) * unit;
            return down >= frameSide * 0.9f ? down : Mathf.CeilToInt(frameSide / (float)unit) * unit;
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
