using System;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// One pose per screen layout (Docs/DESIGN.md §15): a UI element's anchors, pivot, position and size, or a 3D
    /// object's position and scale, for portrait and for landscape. Applied whenever the layout switches. Use the
    /// context menu to store the current pose as the portrait or landscape one while arranging a screen.
    /// </summary>
    [ExecuteAlways]
    public class AspectLayout : MonoBehaviour
    {
        [Serializable]
        public struct Pose
        {
            public bool set;
            public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta;
            public Vector3 localPosition, localScale;
        }

        public Pose portrait;
        public Pose landscape;
        bool? applied;

        void OnEnable()
        {
            ScreenLayout.Changed += Apply;
            applied = null;
            Apply();
        }

        void OnDisable() => ScreenLayout.Changed -= Apply;

        void Update()
        {
            // Covers objects enabled before ScreenLayout settled, and edit mode.
            if (applied != ScreenLayout.IsLandscape) Apply();
        }

        public void Apply()
        {
            applied = ScreenLayout.IsLandscape;
            var pose = ScreenLayout.IsLandscape ? landscape : portrait;
            if (!pose.set) return;
            if (transform is RectTransform rt)
            {
                rt.anchorMin = pose.anchorMin;
                rt.anchorMax = pose.anchorMax;
                rt.pivot = pose.pivot;
                rt.sizeDelta = pose.sizeDelta;
                rt.anchoredPosition = pose.anchoredPosition;
            }
            else transform.localPosition = pose.localPosition;
            transform.localScale = pose.localScale;
        }

        public Pose Capture()
        {
            var pose = new Pose { set = true, localPosition = transform.localPosition, localScale = transform.localScale };
            if (transform is RectTransform rt)
            {
                pose.anchorMin = rt.anchorMin;
                pose.anchorMax = rt.anchorMax;
                pose.pivot = rt.pivot;
                pose.anchoredPosition = rt.anchoredPosition;
                pose.sizeDelta = rt.sizeDelta;
            }
            return pose;
        }

        [ContextMenu("Store current pose as portrait")]
        void StorePortrait() => portrait = Capture();

        [ContextMenu("Store current pose as landscape")]
        void StoreLandscape() => landscape = Capture();
    }
}
