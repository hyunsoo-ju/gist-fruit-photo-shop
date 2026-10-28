using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The HUD camera for 3D UI (Docs/DESIGN.md §11): a perspective overlay camera, stacked on the main camera and seeing
    /// only the HUD3D layer. At PixelDistance in front of it, one world unit is one screen pixel of the base camera's
    /// viewport, so VoxelText can place block digits exactly where the HUD text would be, while the perspective shows
    /// their sides and makes moves toward the camera read as depth.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HudCamera : MonoBehaviour
    {
        [Tooltip("The base camera this overlay is stacked on: an overlay draws into the base camera's (letterboxed) viewport.")]
        [SerializeField] Camera baseCamera;
        [SerializeField] float fieldOfView = 30f;
        Camera cam;

        /// <summary>Distance at which one world unit is one screen pixel.</summary>
        public float PixelDistance { get; private set; } = 1000f;

        void Awake()
        {
            cam = GetComponent<Camera>();
            Fit();
        }

        void LateUpdate() => Fit();

        void Fit()
        {
            var r = baseCamera.pixelRect;
            if (r.height <= 0f) return;
            cam.orthographic = false;
            cam.fieldOfView = fieldOfView;
            // An overlay renders into the base viewport but keeps its own aspect unless told otherwise.
            cam.aspect = r.width / r.height;
            PixelDistance = r.height * 0.5f / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            // Generous clip planes: the digits are a few hundred units deep at most, and bump toward the camera.
            cam.nearClipPlane = PixelDistance * 0.2f;
            cam.farClipPlane = PixelDistance * 3f;
        }

        /// <summary>World position at PixelDistance of a screen point, measured in the base camera's viewport.</summary>
        public Vector3 ScreenToWorld(Vector2 screen)
        {
            var r = baseCamera.pixelRect;
            var local = screen - r.center;
            return transform.position + transform.right * local.x + transform.up * local.y + transform.forward * PixelDistance;
        }
    }
}
