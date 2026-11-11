using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Scales a quad to the game camera's view height at its depth (Docs/DESIGN.md §4, §13): in the current layout's
    /// aspect, or with Camera Frame on, in the camera frame's aspect at the 3:4 canvas's height, centred (CameraFrame). Used for the player
    /// cut-out and the backdrop, which show the whole camera frame.
    /// </summary>
    [ExecuteAlways]
    public class ViewFill : MonoBehaviour
    {
        [SerializeField] Camera view;
        [Tooltip("Take the camera frame's shape (the player cut-out and the backdrop) instead of the view's.")]
        [SerializeField] bool cameraFrame = true;

        void LateUpdate()
        {
            if (view == null) return;
            float distance = Vector3.Dot(transform.position - view.transform.position, view.transform.forward);
            float h = 2f * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
            // The camera frame fills the 3:4 canvas's height (the whole view in landscape, its middle band in portrait).
            if (cameraFrame) h *= CameraFrame.HeightFit;
            var scale = new Vector3(h * (cameraFrame ? CameraFrame.Aspect : ScreenLayout.Aspect), h, transform.localScale.z);
            if (transform.localScale != scale) transform.localScale = scale;
        }
    }
}
