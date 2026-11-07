using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Times each vision model on this computer's GPU (Docs/DESIGN.md §13, M6 2.2/2.3): one full inference at a time,
    /// waiting for the result, so the numbers are what each model costs per frame, whatever is in front of the camera.
    /// Models that aren't in the project are skipped. FruitGame > ML > Benchmark Models.
    /// </summary>
    public static class ModelBench
    {
        const string Dir = "Assets/FruitGame/ML/";
        const int Warmup = 3;
        const int Runs = 20;

        [MenuItem("FruitGame/ML/Benchmark Models")]
        public static string Run()
        {
            var log = new StringBuilder($"Model benchmark on {SystemInfo.graphicsDeviceName} ({Runs} runs each, GPU)\n");
            Bench(log, "BlazeFace short range, 128²", "blaze_face_short_range.onnx", m => new[] { Zeros(1, 128, 128, 3) });
            Bench(log, "Face Mesh v2, 256² (478 points)", "face_mesh.onnx", m => new[] { Zeros(1, 256, 256, 3) });
            Bench(log, "MediaPipe selfie segmentation, 256²", "selfie_segmentation.onnx", m => new[] { Zeros(Shape(m, 256)) });
            foreach (var extra in new[] { "selfie_segmenter.onnx", "selfie_multiclass_256x256.onnx", "face_landmarker.onnx" })
                Bench(log, extra, extra, m => new[] { Zeros(Shape(m, 256)) });
            BenchRvm(log, "RVM ResNet-50", "rvm_resnet50_fp32.onnx", 576, 768, 0.5f);
            foreach (var (w, h, ratio) in new[] { (576, 768, 0.5f), (576, 768, 0.25f), (384, 512, 0.5f), (384, 512, 0.25f) })
                BenchRvm(log, "RVM MobileNetV3", "rvm_mobilenetv3_fp32.onnx", w, h, ratio);
            Debug.Log("[FruitGame] " + log);
            return log.ToString();
        }

        static Tensor<float> Zeros(params int[] shape) => new Tensor<float>(new TensorShape(shape));
        static Tensor<float> Zeros(TensorShape shape) => new Tensor<float>(shape);

        /// <summary>The model's input shape, with any unknown sizes set to `side` (batch 1).</summary>
        static TensorShape Shape(Model m, int side)
        {
            var dyn = m.inputs[0].shape;
            if (dyn.IsStatic()) return dyn.ToTensorShape();
            // Only the batch is unknown: channels first (as the onnx-community export) or last (MediaPipe's own).
            return dyn.ToString().Contains(", 3, ") ? new TensorShape(1, 3, side, side) : new TensorShape(1, side, side, 3);
        }

        static void Bench(StringBuilder log, string label, string file, System.Func<Model, Tensor[]> inputs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(Dir + file);
            if (asset == null) { if (File.Exists(Dir + file) || !file.Contains("_")) log.AppendLine($"{label}: not found"); return; }
            var model = ModelLoader.Load(asset);
            Time(log, label, model, inputs(model));
        }

        static void BenchRvm(StringBuilder log, string label, string file, int w, int h, float ratioValue)
        {
            label = $"{label}, {w}×{h} at {ratioValue}";
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(Dir + file);
            if (asset == null) { log.AppendLine($"{label}: not in the project"); return; }
            var rvm = ModelLoader.Load(asset);
            var graph = new FunctionalGraph();
            var src = graph.AddInput(rvm, 0);
            var r = new FunctionalTensor[4];
            for (int i = 0; i < 4; i++) r[i] = graph.AddInput(rvm, i + 1);
            var ratio = Functional.Constant(new TensorShape(1), new[] { ratioValue });
            var outs = Functional.Forward(rvm, src, r[0], r[1], r[2], r[3], ratio);
            var model = graph.Compile(outs[1]);
            var inputs = new Tensor[] { Zeros(1, 3, h, w), Zeros(1, 1, 1, 1), Zeros(1, 1, 1, 1), Zeros(1, 1, 1, 1), Zeros(1, 1, 1, 1) };
            Time(log, label, model, inputs);
        }

        static void Time(StringBuilder log, string label, Model model, Tensor[] inputs)
        {
            try
            {
                using var worker = new Worker(model, BackendType.GPUCompute);
                for (int i = 0; i < Warmup; i++) Once(worker, inputs);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < Runs; i++) Once(worker, inputs);
                double ms = sw.Elapsed.TotalMilliseconds / Runs;
                log.AppendLine($"{label}: {ms:F1} ms ({1000.0 / ms:F0} per second at most)");
            }
            catch (System.Exception e)
            {
                log.AppendLine($"{label}: failed ({e.Message})");
            }
            finally
            {
                foreach (var t in inputs) t.Dispose();
            }
        }

        static void Once(Worker worker, Tensor[] inputs)
        {
            worker.Schedule(inputs);
            using var result = (worker.PeekOutput() as Tensor<float>).ReadbackAndClone();
        }
    }
}
