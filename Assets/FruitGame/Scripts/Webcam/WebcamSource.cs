using System.Collections;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Opens a webcam and keeps a mirrored copy of its whole frames in Output, at the camera's own shape with the long
    /// side at most 960 pixels (Docs/DESIGN.md §13). CameraFrame describes where it sits on screen and in the play area.
    /// Camera choice: the camera staff picked in the camera menu (F4, remembered across restarts), else
    /// GameConfig.preferredCameraName if it matches, otherwise the first camera that isn't the laptop's built-in one
    /// (e.g. an iPhone via Continuity Camera, or a USB webcam), otherwise any camera.
    /// </summary>
    // Before everything that reads the frame (PersonSegmenter is -400), so the matte is made from the same picture that
    // is shown in this frame, not the one before it.
    [DefaultExecutionOrder(-500)]
    public class WebcamSource : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [Tooltip("Asked of the camera; it picks its nearest mode. 1080p60 suits the DSLR on the Elgato 4K X (Docs/DESIGN.md §13).")]
        [SerializeField] Vector2Int requestedSize = new Vector2Int(1920, 1080);
        [SerializeField] int requestedFps = 60;
        [SerializeField] bool mirror = true;
        [Tooltip("Seconds without a frame before the camera counts as not delivering (e.g. a closed laptop lid).")]
        [SerializeField] float noFrameTimeout = 3f;

        static readonly string[] BuiltInHints = { "MacBook", "FaceTime", "Built-in", "Integrated" };

        public RenderTexture Output { get; private set; }
        public string DeviceName { get; private set; }
        /// <summary>True once real frames are arriving.</summary>
        public bool HasFrames { get; private set; }
        public bool Failed { get; private set; }
        /// <summary>Counts the camera frames copied into Output: a change means a new picture.</summary>
        public int FrameCount { get; private set; }
        public string Status { get; private set; } = "Starting";
        /// <summary>
        /// Turned off from the staff panel (Camera page): the camera is closed and Output shows a plain, slowly changing
        /// colour instead (the one camera-off test sessions use), so the backdrop has something calm behind the scene.
        /// No frames count as new, so tracking and background removal rest, and the basket follows the mouse or keys.
        /// </summary>
        public bool Off { get; private set; }
        /// <summary>Closed for standby (Standby): like Off, but staff's own Camera setting is left as it was.</summary>
        public bool Suspended { get; private set; }
        /// <summary>No camera frames are coming on purpose (turned off, or standby).</summary>
        public bool Idle => Off || Suspended;

        /// <summary>Closes the camera while the booth is in standby, and opens it again after (unless staff turned it off).</summary>
        public void Suspend(bool suspend)
        {
            if (suspend == Suspended) return;
            Suspended = suspend;
            if (Off) return;
            CloseCamera();
            if (suspend) Status = "Paused (standby)";
            else { Status = "Starting"; StartCoroutine(Open()); }
        }

        WebCamTexture cam;
        float startedAt;

        const int OutputLongSide = 960;
        const string ChoiceKey = "FruitGame.camera";

        /// <summary>The camera picked in the camera menu, or empty for the automatic choice.</summary>
        public static string Choice => PlayerPrefs.GetString(ChoiceKey, "");

        /// <summary>
        /// Switches to a camera by name (empty or null = the automatic choice) and remembers it. The current Output stays
        /// until the new camera's first frame, so nothing that reads it sees a missing texture.
        /// </summary>
        public void Use(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) PlayerPrefs.DeleteKey(ChoiceKey);
            else PlayerPrefs.SetString(ChoiceKey, deviceName);
            PlayerPrefs.Save();
            Off = false;
            CloseCamera();
            Status = "Switching camera";
            StartCoroutine(Open());
        }

        /// <summary>Turns the camera off (a plain background instead) or back on.</summary>
        public void SetOff(bool off)
        {
            if (off == Off) return;
            Off = off;
            CloseCamera();
            if (!off && Suspended) { Status = "Paused (standby)"; return; }
            if (off)
            {
                Status = "Camera off (staff panel)";
                Debug.Log("[FruitGame] Webcam: turned off from the staff panel");
            }
            else
            {
                Status = "Starting";
                StartCoroutine(Open());
            }
        }

        void CloseCamera()
        {
            StopAllCoroutines();
            if (cam != null)
            {
                cam.Stop();
                Destroy(cam);
                cam = null;
            }
            HasFrames = false;
            Failed = false;
        }

        void OnEnable()
        {
            // `-nocamera` on the command line: start with the camera off (for trying a build without opening the camera).
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nocamera") >= 0 && !Off) { Off = true; Status = "Camera off (-nocamera)"; }
            StartCoroutine(Open());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            if (cam != null)
            {
                cam.Stop();
                Destroy(cam);
                cam = null;
            }
            ReleaseOutput();
            HasFrames = false;
        }

        /// <summary>(Re)creates Output at the camera frame's shape, once the frame size is known.</summary>
        void MakeOutput(int width, int height)
        {
            float k = (float)OutputLongSide / Mathf.Max(width, height);
            var size = new Vector2Int(Mathf.RoundToInt(width * k / 2f) * 2, Mathf.RoundToInt(height * k / 2f) * 2);
            if (Output != null && Output.width == size.x && Output.height == size.y) return;
            ReleaseOutput();
            Output = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32) { name = $"Webcam {size.x}x{size.y}" };
            Output.Create();
            CameraFrame.Aspect = (float)size.x / size.y;
        }

        void ReleaseOutput()
        {
            if (Output == null) return;
            Output.Release();
            Destroy(Output);
            Output = null;
        }

        IEnumerator Open()
        {
            if (Off || Suspended) yield break;
            if (TestMode.TestFrames)
            {
                DeviceName = "Test frames";
                startedAt = Time.unscaledTime;
                yield break;
            }
            if (TestMode.CameraOff)
            {
                Fail("Camera off (test mode)");
                yield break;
            }
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Fail("Camera permission denied. Allow it in System Settings > Privacy & Security > Camera.");
                yield break;
            }

            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Fail("No camera found.");
                yield break;
            }

            DeviceName = PickDevice(devices);
            cam = new WebCamTexture(DeviceName, requestedSize.x, requestedSize.y, requestedFps);
            cam.Play();
            startedAt = Time.unscaledTime;
            Status = $"Opening {DeviceName}";
            var names = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++) names[i] = devices[i].name;
            Debug.Log($"[FruitGame] Webcam: opening '{DeviceName}'. Cameras found: {string.Join(", ", names)} " +
                      "(set GameConfig → Preferred Camera Name to part of a name to pick one)");
        }

        string PickDevice(WebCamDevice[] devices)
        {
            string chosen = Choice;
            if (!string.IsNullOrEmpty(chosen))
                foreach (var d in devices)
                    if (d.name == chosen) return d.name;

            string preferred = config != null ? config.preferredCameraName : null;
            if (!string.IsNullOrEmpty(preferred))
                foreach (var d in devices)
                    if (d.name.IndexOf(preferred, System.StringComparison.OrdinalIgnoreCase) >= 0) return d.name;

            foreach (var d in devices)
            {
                bool builtIn = false;
                foreach (var hint in BuiltInHints)
                    if (d.name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0) builtIn = true;
                if (!builtIn) return d.name;
            }
            return devices[0].name;
        }

        void Update()
        {
            if (Off) { Wash(30f); return; }
            if (Suspended) return;
            if (TestMode.TestFrames) { TestFrame(); return; }
            if (cam == null || Failed) return;

            // WebCamTexture reports a 16×16 placeholder until real frames arrive.
            if (cam.didUpdateThisFrame && cam.width > 16)
            {
                if (!HasFrames)
                {
                    HasFrames = true;
                    Status = $"Live: {DeviceName} {cam.width}×{cam.height}";
                    Debug.Log($"[FruitGame] Webcam: {Status}, rotation {cam.videoRotationAngle}°, vflip {cam.videoVerticallyMirrored}");
                }
                MakeOutput(cam.width, cam.height);
                Blit();
                FrameCount++;
            }
            else if (!HasFrames && Time.unscaledTime - startedAt > noFrameTimeout)
            {
                Fail($"'{DeviceName}' is not sending frames.");
            }
        }

        /// <summary>Copies the whole frame, mirrored, with vertical flip corrected.</summary>
        void Blit()
        {
            var scale = Vector2.one;
            var offset = Vector2.zero;
            if (config != null ? config.mirrorCamera : mirror) { scale.x = -1f; offset.x = 1f; }
            if (cam.videoVerticallyMirrored) { scale.y = -1f; offset.y = 1f; }
            Graphics.Blit(cam, Output, scale, offset);
        }

        float nextTestFrame;

        /// <summary>Test mode: a 1920 × 1080 camera at 60 fps showing a slowly moving pattern (no camera image).</summary>
        void TestFrame()
        {
            if (!Wash(60f)) return;
            if (!HasFrames) { HasFrames = true; Status = "Live: test frames 1920×1080"; }
            FrameCount++;
        }

        /// <summary>
        /// Fills Output with one plain colour that drifts slowly between soft rose, lilac and sand, at most `rate`
        /// times a second (the camera's 16:9 shape). False when it isn't time for a new one yet.
        /// </summary>
        bool Wash(float rate)
        {
            if (Time.unscaledTime < nextTestFrame && Output != null) return false;
            nextTestFrame = Mathf.Max(nextTestFrame + 1f / rate, Time.unscaledTime - 0.05f);
            MakeOutput(1920, 1080);
            float t = Time.unscaledTime * 0.2f;
            var prev = RenderTexture.active;
            RenderTexture.active = Output;
            GL.Clear(false, true, new Color(0.5f + 0.3f * Mathf.Sin(t), 0.45f, 0.5f + 0.3f * Mathf.Cos(t)));
            RenderTexture.active = prev;
            return true;
        }

        void Fail(string reason)
        {
            Failed = true;
            Status = reason;
            Debug.LogWarning($"[FruitGame] Webcam: {reason}");
            if (cam != null) cam.Stop();
        }
    }
}
