using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Draws the voxel buttons (Docs/DESIGN.md §15, M6 1.3). Each button's blocks are rendered into its own small image
    /// that sits in the button's place in the UI, so panels, fades and transitions treat it like any other image (3D
    /// meshes on the HUD camera would draw over or under panels). One offscreen perspective camera, far from the game,
    /// renders a button only when it changes (hover, press, its label, its size or place). The screen reads like the
    /// inside of a box seen from its middle: a button in the middle faces straight out, and the further out one is, the
    /// more its outer edge swings toward the viewer and grows, its inner edge shrinking away.
    /// </summary>
    public class VoxelButtons : MonoBehaviour, IEditPreview
    {
        [Tooltip("Lit vertex-colour material for the blocks (the title's).")]
        [SerializeField] Material material;
        [Tooltip("The HUD frame, whose middle the buttons are seen from.")]
        [SerializeField] RectTransform frame;
        [Tooltip("How far out a button must be to turn 45°, as a multiple of the frame's short side: smaller = stronger turn.")]
        [SerializeField] float turnDistance = 1.5f;
        [Tooltip("The most any button turns, sideways or up and down (degrees).")]
        [SerializeField] float maxTurn = 16f;
        [Tooltip("The camera's distance from each button, as a multiple of the button's longer side: smaller = stronger taper.")]
        [SerializeField] float closeness = 5f;
        [Tooltip("Room around each button's image for its growing outer edge, as a fraction of its size.")]
        [SerializeField] float margin = 0.16f;
        [SerializeField] bool enableVoxelButtons = true;

        public static VoxelButtons Instance { get; private set; }
        public static bool On => Instance != null && Instance.enableVoxelButtons;
        public float Padding => margin;
        public float Closeness => closeness;
        public RectTransform Frame => frame;
        public Material BlockMaterial => blockMaterial;

        static readonly Vector3 Stage = new Vector3(0f, -100000f, 0f);
        Camera cam;
        Material blockMaterial;
        int layer;
        readonly HashSet<KeycapButton> dirty = new HashSet<KeycapButton>();

        void Awake()
        {
            Instance = this;
            layer = LayerMask.NameToLayer("HUD3D");
            if (layer < 0) layer = 0;
            blockMaterial = new Material(material);
            if (blockMaterial.HasProperty("_SweepStrength")) blockMaterial.SetFloat("_SweepStrength", 0f);
            if (blockMaterial.HasProperty("_StageLit")) blockMaterial.SetFloat("_StageLit", 1f); // SceneLighting decides when

            cam = new GameObject("Voxel Button Camera", typeof(Camera)).GetComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.transform.position = Stage + new Vector3(0f, 0f, -1000f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << layer;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 2000f;
            cam.allowMSAA = false;
            cam.enabled = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (blockMaterial != null) Destroy(blockMaterial);
        }

        public int Layer => layer;
        public Vector3 StagePosition => Stage;

        public void MarkDirty(KeycapButton button) => dirty.Add(button);

        /// <summary>Buttons only redraw when they change, so a value edited in the Inspector during play redraws them all.</summary>
        void OnValidate()
        {
            if (!Application.isPlaying) return;
            foreach (var b in FindObjectsByType<KeycapButton>()) dirty.Add(b);
        }

        void LateUpdate()
        {
            if (dirty.Count == 0) return;
            foreach (var b in dirty)
                if (b != null && b.isActiveAndEnabled) b.RenderVoxels(cam);
            dirty.Clear();
        }

        /// <summary>The turn for a button centred at `screenCentre` (canvas units, relative to the frame's middle).</summary>
        public Quaternion ViewTurn(Vector2 fromMiddle)
        {
            float d = Mathf.Max(1f, Mathf.Min(frame.rect.width, frame.rect.height) * turnDistance);
            // The outer edge swings toward the viewer (a button on the right turns its face toward the left).
            float yaw = Mathf.Clamp(Mathf.Atan2(fromMiddle.x, d) * Mathf.Rad2Deg, -maxTurn, maxTurn);
            float pitch = Mathf.Clamp(-Mathf.Atan2(fromMiddle.y, d) * Mathf.Rad2Deg, -maxTurn, maxTurn);
            return Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>Where a rect's centre is relative to the frame's middle, in the frame's canvas units.</summary>
        public Vector2 FromMiddle(RectTransform r)
        {
            var corners = new Vector3[4];
            r.GetWorldCorners(corners);
            var centre = (corners[0] + corners[2]) * 0.5f;
            return (Vector2)frame.InverseTransformPoint(centre) - frame.rect.center;
        }

        // ---- Edit-mode preview (IEditPreview) -------------------------------------------------------------------------

        public int PreviewOrder => 0;
        public void BuildPreview() => Awake();
        /// <summary>Draws the buttons waiting to be drawn now (the preview has no LateUpdate in edit mode).</summary>
        public void RenderPending() => LateUpdate();

        public void EndPreview()
        {
            if (Instance == this) Instance = null;
            dirty.Clear();
            cam = null;
            if (blockMaterial != null) DestroyImmediate(blockMaterial);
            blockMaterial = null;
        }
    }
}
