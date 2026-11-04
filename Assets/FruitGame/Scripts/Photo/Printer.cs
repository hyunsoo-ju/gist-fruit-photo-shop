using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace FruitGame
{
    /// <summary>
    /// The booth printer (Docs/DESIGN.md §14, M17): sends the card to the Canon SELPHY CP1500 at its exact paper size
    /// (borderless postcard, no rescaling to a page), keeps track of the jobs still in its queue so the preview can say
    /// how long to wait, and counts the paper in the tray and the prints left in the ink cassette. Its staff text is English, like the other debug views.
    /// Staff: F6 shows the printer box (state, queue, paper and ink, the printer's own alerts); while it's open, 1 means
    /// "paper tray refilled" and 2 "new ink cassette". A warning line shows by itself when either is nearly out or the
    /// printer reports a problem. macOS prints through CUPS (`lp`); Windows uses the shell "print" verb (no queue info).
    /// </summary>
    public class Printer : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [Tooltip("Staff printer box (F6).")]
        [SerializeField] TMP_Text staffBox;
        [Tooltip("Dark backing behind the staff box, shown and hidden with it (optional).")]
        [SerializeField] GameObject staffPanel;
        [Tooltip("Warning line for staff, shown when paper or ink is low or the printer reports a problem.")]
        [SerializeField] TMP_Text warning;
        [SerializeField] float pollEvery = 2f;

        const string PaperKey = "FruitGame.print.paper";
        const string InkKey = "FruitGame.print.ink";
        const string TotalKey = "FruitGame.print.total";

        static bool IsMac => Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor;

        // Shared with the polling thread.
        readonly object gate = new object();
        readonly HashSet<string> pending = new HashSet<string>();
        string printer;
        string state = "…";
        string alerts = "";
        volatile bool running;
        Thread poller;
        readonly AutoResetEvent wake = new AutoResetEvent(false);

        bool showBox;

        public string LastError { get; private set; }
        public int PaperLeft => PlayerPrefs.GetInt(PaperKey, config.trayCapacity);
        public int InkLeft => PlayerPrefs.GetInt(InkKey, config.inkCapacity);
        /// <summary>Our jobs still in the printer's queue (printing or waiting).</summary>
        public int Pending { get { lock (gate) return pending.Count; } }
        public bool LowPaper => PaperLeft <= config.printWarnAt;
        public bool LowInk => InkLeft <= config.printWarnAt;

        void Start()
        {
            BoxObject?.SetActive(false);
            if (warning != null) warning.gameObject.SetActive(false);
            if (!IsMac) return;
            running = true;
            poller = new Thread(Poll) { IsBackground = true, Name = "FruitGame Printer" };
            poller.Start();
        }

        void OnDestroy()
        {
            running = false;
            wake.Set();
        }

        /// <summary>
        /// Prints the card at `path` (config.printCopies copies). Returns false with LastError if it couldn't be sent.
        /// `ahead` is how many of our prints were already queued in front of it.
        /// </summary>
        public bool Print(string path, out int ahead)
        {
            ahead = Pending;
            int copies = Mathf.Max(1, config.printCopies);
            try
            {
                if (IsMac)
                {
                    string dest = Destination();
                    string args = (dest != null ? $"-d \"{dest}\" " : "") +
                                  $"-n {copies} -t FruitGame -o media={config.printMedia} -o print-scaling=fill \"{path}\"";
                    var (code, output, error) = Run("/usr/bin/lp", args, 10000);
                    if (code != 0)
                    {
                        LastError = string.IsNullOrEmpty(error) ? $"lp exited with {code}" : error;
                        Debug.LogWarning($"[FruitGame] Print failed: {LastError}");
                        return false;
                    }
                    // "request id is Canon_SELPHY_CP1500-12 (1 file(s))"
                    var m = Regex.Match(output, @"request id is (\S+)");
                    if (m.Success) lock (gate) pending.Add(m.Groups[1].Value);
                    wake.Set();
                }
                else
                {
                    for (int i = 0; i < copies; i++)
                        Process.Start(new ProcessStartInfo(path) { Verb = "print", UseShellExecute = true, CreateNoWindow = true });
                }
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning($"[FruitGame] Print failed: {e.Message}");
                return false;
            }

            LastError = null;
            PlayerPrefs.SetInt(PaperKey, Mathf.Max(0, PaperLeft - copies));
            PlayerPrefs.SetInt(InkKey, Mathf.Max(0, InkLeft - copies));
            PlayerPrefs.SetInt(TotalKey, PlayerPrefs.GetInt(TotalKey, 0) + copies);
            PlayerPrefs.Save();
            Debug.Log($"[FruitGame] Sent to printer ({copies}×, {ahead} ahead): {path}");
            return true;
        }

        /// <summary>Seconds until a print sent now (with `ahead` queued in front) comes out, roughly.</summary>
        public int WaitSeconds(int ahead) => Mathf.RoundToInt(config.printSeconds * (ahead + 1) * Mathf.Max(1, config.printCopies));

        public void PaperRefilled()
        {
            PlayerPrefs.SetInt(PaperKey, config.trayCapacity);
            PlayerPrefs.Save();
        }

        public void InkReplaced()
        {
            PlayerPrefs.SetInt(InkKey, config.inkCapacity);
            PlayerPrefs.Save();
        }

        GameObject BoxObject => staffPanel != null ? staffPanel : staffBox != null ? staffBox.gameObject : null;

        string Destination()
        {
            if (!string.IsNullOrEmpty(config.printerName)) return config.printerName;
            lock (gate) return printer;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f6Key.wasPressedThisFrame) showBox = !showBox;
                if (showBox && !CameraMenu.IsOpen && (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame)) PaperRefilled();
                if (showBox && !CameraMenu.IsOpen && (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame)) InkReplaced();
            }

            string st, al;
            int queued;
            lock (gate) { st = state; al = alerts; queued = pending.Count; }
            bool problem = !string.IsNullOrEmpty(al) && al != "none";

            if (staffBox != null)
            {
                if (BoxObject.activeSelf != showBox) BoxObject.SetActive(showBox);
                if (showBox)
                    staffBox.text = "Printer  (F6)\n" +
                                    $"{st}\n" +
                                    (problem ? $"Printer alert: {al}\n" : "") +
                                    $"Queued: {queued}\n" +
                                    $"Paper: {PaperLeft}/{config.trayCapacity} left  [1]\n" +
                                    $"Ink: {InkLeft}/{config.inkCapacity} prints left  [2]\n" +
                                    $"Printed so far: {PlayerPrefs.GetInt(TotalKey, 0)}" +
                                    (LastError != null ? $"\n{LastError}" : "");
            }

            if (warning != null)
            {
                string w = problem ? $"Printer alert: {al}"
                    : LowPaper ? $"Paper low: {PaperLeft} left"
                    : LowInk ? $"Ink low: {InkLeft} prints left"
                    : null;
                bool show = w != null && !showBox;
                if (warning.gameObject.activeSelf != show) warning.gameObject.SetActive(show);
                if (show) warning.text = w + "  (F6)";
            }
        }

        // Background: find the default printer, then every few seconds read its state, alerts and queue.
        void Poll()
        {
            while (running)
            {
                try
                {
                    string dest = !string.IsNullOrEmpty(config.printerName) ? config.printerName : DefaultPrinter();
                    lock (gate) printer = dest;
                    if (dest == null)
                    {
                        lock (gate) { state = "No printer set up"; alerts = ""; }
                    }
                    else
                    {
                        var (_, info, _) = Run("/usr/bin/lpstat", $"-l -p \"{dest}\"", 5000);
                        string first = info.Split('\n').FirstOrDefault()?.Trim() ?? "";
                        var al = Regex.Match(info, @"Alerts:\s*(.+)");
                        var (_, queue, _) = Run("/usr/bin/lpstat", $"-o \"{dest}\"", 5000);
                        var live = new HashSet<string>(queue.Split('\n').Select(l => l.Split(' ').FirstOrDefault()).Where(s => !string.IsNullOrEmpty(s)));
                        lock (gate)
                        {
                            // "printer X is idle.  enabled since …" / "printer X now printing X-12." / "… disabled since …"
                            string key = first.Contains("now printing") ? "printing"
                                : first.Contains("disabled") ? "stopped (check System Settings)"
                                : first.Contains("is idle") ? "idle" : null;
                            state = key != null ? $"{dest}: {key}" : first.Length > 0 ? first : dest;
                            alerts = al.Success ? al.Groups[1].Value.Trim() : "";
                            pending.RemoveWhere(id => !live.Contains(id));
                        }
                    }
                }
                catch (Exception e)
                {
                    lock (gate) state = e.Message;
                }
                wake.WaitOne(TimeSpan.FromSeconds(Mathf.Max(0.5f, pollEvery)));
            }
        }

        static string DefaultPrinter()
        {
            var (_, output, _) = Run("/usr/bin/lpstat", "-d", 5000);
            var m = Regex.Match(output, @"destination:\s*(\S+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        static (int code, string output, string error) Run(string file, string args, int timeoutMs)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (-1, output, "timed out"); }
            return (p.ExitCode, output.Trim(), error.Trim());
        }
    }
}
