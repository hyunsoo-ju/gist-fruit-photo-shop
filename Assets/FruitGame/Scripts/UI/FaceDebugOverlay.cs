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
        [Tooltip("Face mesh, for its rate and the head's rotation.")]
        [SerializeField] FaceMesh mesh;
        [Tooltip("Small live view of the person mask (M4).")]
        [SerializeField] RawImage maskPreview;
        [SerializeField] bool visible = true;

        /// <summary>Set by StaffScreen with two screens: the views live on the staff screen and are always shown.</summary>
        public bool StaffScreen { get; set; }

        /// <summary>The face box and mask on the guest screen (one screen; the staff panel's D).</summary>
        public bool Visible { get => visible; set => visible = value; }

        void Update()
        {
            if (StaffScreen) visible = true;

            bool showBox = visible && tracker != null && tracker.HasFace;
            if (box.gameObject.activeSelf != showBox) box.gameObject.SetActive(showBox);
            if (showBox)
            {
                var r = CameraFrame.ToView(tracker.FaceRect);
                box.anchorMin = r.min;
                box.anchorMax = r.max;
                box.offsetMin = box.offsetMax = Vector2.zero;
            }

            // The mask and the status line sit in the staff panel's Status card, shown whenever the panel is.
            bool showMask = maskPreview != null && segmenter != null && segmenter.HasMask;
            if (maskPreview != null && maskPreview.gameObject.activeSelf != showMask) maskPreview.gameObject.SetActive(showMask);
            if (showMask && maskPreview.texture != segmenter.Mask) maskPreview.texture = segmenter.Mask;
            // The whole camera frame, at its own shape.
            if (showMask) maskPreview.rectTransform.sizeDelta = new Vector2(maskPreview.rectTransform.sizeDelta.y * CameraFrame.Aspect, maskPreview.rectTransform.sizeDelta.y);

            if (!status.gameObject.activeSelf) status.gameObject.SetActive(true);
            {
                string cam = source != null ? source.Status : "no camera";
                // With two screens the rates (mattes, face finding, mesh) are on the staff screen's performance meter.
                bool rates = !StaffScreen;
                string face = tracker != null ? tracker.Status + (rates ? $" · {tracker.Rate:0} det/s" : "") : "no tracker";
                string centre = tracker != null && tracker.HasFace ? $" · play u={tracker.PlayFaceCenter.x:0.000}" : "";
                if (mesh != null && mesh.HasMesh) centre += $" · mesh u={mesh.PlayCentre.x:0.000}";
                string seg = segmenter != null ? $"\nSegmentation: {segmenter.Status}" + (rates ? $" · {segmenter.Rate:0}/s" : "") : "";
                string head = mesh != null && mesh.HasMesh
                    ? "\n" + (rates ? $"Face mesh {mesh.Rate:0}/s · head" : "Head") + $" yaw {mesh.Yaw:+0;-0} pitch {mesh.Pitch:+0;-0} roll {mesh.Roll:+0;-0}" : "";
                status.text = $"{cam}\n{face}{centre}{seg}{head}";
            }
        }
    }
}
