using UnityEngine;

namespace Mandala.Game
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class MandalaAudio : MonoBehaviour
    {
        // D3, F#3, A3, B3, A3, F#3, E3, D3: a rise and return in D major pentatonic.
        static readonly int[] Pattern = { 0, 2, 3, 4, 3, 2, 1, 0 };
        static readonly float[] Frequencies = { 146.8324f, 164.8138f, 184.9972f, 220f, 246.9417f };
        // Compensate the lower notes individually; keep A3 and B3 at their existing levels.
        static readonly float[] NoteGainDb = { 4, 3, 1.5f, 0, 0 };
        // Give D3 and F#3 sustained, rounded harmonics that carry on small speakers.
        static readonly float[] NoteBody = { .5f, 0, .4f, 0, 0 };
        const int Rate = 22050;
        const float NoteInterval = .65f;
        [SerializeField, HideInInspector] AudioSource source, feedback, fadingSource;
        [SerializeField, Range(0, 1)] float completionVolume = .8f;
        AudioClip pour, vacuum, sweep;
        AudioClip[] notes;
        SandTool current;
        float volume, fadingVolume, targetVolume, duck = 1, duckUntil, nextNoteAt;
        int nextNote, pendingNotes;
        bool resolutionPending;
        public bool Enabled { get; private set; } = true;
        void OnEnable() => EnsureAudio();

        void EnsureAudio()
        {
            if (source != null && feedback != null && fadingSource != null && notes != null
                && notes.Length == Frequencies.Length && notes[0] != null
                && pour != null && vacuum != null && sweep != null) return;
            // Serialized source references survive a Play Mode script reload; generated clips don't.
            if (source == null) source = GetComponent<AudioSource>();
            if (feedback == null || feedback == source) feedback = gameObject.AddComponent<AudioSource>();
            if (fadingSource == null || fadingSource == source || fadingSource == feedback)
                fadingSource = gameObject.AddComponent<AudioSource>();
            Configure(source, 160); Configure(fadingSource, 160); Configure(feedback, 32);
            StopAudio();
            DestroyClips();
            pour = Noise("Soft sand pour", 650, 131);
            vacuum = Noise("Soft sand vacuum", 260, 211);
            sweep = Noise("Soft sand sweep", 420, 331);
            notes = new AudioClip[Frequencies.Length];
            for (int i = 0; i < notes.Length; i++) notes[i] = Tone(Frequencies[i], NoteGainDb[i], NoteBody[i]);
        }

        static void Configure(AudioSource audio, int priority)
        {
            audio.playOnAwake = false;
            audio.spatialBlend = 0;
            audio.priority = priority;
            audio.volume = 0;
        }

        static AudioClip Tone(float frequency, float gainDb, float body)
        {
            var samples = new float[Mathf.RoundToInt(Rate * 1.5f)];
            float peak = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / Rate;
                float attack = Mathf.SmoothStep(0, 1, t / .065f);
                float release = Mathf.SmoothStep(0, 1, (samples.Length - 1 - i) / (Rate * .25f));
                float phase = t * 2 * Mathf.PI * frequency;
                // A small, quickly fading octave adds warmth without a bright bell attack.
                float tone = Mathf.Sin(phase) + Mathf.Sin(phase * 2) * .08f * Mathf.Exp(-t * 5);
                // The octave and a quieter third harmonic share the soft attack and release.
                // A slow extra decay keeps them audible past the onset without a bright edge.
                tone += body * Mathf.Exp(-t * .6f)
                    * (Mathf.Sin(phase * 2) + .4f * Mathf.Sin(phase * 3));
                samples[i] = tone * attack * Mathf.Exp(-t * 2.6f) * release;
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            float level = .42f * Mathf.Pow(10, gainDb / 20);
            for (int i = 0; i < samples.Length; i++) samples[i] *= level / Mathf.Max(peak, .001f);
            var clip = AudioClip.Create("Settled " + frequency.ToString("F1") + " Hz", samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip Noise(string name, float cutoff, int seed)
        {
            const int length = Rate * 8, overlap = Rate / 5;
            var raw = new float[length + overlap];
            var random = new System.Random(seed);
            float low = 0, smooth = 0, rumble = 0;
            float filter = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff / Rate);
            float highPass = 1 - Mathf.Exp(-2 * Mathf.PI * 65 / Rate);
            // Warm up the filters, then remove hiss with two low-pass stages and deep rumble.
            for (int i = -Rate; i < raw.Length; i++)
            {
                low += ((float)random.NextDouble() * 2 - 1 - low) * filter;
                smooth += (low - smooth) * filter;
                rumble += (smooth - rumble) * highPass;
                if (i >= 0) raw[i] = smooth - rumble;
            }
            var samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                // Continue the loop's tail into its head, crossfading back to the original head.
                // Playback gain supplies start/stop fades; the loop itself never dips to silence.
                samples[i] = i < overlap
                    ? Mathf.Lerp(raw[length + i], raw[i], Mathf.SmoothStep(0, 1, (float)i / (overlap - 1)))
                    : raw[i];
            }
            var clip = AudioClip.Create(name, length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public void SetEnabled(bool enabled)
        {
            EnsureAudio();
            Enabled = enabled;
            if (!enabled) StopAudio();
        }

        public void Tool(SandTool tool, bool active, float strength)
        {
            if (!Enabled || !isActiveAndEnabled) return;
            EnsureAudio();
            targetVolume = active ? Mathf.Clamp01(strength) * .42f : 0;
            if (!active || (source.isPlaying && current == tool)) return;
            if (source.isPlaying)
            {
                // Let the old tool fade out while the new tool fades in.
                var previous = source; source = fadingSource; fadingSource = previous;
                fadingVolume = volume;
            }
            source.Stop(); source.volume = volume = 0;
            current = tool;
            source.clip = tool == SandTool.Pour ? pour : tool == SandTool.Vacuum ? vacuum : sweep;
            source.loop = true;
            source.Play();
        }

        void Update()
        {
            if (!Enabled) return;
            EnsureAudio();
            PlayNextNote();
            float dt = Time.unscaledDeltaTime;
            volume = Approach(volume, targetVolume, targetVolume > volume ? .09f : .2f, dt);
            fadingVolume = Approach(fadingVolume, 0, .18f, dt);
            float targetDuck = Time.unscaledTime < duckUntil ? .6f : 1;
            duck = Approach(duck, targetDuck, targetDuck < duck ? .12f : .35f, dt);
            source.volume = volume * duck;
            fadingSource.volume = fadingVolume * duck;
            if (targetVolume == 0 && volume < .0001f) { source.Stop(); volume = 0; }
            if (fadingVolume < .0001f) { fadingSource.Stop(); fadingVolume = 0; }
        }

        static float Approach(float value, float target, float seconds, float dt)
            => Mathf.Lerp(value, target, 1 - Mathf.Exp(-dt / seconds));

        [ContextMenu("Play Section Completion Sound")]
        public void Celebrate() => Celebrate(1);

        public void Celebrate(int sections)
        {
            if (!Enabled || !isActiveAndEnabled || sections <= 0) return;
            EnsureAudio();
            pendingNotes += sections;
            PlayNextNote();
        }

        public void CompletePainting()
        {
            if (!Enabled || !isActiveAndEnabled) return;
            EnsureAudio();
            resolutionPending = true;
            PlayNextNote();
        }

        void PlayNextNote()
        {
            if ((pendingNotes == 0 && !resolutionPending) || Time.unscaledTime < nextNoteAt) return;
            int note = 0;
            if (pendingNotes > 0)
            {
                note = Pattern[nextNote];
                nextNote = (nextNote + 1) % Pattern.Length;
                pendingNotes--;
            }
            else resolutionPending = false;
            // One onset per 650 ms limits overlap to three gently decaying 1.5-second tails.
            feedback.volume = 1;
            feedback.PlayOneShot(notes[note], completionVolume);
            nextNoteAt = Time.unscaledTime + NoteInterval;
            duckUntil = Time.unscaledTime + .85f;
        }

        public void ResetPattern()
        {
            nextNote = pendingNotes = 0;
            resolutionPending = false;
            // Existing tails can finish; retain the onset spacing to avoid a reset-time pile-up.
        }

        void StopAudio()
        {
            if (source != null) source.Stop();
            if (fadingSource != null) fadingSource.Stop();
            if (feedback != null) feedback.Stop();
            volume = fadingVolume = targetVolume = duckUntil = nextNoteAt = 0;
            duck = 1;
            pendingNotes = 0; resolutionPending = false;
        }

        void OnDisable() => StopAudio();
        void DestroyClips()
        {
            Destroy(pour); Destroy(vacuum); Destroy(sweep);
            if (notes != null) foreach (var note in notes) Destroy(note);
        }
        void OnDestroy() => DestroyClips();
    }
}
