using System;
using System.Collections.Generic;

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
        Forge,
        Page,
        Achievement,
        // A blow by what struck it (Docs/WebDemo.md 소리): blades, shots, spells, fists.
        Slash,
        Arrow,
        Spell,
        Punch,
        // The bosses' rules landing: a skill sealed, skills erased, frozen, branded, dazed by the bell.
        Seal,
        Erase,
        Freeze,
        Brand,
        Bell,
        // Blows by feel: a blunt thud, an arrow or bolt striking home, a burst of flame, a cold crackle.
        Impact,
        Pierce,
        Fire,
        Frost,
        // Movement: a projectile leaving or a quick swing, two running steps for a lunge, a body hitting the ground,
        // a boss crashing down.
        Whoosh,
        Run,
        Fall,
        BossDown,
        // The dice: tumbling across the table, then settling.
        DiceTumble,
        DiceLand,
        // A bright chime: a sure success, perseverance carrying a roll.
        Sure,
        // The shop and the smithy: coins paid, gear put on.
        Coin,
        Equip,
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
                case Sound.Forge: return Join(Mix(Noise(0.05f, 0.4f, 11), Tone(1568, 0.05f, 0.3f, Wave.Square)), Mix(Noise(0.05f, 0.35f, 12), Tone(1397, 0.12f, 0.25f, Wave.Triangle)));
                case Sound.Page: return Sweep(900, 1400, 0.12f, 0.15f, Wave.Triangle);
                case Sound.Slash: return Mix(Noise(0.08f, 0.4f, 2), Sweep(1200, 300, 0.08f, 0.18f, Wave.Square));
                case Sound.Arrow: return Join(Sweep(1800, 900, 0.05f, 0.18f, Wave.Triangle), Noise(0.05f, 0.35f, 4));
                case Sound.Spell: return Join(Sweep(400, 1200, 0.12f, 0.22f, Wave.Triangle), Tone(1200, 0.08f, 0.15f, Wave.Triangle));
                case Sound.Punch: return Mix(Noise(0.06f, 0.32f, 9), Tone(110, 0.08f, 0.26f, Wave.Square));
                case Sound.Seal: return Join(Tone(1568, 0.04f, 0.28f, Wave.Square), Mix(Noise(0.04f, 0.3f, 6), Tone(784, 0.12f, 0.28f, Wave.Square)));
                case Sound.Erase: return Mix(Sweep(1600, 200, 0.3f, 0.2f, Wave.Triangle), Noise(0.3f, 0.12f, 1));
                case Sound.Freeze: return Join(Tone(2093, 0.05f, 0.2f, Wave.Triangle), Tone(2637, 0.05f, 0.2f, Wave.Triangle), Mix(Noise(0.1f, 0.25f, 1), Tone(3136, 0.1f, 0.15f, Wave.Triangle)));
                case Sound.Brand: return Mix(Noise(0.25f, 0.32f, 1), Sweep(300, 150, 0.25f, 0.2f, Wave.Square));
                case Sound.Bell: return Mix(Tone(880, 0.35f, 0.28f, Wave.Triangle), Tone(1320, 0.35f, 0.14f, Wave.Triangle));
                case Sound.Achievement: return Join(Tone(523, 0.08f, 0.28f, Wave.Triangle), Tone(784, 0.08f, 0.28f, Wave.Triangle), Tone(1047, 0.08f, 0.28f, Wave.Triangle), Tone(1568, 0.3f, 0.25f, Wave.Triangle));
                case Sound.Impact: return Mix(Thump(170, 55, 0.16f, 0.42f), Lowpass(Noise(0.07f, 0.4f, 1, 21), 0.25f, 0.08f));
                case Sound.Pierce: return Join(Mix(Noise(0.012f, 0.3f, 1, 22), Tone(2400, 0.012f, 0.1f, Wave.Triangle)), Mix(Twang(330, 0.16f, 0.24f), Lowpass(Noise(0.04f, 0.3f, 1, 23), 0.3f, 0.1f)));
                case Sound.Fire: return Mix(Lowpass(Noise(0.42f, 0.5f, 1, 24), 0.05f, 0.4f, 0.08f), Crackle(0.42f, 0.22f, 25));
                case Sound.Frost: return Mix(Highpass(Noise(0.3f, 0.14f, 1, 26)), Join(Ring(3520, 0.06f, 0.16f, 8), Ring(2960, 0.06f, 0.16f, 8), Ring(3951, 0.06f, 0.16f, 8), Ring(2637, 0.14f, 0.16f, 6)));
                case Sound.Whoosh: return Lowpass(Noise(0.2f, 0.55f, 1, 27), 0.03f, 0.35f, 0.04f);
                case Sound.Run: return Join(Footstep(140, 28), Silence(0.06f), Footstep(165, 29));
                case Sound.Fall: return Join(Lowpass(Noise(0.12f, 0.3f, 1, 30), 0.25f, 0.05f), Mix(Thump(110, 40, 0.2f, 0.45f), Lowpass(Noise(0.12f, 0.35f, 1, 31), 0.12f, 0.03f)), Silence(0.04f), Thump(90, 45, 0.08f, 0.16f));
                case Sound.BossDown: return BossCrash();
                case Sound.DiceTumble: return Tumble();
                case Sound.DiceLand: return Join(Clack(1150, 0.32f, 32), Silence(0.06f), Clack(1300, 0.2f, 33), Silence(0.09f), Clack(1450, 0.1f, 34), Thump(160, 90, 0.05f, 0.12f));
                case Sound.Sure: return Mix(Join(Ring(1047, 0.07f, 0.16f, 5), Ring(1568, 0.5f, 0.18f, 4)), Join(Silence(0.07f), Ring(2349, 0.45f, 0.08f, 5)), Join(Silence(0.13f), Ring(3136, 0.3f, 0.06f, 6)));
                case Sound.Coin: return Join(Tone(988, 0.05f, 0.18f, Wave.Square), Mix(Ring(1319, 0.25f, 0.2f, 5), Tone(1319, 0.06f, 0.08f, Wave.Square)));
                case Sound.Equip: return Join(Mix(Noise(0.02f, 0.3f, 1, 35), Mix(Ring(1760, 0.16f, 0.14f, 6), Ring(2489, 0.16f, 0.1f, 7))), Mix(Thump(190, 120, 0.06f, 0.22f), Lowpass(Noise(0.05f, 0.3f, 1, 36), 0.2f, 0.06f)));
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

        // Noise from a fixed little generator (the same every time for the same seed), coarser for a higher `hold`.
        static float[] Noise(float seconds, float volume, int hold, uint seed = 0)
        {
            int count = Math.Max(1, (int)(Rate * seconds));
            var samples = new float[count];
            uint state = 0x9E3779B9 ^ (seed * 0x85EBCA6B);
            if (state == 0) state = 1;
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

        static float[] Silence(float seconds) => new float[Math.Max(1, (int)(Rate * seconds))];

        // A struck note that rings out: a triangle with a fast start and an exponential fade (`decay` per second
        // length), easing to zero at the end.
        static float[] Ring(double hz, float seconds, float volume, float decay)
        {
            int count = Math.Max(1, (int)(Rate * seconds));
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float attack = Math.Min(1f, i / (Rate * 0.002f));
                float release = Math.Min(1f, (count - i) / (Rate * 0.004f));
                float fade = (float)Math.Exp(-decay * i / (double)count);
                samples[i] = Shape(Wave.Triangle, hz * i / Rate) * volume * attack * release * fade;
            }
            return samples;
        }

        // A soft round body blow: a sine falling in pitch, fading fast.
        static float[] Thump(double fromHz, double toHz, float seconds, float volume)
        {
            int count = Math.Max(1, (int)(Rate * seconds));
            var samples = new float[count];
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)count;
                phase += (toHz + (fromHz - toHz) * Math.Exp(-5 * t)) / Rate;
                float attack = Math.Min(1f, i / (Rate * 0.002f));
                float release = Math.Min(1f, (count - i) / (Rate * 0.004f));
                samples[i] = (float)Math.Sin(2 * Math.PI * phase) * volume * attack * release * (float)Math.Exp(-4 * t);
            }
            return samples;
        }

        // A shaft quivering where it struck: a triangle whose pitch wobbles fast and settles.
        static float[] Twang(double hz, float seconds, float volume)
        {
            int count = Math.Max(1, (int)(Rate * seconds));
            var samples = new float[count];
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)count;
                phase += hz * (1 + 0.06 * Math.Sin(2 * Math.PI * 28 * i / Rate) * (1 - t)) / Rate;
                float release = Math.Min(1f, (count - i) / (Rate * 0.004f));
                samples[i] = Shape(Wave.Triangle, phase) * volume * release * (float)Math.Exp(-5 * t) * Math.Min(1f, i / (Rate * 0.002f));
            }
            return samples;
        }

        // A one-pole low-pass whose openness goes from `from` to `to` over the sound (0 dull, 1 open), and back to
        // `end` in the second half when given: a breath of air rising and falling.
        static float[] Lowpass(float[] input, float from, float to, float end = -1)
        {
            var samples = new float[input.Length];
            float y = 0;
            for (int i = 0; i < input.Length; i++)
            {
                float t = i / (float)Math.Max(1, input.Length - 1);
                float a = end < 0 ? from + (to - from) * t : t < 0.5f ? from + (to - from) * t * 2 : to + (end - to) * (t - 0.5f) * 2;
                y += (input[i] - y) * a;
                samples[i] = y;
            }
            // Filtering darkens and quiets the noise; bring it back up to the input's peak.
            float inPeak = 0, outPeak = 0;
            for (int i = 0; i < input.Length; i++)
            {
                inPeak = Math.Max(inPeak, Math.Abs(input[i]));
                outPeak = Math.Max(outPeak, Math.Abs(samples[i]));
            }
            if (outPeak > 0) for (int i = 0; i < samples.Length; i++) samples[i] *= inPeak / outPeak;
            return samples;
        }

        // Only the hiss: the input minus its low-passed self.
        static float[] Highpass(float[] input)
        {
            var samples = new float[input.Length];
            float y = 0;
            for (int i = 0; i < input.Length; i++)
            {
                y += (input[i] - y) * 0.2f;
                samples[i] = input[i] - y;
            }
            return samples;
        }

        // Pops in a fire: short noise bursts at fixed, uneven places.
        static float[] Crackle(float seconds, float volume, uint seed)
        {
            var samples = new float[Math.Max(1, (int)(Rate * seconds))];
            uint state = 0x2545F491 ^ seed;
            uint Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return state;
            }
            for (int p = 0; p < 9; p++)
            {
                int len = Rate / 500 + (int)(Next() % (uint)(Rate / 250));
                int at = (int)(Next() % (uint)Math.Max(1, samples.Length - len));
                float loud = volume * (0.4f + (Next() % 100) / 160f);
                for (int i = 0; i < len; i++) samples[at + i] += ((Next() & 0xFFFF) / 32768f - 1f) * loud * (float)Math.Exp(-5.0 * i / len);
            }
            return samples;
        }

        // One running step: a dull scuff with a low knock under it.
        static float[] Footstep(double hz, uint seed) => Mix(Lowpass(Noise(0.045f, 0.4f, 1, seed), 0.3f, 0.06f), Thump(hz, hz * 0.6, 0.05f, 0.2f));

        // A die hitting the table: a tick of noise and a short wooden knock.
        static float[] Clack(double hz, float volume, uint seed) => Mix(Noise(0.012f, volume, 1, seed), Ring(hz, 0.035f, volume * 0.6f, 6));

        // Dice tumbling over the table: knocks at uneven places, quickening then thinning out, over a low roll.
        static float[] Tumble()
        {
            float[] gaps = { 0.035f, 0.02f, 0.045f, 0.025f, 0.03f, 0.055f, 0.04f, 0.07f };
            var parts = new List<float[]>();
            for (int i = 0; i < gaps.Length; i++)
            {
                parts.Add(Clack(900 + (i * 7 % 5) * 110, 0.26f - i * 0.015f, 40 + (uint)i));
                parts.Add(Silence(gaps[i]));
            }
            var knocks = Join(parts.ToArray());
            return Mix(knocks, Lowpass(Noise(knocks.Length / (float)Rate, 0.18f, 1, 48), 0.04f, 0.06f, 0.02f));
        }

        // A boss falling: a deep boom sinking out of hearing, a crash of rubble, a last low groan of a square.
        static float[] BossCrash()
        {
            var boom = Thump(120, 28, 1.3f, 0.42f);
            var rubble = Lowpass(Noise(1.0f, 0.32f, 1, 50), 0.35f, 0.04f);
            var crack = Mix(Noise(0.08f, 0.3f, 1, 51), Crackle(0.5f, 0.15f, 52));
            var groan = Join(Silence(0.1f), Sweep(220, 55, 0.7f, 0.08f, Wave.Square));
            return Mix(Mix(boom, rubble), Mix(crack, groan));
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

        static float[] Mix(params float[][] parts)
        {
            int length = 0;
            foreach (var part in parts) length = Math.Max(length, part.Length);
            var samples = new float[length];
            for (int i = 0; i < samples.Length; i++)
            {
                float v = 0;
                foreach (var part in parts) if (i < part.Length) v += part[i];
                samples[i] = Math.Max(-1f, Math.Min(1f, v));
            }
            return samples;
        }
    }
}
