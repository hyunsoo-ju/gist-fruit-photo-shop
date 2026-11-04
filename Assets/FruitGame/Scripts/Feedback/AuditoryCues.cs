using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The auditory assist mode's sound (Docs/DESIGN.md §11, M18), synthesised sample by sample for headphones. The
    /// lowest fruit in the air sounds like a parking sensor heard from the basket:
    /// - <b>Direction</b> is binaural: the beep is placed at an angle from the fruit's offset to the side of the basket,
    ///   with an interaural time difference (Woodworth: r/c · (θ + sin θ), up to about 0.65 ms), a level difference and
    ///   a low-passed far ear (head shadow).
    /// - <b>Rate</b>: beeps speed up as the basket lines up under the fruit, and become a steady tone when it's right under.
    /// - <b>Pitch</b> rises as the fruit falls closer to the basket.
    /// Golden fruit get a brighter, shimmering timbre. A rising chime plays on a catch (sparklier for golden), a soft
    /// falling note on a miss. Everything ducks while the narrator speaks.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AuditoryCues : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] FruitSpawner spawner;
        [SerializeField] BasketController basket;
        [SerializeField] Narrator narrator;
        [SerializeField, Range(0f, 1f)] float volume = 0.5f;
        [Tooltip("Offset (m) from the basket that maps to fully to one side (90°).")]
        [SerializeField] float sideRange = 2.2f;
        [Tooltip("Within this offset (m) the basket counts as lined up: a steady tone.")]
        [SerializeField] float lineUp = 0.32f;
        [SerializeField] Vector2 beepsPerSecond = new Vector2(1.5f, 15f);
        [Tooltip("Pitch (Hz) when the fruit appears, and when it reaches the basket.")]
        [SerializeField] Vector2 pitch = new Vector2(440f, 1245f);
        [SerializeField, Range(0f, 1f)] float duckWhileSpeaking = 0.25f;

        const float HeadRadius = 0.0875f, SpeedOfSound = 343f;
        const float BeepLength = 0.045f;

        // Written on the main thread, read on the audio thread.
        volatile bool active, steady, golden, chimeGolden;
        volatile float azimuth, toneHz, interval, gain = 1f;
        volatile int chimeRequest, missRequest;

        int sampleRate;
        double phase, shimmerPhase;
        float beepClock, beepAge = 1f;
        float[] delayLine = new float[2048];
        int delayWrite;
        float farLowpass;
        // One-shots: a short note sequence, one oscillator.
        float[] shotNotes;
        float shotNoteLength, shotAge = -1f, shotGain;
        int lastChime, lastMiss;

        public bool Enabled => Difficulty.Auditory;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var source = GetComponent<AudioSource>();
            source.playOnAwake = true;
            source.spatialBlend = 0f;
            source.loop = true;
            if (!source.isPlaying) source.Play();
            if (spawner != null) spawner.FruitResolved += OnResolved;
        }

        void OnDestroy()
        {
            if (spawner != null) spawner.FruitResolved -= OnResolved;
        }

        void OnResolved(FallingFruit fruit, FallingFruit.Outcome outcome)
        {
            if (!Enabled || fruit.FruitKind == FallingFruit.Kind.Bomb) return;
            if (outcome == FallingFruit.Outcome.Caught)
            {
                chimeGolden = fruit.FruitKind == FallingFruit.Kind.Golden;
                chimeRequest++;
            }
            else missRequest++;
        }

        void Update()
        {
            gain = narrator != null && narrator.IsSpeaking ? duckWhileSpeaking : 1f;
            FallingFruit target = null;
            if (Enabled && spawner != null && basket != null)
                foreach (var f in spawner.GetComponentsInChildren<FallingFruit>())
                    if (!f.Resolved && f.FruitKind != FallingFruit.Kind.Bomb && (target == null || f.transform.position.y < target.transform.position.y))
                        target = f;
            if (target == null) { active = false; return; }

            var p = target.transform.position;
            float dx = p.x - basket.X;
            float side = Mathf.Clamp(dx / sideRange, -1f, 1f);
            azimuth = side * Mathf.PI * 0.5f;
            float fallen = Mathf.Clamp01((config.spawnY - p.y) / Mathf.Max(0.01f, config.spawnY - config.rimY));
            toneHz = Mathf.Lerp(pitch.x, pitch.y, fallen * fallen * 0.4f + fallen * 0.6f);
            float away = Mathf.Clamp01((Mathf.Abs(dx) - lineUp) / Mathf.Max(0.01f, sideRange - lineUp));
            steady = Mathf.Abs(dx) <= lineUp;
            interval = 1f / Mathf.Lerp(beepsPerSecond.y, beepsPerSecond.x, away);
            golden = target.FruitKind == FallingFruit.Kind.Golden;
            active = true;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels < 2 || sampleRate <= 0) return;
            float dt = 1f / sampleRate;
            bool on = active;
            float az = azimuth;
            float sinAz = Mathf.Sin(Mathf.Abs(az));
            // Interaural time difference in samples (the far ear hears it later), level and head-shadow for the far ear.
            int itd = Mathf.Min(delayLine.Length - 1, Mathf.RoundToInt(HeadRadius / SpeedOfSound * (Mathf.Abs(az) + sinAz) * sampleRate));
            float nearGain = 1f + 0.15f * sinAz, farGain = 1f - 0.55f * sinAz;
            float cutoff = Mathf.Lerp(18000f, 1600f, sinAz);
            float lpA = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sampleRate);
            bool rightNear = az >= 0f;
            float hz = toneHz, iv = interval, g = gain * volume;
            bool st = steady, gold = golden;

            if (chimeRequest != lastChime) { lastChime = chimeRequest; StartShot(chimeGolden ? new[] { 1047f, 1319f, 1568f, 2093f, 2637f } : new[] { 784f, 988f, 1175f }, 0.07f, 0.9f); }
            if (missRequest != lastMiss) { lastMiss = missRequest; StartShot(new[] { 330f, 262f }, 0.12f, 0.6f); }

            for (int i = 0; i < data.Length; i += channels)
            {
                // The beep: a short enveloped tone every `interval`, or a steady tone when lined up.
                float s = 0f;
                if (on)
                {
                    beepClock += dt;
                    if (beepClock >= iv) { beepClock = 0f; beepAge = 0f; }
                    beepAge += dt;
                    float env = st ? 1f : Envelope(beepAge, BeepLength);
                    phase += hz * dt;
                    float tone = Mathf.Sin((float)(phase * 2.0 * Mathf.PI));
                    if (gold)
                    {
                        shimmerPhase += 7.0 * dt;
                        tone = 0.7f * tone + 0.3f * Mathf.Sin((float)(phase * 4.0 * Mathf.PI)) * (0.6f + 0.4f * Mathf.Sin((float)(shimmerPhase * 2.0 * Mathf.PI)));
                    }
                    s = tone * env * (st ? 0.55f : 0.8f);
                }
                if (phase > 1e6) phase -= System.Math.Floor(phase);

                // Binaural placement.
                delayLine[delayWrite] = s;
                int readIndex = (delayWrite - itd + delayLine.Length) % delayLine.Length;
                float far = delayLine[readIndex];
                delayWrite = (delayWrite + 1) % delayLine.Length;
                farLowpass += lpA * (far - farLowpass);
                float near = s * nearGain, farOut = farLowpass * farGain;
                float left = rightNear ? farOut : near, right = rightNear ? near : farOut;

                // One-shots (chime, miss) play centred.
                float shot = ShotSample(dt);
                data[i] += left * g + shot * volume;
                data[i + 1] += right * g + shot * volume;
            }
        }

        static float Envelope(float age, float length)
        {
            if (age >= length) return 0f;
            float a = Mathf.Min(1f, age / 0.005f), r = Mathf.Min(1f, (length - age) / 0.012f);
            return Mathf.Min(a, r);
        }

        void StartShot(float[] notes, float noteLength, float shotVolume)
        {
            shotNotes = notes;
            shotNoteLength = noteLength;
            shotGain = shotVolume;
            shotAge = 0f;
        }

        float ShotSample(float dt)
        {
            if (shotAge < 0f || shotNotes == null) return 0f;
            int n = (int)(shotAge / shotNoteLength);
            if (n >= shotNotes.Length + 3) { shotAge = -1f; return 0f; }
            // Each note rings on a little past its slot, so the chime sounds like a bell run.
            float sum = 0f;
            for (int k = Mathf.Max(0, n - 3); k <= Mathf.Min(n, shotNotes.Length - 1); k++)
            {
                float age = shotAge - k * shotNoteLength;
                float env = Mathf.Exp(-age * 9f) * Mathf.Min(1f, age / 0.004f);
                sum += Mathf.Sin(2f * Mathf.PI * shotNotes[k] * age) * env;
            }
            shotAge += dt;
            return sum * 0.35f * shotGain;
        }
    }
}
