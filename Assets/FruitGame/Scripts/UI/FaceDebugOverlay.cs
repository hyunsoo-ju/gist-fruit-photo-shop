using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Debug view for face tracking (Docs/DESIGN.md §15, F1): an outline box on the tracked face plus a status line
    /// (camera, detection score, rate, the face's position in the 3:4 play area) and a small view of the whole camera
    /// frame's person mask. The box is mapped from the camera frame to the HUD frame (CameraFrame.ToView), so it lines
    /// up with the backdrop.
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
                var r = CameraFrame.ToView(tracker.FaceRect);
                box.anchorMin = r.min;
                box.anchorMax = r.max;
                box.offsetMin = box.offsetMax = Vector2.zero;
            }

            bool showMask = visible && maskPreview != null && segmenter != null && segmenter.HasMask;
            if (maskPreview != null && maskPreview.gameObject.activeSelf != showMask) maskPreview.gameObject.SetActive(showMask);
            if (showMask && maskPreview.texture != segmenter.Mask) maskPreview.texture = segmenter.Mask;
            // The whole camera frame, at its own shape.
            if (showMask) maskPreview.rectTransform.sizeDelta = new Vector2(maskPreview.rectTransform.sizeDelta.y * CameraFrame.Aspect, maskPreview.rectTransform.sizeDelta.y);

            if (status.gameObject.activeSelf != visible) status.gameObject.SetActive(visible);
            if (visible)
            {
                string cam = source != null ? source.Status : "no camera";
                string face = tracker != null ? $"{tracker.Status} · {tracker.Rate:0} det/s" : "no tracker";
                string centre = tracker != null && tracker.HasFace ? $" · play u={tracker.PlayFaceCenter.x:0.00}" : "";
                string seg = segmenter != null ? $"\nSegmentation: {segmenter.Status} · {segmenter.Rate:0}/s" : "";
                status.text = $"{cam}\n{face}{centre}{seg}  (F1 · F4 camera)";
            }
        }
    }
}
