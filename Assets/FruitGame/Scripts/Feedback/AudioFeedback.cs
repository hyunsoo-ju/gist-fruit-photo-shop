using UnityEngine;

namespace FruitGame
{
    /// <summary>One-shot game sounds. Clips are the first pick of each group (Docs/DESIGN.md §11).</summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioFeedback : MonoBehaviour
    {
        [SerializeField] AudioClip catchPop;
        [SerializeField] AudioClip rimTink;
        [SerializeField] AudioClip missThud;
        [SerializeField] AudioClip spawnRustle;
        [SerializeField] AudioClip pileLand;
        [SerializeField] AudioClip countdownTick;
        [SerializeField] AudioClip roundOver;
        [Tooltip("Camera shutter (\"Camera\" by themightyglider, OpenGameArt, CC0).")]
        [SerializeField] AudioClip shutter;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;

        AudioSource source;

        void Awake() => source = GetComponent<AudioSource>();

        public void PlayCatch() => Play(catchPop, Random.Range(0.95f, 1.08f));
        public void PlayRimGraze() => Play(rimTink);
        public void PlayMiss() => Play(missThud);
        public void PlaySpawn() => Play(spawnRustle, Random.Range(0.9f, 1.1f), 0.5f);
        public void PlayPileLand() => Play(pileLand, Random.Range(0.9f, 1.1f), 0.4f);
        public void PlayCountdownTick() => Play(countdownTick);
        public void PlayRoundOver() => Play(roundOver);
        public void PlayShutter() => Play(shutter);

        void Play(AudioClip clip, float pitch = 1f, float gain = 1f)
        {
            if (clip == null || source == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume * gain);
        }
    }
}
