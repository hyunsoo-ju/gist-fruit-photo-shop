using System.Diagnostics;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Times the game's big parts for the staff screen's performance meter (Docs/DESIGN.md §15): each part wraps its
    /// per-frame work in <c>using (PerfMeter.Measure(PerfMeter.Part.X))</c>, and the meter keeps each part's main-thread
    /// time per frame, averaged. These are CPU times: what it costs this computer to run and hand the work to the GPU.
    /// The GPU's own time per part can't be read on this Mac (Unity's GPU recorder isn't supported on Metal); the GPU's
    /// total per frame comes from Unity's frame timing instead (PerfPanel).
    /// </summary>
    public static class PerfMeter
    {
        public enum Part { Matting, FaceFinding, FaceMesh, CutOut, Lighting }
        public const int Count = 5;

        static readonly long[] ticks = new long[Count];
        static readonly float[] average = new float[Count];
        static int lastFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            System.Array.Clear(ticks, 0, Count);
            System.Array.Clear(average, 0, Count);
            lastFrame = -1;
        }

        /// <summary>Times the work up to the end of the using block.</summary>
        public static Scope Measure(Part part) => new Scope(part);

        /// <summary>The part's main-thread time per frame in ms, averaged over the last ~30 frames.</summary>
        public static float Ms(Part part)
        {
            Roll();
            return average[(int)part];
        }

        /// <summary>At the first use in a new frame: fold the last frame's totals into the averages.</summary>
        static void Roll()
        {
            int frame = Time.frameCount;
            if (frame == lastFrame) return;
            int frames = lastFrame < 0 ? 1 : Mathf.Clamp(frame - lastFrame, 1, 10);
            for (int i = 0; i < Count; i++)
            {
                float ms = (float)(ticks[i] * 1000.0 / Stopwatch.Frequency) / frames;
                average[i] = Mathf.Lerp(average[i], ms, 1f - Mathf.Pow(1f - 1f / 30f, frames));
                ticks[i] = 0;
            }
            lastFrame = frame;
        }

        public readonly struct Scope : System.IDisposable
        {
            readonly int part;
            readonly long start;

            public Scope(Part part)
            {
                Roll();
                this.part = (int)part;
                start = Stopwatch.GetTimestamp();
            }

            public void Dispose() => ticks[part] += Stopwatch.GetTimestamp() - start;
        }
    }
}
