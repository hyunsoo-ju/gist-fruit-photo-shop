using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Composes the souvenir print (Docs/DESIGN.md §10) in the pixel style: a cream card with the pixel title on top,
    /// a pink pixel frame holding the four-cut grid (2 × 2: the action shots from the round, then the final shot, which
    /// gets a light grey outline), and along the bottom the lab logo · score + best · school logo.
    /// Built on a dedicated off-screen canvas rendered by its own camera into a 1080 × 1440 texture.
    /// Logos fall back to labelled placeholder boxes until real textures are assigned; without a title image the
    /// caption text is shown instead.
    /// </summary>
    public class PhotoFrame : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] Camera frameCamera;
        [SerializeField] GameObject canvasRoot;
        [Tooltip("The four cut slots, in reading order; the last one holds the final shot.")]
        [SerializeField] RawImage[] cuts;
        [Tooltip("Light grey outline behind the final shot's slot.")]
        [SerializeField] GameObject finalHighlight;
        [SerializeField] RawImage titleImage;
        [SerializeField] TMP_Text caption;
        [SerializeField] TMP_Text score;
        [SerializeField] TMP_Text best;

        [Header("Logos (leave empty for placeholders)")]
        [SerializeField] Texture2D labLogo;
        [SerializeField] Texture2D schoolLogo;
        [SerializeField] RawImage labLogoImage;
        [SerializeField] RawImage schoolLogoImage;
        [SerializeField] GameObject labPlaceholder;
        [SerializeField] GameObject schoolPlaceholder;
        [SerializeField] Vector2Int size = new Vector2Int(1080, 1440);
        [Tooltip("Share of the vertical crop taken from the bottom (0 = keep the bottom edge, 0.5 = centred).")]
        [SerializeField, Range(0f, 0.5f)] float cropFromBottom = 0.35f;

        RenderTexture target;

        void Awake()
        {
            // The camera-space canvas sizes itself from its camera's target, so keep a fixed 1080×1440 target
            // (the layout then matches the photo exactly, whatever the window size). The camera only renders on demand.
            target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { name = "Photo Frame" };
            target.Create();
            frameCamera.targetTexture = target;
            frameCamera.enabled = false;
            canvasRoot.SetActive(false);
        }

        void OnDestroy()
        {
            if (frameCamera != null) frameCamera.targetTexture = null;
            if (target != null) { target.Release(); Destroy(target); }
        }

        /// <param name="shots">The cuts in order; the last one is the final shot (it goes in the last slot).</param>
        public Texture2D Compose(IReadOnlyList<Texture2D> shots, int roundScore, int bestScore)
        {
            canvasRoot.SetActive(true);
            Canvas.ForceUpdateCanvases();

            // Earlier shots fill the slots in order; the final shot always takes the last, highlighted slot.
            for (int i = 0; i < cuts.Length; i++) { cuts[i].texture = null; cuts[i].gameObject.SetActive(false); }
            int n = shots.Count;
            for (int i = 0; i < n && i < cuts.Length; i++)
            {
                bool last = i == n - 1;
                var slot = cuts[last ? cuts.Length - 1 : i];
                Fill(slot, shots[i]);
            }
            if (finalHighlight != null) finalHighlight.SetActive(n > 0);

            bool hasTitle = titleImage != null && titleImage.texture != null;
            if (titleImage != null) titleImage.gameObject.SetActive(hasTitle);
            caption.gameObject.SetActive(!hasTitle);
            caption.text = config.photoCaption.Replace("{date}", DateTime.Now.ToString("yyyy.MM.dd"));
            score.text = roundScore.ToString();
            best.text = Strings.Get("print.best", Mathf.Max(bestScore, roundScore));
            SetLogo(labLogo, labLogoImage, labPlaceholder);
            SetLogo(schoolLogo, schoolLogoImage, schoolPlaceholder);

            Canvas.ForceUpdateCanvases();
            frameCamera.Render();
            canvasRoot.SetActive(false);

            var prev = RenderTexture.active;
            RenderTexture.active = target;
            var result = new Texture2D(size.x, size.y, TextureFormat.RGB24, false) { name = "Framed Photo" };
            result.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            result.Apply();
            RenderTexture.active = prev;
            foreach (var c in cuts) c.texture = null;
            return result;
        }

        /// <summary>
        /// Crops a 3:4 shot to a slot's aspect. Vertically, trims more from the top (sky and canopy) than the bottom so
        /// the player and the pile stay in.
        /// </summary>
        void Fill(RawImage slot, Texture2D shot)
        {
            slot.gameObject.SetActive(true);
            slot.texture = shot;
            var r = slot.rectTransform.rect;
            float slotAspect = r.width / Mathf.Max(1f, r.height);
            float shotAspect = (float)shot.width / shot.height;
            float keepH = shotAspect / slotAspect;
            slot.uvRect = shotAspect < slotAspect
                ? new Rect(0f, (1f - keepH) * cropFromBottom, 1f, keepH)
                : new Rect((1f - slotAspect / shotAspect) * 0.5f, 0f, slotAspect / shotAspect, 1f);
        }

        static void SetLogo(Texture2D logo, RawImage image, GameObject placeholder)
        {
            bool has = logo != null;
            image.gameObject.SetActive(has);
            placeholder.SetActive(!has);
            if (!has) return;
            image.texture = logo;
            // Fit inside the slot without stretching.
            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter != null) fitter.aspectRatio = (float)logo.width / logo.height;
        }
    }
}
