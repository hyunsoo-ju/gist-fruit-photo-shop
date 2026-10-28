using System;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// One fruit in the background pile. Freezes itself (kinematic) once it has rested long enough,
    /// so a settled pile costs nothing and can't creep or jitter.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PileFruit : MonoBehaviour
    {
        public event Action<PileFruit> Landed;

        public bool Frozen => body != null && body.isKinematic;
        /// <summary>Which fruit this is (for its atlas when the pile switches material).</summary>
        public FruitType Type { get; set; }

        Rigidbody body;
        float freezeSpeed;
        float freezeTime;
        float restingFor;
        bool landed;
        float shrinkT = -1f;
        float shrinkTime;
        Vector3 shrinkFrom;

        public void Init(float settleSpeed, float settleTime)
        {
            body = GetComponent<Rigidbody>();
            freezeSpeed = settleSpeed;
            freezeTime = settleTime;
        }

        /// <summary>Lets a frozen fruit simulate again, e.g. after a fruit below it was removed.</summary>
        public void Wake()
        {
            if (body == null || shrinkT >= 0f) return;
            body.isKinematic = false;
            body.WakeUp();
            restingFor = 0f;
        }

        /// <summary>Shrinks away and destroys itself (the pile is over its cap).</summary>
        public void ShrinkAway(float duration)
        {
            if (shrinkT >= 0f) return;
            shrinkT = 0f;
            shrinkTime = Mathf.Max(0.01f, duration);
            shrinkFrom = transform.localScale;
            body.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic) return;

            bool slow = body.linearVelocity.sqrMagnitude < freezeSpeed * freezeSpeed
                        && body.angularVelocity.sqrMagnitude < 0.25f;
            restingFor = slow ? restingFor + Time.fixedDeltaTime : 0f;
            if (restingFor >= freezeTime)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        void Update()
        {
            if (shrinkT < 0f) return;
            shrinkT += Time.deltaTime;
            float u = Mathf.Clamp01(shrinkT / shrinkTime);
            transform.localScale = shrinkFrom * (1f - u);
            if (u >= 1f) Destroy(gameObject);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (landed) return;
            landed = true;
            Landed?.Invoke(this);
        }
    }
}
