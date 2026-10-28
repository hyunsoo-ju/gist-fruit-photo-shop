using UnityEngine;
using UnityEngine.InputSystem;

namespace FruitGame
{
    /// <summary>
    /// Keyboard (←/→, A/D) and mouse x control for testing without a webcam.
    /// The mouse takes over whenever it moves; a key press hands control back to the keyboard.
    /// </summary>
    public class FallbackInput : BasketInputSource
    {
        [SerializeField] GameConfig config;
        [SerializeField] Camera worldCamera;

        float targetX;
        bool mouseActive;

        public override bool HasTarget => true;
        public override float TargetX => targetX;

        public void ResetTarget() => targetX = 0f;

        void Update()
        {
            float limit = config.basketXLimit;
            var keyboard = Keyboard.current;
            float axis = 0f;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed) axis -= 1f;
                if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed) axis += 1f;
            }

            if (axis != 0f)
            {
                mouseActive = false;
                targetX += axis * config.keyboardBasketSpeed * Time.unscaledDeltaTime;
            }

            // The mouse only steers while the pointer is over the 3:4 game frame.
            var mouse = Mouse.current;
            if (mouse != null && worldCamera != null)
            {
                var pos = mouse.position.ReadValue();
                bool inFrame = worldCamera.pixelRect.Contains(pos);
                if (!inFrame) mouseActive = false;
                else if (mouse.delta.ReadValue().sqrMagnitude > 0.5f) mouseActive = true;
                if (mouseActive) targetX = ScreenXToWorldX(pos);
            }

            targetX = Mathf.Clamp(targetX, -limit, limit);
        }

        float ScreenXToWorldX(Vector2 screenPos)
        {
            // Intersect the mouse ray with the play plane (z = 0).
            var ray = worldCamera.ScreenPointToRay(screenPos);
            if (Mathf.Abs(ray.direction.z) < 1e-5f) return targetX;
            float t = -ray.origin.z / ray.direction.z;
            return ray.origin.x + ray.direction.x * t;
        }
    }
}
