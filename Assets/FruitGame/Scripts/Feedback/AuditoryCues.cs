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
    /// With no fruit, during calibration's "try it" step, a gentle pulse stands for the basket itself: placed by where it
    /// is (hard left at the left end of its travel, ahead in the middle), at a fixed low pitch and rate so it can't be
    /// taken for a fruit, so a visitor who can't see it hears the basket follow their head.
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
        [Tooltip("Calibration's try-it step: the basket's pulse pitch (Hz) and pulses a second.")]
        [SerializeField] float tryPitch = 392f;
        [SerializeField] float tryPulsesPerSecond = 4f;

        CalibrationController calibration;

        const float HeadRadius = 0.0875f, SpeedOfSound = 343f;
        const float BeepLength = 0.045f;
        const float GlideTime = 0.02f, LevelGlideTime = 0.002f;

        // Written on the main thread, read on the audio thread.
        volatile bool active, steady, golden, chimeGolden;
        volatile float azimuth, toneHz, interval, gain = 1f;
        volatile int chimeRequest, missRequest;

        int sampleRate;
        double phase, shimmerPhase;
        float beepClock, beepAge = 1f;
        float[] delayLine = new float[2048];
        int delayWrite;
        // Everything that changes while a tone sounds glides per sample; a step in any of these is an audible click.
        float level, smoothGain = 1f, delayL, delayR, gainL = 1f, gainR = 1f, lowpassCoefL = 1f, lowpassCoefR = 1f, lowpassL, lowpassR;
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
            calibration = FindAnyObjectByType<CalibrationController>();
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
            if (target == null)
            {
                // Calibration's try-it step: the basket's own place, so moving the head is heard.
                if (Enabled && basket != null && calibration != null && calibration.Trying)
                {
                    float x = Mathf.Clamp(basket.X / Mathf.Max(0.01f, config.basketXLimit), -1f, 1f);
                    azimuth = x * Mathf.PI * 0.5f;
                    toneHz = tryPitch;
                    interval = 1f / Mathf.Max(0.5f, tryPulsesPerSecond);
                    steady = false;
                    golden = false;
                    active = true;
                    return;
                }
                active = false;
                return;
            }

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
            float itd = Mathf.Min(delayLine.Length - 2, HeadRadius / SpeedOfSound * (Mathf.Abs(az) + sinAz) * sampleRate);
            float nearGain = 1f + 0.15f * sinAz, farGain = 1f - 0.55f * sinAz;
            float cutoff = Mathf.Lerp(18000f, 1600f, sinAz);
            float lpA = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sampleRate);
            bool rightNear = az >= 0f;
            float targetDelayL = rightNear ? itd : 0f, targetDelayR = rightNear ? 0f : itd;
            float targetGainL = rightNear ? farGain : nearGain, targetGainR = rightNear ? nearGain : farGain;
            float targetCoefL = rightNear ? lpA : 1f, targetCoefR = rightNear ? 1f : lpA;
            float glide = 1f - Mathf.Exp(-dt / GlideTime), levelGlide = 1f - Mathf.Exp(-dt / LevelGlideTime);
            float hz = toneHz, iv = interval, targetVol = gain;
            bool st = steady, gold = golden;

            if (chimeRequest != lastChime) { lastChime = chimeRequest; StartShot(chimeGolden ? new[] { 1047f, 1319f, 1568f, 2093f, 2637f } : new[] { 784f, 988f, 1175f }, 0.07f, 0.9f); }
            if (missRequest != lastMiss) { lastMiss = missRequest; StartShot(new[] { 330f, 262f }, 0.12f, 0.6f); }

            for (int i = 0; i < data.Length; i += channels)
            {
                // The beep: a short enveloped tone every `interval`, or a steady tone when lined up. The oscillator runs
                // on until the level has faded, so switching between them or stopping never cuts a wave mid-swing.
                float target = 0f;
                if (on)
                {
                    beepClock += dt;
                    if (beepClock >= iv) { beepClock = 0f; beepAge = 0f; }
                    beepAge += dt;
                    target = st ? 0.55f : Envelope(beepAge, BeepLength) * 0.8f;
                }
                level += (target - level) * levelGlide;
                float s = 0f;
                if (level > 1e-5f)
                {
                    phase += hz * dt;
                    float tone = Mathf.Sin((float)(phase * 2.0 * Mathf.PI));
                    if (gold)
                    {
                        shimmerPhase += 7.0 * dt;
                        tone = 0.7f * tone + 0.3f * Mathf.Sin((float)(phase * 4.0 * Mathf.PI)) * (0.6f + 0.4f * Mathf.Sin((float)(shimmerPhase * 2.0 * Mathf.PI)));
                    }
                    s = tone * level;
                }
                if (phase > 1e6) phase -= System.Math.Floor(phase);

                // Binaural placement: each ear has its own delay, level and head shadow.
                delayLine[delayWrite] = s;
                delayL += (targetDelayL - delayL) * glide;
                delayR += (targetDelayR - delayR) * glide;
                gainL += (targetGainL - gainL) * glide;
                gainR += (targetGainR - gainR) * glide;
                lowpassCoefL += (targetCoefL - lowpassCoefL) * glide;
                lowpassCoefR += (targetCoefR - lowpassCoefR) * glide;
                smoothGain += (targetVol - smoothGain) * glide;
                lowpassL += lowpassCoefL * (ReadDelay(delayL) - lowpassL);
                lowpassR += lowpassCoefR * (ReadDelay(delayR) - lowpassR);
                delayWrite = (delayWrite + 1) % delayLine.Length;
                float left = lowpassL * gainL, right = lowpassR * gainR;
                float g = smoothGain * volume;

                // One-shots (chime, miss) play centred.
                float shot = ShotSample(dt);
                data[i] += left * g + shot * volume;
                data[i + 1] += right * g + shot * volume;
            }
        }

        float ReadDelay(float samples)
        {
            float pos = delayWrite - samples;
            if (pos < 0f) pos += delayLine.Length;
            int a = (int)pos;
            int b = (a + 1) % delayLine.Length;
            float frac = pos - a;
            return delayLine[a % delayLine.Length] * (1f - frac) + delayLine[b] * frac;
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
