using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Where the camera image sits (Docs/DESIGN.md §13). WebcamSource keeps the whole camera frame at its own aspect; on
    /// screen it's fitted to the height of the 3:4 play canvas and centred: the view's whole height in landscape, the
    /// 3:4 band across the middle in portrait (the dirt and leaves cover the rest; fitted to the whole 9:16 height the
    /// player looked bigger than in landscape). A frame wider than the view is cropped at the sides and a narrower one
    /// (a 3:4 camera in landscape) fills only the middle, fading out at its left and right edges.
    /// Face tracking reports positions in this whole-frame uv; calibration and the basket use them relative to the
    /// 3:4 play area (the middle of the frame the photos show), so the same head movement means the same in either
    /// screen layout.
    /// </summary>
    public static class CameraFrame
    {
        public const float PlayAspect = 3f / 4f;
        /// <summary>Width of each faded edge, in frame uv, when the frame is narrower than the view.</summary>
        const float FadeWidth = 0.1f;

        /// <summary>Width / height of the camera frame (3:4 until a camera delivers frames).</summary>
        public static float Aspect { get; set; } = PlayAspect;

        /// <summary>The share of the view's height the camera frame fills: the 3:4 canvas's height (1 in landscape,
        /// 0.75 in a 9:16 portrait view).</summary>
        public static float HeightFit => Mathf.Min(1f, ScreenLayout.Aspect / PlayAspect);

        /// <summary>Faded edge width in frame uv: 0 when the frame covers the view's width.</summary>
        public static float EdgeFade => Aspect * HeightFit < ScreenLayout.Aspect - 0.01f ? FadeWidth : 0f;

        /// <summary>Frame uv → view (HUD frame) uv: centred, fitted to the canvas's height.</summary>
        public static Vector2 ToView(Vector2 uv)
        {
            float k = HeightFit;
            return new Vector2(0.5f + (uv.x - 0.5f) * Aspect * k / ScreenLayout.Aspect, 0.5f + (uv.y - 0.5f) * k);
        }

        public static Rect ToView(Rect r)
        {
            var min = ToView(r.min);
            var max = ToView(r.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Frame uv → play-area uv: the middle 3:4 of the frame spans 0…1 (outside it, beyond).</summary>
        public static Vector2 ToPlay(Vector2 uv)
        {
            float fx = Mathf.Min(1f, PlayAspect / Aspect), fy = Mathf.Min(1f, Aspect / PlayAspect);
            return new Vector2(0.5f + (uv.x - 0.5f) / fx, 0.5f + (uv.y - 0.5f) / fy);
        }

        public static Rect ToPlay(Rect r)
        {
            var min = ToPlay(r.min);
            var max = ToPlay(r.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
