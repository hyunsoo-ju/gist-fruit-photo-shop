using System;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// A falling fruit with two phases (Docs/DESIGN.md §7):
    /// 1. Scripted fall: a Rigidbody whose velocity is held at (vx, −fallSpeed) every step, with the
    ///    basket assist nudging vx and the side walls reflecting it.
    /// 2. Knocked: after it hits the basket, the scripted fall stops and physics takes over (bounce + gravity),
    ///    so a late basket sends the fruit flying instead of swallowing it.
    /// It is caught when its centre is inside the basket's catch zone, and missed when it drops below the miss line.
    /// A golden fruit behaves the same (faster, set by the spawner); a bomb isn't pulled by the assist, and when caught it
    /// vanishes (the game blows it up) instead of sinking into the basket.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FallingFruit : MonoBehaviour
    {
        public enum Outcome
        {
            Caught,
            Missed
        }

        public enum Kind
        {
            Fruit,
            Golden,
            Bomb
        }

        [SerializeField] float popTime = 0.15f;
        [SerializeField] float popSink = 0.25f;

        public FruitType Type { get; private set; }
        public Kind FruitKind { get; private set; }
        public float Radius { get; private set; }
        public bool Resolved { get; private set; }
        public bool Knocked { get; private set; }

        public event Action<FallingFruit, Outcome> OnResolved;
        /// <summary>Raised on every new contact with the basket before the fruit is resolved.</summary>
        public event Action<FallingFruit> OnHitBasket;
        /// <summary>A fruit bounced off a side wall: the contact point on the wall, and its sideways speed.</summary>
        public static event Action<Vector3, float> WallBounced;

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
        float pendingBounceScale = 1f;
        float restingSince = -1f;
        float popT;
        Vector3 popStartScale;

        public void Init(FruitType type, Kind kind, GameConfig cfg, BasketController basketController, BasketAssist basketAssist,
            float fall, float startVx, float cap, PhysicsMaterial material)
        {
            Type = type;
            FruitKind = kind;
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
            if (kind == Kind.Bomb) spin *= 0.3f; // slow enough to keep the burning fuse in view
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
                if (assist != null && FruitKind != Kind.Bomb)
                {
                    vx += assist.AccelerationFor(p) * dt;
                    float cap = driftCap + config.AssistExtraSpeedCap;
                    vx = Mathf.Clamp(vx, -cap, cap);
                }
                v = new Vector3(vx, -fallSpeed, 0f);
            }
            else
            {
                // Hard hits bounce less (applied once, after the solver's bounce), and knocked speeds are capped, so the
                // basket never flings fruit across the screen or high into the air.
                v *= pendingBounceScale;
                pendingBounceScale = 1f;
                v.x = Mathf.Clamp(v.x, -config.knockedMaxSideSpeed, config.knockedMaxSideSpeed);
                v.y = Mathf.Min(v.y, config.knockedMaxUpSpeed);
                v.y -= config.knockedGravity * dt;
                if (!Resolved && CheckRestingInMouth(p, v)) return;
                if (!Resolved && Time.time - knockedAt > config.knockedTimeout)
                {
                    // Still on the basket after all this time: it's caught. Anywhere else it's missed and drops away.
                    if (NearMouth(p)) { CatchNow(); return; }
                    Resolve(Outcome.Missed);
                    ownCollider.enabled = false;
                }
            }

            // Side walls reflect sideways speed in both phases, so fruit never leaves the screen.
            float wall = config.wallX - Radius;
            if ((p.x > wall && v.x > 0f) || (p.x < -wall && v.x < 0f))
            {
                WallBounced?.Invoke(new Vector3(Mathf.Sign(p.x) * config.wallX, p.y, p.z), Mathf.Abs(v.x));
                v.x = -v.x;
                vx = v.x;
            }
            body.linearVelocity = v;

            if (!Resolved && p.y < config.missY) Resolve(Outcome.Missed);
            if (p.y < config.despawnY) Destroy(gameObject);
        }

        /// <summary>On top of or in the basket's mouth (where a fruit can come to rest on the rim or on other fruit).</summary>
        bool NearMouth(Vector3 p) =>
            basket != null && Mathf.Abs(p.x - basket.X) <= config.basketMouthWidth * 0.5f + Radius * 0.5f
            && p.y >= config.rimY - config.basketHeight && p.y <= config.rimY + Radius * 2.5f;

        /// <summary>A knocked fruit that has come to rest on the rim or in the mouth counts as caught, so it can't sit
        /// there unresolved and block the next ones.</summary>
        bool CheckRestingInMouth(Vector3 p, Vector3 v)
        {
            if (!NearMouth(p) || v.sqrMagnitude > 0.6f * 0.6f) { restingSince = -1f; return false; }
            if (restingSince < 0f) restingSince = Time.time;
            if (Time.time - restingSince < config.restCatchTime) return false;
            CatchNow();
            return true;
        }

        void CatchNow()
        {
            Resolve(Outcome.Caught);
            if (FruitKind == Kind.Bomb) Destroy(gameObject);
            else BeginPop();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (Resolved || basket == null || !basket.Owns(collision.collider)) return;
            float impact = collision.relativeVelocity.magnitude;
            float hard = Mathf.InverseLerp(config.hardHitRange.x, config.hardHitRange.y, impact);
            pendingBounceScale = Mathf.Min(pendingBounceScale, Mathf.Lerp(1f, config.hardHitBounceScale, hard));
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
                CatchNow();
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
