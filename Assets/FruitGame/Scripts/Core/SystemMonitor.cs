using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The computer's CPU and GPU load and temperatures for the staff screen's performance meter (Docs/DESIGN.md §15).
    /// macOS lets apps read neither temperatures nor GPU load without administrator rights, so this runs macmon
    /// (open source, MIT, github.com/vladkens/macmon; `brew install macmon`), which reads them from Apple Silicon's
    /// sensors without them, as a background process: `macmon pipe` prints one JSON line a second, read on its own
    /// thread. Without macmon, CPU load falls back to this app's own share of the processor, and the rest is unknown.
    /// </summary>
    public static class SystemMonitor
    {
        static readonly string[] Places = { "/opt/homebrew/bin/macmon", "/usr/local/bin/macmon" };
        static readonly Regex Number = new Regex("\"(cpu_active_ratio|gpu_active_ratio|cpu_temp_avg|gpu_temp_avg|all_power)\":(-?[0-9.eE+-]+|null)", RegexOptions.Compiled);

        static Process process;
        static int users;
        static volatile float cpuLoad = -1f, gpuLoad = -1f, cpuTemp = -1f, gpuTemp = -1f, power = -1f;
        // When the last line came, in ms on a clock that may be read from macmon's thread (Unity's may not).
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static volatile int lastLineMs = int.MinValue / 2;

        // Fallback CPU load: this process's processor time.
        static Process self;
        static TimeSpan lastCpu;
        static float lastCpuAt = -1f, ownLoad = -1f;

        /// <summary>True while macmon is sending readings.</summary>
        public static bool Live => process != null && (int)Clock.ElapsedMilliseconds - lastLineMs < 5000;
        /// <summary>Why there are no readings ("" while live).</summary>
        public static string Problem { get; private set; } = "";

        /// <summary>Whole-CPU load 0..1 (macmon), or this app's share of all cores without it; -1 = unknown.</summary>
        public static float CpuLoad => Live && cpuLoad >= 0f ? cpuLoad : OwnCpuLoad();
        /// <summary>True when CpuLoad is the whole computer's (macmon), false when it's only this app's.</summary>
        public static bool CpuIsWhole => Live && cpuLoad >= 0f;
        /// <summary>GPU load 0..1, or -1 (needs macmon).</summary>
        public static float GpuLoad => Live ? gpuLoad : -1f;
        /// <summary>Average CPU and GPU temperature in °C, or -1 (needs macmon).</summary>
        public static float CpuTemp => Live ? cpuTemp : -1f;
        public static float GpuTemp => Live ? gpuTemp : -1f;
        /// <summary>The chip's power in watts, or -1.</summary>
        public static float Power => Live ? power : -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Kill();
            users = 0;
            self = null;
            lastCpuAt = -1f;
            ownLoad = -1f;
        }

        /// <summary>Starts macmon if it isn't running (counted: each Start needs a Stop).</summary>
        public static void Start()
        {
            users++;
            if (process != null) return;
            Application.quitting -= Kill;
            Application.quitting += Kill;
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            string path = Array.Find(Places, File.Exists);
            if (path == null)
            {
                Problem = "temperatures need macmon (brew install macmon)";
                return;
            }
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo(path, "pipe -i 1000")
                    {
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                    },
                    EnableRaisingEvents = true,
                };
                p.OutputDataReceived += (_, e) => Read(e.Data);
                p.Start();
                p.BeginOutputReadLine();
                process = p;
                Problem = "";
            }
            catch (Exception e)
            {
                Problem = "macmon didn't start: " + e.Message;
            }
#else
            Problem = "temperatures are read on macOS only";
#endif
        }

        public static void Stop()
        {
            users = Mathf.Max(0, users - 1);
            if (users == 0) Kill();
        }

        static void Kill()
        {
            var p = process;
            process = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
        }

        /// <summary>One JSON line from macmon (its own thread): only the few numbers the meter shows are picked out.</summary>
        static void Read(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            foreach (Match m in Number.Matches(line))
            {
                float v = m.Groups[2].Value == "null" ? -1f : float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                switch (m.Groups[1].Value)
                {
                    case "cpu_active_ratio": cpuLoad = v; break;
                    case "gpu_active_ratio": gpuLoad = v; break;
                    case "cpu_temp_avg": cpuTemp = v; break;
                    case "gpu_temp_avg": gpuTemp = v; break;
                    case "all_power": power = v; break;
                }
            }
            lastLineMs = (int)Clock.ElapsedMilliseconds;
        }

        /// <summary>This process's processor time over the last second, as a share of all cores.</summary>
        static float OwnCpuLoad()
        {
            float now = Time.realtimeSinceStartup;
            if (lastCpuAt >= 0f && now - lastCpuAt < 1f) return ownLoad;
            try
            {
                self ??= Process.GetCurrentProcess();
                self.Refresh();
                var cpu = self.TotalProcessorTime;
                if (lastCpuAt >= 0f)
                    ownLoad = Mathf.Clamp01((float)(cpu - lastCpu).TotalSeconds / ((now - lastCpuAt) * SystemInfo.processorCount));
                lastCpu = cpu;
                lastCpuAt = now;
            }
            catch { ownLoad = -1f; }
            return ownLoad;
        }
    }
}
