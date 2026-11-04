using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The auditory assist mode's Korean narrator (Docs/DESIGN.md §11, M18). Plays pre-rendered lines from
    /// Resources/Narration/&lt;key&gt; (rendered by Tools/render_narration.py from Audio/Narration/lines.tsv) one after another.
    /// Lines with numbers use a clip per number (e.g. score_now_12). A missing clip is skipped with one warning.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Narrator : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float volume = 1f;
        [Tooltip("Pause between queued lines (s).")]
        [SerializeField] float gap = 0.08f;

        AudioSource source;
        readonly Queue<AudioClip> queue = new Queue<AudioClip>();
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        readonly HashSet<string> warned = new HashSet<string>();
        float nextAt;

        /// <summary>True while a line is playing or waiting.</summary>
        public bool IsSpeaking => (source != null && source.isPlaying) || queue.Count > 0;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        /// <summary>Queues lines by key, after anything already queued.</summary>
        public void Say(params string[] keys)
        {
            foreach (var key in keys)
            {
                var clip = Clip(key);
                if (clip != null) queue.Enqueue(clip);
            }
        }

        /// <summary>Stops the current line and drops the queue, then says these (for things that can't wait).</summary>
        public void SayNow(params string[] keys)
        {
            Stop();
            Say(keys);
        }

        public void Stop()
        {
            queue.Clear();
            if (source != null) source.Stop();
        }

        AudioClip Clip(string key)
        {
            if (cache.TryGetValue(key, out var clip)) return clip;
            clip = Resources.Load<AudioClip>("Narration/" + key);
            cache[key] = clip;
            if (clip == null && warned.Add(key)) Debug.LogWarning($"[FruitGame] Narrator: no clip for '{key}' (run Tools/render_narration.py)");
            return clip;
        }

        void Update()
        {
            if (source.isPlaying || queue.Count == 0) return;
            if (Time.unscaledTime < nextAt) return;
            source.PlayOneShot(queue.Dequeue(), volume);
            // isPlaying covers PlayOneShot; keep a small gap between lines.
            nextAt = Time.unscaledTime + gap;
        }
    }
}
