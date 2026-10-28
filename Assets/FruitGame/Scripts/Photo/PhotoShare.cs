using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FruitGame
{
    /// <summary>
    /// Getting the photo to the player (M7d, Docs/DESIGN.md §10):
    /// • Print: sends the saved PNG to the system's default printer (macOS `lp`, Windows shell "print" verb).
    /// • Phone: a tiny HTTP server on this computer serves each shared photo at an unguessable link
    ///   (http://LAN-IP:port/p/ID) that phones on the same Wi-Fi open (via QR code or by typing it).
    ///   Links expire after `linkLifetimeMinutes`; nothing leaves the local network.
    /// </summary>
    public class PhotoShare : MonoBehaviour
    {
        [SerializeField] int port = 8765;
        [SerializeField] float linkLifetimeMinutes = 15f;
        [Tooltip("Printer name for macOS `lp -d` (empty = system default printer).")]
        [SerializeField] string printerName = "";

        class Entry { public byte[] Png; public DateTime Expires; }

        readonly ConcurrentDictionary<string, Entry> photos = new ConcurrentDictionary<string, Entry>();
        HttpListener listener;
        Thread serverThread;
        volatile bool running;

        public string LastError { get; private set; }

        /// <summary>Prints a saved photo. Returns false (with LastError) if printing couldn't start.</summary>
        public bool Print(string path)
        {
            try
            {
                ProcessStartInfo psi;
                if (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor)
                {
                    string dest = string.IsNullOrEmpty(printerName) ? "" : $"-d \"{printerName}\" ";
                    psi = new ProcessStartInfo("/usr/bin/lp", $"{dest}-o fit-to-page \"{path}\"")
                    {
                        UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p.WaitForExit(5000);
                    if (p.HasExited && p.ExitCode != 0)
                    {
                        LastError = p.StandardError.ReadToEnd().Trim();
                        Debug.LogWarning($"[FruitGame] Print failed: {LastError}");
                        return false;
                    }
                }
                else
                {
                    psi = new ProcessStartInfo(path) { Verb = "print", UseShellExecute = true, CreateNoWindow = true };
                    Process.Start(psi);
                }
                LastError = null;
                Debug.Log($"[FruitGame] Sent to printer: {path}");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning($"[FruitGame] Print failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Makes the photo downloadable on the local network and returns its link (null if unavailable).</summary>
        public string Publish(byte[] png)
        {
            if (!EnsureServer()) return null;
            string ip = LocalIPv4();
            if (ip == null) { LastError = Strings.Get("photo.noNetwork"); return null; }

            PurgeExpired();
            string id = Guid.NewGuid().ToString("N").Substring(0, 12);
            photos[id] = new Entry { Png = png, Expires = DateTime.UtcNow.AddMinutes(linkLifetimeMinutes) };
            return $"http://{ip}:{port}/p/{id}";
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
        /// The phone page in the game's pixel style (Docs/DESIGN.md §10): cream background, the pixel title, Neo둥근모,
        /// the photo in a dark pixel border, and a pink keycap "Save photo" button that sinks when tapped.
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
                   ".photo{max-width:100%;border:4px solid #1e2233;box-shadow:8px 8px 0 #e3d3bc}" +
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

        void OnDestroy()
        {
            running = false;
            try { listener?.Stop(); listener?.Close(); } catch { }
        }
    }
}
