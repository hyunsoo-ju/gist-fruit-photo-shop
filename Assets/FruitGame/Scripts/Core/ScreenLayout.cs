using System;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    public enum OrientationSetting
    {
        Auto,
        Portrait,
        Landscape
    }

    /// <summary>
    /// The screen's two layouts (Docs/DESIGN.md §3): portrait 9:16 or landscape 16:9, picked from the window's shape
    /// (or forced by GameConfig.orientation) and letterboxed in whatever window the game runs in; a separate background
    /// camera clears the bars. The camera's field of view is fitted around the fixed play column: its width in portrait,
    /// its height in landscape, so gameplay is the same size in both and only the scenery around it changes.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class ScreenLayout : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [Tooltip("UI containers stretched to the game viewport, so the HUD sits inside the frame.")]
        [SerializeField] RectTransform[] uiFrames;
        [Tooltip("Canvas scalers whose reference resolution follows the layout and the booth UI scale (GameConfig.uiPixelScale).")]
        [SerializeField] CanvasScaler[] canvasScalers;

        public static bool IsLandscape { get; private set; }
        /// <summary>The layout's reference resolution: 1080 × 1920 or 1920 × 1080.</summary>
        public static Vector2Int Reference => IsLandscape ? new Vector2Int(1920, 1080) : new Vector2Int(1080, 1920);
        public static float Aspect => (float)Reference.x / Reference.y;
        /// <summary>Raised when the layout switches between portrait and landscape.</summary>
        public static event Action Changed;

        Camera cam;
        int lastW, lastH;
        bool? lastLandscape;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            lastW = lastH = -1;
            lastLandscape = null;
            Apply();
        }

        void Update() => Apply();

        void Apply()
        {
            if (cam == null || config == null) return;
            bool landscape = config.orientation == OrientationSetting.Landscape
                || (config.orientation == OrientationSetting.Auto && Screen.width >= Screen.height);
            if (Screen.width == lastW && Screen.height == lastH && landscape == lastLandscape) return;
            lastW = Screen.width;
            lastH = Screen.height;
            bool switched = landscape != lastLandscape;
            lastLandscape = landscape;
            IsLandscape = landscape;

            float aspect = Aspect;
            float window = (float)Screen.width / Mathf.Max(1, Screen.height);
            Rect rect = window > aspect
                ? new Rect((1f - aspect / window) * 0.5f, 0f, aspect / window, 1f)
                : new Rect(0f, (1f - window / aspect) * 0.5f, 1f, window / aspect);
            cam.rect = rect;

            // Fit the play column at z = 0: its width in portrait (the view grows taller), its height in landscape
            // (the view grows wider).
            float distance = Mathf.Abs(cam.transform.position.z);
            float halfHeight = landscape ? config.playHeight * 0.5f : config.playColumnWidth * 0.5f / aspect;
            cam.fieldOfView = 2f * Mathf.Atan(halfHeight / distance) * Mathf.Rad2Deg;

            if (uiFrames != null)
                foreach (var frame in uiFrames)
                {
                    if (frame == null) continue;
                    frame.anchorMin = rect.min;
                    frame.anchorMax = rect.max;
                    frame.offsetMin = frame.offsetMax = Vector2.zero;
                }
            if (canvasScalers != null)
                foreach (var scaler in canvasScalers)
                {
                    if (scaler == null) continue;
                    // UI art is drawn at 4 canvas units per art pixel; a larger canvas shows it at uiPixelScale screen
                    // pixels per art pixel on the reference screen (3: the booth's smaller UI, still whole pixels).
                    scaler.referenceResolution = (Vector2)Reference * (4f / Mathf.Max(1, config.uiPixelScale));
                    scaler.matchWidthOrHeight = landscape ? 1f : 0f;
                }

            if (switched) Changed?.Invoke();
        }
    }
}
