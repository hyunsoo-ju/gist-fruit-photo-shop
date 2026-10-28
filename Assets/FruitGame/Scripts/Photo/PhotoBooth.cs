using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Photo booth (M7, Docs/DESIGN.md §10): pose → 3·2·1 → flash → capture → preview with Save / Retake / Done.
    /// The capture renders the game camera off-screen at full 1080×1440 with the basket and any fruit in play hidden
    /// and the round's pile switched to full colour. The print is a four-cut: the three action shots ActionShots took
    /// during the round plus this final shot (Retake only replaces the final one).
    /// </summary>
    public class PhotoBooth : MonoBehaviour
    {
        enum Step { Off, Pose, Countdown, Preview }

        [SerializeField] Camera gameCamera;
        [SerializeField] GameConfig config;
        [SerializeField] FruitPile pile;
        [Tooltip("The player cut-out; the booth turns its beauty filter on while open.")]
        [SerializeField] PersonCutout cutout;
        [Tooltip("Adds the border, caption, logos and scores. Without it the raw scene is saved.")]
        [SerializeField] PhotoFrame frame;
        [SerializeField] GameObject[] hideDuringPhoto;
        [SerializeField] AudioFeedback audioFeedback;
        [SerializeField] Vector2Int photoSize = new Vector2Int(1080, 1440);
        [SerializeField] int countdownSeconds = 3;

        [Header("UI")]
        [SerializeField] GameObject panel;
        [SerializeField] GameObject poseGroup;
        [SerializeField] TMP_Text countdownText;
        [SerializeField] GameObject previewGroup;
        [SerializeField] RawImage previewImage;
        [SerializeField] TMP_Text savedText;
        [SerializeField] Image flash;

        [Header("Sharing (M7d)")]
        [SerializeField] PhotoShare share;
        [SerializeField] GameObject phonePanel;
        [SerializeField] RawImage qrImage;
        [SerializeField] TMP_Text phoneLink;
        [SerializeField] float flashTime = 0.35f;

        /// <summary>Raised when the player leaves the booth.</summary>
        public event Action Closed;
        public bool Running => step != Step.Off;
        public Texture2D LastPhoto { get; private set; }
        public int ActionShotCount => actionShots.Count;

        readonly List<Texture2D> actionShots = new List<Texture2D>();
        Texture2D finalShot;

        Step step = Step.Off;
        float countdownLeft;
        int lastShown;
        float flashT = -1f;
        int score;
        int bestScore;
        bool saved;
        string savedPath;

        /// <summary>Turns a link into a QR code texture. Null until a QR encoder is added; the link is shown as text.</summary>
        public static Func<string, Texture2D> QrEncoder;

        public void Open(int roundScore, int best)
        {
            score = roundScore;
            bestScore = best;
            panel.SetActive(true);
            pile.SetPhotoMode(true);
            if (cutout != null)
            {
                cutout.BeautyStrength = config != null ? config.beautyStrength : 1f;
                cutout.BigEyeStrength = config != null ? config.bigEye : 0.4f;
                cutout.SlimStrength = config != null ? config.faceSlim : 0.4f;
            }
            SetHidden(true);
            EnterPose();
        }

        /// <summary>"Take photo" button.</summary>
        public void TakePhoto()
        {
            if (step != Step.Pose) return;
            step = Step.Countdown;
            countdownLeft = countdownSeconds;
            lastShown = -1;
            poseGroup.SetActive(false);
            countdownText.gameObject.SetActive(true);
        }

        public void Retake()
        {
            if (step == Step.Preview) EnterPose();
        }

        public void Save()
        {
            if (step != Step.Preview || LastPhoto == null) return;
            EnsureSaved();
            savedText.text = Strings.Get("photo.saved");
        }

        /// <summary>Print button: saves (once) and sends the photo to the default printer.</summary>
        public void Print()
        {
            if (step != Step.Preview || LastPhoto == null || share == null) return;
            string path = EnsureSaved();
            savedText.text = Strings.Get(share.Print(path) ? "photo.printing" : "photo.printFail");
        }

        /// <summary>Phone button: saves (once), publishes the photo on the local network and shows the link / QR.</summary>
        public void ShowPhone()
        {
            if (step != Step.Preview || LastPhoto == null || share == null) return;
            EnsureSaved();
            string url = share.Publish(LastPhoto.EncodeToPNG());
            if (url == null)
            {
                savedText.text = share.LastError ?? Strings.Get("photo.shareFail");
                return;
            }
            var qr = QrEncoder?.Invoke(url);
            qrImage.gameObject.SetActive(qr != null);
            if (qr != null)
            {
                if (qrImage.texture != null) Destroy(qrImage.texture);
                qrImage.texture = qr;
            }
            // One size for both lines: the pixel font only stays crisp at multiples of 16 px.
            phoneLink.text = Strings.Get(qr != null ? "photo.linkQr" : "photo.link", url);
            phonePanel.SetActive(true);
        }

        public void ClosePhone() => phonePanel.SetActive(false);

        /// <summary>Forgets the previous round's action shots (a new round is starting).</summary>
        public void ClearActionShots()
        {
            foreach (var t in actionShots) if (t != null) Destroy(t);
            actionShots.Clear();
        }

        /// <summary>
        /// Takes an action shot mid-round: the scene as it is (basket and falling fruit included, no HUD), with the pile in
        /// full colour for the photo like the final shot.
        /// </summary>
        public void TakeActionShot()
        {
            pile.SetPhotoMode(true);
            actionShots.Add(RenderPhoto());
            pile.SetPhotoMode(false);
        }

        string EnsureSaved()
        {
            if (saved) return savedPath;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FruitGame");
            Directory.CreateDirectory(dir);
            savedPath = Path.Combine(dir, $"FruitGame_{DateTime.Now:yyyyMMdd_HHmmss}_{score}.png");
            File.WriteAllBytes(savedPath, LastPhoto.EncodeToPNG());
            saved = true;
            Debug.Log($"[FruitGame] Photo saved: {savedPath}");
            return savedPath;
        }

        /// <summary>"Done": leave the booth (the photo is kept only if it was saved).</summary>
        public void Done()
        {
            if (step == Step.Off) return;
            step = Step.Off;
            panel.SetActive(false);
            pile.SetPhotoMode(false);
            if (cutout != null) cutout.BeautyStrength = 0f;
            SetHidden(false);
            Closed?.Invoke();
        }

        void EnterPose()
        {
            step = Step.Pose;
            saved = false;
            poseGroup.SetActive(true);
            previewGroup.SetActive(false);
            countdownText.gameObject.SetActive(false);
            if (phonePanel != null) phonePanel.SetActive(false);
        }

        void Update()
        {
            if (flashT >= 0f)
            {
                flashT += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(flashT / flashTime);
                flash.color = new Color(1f, 1f, 1f, a);
                if (a <= 0f) { flashT = -1f; flash.gameObject.SetActive(false); }
            }

            if (step != Step.Countdown) return;
            countdownLeft -= Time.unscaledDeltaTime;
            int shown = Mathf.CeilToInt(countdownLeft);
            if (shown != lastShown && shown > 0)
            {
                lastShown = shown;
                countdownText.text = shown.ToString();
                if (audioFeedback != null) audioFeedback.PlayCountdownTick();
            }
            if (countdownLeft <= 0f) Capture();
        }

        void Capture()
        {
            countdownText.gameObject.SetActive(false);
            if (LastPhoto != null) Destroy(LastPhoto);
            if (finalShot != null) Destroy(finalShot);
            finalShot = RenderPhoto();
            if (frame != null)
            {
                var cuts = new List<Texture2D>(actionShots) { finalShot };
                LastPhoto = frame.Compose(cuts, score, bestScore);
            }
            else LastPhoto = Instantiate(finalShot);

            flash.gameObject.SetActive(true);
            flashT = 0f;
            if (audioFeedback != null) audioFeedback.PlayShutter();

            previewImage.texture = LastPhoto;
            savedText.text = "";
            previewGroup.SetActive(true);
            step = Step.Preview;
        }

        /// <summary>Renders the game camera (no UI) into a full-resolution photo.</summary>
        Texture2D RenderPhoto()
        {
            var rt = RenderTexture.GetTemporary(photoSize.x, photoSize.y, 24, RenderTextureFormat.ARGB32);
            var oldRect = gameCamera.rect;
            var oldTarget = gameCamera.targetTexture;
            gameCamera.rect = new Rect(0f, 0f, 1f, 1f);
            // The pixel render's layers must show this moment (basket hidden, pile in full colour) before compositing.
            var pixel = gameCamera.GetComponent<PixelRenderer>();
            if (pixel != null) pixel.RenderNow();
            // No HUD in the photo: switch off overlay cameras stacked on the game camera (the 3D score and timer).
            var camData = gameCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var stack = camData != null ? camData.cameraStack : null;
            var stacked = stack != null ? stack.Where(c => c != null && c.enabled).ToList() : new List<Camera>();
            foreach (var c in stacked) c.enabled = false;
            gameCamera.targetTexture = rt;
            gameCamera.Render();
            foreach (var c in stacked) c.enabled = true;
            gameCamera.targetTexture = oldTarget;
            gameCamera.rect = oldRect;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(photoSize.x, photoSize.y, TextureFormat.RGB24, false) { name = "Photo" };
            tex.ReadPixels(new Rect(0, 0, photoSize.x, photoSize.y), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        void SetHidden(bool hidden)
        {
            foreach (var go in hideDuringPhoto)
                if (go != null) go.SetActive(!hidden);
        }

        void OnDestroy()
        {
            if (LastPhoto != null) Destroy(LastPhoto);
            if (finalShot != null) Destroy(finalShot);
            ClearActionShots();
        }
    }
}
