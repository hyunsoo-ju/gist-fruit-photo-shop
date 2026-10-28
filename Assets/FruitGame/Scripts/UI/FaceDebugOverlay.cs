using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Debug view for face tracking (Docs/DESIGN.md §11, F1): an outline box on the tracked face plus a status line
    /// (camera, detection score, rate). The box is anchored in the HUD frame using the webcam UV, which
    /// lines up with the backdrop because both fill the same 3:4 view.
    /// </summary>
    public class FaceDebugOverlay : MonoBehaviour
    {
        [SerializeField] FaceTracker tracker;
        [SerializeField] WebcamSource source;
        [SerializeField] RectTransform box;
        [SerializeField] TMP_Text status;
        [SerializeField] PersonSegmenter segmenter;
        [Tooltip("Small live view of the person mask (M4).")]
        [SerializeField] RawImage maskPreview;
        [SerializeField] bool visible = true;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) visible = !visible;

            bool showBox = visible && tracker != null && tracker.HasFace;
            if (box.gameObject.activeSelf != showBox) box.gameObject.SetActive(showBox);
            if (showBox)
            {
                var r = tracker.FaceRect;
                box.anchorMin = r.min;
                box.anchorMax = r.max;
                box.offsetMin = box.offsetMax = Vector2.zero;
            }

            bool showMask = visible && maskPreview != null && segmenter != null && segmenter.HasMask;
            if (maskPreview != null && maskPreview.gameObject.activeSelf != showMask) maskPreview.gameObject.SetActive(showMask);
            if (showMask && maskPreview.texture != segmenter.Mask) maskPreview.texture = segmenter.Mask;

            if (status.gameObject.activeSelf != visible) status.gameObject.SetActive(visible);
            if (visible)
            {
                string cam = source != null ? source.Status : "no camera";
                string face = tracker != null ? $"{tracker.Status} · {tracker.Rate:0} det/s" : "no tracker";
                string centre = tracker != null && tracker.HasFace ? $" · centre u={tracker.FaceCenter.x:0.00}" : "";
                string seg = segmenter != null ? $"\nSegmentation: {segmenter.Status} · {segmenter.Rate:0}/s" : "";
                status.text = $"{cam}\n{face}{centre}{seg}  (F1)";
            }
        }
    }
}
