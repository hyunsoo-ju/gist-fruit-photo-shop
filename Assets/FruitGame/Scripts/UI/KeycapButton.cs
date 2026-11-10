using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// A keycap button (Docs/DESIGN.md §15). With voxel buttons on (VoxelButtons, M6 1.3) it's drawn as blocks: a flat
    /// cream border that stays put and a cap in the button's colour inside it, with a ridge two blocks in from its edge
    /// and its label raised in cream blocks on a dark edge. Hover lifts the cap one block; pressing sinks it flush with
    /// the border. The button keeps its own image (invisible) for clicks, and its label text (hidden) as the source of
    /// the label, so localisation keeps working. Without voxel buttons it's the flat pixel keycap: the image is the
    /// dark lip and the face (holding the label) sits on it; hover lifts the face a pixel, pressing sinks it.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class KeycapButton : MonoBehaviour, IEditPreview, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] RectTransform face;
        [Tooltip("Canvas units per UI pixel (the sprites are drawn at 1 pixel = this many units; one voxel too).")]
        [SerializeField] float pixel = 4f;
        [Tooltip("Lip height in UI pixels.")]
        [SerializeField] int lipPixels = 3;

        Button button;
        bool hover, pressed;

        // Voxel view.
        bool voxel;
        Image lipImage;
        Image faceImage;
        TMP_Text label;
        RawImage view;
        RenderTexture target;
        Transform pivot, borderT, capT;
        Mesh borderMesh, capMesh;
        Color32 colour;
        string builtLabel;
        bool builtLive, builtContrast;
        Vector2Int builtBlocks;
        Vector2 lastPlace;
        Vector2Int lastPixels;
        int lastState = -1;

        void Awake()
        {
            button = GetComponent<Button>();
            Apply();
        }

        void Start()
        {
            if (!VoxelButtons.On) return;
            voxel = true;
            lipImage = GetComponent<Image>();
            faceImage = face != null ? face.GetComponent<Image>() : null;
            label = GetComponentInChildren<TMP_Text>(true);
            colour = ColourOf(lipImage != null && lipImage.sprite != null ? lipImage.sprite.name : "");

            // Keep the button's own image for clicks, but draw nothing of the flat keycap.
            if (lipImage != null) lipImage.color = new Color(1f, 1f, 1f, 0f);
            if (faceImage != null) faceImage.enabled = false;
            if (label != null) label.enabled = false;

            view = new GameObject("Voxel View", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            view.transform.SetParent(transform, false);
            view.raycastTarget = false;
            float pad = VoxelButtons.Instance.Padding;
            var r = view.rectTransform;
            r.anchorMin = new Vector2(-pad, -pad);
            r.anchorMax = new Vector2(1f + pad, 1f + pad);
            r.offsetMin = r.offsetMax = Vector2.zero;

            var vb = VoxelButtons.Instance;
            pivot = new GameObject(name + " (voxel)").transform;
            pivot.gameObject.layer = vb.Layer;
            pivot.position = vb.StagePosition;
            borderT = Part("Border", out _);
            capT = Part("Cap", out _);
            pivot.gameObject.SetActive(false);
            vb.MarkDirty(this);
        }

        Transform Part(string partName, out MeshRenderer renderer)
        {
            var go = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = pivot.gameObject.layer;
            go.transform.SetParent(pivot, false);
            renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = VoxelButtons.Instance.BlockMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        static Color32 ColourOf(string sprite)
        {
            if (sprite.Contains("Slate")) return new Color32(0x5A, 0x6B, 0x7D, 0xFF);
            if (sprite.Contains("Orange")) return new Color32(0xB8, 0x50, 0x0A, 0xFF); // 4.8:1 with the cream label (M6 6.9)
            return new Color32(0xC2, 0x18, 0x5B, 0xFF);
        }

        void OnDestroy()
        {
            if (pivot != null) Destroy(pivot.gameObject);
            if (borderMesh != null) Destroy(borderMesh);
            if (capMesh != null) Destroy(capMesh);
            if (target != null) { target.Release(); Destroy(target); }
        }

        void OnDisable()
        {
            hover = pressed = false;
            Apply();
        }

        void OnEnable()
        {
            if (voxel && VoxelButtons.Instance != null) VoxelButtons.Instance.MarkDirty(this);
        }

        public void OnPointerEnter(PointerEventData e) { hover = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { hover = false; pressed = false; Apply(); }
        public void OnPointerDown(PointerEventData e) { pressed = true; Apply(); if (Live && Narrator.Instance != null) Narrator.Instance.Click(); }
        public void OnPointerUp(PointerEventData e) { pressed = false; Apply(); }

        bool Live => button == null || button.interactable;

        /// <summary>
        /// Canvas units per block: one glyph pixel of the label (its font size / 16), so the label keeps its size and
        /// stays crisp; for the usual 64-unit labels that's exactly one UI art pixel.
        /// </summary>
        float Block => label != null && label.fontSize > 0f ? label.fontSize / 16f : pixel;

        /// <summary>0 rest, 1 hover, 2 pressed, 3 disabled.</summary>
        int State => !Live ? 3 : pressed ? 2 : hover ? 1 : 0;

        void Apply()
        {
            if (voxel)
            {
                if (VoxelButtons.Instance != null) VoxelButtons.Instance.MarkDirty(this);
                return;
            }
            if (face == null) return;
            float lip = lipPixels * pixel;
            float shift = !Live ? 0f : pressed ? -lip : hover ? pixel : 0f;
            face.anchorMin = Vector2.zero;
            face.anchorMax = Vector2.one;
            face.offsetMin = new Vector2(0f, lip + shift);
            face.offsetMax = new Vector2(0f, shift);
        }

        void LateUpdate()
        {
            if (!voxel || VoxelButtons.Instance == null) return;
            // Redraw when anything that shows has changed: state, label (language), size or place on screen.
            var vb = VoxelButtons.Instance;
            var place = vb.FromMiddle((RectTransform)transform);
            var pixels = TargetPixels();
            string text = label != null ? label.text : "";
            if (State != lastState || text != builtLabel || HighContrast.On != builtContrast || pixels != lastPixels || (place - lastPlace).sqrMagnitude > 4f)
                vb.MarkDirty(this);
        }

        Vector2Int TargetPixels()
        {
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            var size = ((RectTransform)transform).rect.size * (1f + 2f * VoxelButtons.Instance.Padding) * canvas.scaleFactor;
            return new Vector2Int(Mathf.Max(8, Mathf.RoundToInt(size.x)), Mathf.Max(8, Mathf.RoundToInt(size.y)));
        }

        /// <summary>Called by VoxelButtons: rebuilds what changed and renders the blocks into this button's image.</summary>
        public void RenderVoxels(Camera cam)
        {
            var vb = VoxelButtons.Instance;
            var rect = ((RectTransform)transform).rect;
            float block = Block;
            var blocks = new Vector2Int(Mathf.Max(12, Mathf.RoundToInt(rect.width / block)), Mathf.Max(10, Mathf.RoundToInt(rect.height / block)));
            string text = label != null ? label.text : "";

            if (blocks != builtBlocks || text != builtLabel || Live != builtLive || HighContrast.On != builtContrast)
            {
                builtContrast = HighContrast.On;
                builtLive = Live;
                builtBlocks = blocks;
                builtLabel = text;
                if (borderMesh != null) Destroy(borderMesh);
                if (capMesh != null) Destroy(capMesh);
                int capInner = blocks.x - VoxelButtonKit.BorderWidth * 2 - VoxelButtonKit.RidgeInset * 2 - 4;
                int capTall = blocks.y - VoxelButtonKit.BorderWidth * 2 - VoxelButtonKit.RidgeInset * 2 - 2;
                var bits = VoxelButtonKit.Label(label != null ? label.font : null, text, capInner, capTall >= 34 ? 2 : 1);
                borderMesh = VoxelButtonKit.Border(blocks.x, blocks.y);
                var capColour = HighContrast.On ? new Color32(0, 0, 0, 0xFF) : Live ? colour : VoxelButtonKit.Shade(colour, 0.6f);
                capMesh = VoxelButtonKit.Cap(blocks.x, blocks.y, capColour, bits);
                borderT.GetComponent<MeshFilter>().sharedMesh = borderMesh;
                capT.GetComponent<MeshFilter>().sharedMesh = capMesh;
            }

            // Hover lifts the cap a block, pressing sinks it flush with the border (toward the camera is −z).
            int state = State;
            lastState = state;
            float lift = state == 1 ? -VoxelButtonKit.HoverLift : state == 2 ? VoxelButtonKit.CapHeight - VoxelButtonKit.BorderHeight : 0f;
            capT.localPosition = new Vector3(0f, 0f, lift);

            var place = vb.FromMiddle((RectTransform)transform);
            lastPlace = place;
            pivot.rotation = vb.ViewTurn(place);

            var pixels = TargetPixels();
            lastPixels = pixels;
            if (target == null || target.width != pixels.x || target.height != pixels.y)
            {
                if (target != null) { target.Release(); Destroy(target); }
                target = new RenderTexture(pixels.x, pixels.y, 24, RenderTextureFormat.ARGB32) { name = name + " voxels", filterMode = FilterMode.Point };
                target.Create();
                view.texture = target;
            }

            // Frame the padded button exactly at its own plane: the image covers the button plus the margin on every side.
            float pad = vb.Padding;
            float halfHeight = rect.height / block * (1f + 2f * pad) * 0.5f;
            float distance = Mathf.Max(blocks.x, blocks.y) * vb.Closeness;
            cam.transform.position = vb.StagePosition - new Vector3(0f, 0f, distance);
            cam.fieldOfView = 2f * Mathf.Atan(halfHeight / distance) * Mathf.Rad2Deg;
            cam.aspect = (float)pixels.x / pixels.y;
            cam.targetTexture = target;
            pivot.gameObject.SetActive(true);
            cam.Render();
            pivot.gameObject.SetActive(false);
            cam.targetTexture = null;
        }

        // ---- Edit-mode preview (IEditPreview) -------------------------------------------------------------------------

        Color previewLip;
        bool previewFace, previewLabel, previewing;

        public int PreviewOrder => 1;

        public void BuildPreview()
        {
            Awake();
            var lip = GetComponent<Image>();
            var faceImg = face != null ? face.GetComponent<Image>() : null;
            var text = GetComponentInChildren<TMP_Text>(true);
            previewLip = lip != null ? lip.color : Color.white;
            previewFace = faceImg != null && faceImg.enabled;
            previewLabel = text != null && text.enabled;
            previewing = true;
            Start();
            if (voxel && VoxelButtons.Instance != null) VoxelButtons.Instance.RenderPending();
        }

        public void EndPreview()
        {
            if (!previewing) return;
            previewing = false;
            // Put the flat keycap back as it's saved: Start hid it under the voxel view.
            if (lipImage != null) lipImage.color = previewLip;
            if (faceImage != null) faceImage.enabled = previewFace;
            if (label != null) label.enabled = previewLabel;
            if (borderMesh != null) DestroyImmediate(borderMesh);
            if (capMesh != null) DestroyImmediate(capMesh);
            if (target != null) { target.Release(); DestroyImmediate(target); }
            voxel = false;
            view = null;
            pivot = borderT = capT = null;
            borderMesh = capMesh = null;
            target = null;
            builtLabel = null;
            lastState = -1;
        }
    }
}
