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
    /// how long to wait, and passes on the printer's own paper and ink warnings and errors (it doesn't guess what's left
    /// by counting: the SELPHY doesn't report a count). Its staff text is English, like the other debug views.
    /// Staff: the printer box (state, queue, the printer's alerts, prints so far) is on the staff panel's Printing page.
    /// With one screen, a warning line shows by itself while the panel is closed whenever the printer reports something. macOS prints through CUPS (`lp`); Windows uses the shell "print" verb (no queue info).
    /// </summary>
    public class Printer : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [Tooltip("Staff printer box (F6).")]
        [SerializeField] TMP_Text staffBox;
        [Tooltip("Dark backing behind the staff box, shown and hidden with it (optional).")]
        [SerializeField] GameObject staffPanel;
        [Tooltip("Warning line for staff, shown when the printer reports a paper or ink warning or any other problem.")]
        [SerializeField] TMP_Text warning;
        [SerializeField] float pollEvery = 2f;

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
        /// <summary>Our jobs still in the printer's queue (printing or waiting).</summary>
        public int Pending { get { lock (gate) return pending.Count; } }
        /// <summary>A problem the player should hear about (shown on the guest screen too): no printer set up, the queue
        /// stopped, or an alert from the printer itself (offline, no paper, no ink, jam).</summary>
        public bool HasProblem
        {
            get
            {
                if (!IsMac) return false;
                string st, al;
                lock (gate) { st = state; al = alerts; }
                return Blocking(al) || st.Contains("stopped") || st.StartsWith("No printer");
            }
        }
        /// <summary>Two screens: the printer box sits in the staff column all the time (StaffScreen).</summary>
        public bool AlwaysShowBox { get; set; }

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
            PlayerPrefs.SetInt(TotalKey, PlayerPrefs.GetInt(TotalKey, 0) + copies);
            PlayerPrefs.Save();
            Debug.Log($"[FruitGame] Sent to printer ({copies}×, {ahead} ahead): {path}");
            return true;
        }

        /// <summary>Seconds until a print sent now (with `ahead` queued in front) comes out, roughly.</summary>
        public int WaitSeconds(int ahead) => Mathf.RoundToInt(config.printSeconds * (ahead + 1) * Mathf.Max(1, config.printCopies));

        /// <summary>One line for the staff panel's Status card: state, and the printer's paper / ink / other alerts.</summary>
        public string Summary
        {
            get
            {
                string st, al;
                lock (gate) { st = state; al = alerts; }
                st = StateText(st);
                string problem = Describe(al);
                return problem != null ? $"{st} · {problem}" : $"{st} · {StaffText.T("paper and ink OK")}";
            }
        }

        /// <summary>The printer state in the staff panel's language ("Canon_SELPHY: idle" → "Canon_SELPHY: 대기").</summary>
        static string StateText(string st)
        {
            if (string.IsNullOrEmpty(st)) return st;
            int colon = st.LastIndexOf(": ", StringComparison.Ordinal);
            return colon < 0 ? StaffText.T(st) : st.Substring(0, colon + 2) + StaffText.T(st.Substring(colon + 2));
        }

        /// <summary>An alert that stops printing (an error, not a "running low" warning).</summary>
        static bool Blocking(string alerts)
        {
            if (string.IsNullOrEmpty(alerts)) return false;
            foreach (var raw in alerts.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string r = raw.Trim().ToLowerInvariant();
                if (r.EndsWith("-error") || r.StartsWith("media-empty") || r.StartsWith("media-needed") || r.StartsWith("media-jam")
                    || r.StartsWith("marker-supply-empty") || r.StartsWith("cover-open") || r.StartsWith("offline")) return true;
            }
            return false;
        }

        /// <summary>
        /// The printer's alerts (CUPS printer-state-reasons, e.g. "media-empty-error, marker-supply-low-warning") in
        /// plain words, or null when there are none. Errors stop printing; warnings mean it's nearly out.
        /// </summary>
        static string Describe(string alerts)
        {
            if (string.IsNullOrEmpty(alerts) || alerts == "none") return null;
            var parts = new List<string>();
            foreach (var raw in alerts.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string r = raw.Trim().ToLowerInvariant();
                string d = r.StartsWith("media-empty") || r.StartsWith("media-needed") ? "PAPER OUT: refill the tray"
                    : r.StartsWith("media-low") ? "paper low"
                    : r.StartsWith("media-jam") ? "PAPER JAM"
                    : r.StartsWith("input-tray-missing") ? "PAPER TRAY MISSING"
                    : r.StartsWith("marker-supply-empty") || r.StartsWith("toner-empty") || r.StartsWith("marker-waste-full") ? "INK OUT: change the cassette"
                    : r.StartsWith("marker-supply-low") || r.StartsWith("toner-low") ? "ink low"
                    : r.StartsWith("cover-open") || r.StartsWith("door-open") ? "COVER OPEN"
                    : r.StartsWith("offline") || r.StartsWith("connecting-to-device") ? "printer offline (power or USB-C?)"
                    : r == "none" || r.StartsWith("cups-") || r.StartsWith("other") ? null
                    : r;
                if (d != null) d = StaffText.T(d);
                if (d != null && !parts.Contains(d)) parts.Add(d);
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        /// <summary>The printers this computer knows (macOS: lpstat -e), for the staff Settings.</summary>
        public static string[] ListPrinters()
        {
            if (!IsMac) return new string[0];
            try
            {
                var (_, output, _) = Run("/usr/bin/lpstat", "-e", 5000);
                return output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            }
            catch { return new string[0]; }
        }

        GameObject BoxObject => staffPanel != null ? staffPanel : staffBox != null ? staffBox.gameObject : null;

        string Destination()
        {
            if (!string.IsNullOrEmpty(config.printerName)) return config.printerName;
            lock (gate) return printer;
        }

        void Update()
        {
            // The box lives on the staff panel's Printing page (StaffPanel), which shows and hides it.
            showBox = true;
            string st, al;
            int queued;
            lock (gate) { st = state; al = alerts; queued = pending.Count; }
            st = StateText(st);
            string problem = Describe(al);

            if (staffBox != null)
            {
                if (BoxObject.activeSelf != showBox) BoxObject.SetActive(showBox);
                if (showBox)
                    staffBox.text =
                                    $"{st}\n" +
                                    (problem != null ? $"{StaffText.T("Printer: ")}{problem}\n" : StaffText.T("Paper and ink: no warnings") + "\n") +
                                    StaffText.F("Queued: {0}", queued) + "\n" +
                                    StaffText.F("Printed so far: {0}", PlayerPrefs.GetInt(TotalKey, 0)) +
                                    (LastError != null ? $"\n{LastError}" : "");
            }

            if (warning != null)
            {
                string w = problem != null ? StaffText.T("Printer: ") + problem : null;
                // One screen: a staff line on the guest screen while the staff panel is closed (with two screens
                // StaffScreen turns it off: the panel always shows the printer).
                bool show = w != null && !StaffPanel.Open && !AlwaysShowBox;
                if (warning.gameObject.activeSelf != show) warning.gameObject.SetActive(show);
                if (show) warning.text = w + "  " + StaffText.T("(F1 staff panel)");
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
