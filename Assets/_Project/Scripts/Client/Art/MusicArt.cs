using System;

namespace Game.Client
{
    public enum Tune
    {
        None,
        Title,
        Story,
        Forest,
        Harbor,
        Town,
        Battle,
        Boss,
    }

    // The background music, composed and synthesized in code like the sound effects (no audio files): each tune is
    // a short loop of chords with a soft pad, a bass and a picked melody drawn from a fixed seed, and drums for the
    // battles. Plain C# with no UnityEngine, so DevTools/SoundPreview writes the same samples to WAV files and checks
    // them without Unity. Music.cs plays them.
    public static class MusicArt
    {
        public const int Rate = SoundArt.Rate;

        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
        static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Phrygian = { 0, 1, 3, 5, 7, 8, 10 };

        sealed class Style
        {
            public int Bpm;
            public int Root;
            public int[] Scale;
            public int[] Chords;
            public int BeatsPerChord = 4;
            public float Pad = 0.16f;
            public float Bass = 0.2f;
            public float Lead = 0.16f;
            public float Drums;
            public bool SquareLead;
            public float Rest = 0.35f;
            public ulong Seed;
        }

        static Style StyleOf(Tune tune) => tune switch
        {
            Tune.Title => new Style { Bpm = 76, Root = 50, Scale = Dorian, Chords = new[] { 0, 5, 3, 4 }, Lead = 0.14f, Rest = 0.45f, Seed = 11 },
            Tune.Story => new Style { Bpm = 66, Root = 52, Scale = Minor, Chords = new[] { 0, 3, 5, 4 }, Lead = 0.1f, Bass = 0.14f, Rest = 0.6f, Seed = 23 },
            Tune.Forest => new Style { Bpm = 88, Root = 52, Scale = Dorian, Chords = new[] { 0, 6, 3, 4 }, Rest = 0.4f, Seed = 37 },
            Tune.Harbor => new Style { Bpm = 80, Root = 50, Scale = Minor, Chords = new[] { 0, 5, 2, 6 }, BeatsPerChord = 3, Rest = 0.4f, Seed = 41 },
            Tune.Town => new Style { Bpm = 100, Root = 55, Scale = Major, Chords = new[] { 0, 3, 4, 0 }, Lead = 0.15f, Rest = 0.3f, Seed = 53 },
            Tune.Battle => new Style { Bpm = 132, Root = 45, Scale = Minor, Chords = new[] { 0, 0, 5, 6 }, Drums = 0.32f, SquareLead = true, Lead = 0.11f, Bass = 0.24f, Rest = 0.25f, Seed = 67 },
            Tune.Boss => new Style { Bpm = 144, Root = 43, Scale = Phrygian, Chords = new[] { 0, 1, 0, 6 }, Drums = 0.38f, SquareLead = true, Lead = 0.11f, Bass = 0.26f, Rest = 0.2f, Seed = 79 },
            _ => null,
        };

        public static float[] Make(Tune tune)
        {
            var style = StyleOf(tune);
            if (style == null) return new float[1];
            int beats = style.Chords.Length * style.BeatsPerChord;
            int perBeat = Rate * 60 / style.Bpm;
            int length = beats * perBeat;
            var output = new float[length];
            var random = new Random((int)style.Seed);

            for (int c = 0; c < style.Chords.Length; c++)
            {
                int start = c * style.BeatsPerChord * perBeat;
                int span = style.BeatsPerChord * perBeat;
                int degree = style.Chords[c];
                // The pad: the chord's three notes, held, swelling in and out.
                for (int voice = 0; voice < 3; voice++)
                {
                    double freq = Frequency(Note(style, degree + voice * 2, 1));
                    Add(output, start, span, i => Triangle(freq, i) * Swell(i, span) * style.Pad / 3);
                }
                // The bass: the chord's root on every beat (every other beat when slow), plucked.
                double root = Frequency(Note(style, degree, -1));
                int bassEvery = style.Bpm >= 110 ? 1 : 2;
                for (int b = 0; b < style.BeatsPerChord; b += bassEvery)
                {
                    int at = start + b * perBeat;
                    int len = perBeat * bassEvery;
                    Add(output, at, len, i => (Triangle(root, i) * 0.7f + Square(root, i) * 0.3f) * Pluck(i, len, 5f) * style.Bass);
                }
                // The melody: eighth notes from the scale, mostly the chord's own notes, with rests.
                int eighth = perBeat / 2;
                int last = degree + 4;
                for (int e = 0; e < style.BeatsPerChord * 2; e++)
                {
                    if (random.NextDouble() < style.Rest) continue;
                    int step = random.Next(5) - 2;
                    int pick = random.NextDouble() < 0.6 ? degree + 2 * random.Next(3) + 7 : last + step;
                    pick = Math.Max(degree + 3, Math.Min(degree + 13, pick));
                    last = pick;
                    double freq = Frequency(Note(style, pick, 0));
                    int len = eighth * (random.NextDouble() < 0.25 ? 2 : 1);
                    int at = start + e * eighth;
                    if (style.SquareLead) Add(output, at, len, i => Square(freq, i) * Pluck(i, len, 4f) * style.Lead);
                    else Add(output, at, len, i => Triangle(freq, i) * Pluck(i, len, 3f) * style.Lead);
                }
            }

            // Drums: a kick on the beats, a soft hat on the off-beats, a snare on two and four.
            if (style.Drums > 0)
            {
                var noise = new Random((int)style.Seed + 1);
                for (int b = 0; b < beats; b++)
                {
                    int at = b * perBeat;
                    int kick = Rate / 8;
                    Add(output, at, kick, i => (float)Math.Sin(2 * Math.PI * (55 + 90 * Math.Exp(-i / (double)(Rate / 60))) * i / Rate) * Pluck(i, kick, 6f) * style.Drums);
                    int hat = Rate / 30;
                    Add(output, at + perBeat / 2, hat, i => (float)(noise.NextDouble() * 2 - 1) * Pluck(i, hat, 8f) * style.Drums * 0.25f);
                    if (b % 2 == 1)
                    {
                        int snare = Rate / 10;
                        Add(output, at, snare, i => (float)(noise.NextDouble() * 2 - 1) * Pluck(i, snare, 7f) * style.Drums * 0.45f);
                    }
                }
            }

            Normalize(output, 0.55f);
            return output;
        }

        // A scale degree (any number, octaves wrap) as a MIDI note, `octave` octaves from the root.
        static int Note(Style style, int degree, int octave)
        {
            int n = style.Scale.Length;
            int wrapped = ((degree % n) + n) % n;
            int octaves = (degree - wrapped) / n;
            return style.Root + style.Scale[wrapped] + 12 * (octaves + octave);
        }

        static double Frequency(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

        static float Triangle(double freq, int i)
        {
            double phase = freq * i / Rate % 1.0;
            return (float)(phase < 0.5 ? phase * 4 - 1 : 3 - phase * 4);
        }

        static float Square(double freq, int i) => freq * i / Rate % 1.0 < 0.5 ? 0.6f : -0.6f;

        // A quick start and an exponential fade over the note.
        static float Pluck(int i, int length, float decay)
        {
            float attack = Math.Min(1f, i / (Rate * 0.004f));
            return attack * (float)Math.Exp(-decay * i / (double)length) * Math.Min(1f, (length - i) / (Rate * 0.004f));
        }

        // A slow rise and fall over the whole chord.
        static float Swell(int i, int length)
        {
            float t = i / (float)length;
            return Math.Min(1f, t * 6f) * Math.Min(1f, (1 - t) * 6f);
        }

        static void Add(float[] output, int start, int length, Func<int, float> sample)
        {
            for (int i = 0; i < length; i++)
            {
                int at = start + i;
                if (at >= output.Length) at -= output.Length;
                output[at] += sample(i);
            }
        }

        static void Normalize(float[] samples, float peak)
        {
            float max = 0;
            foreach (float s in samples) max = Math.Max(max, Math.Abs(s));
            if (max <= 0) return;
            float gain = peak / max;
            for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
        }
    }
}
