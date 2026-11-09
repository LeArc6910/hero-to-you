using System;

namespace Game.Client
{
    public enum Sound
    {
        Click,
        Dice,
        Success,
        Perfect,
        Miss,
        Hit,
        Heal,
        Down,
        Battle,
        Victory,
        Defeat,
        Level,
        Step,
    }

    // The game's sound effects, synthesized in code (no audio files): short tones, noise and sweeps, 8-bit in feel.
    // Plain C# with no UnityEngine, so DevTools/SoundPreview can write the same samples to WAV files and check them
    // without Unity. Sounds.cs turns them into AudioClips.
    public static class SoundArt
    {
        public const int Rate = 22050;

        public static float[] Make(Sound sound)
        {
            switch (sound)
            {
                case Sound.Click: return Tone(880, 0.04f, 0.25f, Wave.Square);
                case Sound.Dice: return Rattle();
                case Sound.Success: return Join(Tone(660, 0.06f, 0.3f, Wave.Square), Tone(990, 0.09f, 0.3f, Wave.Square));
                case Sound.Perfect: return Join(Tone(784, 0.07f, 0.3f, Wave.Square), Tone(988, 0.07f, 0.3f, Wave.Square), Tone(1319, 0.18f, 0.32f, Wave.Square));
                case Sound.Miss: return Sweep(420, 180, 0.16f, 0.25f, Wave.Triangle);
                case Sound.Hit: return Mix(Noise(0.09f, 0.5f, 7), Sweep(220, 90, 0.1f, 0.35f, Wave.Square));
                case Sound.Heal: return Join(Tone(523, 0.08f, 0.22f, Wave.Triangle), Tone(659, 0.08f, 0.22f, Wave.Triangle), Tone(784, 0.14f, 0.22f, Wave.Triangle));
                case Sound.Down: return Sweep(330, 70, 0.4f, 0.35f, Wave.Square);
                case Sound.Battle: return Join(Tone(196, 0.1f, 0.35f, Wave.Square), Tone(196, 0.1f, 0.35f, Wave.Square), Tone(262, 0.22f, 0.35f, Wave.Square));
                case Sound.Victory: return Join(Tone(523, 0.1f, 0.3f, Wave.Square), Tone(659, 0.1f, 0.3f, Wave.Square), Tone(784, 0.1f, 0.3f, Wave.Square), Tone(1047, 0.3f, 0.3f, Wave.Square));
                case Sound.Defeat: return Join(Tone(392, 0.18f, 0.3f, Wave.Triangle), Tone(330, 0.18f, 0.3f, Wave.Triangle), Tone(262, 0.4f, 0.3f, Wave.Triangle));
                case Sound.Level: return Join(Tone(659, 0.08f, 0.28f, Wave.Square), Tone(880, 0.08f, 0.28f, Wave.Square), Tone(1175, 0.08f, 0.28f, Wave.Square), Tone(1760, 0.2f, 0.25f, Wave.Triangle));
                case Sound.Step: return Noise(0.05f, 0.25f, 3);
                default: return new float[1];
            }
        }

        enum Wave
        {
            Square,
            Triangle,
        }

        static float Shape(Wave wave, double phase)
        {
            phase -= Math.Floor(phase);
            return wave == Wave.Square ? (phase < 0.5 ? 1f : -1f) : (float)(phase < 0.5 ? 4 * phase - 1 : 3 - 4 * phase);
        }

        // A short attack and a linear release, so nothing clicks.
        static float Envelope(int i, int count)
        {
            int attack = Math.Min(count / 10, Rate / 200);
            if (i < attack) return (float)i / Math.Max(1, attack);
            return 1f - (float)(i - attack) / Math.Max(1, count - attack);
        }

        static float[] Tone(double hz, float seconds, float volume, Wave wave) => Sweep(hz, hz, seconds, volume, wave);

        static float[] Sweep(double fromHz, double toHz, float seconds, float volume, Wave wave)
        {
            int count = Math.Max(1, (int)(Rate * seconds));
            var samples = new float[count];
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                double hz = fromHz + (toHz - fromHz) * i / count;
                phase += hz / Rate;
                samples[i] = Shape(wave, phase) * volume * Envelope(i, count);
            }
            return samples;
        }

        // Noise from a fixed little generator (the same every time), coarser for a lower `hold`.
        static float[] Noise(float seconds, float volume, int hold)
        {
            int count = Math.Max(1, (int)(Rate * seconds));
            var samples = new float[count];
            uint state = 0x9E3779B9;
            float value = 0;
            for (int i = 0; i < count; i++)
            {
                if (i % Math.Max(1, hold) == 0)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    value = (state & 0xFFFF) / 32768f - 1f;
                }
                samples[i] = value * volume * Envelope(i, count);
            }
            return samples;
        }

        // Dice tumbling: a few short clicks of noise, closer together then apart.
        static float[] Rattle()
        {
            float[] gaps = { 0.03f, 0.025f, 0.035f, 0.05f, 0.07f };
            var parts = new float[gaps.Length * 2][];
            for (int i = 0; i < gaps.Length; i++)
            {
                parts[i * 2] = Mix(Noise(0.018f, 0.45f, 2), Tone(1400 - i * 120, 0.018f, 0.15f, Wave.Square));
                parts[i * 2 + 1] = new float[(int)(Rate * gaps[i])];
            }
            return Join(parts);
        }

        static float[] Join(params float[][] parts)
        {
            int length = 0;
            foreach (var part in parts) length += part.Length;
            var samples = new float[length];
            int at = 0;
            foreach (var part in parts)
            {
                Array.Copy(part, 0, samples, at, part.Length);
                at += part.Length;
            }
            return samples;
        }

        static float[] Mix(float[] a, float[] b)
        {
            var samples = new float[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < samples.Length; i++)
            {
                float v = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
                samples[i] = Math.Max(-1f, Math.Min(1f, v));
            }
            return samples;
        }
    }
}
