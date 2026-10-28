using UnityEngine;
using UnityEngine.InputSystem;

namespace FruitGame
{
    /// <summary>
    /// Picks what steers the basket: the face whenever the webcam is live, otherwise mouse/keyboard.
    /// F2 forces mouse/keyboard (testing). Also reports how long the face has been gone, for the face-lost pause.
    /// </summary>
    public class BasketInputRouter : BasketInputSource
    {
        [SerializeField] WebcamSource webcam;
        [SerializeField] FaceTracker tracker;
        [SerializeField] FaceToBasketMapper face;
        [SerializeField] FallbackInput fallback;
        [SerializeField] bool forceFallback;

        /// <summary>True when the face is the active control (camera live and not forced to fallback).</summary>
        public bool UsingFace => !forceFallback && webcam != null && webcam.HasFrames && !webcam.Failed;
        /// <summary>Seconds since a face was last seen; 0 while one is visible or when not using the face.</summary>
        public float FaceMissingFor => UsingFace && !tracker.HasFace ? Time.unscaledTime - tracker.LastSeenTime : 0f;

        public override bool HasTarget => UsingFace ? face.HasTarget : fallback.HasTarget;
        public override float TargetX => UsingFace ? face.TargetX : fallback.TargetX;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                forceFallback = !forceFallback;
                Debug.Log($"[FruitGame] Basket control: {(forceFallback ? "mouse/keyboard (F2)" : "face when available")}");
            }
        }
    }
}
