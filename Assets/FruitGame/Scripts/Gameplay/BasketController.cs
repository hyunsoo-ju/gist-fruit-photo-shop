using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Moves the basket along x (y fixed) toward the active input source's target. The basket is a
    /// kinematic Rigidbody with a solid tub (two tapered side walls + a floor) that fruit bounce off,
    /// and a trigger inside it that catches fruit. The root sits on the rim centre (GameConfig.rimY).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BasketController : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] BasketInputSource input;
        [Tooltip("Child that holds the visuals; squash and wobble are applied here so colliders stay put.")]
        [SerializeField] Transform visual;
        [Tooltip("Depth of the colliders along z. Fruit live on z = 0, so this only needs to be generous.")]
        [SerializeField] float colliderDepth = 0.6f;

        [Header("Feedback")]
        [SerializeField] float squashTime = 0.12f;
        [SerializeField] float squashAmount = 0.18f;
        [SerializeField] float wobbleTime = 0.3f;
        [SerializeField] float wobbleDegrees = 6f;

        public bool InputEnabled { get; set; } = true;
        public float X => body != null ? body.position.x : transform.position.x;
        public float MouthHalfWidth => config.basketMouthWidth * 0.5f;
        public Collider CatchZone { get; private set; }

        Rigidbody body;
        Collider[] solids;
        float targetX;
        float squashT = -1f;
        float wobbleT = -1f;
        Vector3 visualBaseScale;

        public void SetInput(BasketInputSource source) => input = source;

        public void ResetPosition()
        {
            targetX = 0f;
            var p = new Vector3(0f, config.rimY, transform.position.z);
            transform.position = p;
            if (body != null) body.position = p;
        }

        public void PlayCatch() => squashT = 0f;
        public void PlayBump() => wobbleT = 0f;

        public bool Owns(Collider c)
        {
            if (solids == null) return false;
            foreach (var s in solids)
                if (s == c) return true;
            return false;
        }

        /// <summary>True when a world point is inside the tub (below the rim, between the tapered walls).</summary>
        public bool ContainsPoint(Vector3 world)
        {
            float depth = config.rimY - world.y;
            if (depth < 0.02f || depth > config.basketHeight) return false;
            float top = MouthHalfWidth - config.basketWallThickness;
            float bottom = MouthHalfWidth * config.basketTaper - config.basketWallThickness;
            float half = Mathf.Lerp(top, bottom, depth / config.basketHeight);
            return Mathf.Abs(world.x - X) <= half;
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (visual != null) visualBaseScale = visual.localScale;
            BuildColliders();
            ResetPosition();
        }

        void BuildColliders()
        {
            float top = MouthHalfWidth;
            float bottom = top * config.basketTaper;
            float h = config.basketHeight;
            float t = config.basketWallThickness;
            float angle = Mathf.Atan2(top - bottom, h) * Mathf.Rad2Deg;
            float slant = Mathf.Sqrt(h * h + (top - bottom) * (top - bottom));

            var mat = new PhysicsMaterial("Basket")
            {
                bounciness = 0.3f,
                dynamicFriction = 0.4f,
                staticFriction = 0.4f,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };

            // Each wall's outer face lines up with the visual tub; the box is pushed inward by half its thickness.
            BoxCollider Wall(string name, float side)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                float midX = side * ((top + bottom) * 0.5f - t * 0.5f);
                go.transform.localPosition = new Vector3(midX, -h * 0.5f, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, side * -angle);
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(t, slant, colliderDepth);
                box.sharedMaterial = mat;
                return box;
            }

            var left = Wall("Wall L", -1f);
            var right = Wall("Wall R", 1f);

            var floorGo = new GameObject("Floor");
            floorGo.transform.SetParent(transform, false);
            floorGo.transform.localPosition = new Vector3(0f, -h + t * 0.5f, 0f);
            var floor = floorGo.AddComponent<BoxCollider>();
            floor.size = new Vector3(bottom * 2f, t, colliderDepth);
            floor.sharedMaterial = mat;

            solids = new Collider[] { left, right, floor };

            var zoneGo = new GameObject("Catch Zone");
            zoneGo.transform.SetParent(transform, false);
            zoneGo.transform.localPosition = new Vector3(0f, -h * 0.5f, 0f);
            var zone = zoneGo.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.size = new Vector3(top * 2f, h, colliderDepth);
            CatchZone = zone;
        }

        void Update()
        {
            if (InputEnabled && input != null && input.HasTarget)
                targetX = Mathf.Clamp(input.TargetX, -config.basketXLimit, config.basketXLimit);

            AnimateFeedback();
        }

        void FixedUpdate()
        {
            var p = body.position;
            p.x = Mathf.MoveTowards(p.x, targetX, config.basketMaxSpeed * Time.fixedDeltaTime);
            p.y = config.rimY;
            body.MovePosition(p);
        }

        void AnimateFeedback()
        {
            if (visual == null) return;

            float sy = 1f, sxz = 1f;
            if (squashT >= 0f)
            {
                squashT += Time.deltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(squashT / squashTime) * Mathf.PI);
                sy = 1f - squashAmount * k;
                sxz = 1f + squashAmount * 0.5f * k;
                if (squashT >= squashTime) squashT = -1f;
            }
            visual.localScale = Vector3.Scale(visualBaseScale, new Vector3(sxz, sy, sxz));

            float angle = 0f;
            if (wobbleT >= 0f)
            {
                wobbleT += Time.deltaTime;
                float u = Mathf.Clamp01(wobbleT / wobbleTime);
                angle = Mathf.Sin(u * Mathf.PI * 4f) * wobbleDegrees * (1f - u);
                if (wobbleT >= wobbleTime) wobbleT = -1f;
            }
            visual.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
