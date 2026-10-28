using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Keeps the game camera at a fixed 3:4 portrait viewport, letterboxed in whatever window it runs in.
    /// A separate background camera clears the bars.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class PortraitLetterbox : MonoBehaviour
    {
        [SerializeField] float aspect = 3f / 4f;
        [Tooltip("UI containers stretched to the same 3:4 viewport, so the HUD sits inside the frame.")]
        [SerializeField] RectTransform[] uiFrames;

        Camera cam;
        int lastW, lastH;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            lastW = lastH = -1;
        }

        void Update()
        {
            if (cam == null || (Screen.width == lastW && Screen.height == lastH)) return;
            lastW = Screen.width;
            lastH = Screen.height;

            float window = (float)Screen.width / Mathf.Max(1, Screen.height);
            Rect rect;
            if (window > aspect)
            {
                float w = aspect / window;
                rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else
            {
                float h = window / aspect;
                rect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }
            cam.rect = rect;

            if (uiFrames == null) return;
            foreach (var frame in uiFrames)
            {
                if (frame == null) continue;
                frame.anchorMin = rect.min;
                frame.anchorMax = rect.max;
                frame.offsetMin = frame.offsetMax = Vector2.zero;
            }
        }
    }
}
