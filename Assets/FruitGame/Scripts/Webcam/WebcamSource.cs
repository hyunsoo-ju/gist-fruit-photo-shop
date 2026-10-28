using System.Collections;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Opens a webcam and keeps a mirrored, centre-cropped 3:4 copy of its frames in Output (Docs/DESIGN.md §9).
    /// Camera choice: GameConfig.preferredCameraName if it matches, otherwise the first camera that isn't the
    /// laptop's built-in one (e.g. an iPhone via Continuity Camera, or a USB webcam), otherwise any camera.
    /// </summary>
    public class WebcamSource : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] Vector2Int requestedSize = new Vector2Int(1280, 720);
        [SerializeField] int requestedFps = 30;
        [SerializeField] Vector2Int outputSize = new Vector2Int(540, 720);
        [SerializeField] bool mirror = true;
        [Tooltip("Seconds without a frame before the camera counts as not delivering (e.g. a closed laptop lid).")]
        [SerializeField] float noFrameTimeout = 3f;

        static readonly string[] BuiltInHints = { "MacBook", "FaceTime", "Built-in", "Integrated" };

        public RenderTexture Output { get; private set; }
        public string DeviceName { get; private set; }
        /// <summary>True once real frames are arriving.</summary>
        public bool HasFrames { get; private set; }
        public bool Failed { get; private set; }
        public string Status { get; private set; } = "Starting";

        WebCamTexture cam;
        float startedAt;

        void OnEnable()
        {
            Output = new RenderTexture(outputSize.x, outputSize.y, 0, RenderTextureFormat.ARGB32) { name = "Webcam 3x4" };
            Output.Create();
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
            if (Output != null)
            {
                Output.Release();
                Destroy(Output);
                Output = null;
            }
            HasFrames = false;
        }

        IEnumerator Open()
        {
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
                Blit();
            }
            else if (!HasFrames && Time.unscaledTime - startedAt > noFrameTimeout)
            {
                Fail($"'{DeviceName}' is not sending frames.");
            }
        }

        /// <summary>Centre-crops the frame to the output aspect, mirrors it, and corrects vertical flip.</summary>
        void Blit()
        {
            float srcAspect = (float)cam.width / cam.height;
            float dstAspect = (float)outputSize.x / outputSize.y;
            float sx = 1f, sy = 1f;
            if (srcAspect > dstAspect) sx = dstAspect / srcAspect;
            else sy = srcAspect / dstAspect;
            float ox = (1f - sx) * 0.5f, oy = (1f - sy) * 0.5f;

            var scale = new Vector2(sx, sy);
            var offset = new Vector2(ox, oy);
            if (mirror) { scale.x = -sx; offset.x = ox + sx; }
            if (cam.videoVerticallyMirrored) { scale.y = -sy; offset.y = oy + sy; }
            Graphics.Blit(cam, Output, scale, offset);
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
