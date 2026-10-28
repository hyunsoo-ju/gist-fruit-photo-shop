using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Snaps a visual's on-screen position to the pixel render's grid (Docs/DESIGN.md §3), so moving objects step a whole
    /// game pixel at a time instead of shimmering between pixels. Only the visual moves; physics stays on the parent.
    /// </summary>
    public class PixelSnap : MonoBehaviour
    {
        public float renderScale = 0.5f;
        Vector3 baseLocal;
        Camera cam;

        void Awake()
        {
            baseLocal = transform.localPosition;
            cam = Camera.main;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            transform.localPosition = baseLocal;
            var p = transform.position;
            // World size of one game pixel at this depth (1440 × renderScale pixels span the view height).
            float distance = Vector3.Dot(p - cam.transform.position, cam.transform.forward);
            float pixel = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / (1440f * renderScale);
            if (pixel <= 0f) return;
            var local = cam.transform.InverseTransformPoint(p);
            local.x = Mathf.Round(local.x / pixel) * pixel;
            local.y = Mathf.Round(local.y / pixel) * pixel;
            transform.position = cam.transform.TransformPoint(local);
        }
    }
}
