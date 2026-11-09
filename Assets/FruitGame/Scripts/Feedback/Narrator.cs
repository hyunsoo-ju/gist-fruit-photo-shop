using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The Korean narrator (Docs/DESIGN.md §11, M6 item 6). Plays pre-rendered lines from Resources/Narration/s&lt;speed&gt;/&lt;key&gt;
    /// (rendered by Tools/render_narration.py from Resources/NarrationLines.txt), falling back to Resources/Narration/&lt;key&gt;.
    /// One voice path: SayNow cuts whatever is playing, Say adds to the current utterance, and the visitor can stop,
    /// repeat, mute, and change speed and volume; those last for one visit (ResetVisit). A line with no clip yet shows
    /// its caption for a moment and moves on.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Narrator : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float volume = 1f;
        [Tooltip("Volume while Quiet (the title's short invitation, outside voice assist).")]
        [SerializeField, Range(0f, 1f)] float quietVolume = 0.6f;
        [Tooltip("Pause between queued lines (s).")]
        [SerializeField] float gap = 0.08f;

        public static Narrator Instance { get; private set; }

        /// <summary>Speak at quietVolume instead of full volume.</summary>
        public bool Quiet { get; set; }
        /// <summary>The visitor turned the voice off for this visit.</summary>
        public bool Muted { get; private set; }

        // 0 dB is the booth's default level: "0" is half of full scale, so the +6 step has room. 18 dB in 6 dB steps.
        static readonly float[] VolumeSteps = { -12f, -6f, 0f, 6f };
        const int DefaultVolumeStep = 2;
        const float DefaultGain = 0.5f;
        static readonly int[] AllSpeeds = { 100, 125, 150, 175, 200 };

        AudioSource source, clickSource;
        AudioClip click;
        readonly Queue<string> queue = new Queue<string>();
        readonly List<string> last = new List<string>();
        readonly List<int> speeds = new List<int>();
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, string> captions = new Dictionary<string, string>();
        readonly HashSet<string> warned = new HashSet<string>();
        int speedIndex, volumeStep = DefaultVolumeStep;
        float nextAt, captionUntil;
        bool heard = true; // the last instructions played to the end, or the visitor chose to stop them

        /// <summary>In a line list: reads out the screen's buttons in 4/6 order when it is reached.</summary>
        public const string Buttons = "@buttons";

        /// <summary>The line being spoken, or "" (for the caption strip).</summary>
        public string Caption { get; private set; } = "";

        /// <summary>True while a line is playing or waiting.</summary>
        public bool IsSpeaking => (source != null && source.isPlaying) || queue.Count > 0;

        void Awake()
        {
            Instance = this;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            clickSource = gameObject.AddComponent<AudioSource>();
            clickSource.playOnAwake = false;
            clickSource.spatialBlend = 0f;
            click = MakeClick();
            LoadCaptions();
            foreach (int s in AllSpeeds)
                if (Resources.Load<AudioClip>($"Narration/s{s}/count_3") != null) speeds.Add(s);
            if (!gameObject.TryGetComponent<VoiceAssist>(out _)) gameObject.AddComponent<VoiceAssist>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (click != null) Destroy(click);
        }

        void LoadCaptions()
        {
            var asset = Resources.Load<TextAsset>("NarrationLines");
            if (asset == null) return;
            foreach (var raw in asset.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                if (tab > 0) captions[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
        }

        string CaptionOf(string key)
        {
            if (captions.TryGetValue(key, out var text)) return text;
            int i = key.Length;
            while (i > 0 && char.IsDigit(key[i - 1])) i--;
            if (i < key.Length && captions.TryGetValue(key.Substring(0, i) + "{n}", out text))
                return text.Replace("{n}", key.Substring(i));
            return "";
        }

        /// <summary>Adds lines to what's being said (a new utterance if nothing is).</summary>
        public void Say(params string[] keys)
        {
            if (!IsSpeaking) last.Clear();
            last.AddRange(keys);
            if (Muted) return;
            foreach (var key in keys) queue.Enqueue(key);
            heard = false;
        }

        /// <summary>Stops the current line and drops the queue, then says these.</summary>
        public void SayNow(params string[] keys)
        {
            Stop();
            Say(keys);
        }

        /// <summary>
        /// A short reply to a key (a button's name, the volume): cuts the voice but leaves "repeat" on the screen's
        /// guidance. Instructions it cut off start again afterwards, so nobody is taken to have heard them.
        /// </summary>
        public void SayFocus(params string[] keys)
        {
            bool interrupted = !heard;
            Stop();
            if (Muted) return;
            foreach (var key in keys) queue.Enqueue(key);
            if (!interrupted) return;
            foreach (var key in last) queue.Enqueue(key);
            heard = false;
        }

        public void Stop()
        {
            queue.Clear();
            if (source != null) source.Stop();
            Caption = "";
            captionUntil = 0f;
            nextAt = 0f;
            heard = true;
        }

        public void Repeat()
        {
            if (last.Count == 0) return;
            var copy = last.ToArray();
            SayNow(copy);
        }

        /// <summary>Stop what's playing; with nothing playing, turn the voice off or back on for this visit.</summary>
        public void StopOrMute()
        {
            if (IsSpeaking) { Stop(); return; }
            if (Muted) { Muted = false; SayFocus("voice_on"); }
            else { SayFocus("voice_off"); Muted = true; } // queued before muting, so it's still heard
        }

        public void ChangeVolume(int delta)
        {
            int next = Mathf.Clamp(volumeStep + delta, 0, VolumeSteps.Length - 1);
            string line = next == volumeStep ? (delta > 0 ? "volume_max" : "volume_min") : delta > 0 ? "volume_up" : "volume_down";
            volumeStep = next;
            SayFocus(line);
        }

        public void NextSpeed()
        {
            if (speeds.Count == 0) return;
            speedIndex = (speedIndex + 1) % speeds.Count;
            SayFocus("speed_" + speeds[speedIndex]);
        }

        /// <summary>The visit is over: the next visitor starts at the defaults, at or under the booth's default level.</summary>
        public void ResetVisit()
        {
            Stop();
            last.Clear();
            Muted = false;
            Quiet = false;
            speedIndex = 0;
            volumeStep = DefaultVolumeStep;
        }

        public void Click() { if (clickSource != null) clickSource.PlayOneShot(click, 0.5f); }

        static AudioClip MakeClick()
        {
            const int rate = 24000, n = 1000;
            var data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = Mathf.Sin(i * 2f * Mathf.PI * 1400f / rate) * Mathf.Exp(-i / 180f);
            var clip = AudioClip.Create("Click", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void ExpandButtons()
        {
            var rest = queue.ToArray();
            queue.Clear();
            var names = VoiceAssist.Instance != null ? VoiceAssist.Instance.ButtonLines() : new List<string>();
            if (names.Count > 0) queue.Enqueue("buttons_order");
            foreach (var n in names) queue.Enqueue(n);
            foreach (var r in rest) queue.Enqueue(r);
        }

        AudioClip Clip(string key)
        {
            string path = speeds.Count > 0 ? $"Narration/s{speeds[speedIndex]}/{key}" : "Narration/" + key;
            if (cache.TryGetValue(path, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(path);
            if (clip == null && speeds.Count > 0) clip = Resources.Load<AudioClip>("Narration/" + key);
            cache[path] = clip;
            if (clip == null && warned.Add(key)) Debug.LogWarning($"[FruitGame] Narrator: no clip for '{key}' (run Tools/render_narration.py)");
            return clip;
        }

        void Update()
        {
            if (source.isPlaying) return;
            if (queue.Count == 0)
            {
                if (Time.unscaledTime >= captionUntil) Caption = "";
                heard = true;
                return;
            }
            if (Time.unscaledTime < nextAt) return;
            string key = queue.Dequeue();
            if (key == Buttons) { ExpandButtons(); return; }
            Caption = CaptionOf(key);
            var clip = Clip(key);
            if (clip == null)
            {
                captionUntil = nextAt = Time.unscaledTime + 1.5f;
                return;
            }
            float level = (Quiet ? quietVolume : volume) * DefaultGain * Mathf.Pow(10f, VolumeSteps[volumeStep] / 20f);
            source.PlayOneShot(clip, level);
            captionUntil = 0f;
            nextAt = Time.unscaledTime + gap;
        }
    }
}
