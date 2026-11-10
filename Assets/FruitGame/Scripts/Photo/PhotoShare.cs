using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FruitGame
{
    /// <summary>
    /// Getting the photo to the player's phone (M7d, Docs/DESIGN.md §14; printing is Printer's job): a tiny HTTP server
    /// on this computer serves each shared photo at an unguessable link that expires after `linkLifetimeMinutes`.
    /// By default (`GameConfig.shareOverInternet`) the link goes through a Cloudflare quick tunnel: `cloudflared` runs
    /// beside the game and gives a public https address that forwards to this server, so phones open it on mobile data
    /// and venue Wi-Fi that keeps devices apart doesn't matter. Until the tunnel is up and answering (or without
    /// cloudflared or internet), and with the setting off, the link is http://LAN-IP:port/p/ID for phones on the same Wi-Fi.
    /// </summary>
    public class PhotoShare : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] int port = 8765;
        [SerializeField] float linkLifetimeMinutes = 15f;

        class Entry { public byte[] Png; public DateTime Expires; }

        readonly ConcurrentDictionary<string, Entry> photos = new ConcurrentDictionary<string, Entry>();
        HttpListener listener;
        Thread serverThread;
        volatile bool running;

        public string LastError { get; private set; }
        /// <summary>Whether the last link Publish returned goes through the tunnel (phones need no Wi-Fi) or the local network.</summary>
        public bool LastLinkPublic { get; private set; }

        /// <summary>Makes the photo downloadable and returns its link (null if unavailable): the tunnel's when it's up, else the local one.</summary>
        public string Publish(byte[] png)
        {
            if (!EnsureServer()) return null;
            string baseUrl = publicUrl;
            if (baseUrl == null)
            {
                if (TunnelWanted) Debug.LogWarning($"[FruitGame] Photo link: tunnel not up ({TunnelStatus}), using the local network");
                string ip = LocalIPv4();
                if (ip == null) { LastError = Strings.Get("photo.noNetwork"); return null; }
                baseUrl = $"http://{ip}:{port}";
            }

            PurgeExpired();
            string id = Guid.NewGuid().ToString("N").Substring(0, 12);
            photos[id] = new Entry { Png = png, Expires = DateTime.UtcNow.AddMinutes(linkLifetimeMinutes) };
            LastLinkPublic = baseUrl != null && baseUrl == publicUrl;
            return $"{baseUrl}/p/{id}";
        }

        // ---- Cloudflare quick tunnel ----------------------------------------------------------------------------------

        const string PidPref = "FruitGame.tunnelPid";
        static readonly Regex TunnelAddress = new Regex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.Compiled);

        Process tunnel;
        bool tunnelRunning;            // main thread: we started a tunnel and want it up
        float restartAt;
        volatile string candidateUrl;  // printed by cloudflared, not checked yet
        volatile string publicUrl;     // checked from outside: the link base phones get
        volatile string tunnelNote = "off";

        bool TunnelWanted => config == null || config.shareOverInternet;

        /// <summary>For the staff panel: the tunnel's state and address.</summary>
        public string TunnelStatus => !TunnelWanted ? "off (local Wi-Fi link)" : publicUrl != null ? publicUrl.Replace("https://", "") : tunnelNote;
        public bool TunnelUp => publicUrl != null;

        void Start()
        {
            KillLeftoverTunnel();
            EnsureServer();
        }

        void Update()
        {
            bool want = TunnelWanted;
            if (want && !tunnelRunning && Time.unscaledTime >= restartAt) StartTunnel();
            else if (!want && tunnelRunning) StopTunnel("off");
            else if (tunnelRunning && tunnel != null && tunnel.HasExited)
            {
                Debug.LogWarning("[FruitGame] Photo tunnel stopped; restarting in 15 s");
                StopTunnel("stopped, restarting…");
                restartAt = Time.unscaledTime + 15f;
            }
        }

        void StartTunnel()
        {
            tunnelRunning = true;
            if (!EnsureServer()) { StopTunnel("photo server down"); restartAt = Time.unscaledTime + 30f; return; }
            string exe = FindCloudflared();
            candidateUrl = publicUrl = null;
            tunnelNote = "starting…";
            try
            {
                var info = new ProcessStartInfo(exe, $"tunnel --no-autoupdate --url http://127.0.0.1:{port}")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
                };
                var p = new Process { StartInfo = info, EnableRaisingEvents = true };
                p.ErrorDataReceived += (_, e) => OnTunnelLine(p, e.Data);
                p.OutputDataReceived += (_, e) => OnTunnelLine(p, e.Data);
                p.Start();
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                tunnel = p;
                PlayerPrefs.SetInt(PidPref, p.Id);
                PlayerPrefs.Save();
                Debug.Log($"[FruitGame] Photo tunnel starting ({exe})");
            }
            catch (Exception e)
            {
                // No cloudflared: say so once and look again in a minute (it may be installed meanwhile).
                Debug.LogWarning($"[FruitGame] Photo tunnel: couldn't run cloudflared ({e.Message}); photo links use the local network. Install it: brew install cloudflared");
                StopTunnel("cloudflared not found");
                restartAt = Time.unscaledTime + 60f;
            }
        }

        /// <summary>cloudflared's log (any thread): its address, then once a connection registers, check it from outside.</summary>
        void OnTunnelLine(Process p, string line)
        {
            if (line == null || p != tunnel) return;
            if (candidateUrl == null)
            {
                var m = TunnelAddress.Match(line);
                if (m.Success) { candidateUrl = m.Value; tunnelNote = "connecting…"; }
            }
            if (line.Contains("Registered tunnel connection") && candidateUrl != null && publicUrl == null && tunnelNote != "checking…")
            {
                tunnelNote = "checking…";
                string url = candidateUrl;
                new Thread(() => Verify(p, url)) { IsBackground = true, Name = "FruitGame Tunnel Check" }.Start();
            }
        }

        /// <summary>Fetches /ok through the public address until it answers, so a QR code never points at a dead link.</summary>
        void Verify(Process p, string url)
        {
            for (int i = 0; i < 40 && p == tunnel && !p.HasExited; i++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url + "/ok");
                    req.Timeout = req.ReadWriteTimeout = 6000;
                    using (var res = (HttpWebResponse)req.GetResponse())
                    using (var r = new StreamReader(res.GetResponseStream()))
                        if (res.StatusCode == HttpStatusCode.OK && r.ReadToEnd().Trim() == "ok")
                        {
                            publicUrl = url;
                            tunnelNote = "up";
                            Debug.Log($"[FruitGame] Photo tunnel up: {url}");
                            return;
                        }
                }
                catch { }
                Thread.Sleep(3000);
            }
            if (p == tunnel)
            {
                tunnelNote = "no answer (no internet?)";
                Debug.LogWarning($"[FruitGame] Photo tunnel {url} didn't answer; photo links use the local network");
                try { p.Kill(); } catch { } // Update restarts it
            }
        }

        void StopTunnel(string note)
        {
            tunnelRunning = false;
            candidateUrl = publicUrl = null;
            tunnelNote = note;
            var p = tunnel;
            tunnel = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
            PlayerPrefs.DeleteKey(PidPref);
        }

        /// <summary>A tunnel left running by a crash of the last session would forward to nothing useful: stop it.</summary>
        static void KillLeftoverTunnel()
        {
            int pid = PlayerPrefs.GetInt(PidPref, 0);
            if (pid <= 0) return;
            PlayerPrefs.DeleteKey(PidPref);
            try
            {
                var old = Process.GetProcessById(pid);
                if (old.ProcessName.IndexOf("cloudflared", StringComparison.OrdinalIgnoreCase) >= 0) old.Kill();
            }
            catch { }
        }

        string FindCloudflared()
        {
            if (config != null && !string.IsNullOrEmpty(config.cloudflaredPath)) return config.cloudflaredPath;
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            // An app opened from Finder doesn't get the shell's PATH, so look where Homebrew and the installers put it.
            var candidates = new[]
            {
                "/opt/homebrew/bin/cloudflared", "/usr/local/bin/cloudflared",
                string.IsNullOrEmpty(pf86) ? null : System.IO.Path.Combine(pf86, "cloudflared", "cloudflared.exe"),
                string.IsNullOrEmpty(pf) ? null : System.IO.Path.Combine(pf, "cloudflared", "cloudflared.exe"),
            };
            return candidates.FirstOrDefault(c => c != null && File.Exists(c)) ?? "cloudflared";
        }

        // Page assets (pixel font, title) from StreamingAssets/PhonePage, read once on the main thread.
        byte[] pageFont, pageTitle;

        bool EnsureServer()
        {
            if (pageFont == null)
            {
                string dir = System.IO.Path.Combine(Application.streamingAssetsPath, "PhonePage");
                try
                {
                    pageFont = System.IO.File.ReadAllBytes(System.IO.Path.Combine(dir, "NeoDunggeunmoPro-Regular.ttf"));
                    pageTitle = System.IO.File.ReadAllBytes(System.IO.Path.Combine(dir, "title.png"));
                }
                catch (Exception e) { Debug.LogWarning($"[FruitGame] Phone page assets missing: {e.Message}"); }
            }
            if (running) return true;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://*:{port}/");
                listener.Start();
                running = true;
                serverThread = new Thread(Serve) { IsBackground = true, Name = "FruitGame Photo Server" };
                serverThread.Start();
                Debug.Log($"[FruitGame] Photo server listening on port {port}");
                return true;
            }
            catch (Exception e)
            {
                LastError = Strings.Get("photo.serverFail");
                Debug.LogWarning($"[FruitGame] Photo server: {e.Message}");
                Debug.LogWarning($"[FruitGame] {LastError}");
                return false;
            }
        }

        void Serve()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { break; }
                try { Handle(ctx); }
                catch (Exception e) { Debug.LogWarning($"[FruitGame] Photo server: {e.Message}"); }
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            // Routes: /p/ID → small page with the photo and a download button; /p/ID.png → the image itself;
            // /font.ttf and /title.png → the page's pixel font and title.
            string path = ctx.Request.Url.AbsolutePath.Trim('/');
            var res = ctx.Response;
            if (path == "ok") { Write(res, 200, "text/plain", Encoding.ASCII.GetBytes("ok")); return; }
            if (path == "font.ttf" && pageFont != null) { Write(res, 200, "font/ttf", pageFont); return; }
            if (path == "title.png" && pageTitle != null) { Write(res, 200, "image/png", pageTitle); return; }
            bool png = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            string id = path.StartsWith("p/") ? path.Substring(2).Replace(".png", "") : null;

            if (id == null || !photos.TryGetValue(id, out var entry) || entry.Expires < DateTime.UtcNow)
            {
                Write(res, 404, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page(Strings.Get("page.expired"), null)));
                return;
            }

            if (png)
            {
                res.AddHeader("Content-Disposition", $"attachment; filename=\"FruitGame_{id}.png\"");
                Write(res, 200, "image/png", entry.Png);
            }
            else
            {
                Write(res, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page(Strings.Get("page.title"), id)));
            }
        }

        /// <summary>
        /// The phone page in the game's pixel style (Docs/DESIGN.md §14): cream background, the pixel title, Neo둥근모,
        /// the portrait card in a dark pixel border (short enough that the button stays on screen), and a pink keycap "Save photo" button that sinks when tapped.
        /// </summary>
        static string Page(string title, string id)
        {
            string body = id == null ? "" :
                $"<img class=\"photo\" src=\"/p/{id}.png\" alt=\"photo\"><br><a class=\"btn\" href=\"/p/{id}.png\" download>{Strings.Get("page.save")}</a>" +
                $"<p>{Strings.Get("page.tip")}</p>";
            return $"<!doctype html><html lang=\"{(Strings.Current == Language.Korean ? "ko" : "en")}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                   $"<title>{title}</title><style>" +
                   "@font-face{font-family:Neo;src:url(/font.ttf)}" +
                   "body{font-family:Neo,monospace;background:#fff8ec;color:#1e2233;text-align:center;margin:0;padding:16px;-webkit-font-smoothing:none}" +
                   ".title{width:80%;max-width:420px;image-rendering:pixelated}" +
                   "h2{font-weight:normal;font-size:24px;margin:8px 0 16px}" +
                   ".photo{max-width:100%;max-height:72vh;border:4px solid #1e2233;box-shadow:8px 8px 0 #e3d3bc}" +
                   ".btn{display:inline-block;margin:20px 0 8px;padding:12px 28px;background:#c2185b;color:#fff;border:4px solid #1e2233;" +
                   "box-shadow:inset 4px 4px 0 #e85a8c,0 8px 0 #7a0e3a;text-decoration:none;font-size:24px}" +
                   ".btn:active{transform:translateY(8px);box-shadow:inset 4px 4px 0 #e85a8c}" +
                   "p{font-size:16px;opacity:.7}</style></head>" +
                   $"<body><img class=\"title\" src=\"/title.png\" alt=\"\"><h2>{title}</h2>{body}</body></html>";
        }

        static void Write(HttpListenerResponse res, int status, string type, byte[] data)
        {
            res.StatusCode = status;
            res.ContentType = type;
            res.ContentLength64 = data.Length;
            res.OutputStream.Write(data, 0, data.Length);
            res.OutputStream.Close();
        }

        void PurgeExpired()
        {
            foreach (var kv in photos.Where(kv => kv.Value.Expires < DateTime.UtcNow).ToList())
                photos.TryRemove(kv.Key, out _);
        }

        /// <summary>This computer's address on the local network (Wi-Fi / Ethernet), preferring private ranges.</summary>
        static string LocalIPv4()
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .ToList();
            return candidates.FirstOrDefault(a => a.StartsWith("192.168.") || a.StartsWith("10.") || a.StartsWith("172."))
                   ?? candidates.FirstOrDefault();
        }

        void OnApplicationQuit() => StopTunnel("off");

        void OnDestroy()
        {
            StopTunnel("off");
            running = false;
            try { listener?.Stop(); listener?.Close(); } catch { }
        }
    }
}
