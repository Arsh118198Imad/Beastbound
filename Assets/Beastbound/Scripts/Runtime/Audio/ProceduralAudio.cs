using System;
using UnityEngine;

namespace Beastbound.Audio
{
    public enum SfxId
    {
        Footstep, Splash, Jump, Land, Swing, Hit, HitHeavy, Spark, SparkImpact, Dodge,
        BindThrow, BindShake, BindSuccess, BindFail,
        CreatureChirp, CreatureAlert, CreatureHurt, CreatureWindup, PebbleThrow,
        UiClick, UiMove, UiOpen, UiBack, Victory, Defeat, Discovery, Save, BattleStart
    }

    /// <summary>
    /// Synthesises every sound in the prototype at startup (original, license-free). Each clip is a few
    /// lines of maths, so they are easy to tweak and trivially replaceable with recorded audio later.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int Rate = 22050;
        private static System.Random _rng = new System.Random(12345);

        // ------------------------------------------------------------------ Building blocks

        private static float Noise() => (float)(_rng.NextDouble() * 2.0 - 1.0);
        private static float Sine(float phase) => Mathf.Sin(phase * Mathf.PI * 2f);
        private static float Tri(float phase) => 1f - 4f * Mathf.Abs(Mathf.Repeat(phase + 0.25f, 1f) - 0.5f);

        /// <summary>Attack/decay envelope over normalised time t (0..1).</summary>
        private static float Env(float t, float attack, float decayPower = 2f)
        {
            if (t < attack) return t / Mathf.Max(0.0001f, attack);
            return Mathf.Pow(1f - Mathf.Clamp01((t - attack) / (1f - attack)), decayPower);
        }

        private static AudioClip Make(string name, float[] data)
        {
            Normalize(data, 0.9f);
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void Normalize(float[] data, float peak)
        {
            float max = 0f;
            for (int i = 0; i < data.Length; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
            if (max < 0.0001f) return;
            float k = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
        }

        private static float[] Buffer(float seconds) => new float[Mathf.Max(1, (int)(seconds * Rate))];

        /// <summary>One-pole low-pass, in place.</summary>
        private static void LowPass(float[] data, float cutoffHz)
        {
            float rc = 1f / (2f * Mathf.PI * cutoffHz);
            float a = (1f / Rate) / (rc + 1f / Rate);
            float y = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                y += a * (data[i] - y);
                data[i] = y;
            }
        }

        /// <summary>Crossfades the end into the start so a buffer loops without clicks.</summary>
        private static float[] MakeLoop(float[] data, int fadeSamples)
        {
            fadeSamples = Mathf.Clamp(fadeSamples, 1, data.Length / 2);
            int len = data.Length - fadeSamples;
            var result = new float[len];
            Array.Copy(data, result, len);
            for (int i = 0; i < fadeSamples; i++)
            {
                float k = i / (float)fadeSamples;
                result[i] = data[i] * k + data[len + i] * (1f - k);
            }
            return result;
        }

        private static float NoteHz(int semitonesFromA4) => 440f * Mathf.Pow(2f, semitonesFromA4 / 12f);

        // ------------------------------------------------------------------ Sound effects

        public static AudioClip Build(SfxId id)
        {
            switch (id)
            {
                case SfxId.Footstep: return Thump("Footstep", 0.09f, 140f, 900f, 0.7f);
                case SfxId.Land: return Thump("Land", 0.16f, 90f, 700f, 1f);
                case SfxId.Splash: return NoiseBurst("Splash", 0.22f, 2500f, 0.02f, 1.5f);
                case SfxId.Jump: return Sweep("Jump", 0.12f, 300f, 520f, 0.3f, false);
                case SfxId.Swing: return NoiseSweep("Swing", 0.18f, 600f, 3500f);
                case SfxId.Hit: return Impact("Hit", 0.14f, 180f, 0.6f);
                case SfxId.HitHeavy: return Impact("HitHeavy", 0.24f, 110f, 1f);
                case SfxId.Spark: return Sweep("Spark", 0.28f, 400f, 1400f, 0.5f, true);
                case SfxId.SparkImpact: return NoiseBurst("SparkImpact", 0.25f, 4000f, 0.005f, 2.5f);
                case SfxId.Dodge: return NoiseSweep("Dodge", 0.16f, 2500f, 800f);
                case SfxId.BindThrow: return Sweep("BindThrow", 0.3f, 500f, 900f, 0.4f, true);
                case SfxId.BindShake: return Chime("BindShake", 0.25f, new[] { 7 }, 0.08f);
                case SfxId.BindSuccess: return Chime("BindSuccess", 1.1f, new[] { 3, 7, 10, 15 }, 0.09f);
                case SfxId.BindFail: return Chime("BindFail", 0.6f, new[] { 3, -2, -6 }, 0.12f);
                case SfxId.CreatureChirp: return Chirp("Chirp", 0.22f, 900f, 1400f, 2);
                case SfxId.CreatureAlert: return Chirp("Alert", 0.3f, 1200f, 2000f, 1);
                case SfxId.CreatureHurt: return Chirp("Hurt", 0.2f, 1100f, 600f, 1);
                case SfxId.CreatureWindup: return Sweep("Windup", 0.4f, 200f, 420f, 0.6f, false);
                case SfxId.PebbleThrow: return NoiseSweep("PebbleThrow", 0.15f, 1200f, 2400f);
                case SfxId.UiClick: return Blip("UiClick", 0.06f, 880f);
                case SfxId.UiMove: return Blip("UiMove", 0.04f, 660f);
                case SfxId.UiOpen: return Chime("UiOpen", 0.25f, new[] { 0, 7 }, 0.05f);
                case SfxId.UiBack: return Chime("UiBack", 0.2f, new[] { 7, 0 }, 0.05f);
                case SfxId.Victory: return Chime("Victory", 1.4f, new[] { 0, 4, 7, 12, 16 }, 0.11f);
                case SfxId.Defeat: return Chime("Defeat", 1.4f, new[] { 0, -3, -7, -12 }, 0.2f);
                case SfxId.Discovery: return Chime("Discovery", 0.9f, new[] { 7, 12, 19 }, 0.1f);
                case SfxId.Save: return Chime("Save", 0.4f, new[] { 12, 19 }, 0.06f);
                case SfxId.BattleStart: return Chime("BattleStart", 0.5f, new[] { 0, 6, 12 }, 0.05f);
                default: return Blip("Unknown", 0.05f, 440f);
            }
        }

        private static AudioClip Thump(string name, float seconds, float freq, float noiseCutoff, float noiseMix)
        {
            var d = Buffer(seconds);
            var n = Buffer(seconds);
            for (int i = 0; i < n.Length; i++) n[i] = Noise();
            LowPass(n, noiseCutoff);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float f = freq * (1f - 0.5f * t);
                phase += f / Rate;
                d[i] = (Sine(phase) * 0.6f + n[i] * noiseMix * 3f) * Env(t, 0.02f, 3f);
            }
            return Make(name, d);
        }

        private static AudioClip NoiseBurst(string name, float seconds, float cutoff, float attack, float decay)
        {
            var d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++) d[i] = Noise();
            LowPass(d, cutoff);
            for (int i = 0; i < d.Length; i++) d[i] *= Env(i / (float)d.Length, attack, decay);
            return Make(name, d);
        }

        private static AudioClip NoiseSweep(string name, float seconds, float fromHz, float toHz)
        {
            var d = Buffer(seconds);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float cutoff = Mathf.Lerp(fromHz, toHz, t);
                float rc = 1f / (2f * Mathf.PI * cutoff);
                float a = (1f / Rate) / (rc + 1f / Rate);
                y += a * (Noise() - y);
                d[i] = y * Mathf.Sin(t * Mathf.PI);
            }
            return Make(name, d);
        }

        private static AudioClip Sweep(string name, float seconds, float fromHz, float toHz, float shimmer, bool sparkle)
        {
            var d = Buffer(seconds);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float f = Mathf.Lerp(fromHz, toHz, t * t);
                phase += f / Rate;
                float v = Tri(phase) * 0.7f + Sine(phase * 2f) * shimmer * 0.3f;
                if (sparkle) v += Sine(phase * 3.01f) * 0.15f * Mathf.Sin(t * 40f);
                d[i] = v * Env(t, 0.05f, 2f);
            }
            return Make(name, d);
        }

        private static AudioClip Impact(string name, float seconds, float freq, float weight)
        {
            var d = Buffer(seconds);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                phase += freq * (1f - 0.6f * t) / Rate;
                float click = i < Rate * 0.006f ? Noise() : 0f;
                d[i] = (Sine(phase) * weight + Noise() * 0.35f * (1f - t) + click) * Env(t, 0.01f, 3f);
            }
            LowPass(d, 3000f);
            return Make(name, d);
        }

        private static AudioClip Chime(string name, float seconds, int[] semitones, float noteGap)
        {
            var d = Buffer(seconds);
            for (int n = 0; n < semitones.Length; n++)
            {
                float hz = NoteHz(semitones[n] + 3); // around C5
                int start = (int)(n * noteGap * Rate);
                float phase = 0f;
                for (int i = start; i < d.Length; i++)
                {
                    float t = (i - start) / (float)(d.Length - start);
                    phase += hz / Rate;
                    d[i] += (Sine(phase) * 0.6f + Sine(phase * 2f) * 0.25f + Sine(phase * 3f) * 0.1f) * Env(t, 0.01f, 2.5f);
                }
            }
            return Make(name, d);
        }

        private static AudioClip Chirp(string name, float seconds, float fromHz, float toHz, int syllables)
        {
            var d = Buffer(seconds);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                float local = Mathf.Repeat(t * syllables, 1f);
                float f = Mathf.Lerp(fromHz, toHz, Mathf.Sin(local * Mathf.PI));
                phase += f / Rate;
                float vibrato = 1f + 0.03f * Mathf.Sin(t * 60f);
                d[i] = (Sine(phase * vibrato) * 0.8f + Sine(phase * 2f) * 0.2f) * Env(local, 0.1f, 1.5f);
            }
            return Make(name, d);
        }

        private static AudioClip Blip(string name, float seconds, float hz)
        {
            var d = Buffer(seconds);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)d.Length;
                phase += hz / Rate;
                d[i] = Sine(phase) * Env(t, 0.05f, 2f);
            }
            return Make(name, d);
        }

        // ------------------------------------------------------------------ Loops

        /// <summary>Daytime meadow: soft wind plus occasional bird calls. Loops seamlessly.</summary>
        public static AudioClip BuildDayAmbience()
        {
            float seconds = 14f;
            var d = Buffer(seconds + 1f);
            var wind = Buffer(seconds + 1f);
            for (int i = 0; i < wind.Length; i++) wind[i] = Noise();
            LowPass(wind, 400f);
            LowPass(wind, 400f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float gust = 0.55f + 0.45f * Mathf.Sin(t * 0.45f) * Mathf.Sin(t * 0.17f + 1f);
                d[i] = wind[i] * 4f * gust;
            }
            // Birds
            var rng = new System.Random(7);
            for (int b = 0; b < 9; b++)
            {
                int start = rng.Next(0, d.Length - Rate);
                float baseHz = 2200f + (float)rng.NextDouble() * 1800f;
                int notes = 2 + rng.Next(4);
                for (int n = 0; n < notes; n++)
                {
                    int s0 = start + (int)(n * 0.11f * Rate);
                    int len = (int)(0.08f * Rate);
                    float phase = 0f;
                    for (int i = 0; i < len && s0 + i < d.Length; i++)
                    {
                        float t = i / (float)len;
                        phase += baseHz * (1f + 0.25f * Mathf.Sin(t * Mathf.PI)) / Rate;
                        d[s0 + i] += Sine(phase) * 0.12f * Mathf.Sin(t * Mathf.PI);
                    }
                }
            }
            var looped = MakeLoop(d, Rate);
            Normalize(looped, 0.6f);
            var clip = AudioClip.Create("AmbienceDay", looped.Length, 1, Rate, false);
            clip.SetData(looped, 0);
            return clip;
        }

        /// <summary>Night: gentle wind and crickets.</summary>
        public static AudioClip BuildNightAmbience()
        {
            float seconds = 10f;
            var d = Buffer(seconds + 1f);
            for (int i = 0; i < d.Length; i++) d[i] = Noise();
            LowPass(d, 250f);
            for (int i = 0; i < d.Length; i++) d[i] *= 3f;
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                phase += 4400f / Rate;
                float pulse = Mathf.Repeat(t * 14f, 1f) < 0.45f ? 1f : 0f;
                float chirpGroup = Mathf.Repeat(t * 0.7f, 1f) < 0.5f ? 1f : 0.15f;
                d[i] += Sine(phase) * 0.05f * pulse * chirpGroup;
            }
            var looped = MakeLoop(d, Rate);
            Normalize(looped, 0.5f);
            var clip = AudioClip.Create("AmbienceNight", looped.Length, 1, Rate, false);
            clip.SetData(looped, 0);
            return clip;
        }

        /// <summary>Calm exploration music: warm pad chords with a light plucked arpeggio (16 s loop).</summary>
        public static AudioClip BuildExploreMusic()
        {
            // I - vi - IV - V in C major, as semitone offsets from A4.
            int[][] chords =
            {
                new[] { -9, -5, -2, 3 }, new[] { -12, -9, -5, 0 }, new[] { -16, -12, -9, -4 }, new[] { -14, -10, -7, -2 }
            };
            float chordLen = 4f;
            var d = Buffer(chordLen * chords.Length + 1f);
            for (int c = 0; c < chords.Length; c++)
            {
                int start = (int)(c * chordLen * Rate);
                int len = (int)((chordLen + 1f) * Rate);
                foreach (int note in chords[c])
                {
                    float hz = NoteHz(note);
                    float phase = 0f;
                    for (int i = 0; i < len && start + i < d.Length; i++)
                    {
                        float t = i / (float)len;
                        phase += hz / Rate;
                        float env = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI); // slow swell
                        d[start + i] += (Tri(phase) * 0.5f + Sine(phase * 2f) * 0.15f) * env * 0.12f;
                    }
                }
                // Arpeggio: 8 plucks per chord, an octave up
                for (int k = 0; k < 8; k++)
                {
                    int note = chords[c][k % 4] + 12;
                    float hz = NoteHz(note);
                    int s0 = start + (int)(k * chordLen / 8f * Rate);
                    int plen = (int)(0.6f * Rate);
                    float phase = 0f;
                    for (int i = 0; i < plen && s0 + i < d.Length; i++)
                    {
                        float t = i / (float)plen;
                        phase += hz / Rate;
                        d[s0 + i] += Sine(phase) * Mathf.Exp(-t * 6f) * 0.08f;
                    }
                }
            }
            LowPass(d, 3500f);
            var looped = MakeLoop(d, Rate);
            Normalize(looped, 0.55f);
            var clip = AudioClip.Create("MusicExplore", looped.Length, 1, Rate, false);
            clip.SetData(looped, 0);
            return clip;
        }

        /// <summary>Battle music: driving bass, percussive noise hits and a minor riff (8 s loop).</summary>
        public static AudioClip BuildBattleMusic()
        {
            float bpm = 132f;
            float beat = 60f / bpm;
            int beats = 16;
            const float tail = 0.05f; // crossfaded back into the start so the loop stays on the beat
            var d = Buffer(beat * beats + tail);
            int[] bass = { -21, -21, -21, -21, -26, -26, -24, -24, -21, -21, -21, -21, -28, -28, -26, -24 };
            for (int b = 0; b < beats; b++)
            {
                int s0 = (int)(b * beat * Rate);
                // Kick on every beat
                int klen = (int)(0.18f * Rate);
                float phase = 0f;
                for (int i = 0; i < klen && s0 + i < d.Length; i++)
                {
                    float t = i / (float)klen;
                    phase += Mathf.Lerp(120f, 45f, t) / Rate;
                    d[s0 + i] += Sine(phase) * Mathf.Pow(1f - t, 2f) * 0.5f;
                }
                // Hat on off-beats
                int h0 = s0 + (int)(beat * 0.5f * Rate);
                int hlen = (int)(0.05f * Rate);
                for (int i = 0; i < hlen && h0 + i < d.Length; i++) d[h0 + i] += Noise() * (1f - i / (float)hlen) * 0.12f;
                // Bass eighths
                float hz = NoteHz(bass[b]);
                for (int e = 0; e < 2; e++)
                {
                    int e0 = s0 + (int)(e * beat * 0.5f * Rate);
                    int elen = (int)(beat * 0.45f * Rate);
                    float ph = 0f;
                    for (int i = 0; i < elen && e0 + i < d.Length; i++)
                    {
                        float t = i / (float)elen;
                        ph += hz / Rate;
                        d[e0 + i] += Tri(ph) * (1f - t) * 0.22f;
                    }
                }
            }
            // Lead riff
            int[] riff = { 0, 3, 7, 10, 7, 3, 5, 2 };
            for (int r = 0; r < riff.Length; r++)
            {
                int s0 = (int)(r * beat * 2f * Rate);
                int len = (int)(beat * 1.6f * Rate);
                float hz = NoteHz(riff[r] - 9);
                float phase = 0f;
                for (int i = 0; i < len && s0 + i < d.Length; i++)
                {
                    float t = i / (float)len;
                    phase += hz / Rate;
                    d[s0 + i] += (Tri(phase) * 0.6f + Sine(phase * 2f) * 0.2f) * Env(t, 0.03f, 1.5f) * 0.1f;
                }
            }
            LowPass(d, 5000f);
            var looped = MakeLoop(d, (int)(tail * Rate));
            Normalize(looped, 0.6f);
            var clip = AudioClip.Create("MusicBattle", looped.Length, 1, Rate, false);
            clip.SetData(looped, 0);
            return clip;
        }
    }
}
