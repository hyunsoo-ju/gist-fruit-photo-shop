using System;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// A falling fruit with two phases (Docs/DESIGN.md §5):
    /// 1. Scripted fall: a Rigidbody whose velocity is held at (vx, −fallSpeed) every step, with the
    ///    basket assist nudging vx and the side walls reflecting it.
    /// 2. Knocked: after it hits the basket, the scripted fall stops and physics takes over (bounce + gravity),
    ///    so a late basket sends the fruit flying instead of swallowing it.
    /// It is caught when its centre is inside the basket's catch zone, and missed when it drops below the miss line.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FallingFruit : MonoBehaviour
    {
        public enum Outcome
        {
            Caught,
            Missed
        }

        [SerializeField] float popTime = 0.15f;
        [SerializeField] float popSink = 0.25f;

        public FruitType Type { get; private set; }
        public float Radius { get; private set; }
        public bool Resolved { get; private set; }
        public bool Knocked { get; private set; }

        public event Action<FallingFruit, Outcome> OnResolved;
        /// <summary>Raised on every new contact with the basket before the fruit is resolved.</summary>
        public event Action<FallingFruit> OnHitBasket;

        GameConfig config;
        BasketController basket;
        BasketAssist assist;
        Rigidbody body;
        Collider ownCollider;
        float fallSpeed;
        float vx;
        float driftCap;
        float knockedAt;
        bool caught;
        float popT;
        Vector3 popStartScale;

        public void Init(FruitType type, GameConfig cfg, BasketController basketController, BasketAssist basketAssist,
            float fall, float startVx, float cap, PhysicsMaterial material)
        {
            Type = type;
            config = cfg;
            basket = basketController;
            assist = basketAssist;
            Radius = cfg.fruitSize * 0.5f * (type != null ? type.sizeScale : 1f);
            fallSpeed = fall;
            vx = startVx;
            driftCap = cap;

            var sphere = gameObject.AddComponent<SphereCollider>();
            sphere.radius = Radius;
            sphere.sharedMaterial = material;
            ownCollider = sphere;

            body = GetComponent<Rigidbody>();
            body.mass = 0.2f;
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezePositionZ;
            body.linearVelocity = new Vector3(vx, -fallSpeed, 0f);
            float spin = UnityEngine.Random.Range(cfg.spinDegPerSec.x, cfg.spinDegPerSec.y) * Mathf.Deg2Rad;
            body.angularVelocity = UnityEngine.Random.onUnitSphere * spin;
        }

        void FixedUpdate()
        {
            if (config == null || caught) return;

            float dt = Time.fixedDeltaTime;
            Vector3 p = body.position;
            Vector3 v = body.linearVelocity;

            if (!Knocked)
            {
                if (assist != null)
                {
                    vx += assist.AccelerationFor(p) * dt;
                    float cap = driftCap + config.assistExtraSpeedCap;
                    vx = Mathf.Clamp(vx, -cap, cap);
                }
                v = new Vector3(vx, -fallSpeed, 0f);
            }
            else
            {
                v.y -= config.knockedGravity * dt;
                if (!Resolved && Time.time - knockedAt > config.knockedTimeout) Resolve(Outcome.Missed);
            }

            // Side walls reflect sideways speed in both phases, so fruit never leaves the screen.
            float wall = config.wallX - Radius;
            if ((p.x > wall && v.x > 0f) || (p.x < -wall && v.x < 0f))
            {
                v.x = -v.x;
                vx = v.x;
            }
            body.linearVelocity = v;

            if (!Resolved && p.y < config.missY) Resolve(Outcome.Missed);
            if (p.y < config.despawnY) Destroy(gameObject);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (Resolved || basket == null || !basket.Owns(collision.collider)) return;
            if (!Knocked)
            {
                Knocked = true;
                knockedAt = Time.time;
            }
            OnHitBasket?.Invoke(this);
        }

        void OnTriggerStay(Collider other)
        {
            if (Resolved || basket == null || other != basket.CatchZone) return;
            // Count it only once the centre is inside the tub, not when the fruit merely grazes the zone's top.
            if (basket.ContainsPoint(body.position))
            {
                Resolve(Outcome.Caught);
                BeginPop();
            }
        }

        void Resolve(Outcome outcome)
        {
            Resolved = true;
            OnResolved?.Invoke(this, outcome);
        }

        void BeginPop()
        {
            caught = true;
            popT = 0f;
            popStartScale = transform.localScale;
            body.isKinematic = true;
            ownCollider.enabled = false;
        }

        void Update()
        {
            if (!caught) return;

            // Sink into the basket while shrinking, following the basket sideways.
            popT += Time.deltaTime;
            float u = Mathf.Clamp01(popT / popTime);
            var p = transform.position;
            p.x = Mathf.Lerp(p.x, basket != null ? basket.X : p.x, u);
            p.y = Mathf.Lerp(p.y, config.rimY - popSink, u);
            transform.position = p;
            transform.localScale = popStartScale * (1f - u);
            if (u >= 1f) Destroy(gameObject);
        }
    }
}
