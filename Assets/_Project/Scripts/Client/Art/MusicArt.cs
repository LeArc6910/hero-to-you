using System;
using System.Collections.Generic;

namespace Game.Client
{
    // The tunes. The map plays the chapter's own theme, named by chapters.csv's `music` column (forest, harbor, snow,
    // tomb, rift). Victory and Defeat are short stingers that play once (MusicArt.Loops); the rest loop.
    public enum Tune
    {
        None,
        Title,
        Story,
        Forest,
        Harbor,
        Snow,
        Tomb,
        Rift,
        Town,
        Battle,
        Boss,
        BossFinal,
        // A calm theme by the fire: camps, dungeon rests, campfire talks, the interludes between chapters.
        Camp,
        // Stingers: a short fanfare when a battle is won, a falling phrase when it is lost. Once, no loop.
        Victory,
        Defeat,
    }

    // The sound of the place under the music (Music plays it on the map): wind through trees, waves, a snowstorm,
    // desert wind, the rift's hum, a crackling campfire.
    public enum Ambience
    {
        None,
        Forest,
        Harbor,
        Snow,
        Tomb,
        Rift,
        Camp,
    }

    // The background music, composed and synthesized in code like the sound effects (no audio files): each tune is
    // a short loop of chords with a pad, a bass and a melody, and drums for the battles. A chapter's theme opens with
    // a written phrase (its motif, which comes back halfway) and fills the rest from a fixed seed; each place has its
    // own voice: the forest a lute and a flute, the harbor a waltz on a reedy pulse, the snow bells with an echo, the
    // tomb hand drums over a drone, the rift a wavering pad. Plain C# with no UnityEngine, so DevTools/SoundPreview
    // writes the same samples to WAV files and checks them without Unity. Music.cs plays them.
    public static class MusicArt
    {
        public const int Rate = SoundArt.Rate;

        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
        static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Phrygian = { 0, 1, 3, 5, 7, 8, 10 };

        // In a motif: a rest, or the note before held one more eighth.
        const int R = -99;
        const int H = -98;

        enum Voice
        {
            Triangle,
            Square,
            Pulse,
            Bell,
        }

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
            public Voice Voice = Voice.Triangle;
            // A slight waver on the melody (a flute, an accordion).
            public bool Vibrato;
            // The melody again three eighths later, this loud.
            public float Echo;
            // The chord picked as eighth notes (a lute) as well as held.
            public bool Arp;
            // The bass on the first beat and the chord's notes on the others (three beats to a chord).
            public bool Waltz;
            // The pad doubled a little off pitch, wavering.
            public bool Detune;
            // Hand drums, this loud.
            public float Toms;
            // The root held under everything, this loud.
            public float Drone;
            // Eighth notes in scale degrees from the tonic an octave up (R rests, H holds), for the first chords and
            // again from MotifAt.
            public int[] Motif;
            public int MotifAt = -1;
        }

        static Style StyleOf(Tune tune) => tune switch
        {
            Tune.Title => new Style { Bpm = 76, Root = 50, Scale = Dorian, Chords = new[] { 0, 5, 3, 4 }, Lead = 0.14f, Rest = 0.45f, Seed = 11 },
            Tune.Story => new Style { Bpm = 66, Root = 52, Scale = Minor, Chords = new[] { 0, 3, 5, 4 }, Lead = 0.1f, Bass = 0.14f, Rest = 0.6f, Seed = 23 },
            // Chapter 1, the ash forest: a lute picks the chords, a flute sings a folk tune.
            Tune.Forest => new Style
            {
                Bpm = 88, Root = 52, Scale = Dorian, Chords = new[] { 0, 6, 3, 4, 0, 6, 2, 4 }, Pad = 0.09f, Bass = 0.17f, Lead = 0.15f,
                Rest = 0.45f, Seed = 37, Arp = true, Vibrato = true,
                Motif = new[] { 4, H, 3, 2, 1, H, R, R, 3, H, 1, H, -1, H, H, R }, MotifAt = 4,
            },
            // Chapter 2, the sunken harbor: a sea waltz on a reedy pulse.
            Tune.Harbor => new Style
            {
                Bpm = 92, Root = 50, Scale = Minor, Chords = new[] { 0, 5, 2, 6, 0, 3, 6, 4 }, BeatsPerChord = 3, Pad = 0.1f, Bass = 0.2f,
                Lead = 0.12f, Rest = 0.4f, Seed = 41, Voice = Voice.Pulse, Vibrato = true, Waltz = true,
                Motif = new[] { 0, H, 2, 4, H, 2, 5, H, 4, 2, H, R }, MotifAt = 4,
            },
            // Chapter 3, the snowed-in monastery: slow bells ringing into an echo.
            Tune.Snow => new Style
            {
                Bpm = 70, Root = 49, Scale = Minor, Chords = new[] { 0, 5, 3, 6, 0, 5, 2, 4 }, Pad = 0.17f, Lead = 0.15f, Bass = 0.13f,
                Rest = 0.55f, Seed = 97, Voice = Voice.Bell, Echo = 0.35f,
                Motif = new[] { 4, H, H, 2, 0, H, H, R, 5, H, H, 4, 2, H, H, R }, MotifAt = 4,
            },
            // Chapter 4, the red tomb: hand drums over a drone, a snaking reed.
            Tune.Tomb => new Style
            {
                Bpm = 84, Root = 50, Scale = Phrygian, Chords = new[] { 0, 1, 0, 5, 0, 1, 3, 4 }, Pad = 0.1f, Lead = 0.12f, Bass = 0.16f,
                Rest = 0.4f, Seed = 101, Voice = Voice.Pulse, Vibrato = true, Toms = 0.3f, Drone = 0.09f,
                Motif = new[] { 4, H, 3, 2, 1, 2, 1, 0, 1, H, H, R, 2, 1, 0, H }, MotifAt = 4,
            },
            // Chapter 5, the rift gate: a wavering pad and a drone, a lost melody echoing.
            Tune.Rift => new Style
            {
                Bpm = 72, Root = 47, Scale = Minor, Chords = new[] { 0, 5, 1, 4, 0, 5, 6, 4 }, Pad = 0.2f, Lead = 0.11f, Bass = 0.17f,
                Rest = 0.5f, Seed = 113, Detune = true, Echo = 0.3f, Drone = 0.07f,
                Motif = new[] { 0, H, H, 1, 2, H, H, R, 5, H, 4, H, 2, H, H, R }, MotifAt = 4,
            },
            Tune.BossFinal => new Style { Bpm = 150, Root = 45, Scale = Phrygian, Chords = new[] { 0, 1, 5, 6, 0, 1, 3, 4 }, BeatsPerChord = 2, Drums = 0.4f, SquareLead = true, Lead = 0.12f, Bass = 0.28f, Rest = 0.15f, Seed = 127 },
            Tune.Town => new Style { Bpm = 100, Root = 55, Scale = Major, Chords = new[] { 0, 3, 4, 0 }, Lead = 0.15f, Rest = 0.3f, Seed = 53 },
            Tune.Battle => new Style
            {
                Bpm = 132, Root = 45, Scale = Minor, Chords = new[] { 0, 0, 5, 6 }, Drums = 0.32f, SquareLead = true, Lead = 0.11f, Bass = 0.24f,
                Rest = 0.25f, Seed = 67, Motif = new[] { 0, 0, 4, 0, 5, 4, 3, 2, 0, 0, 4, 0, 7, H, 6, H },
            },
            Tune.Boss => new Style
            {
                Bpm = 144, Root = 43, Scale = Phrygian, Chords = new[] { 0, 1, 0, 6 }, Drums = 0.38f, SquareLead = true, Lead = 0.11f, Bass = 0.26f,
                Rest = 0.2f, Seed = 79, Motif = new[] { 0, H, 0, 1, 0, H, -2, H, 1, H, 1, 3, 5, H, 3, H },
            },
            // By the fire: a slow lute and a soft tune, nothing pressing.
            Tune.Camp => new Style
            {
                Bpm = 64, Root = 53, Scale = Major, Chords = new[] { 0, 5, 3, 4 }, Pad = 0.07f, Bass = 0.12f, Lead = 0.11f, Rest = 0.55f,
                Seed = 131, Arp = true, Echo = 0.2f, Motif = new[] { 4, H, 2, H, 0, H, H, R, 5, H, 4, H, 2, H, H, R },
            },
            _ => null,
        };

        // Whether the tune loops (the stingers play once).
        public static bool Loops(Tune tune) => tune != Tune.Victory && tune != Tune.Defeat;

        public static float[] Make(Tune tune)
        {
            float[] made = null;
            foreach (var step in MakeInSteps(tune)) made = step ?? made;
            return made ?? new float[1];
        }

        // The same tune a part at a time (each chord's pad voices, bass and melody, then the drums): null after each
        // part, the samples at the end. Music makes a tune over several frames this way so the screen never stops
        // while it is made (the web build has one thread).
        public static IEnumerable<float[]> MakeInSteps(Tune tune)
        {
            if (tune == Tune.Victory || tune == Tune.Defeat)
            {
                yield return null;
                yield return MakeStinger(tune == Tune.Victory);
                yield break;
            }
            var style = StyleOf(tune);
            if (style == null)
            {
                yield return new float[1];
                yield break;
            }
            int beats = style.Chords.Length * style.BeatsPerChord;
            int perBeat = Rate * 60 / style.Bpm;
            int length = beats * perBeat;
            int eighth = perBeat / 2;
            var output = new float[length];
            var random = new Random((int)style.Seed);
            var noise = new Random((int)style.Seed + 1);
            var written = Written(style);
            var lead = style.SquareLead ? Voice.Square : style.Voice;
            // A drone's pitch: the root, tuned to whole cycles over the loop so it joins without a click.
            double drone = Math.Max(1, Math.Round(Frequency(Note(style, 0, -1)) * length / Rate)) * Rate / length;

            for (int c = 0; c < style.Chords.Length; c++)
            {
                int start = c * style.BeatsPerChord * perBeat;
                int span = style.BeatsPerChord * perBeat;
                int degree = style.Chords[c];
                // The pad: the chord's three notes, held, swelling in and out (doubled off pitch when it wavers).
                float pad = style.Pad / 3 * (style.Detune ? 0.6f : 1f);
                for (int voice = 0; voice < 3; voice++)
                {
                    double freq = Frequency(Note(style, degree + voice * 2, 1));
                    if (style.Detune) Add(output, start, span, i => (Triangle(freq, i) + Triangle(freq * 1.007, i)) * Swell(i, span) * pad);
                    else Add(output, start, span, i => Triangle(freq, i) * Swell(i, span) * pad);
                    yield return null;
                }
                // A lute: the chord's notes picked up and down in eighth notes.
                if (style.Arp)
                {
                    int[] shape = { 0, 2, 4, 7, 4, 2 };
                    for (int e = 0; e < style.BeatsPerChord * 2; e++)
                    {
                        double freq = Frequency(Note(style, degree + shape[e % shape.Length], 0));
                        int len = eighth * 3 / 2;
                        Add(output, start + e * eighth, len, i => Triangle(freq, i) * Pluck(i, len, 4f) * style.Pad * 0.9f);
                    }
                }
                // The bass: the chord's root on every beat (every other beat when slow), plucked; in a waltz the root
                // on one and the chord's third and fifth, softer, on two and three.
                double root = Frequency(Note(style, degree, -1));
                if (style.Waltz)
                {
                    for (int b = 0; b < style.BeatsPerChord; b++)
                    {
                        int at = start + b * perBeat;
                        int len = perBeat;
                        if (b == 0) Add(output, at, len, i => (Triangle(root, i) * 0.7f + Square(root, i) * 0.3f) * Pluck(i, len, 5f) * style.Bass);
                        else
                        {
                            double third = Frequency(Note(style, degree + 2, 0));
                            double fifth = Frequency(Note(style, degree + 4, 0));
                            Add(output, at, len / 2, i => (Triangle(third, i) + Triangle(fifth, i)) * Pluck(i, len / 2, 5f) * style.Bass * 0.35f);
                        }
                    }
                }
                else
                {
                    int bassEvery = style.Bpm >= 110 ? 1 : 2;
                    for (int b = 0; b < style.BeatsPerChord; b += bassEvery)
                    {
                        int at = start + b * perBeat;
                        int len = perBeat * bassEvery;
                        Add(output, at, len, i => (Triangle(root, i) * 0.7f + Square(root, i) * 0.3f) * Pluck(i, len, 5f) * style.Bass);
                    }
                }
                yield return null;
                // The melody: the motif where it is written, elsewhere eighth notes from the scale, mostly the chord's
                // own notes, with rests.
                int last = degree + 4;
                for (int e = 0; e < style.BeatsPerChord * 2; e++)
                {
                    int g = c * style.BeatsPerChord * 2 + e;
                    int at = start + e * eighth;
                    if (written[g] != int.MinValue)
                    {
                        int pickWritten = written[g];
                        if (pickWritten == R || pickWritten == H) continue;
                        int holds = 1;
                        while (g + holds < written.Length && written[g + holds] == H) holds++;
                        last = pickWritten + 7;
                        AddLead(output, style, lead, Frequency(Note(style, pickWritten + 7, 0)), at, eighth * holds);
                        continue;
                    }
                    if (random.NextDouble() < style.Rest) continue;
                    int step = random.Next(5) - 2;
                    int pick = random.NextDouble() < 0.6 ? degree + 2 * random.Next(3) + 7 : last + step;
                    pick = Math.Max(degree + 3, Math.Min(degree + 13, pick));
                    last = pick;
                    int len = eighth * (random.NextDouble() < 0.25 ? 2 : 1);
                    AddLead(output, style, lead, Frequency(Note(style, pick, 0)), at, len);
                }
                // The drone under this chord (held over the whole loop, a chord at a time).
                if (style.Drone > 0) Add(output, start, span, i => (Triangle(drone, start + i) * 0.8f + Triangle(drone * 2, start + i) * 0.2f) * style.Drone);
                // Hand drums: a low and a high skin in a limping pattern.
                if (style.Toms > 0)
                {
                    int[] hits = { 0, 3, 4, 6 };
                    foreach (int h in hits)
                    {
                        if (h >= style.BeatsPerChord * 2) continue;
                        int len = Rate / 6;
                        double from = h == 0 || h == 4 ? 120 : 190;
                        float loud = style.Toms * (h == 0 ? 1f : 0.6f);
                        Add(output, start + h * eighth, len, i => (float)Math.Sin(2 * Math.PI * (from * 0.75 * i + from * 0.25 * Rate / 40.0 * (1 - Math.Exp(-i / (Rate / 40.0)))) / Rate) * Pluck(i, len, 7f) * loud);
                    }
                }
                yield return null;
            }

            // Drums: a kick on the beats, a soft hat on the off-beats, a snare on two and four.
            if (style.Drums > 0)
            {
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
            yield return output;
        }

        // The motif laid on the tune's eighth notes (int.MinValue where the melody is free).
        static int[] Written(Style style)
        {
            int eighths = style.Chords.Length * style.BeatsPerChord * 2;
            var written = new int[eighths];
            for (int i = 0; i < eighths; i++) written[i] = int.MinValue;
            if (style.Motif == null) return written;
            void Lay(int from)
            {
                for (int i = 0; i < style.Motif.Length && from + i < eighths; i++) written[from + i] = style.Motif[i];
            }
            Lay(0);
            if (style.MotifAt > 0) Lay(style.MotifAt * style.BeatsPerChord * 2);
            return written;
        }

        static void AddLead(float[] output, Style style, Voice voice, double freq, int at, int len)
        {
            Func<int, float> note = voice switch
            {
                Voice.Square => i => Square(freq, i) * Pluck(i, len, 4f) * style.Lead,
                Voice.Pulse => i => Pulse(Phase(freq, i, style.Vibrato)) * Pluck(i, len, 3f) * style.Lead,
                // A bell: the note and a quieter octave, ringing out fast.
                Voice.Bell => i => (TriangleAt(Phase(freq, i, false)) * 0.75f + TriangleAt(Phase(freq * 2, i, false)) * 0.25f) * Pluck(i, len, 5f) * style.Lead,
                _ => i => TriangleAt(Phase(freq, i, style.Vibrato)) * Pluck(i, len, 3f) * style.Lead,
            };
            Add(output, at, len, note);
            if (style.Echo > 0)
            {
                int delay = Rate * 60 / style.Bpm * 3 / 2;
                Add(output, at + delay, len, i => note(i) * style.Echo);
            }
        }

        // ---- The stingers: written out in full, a few seconds, once ----

        // Victory: a rising fanfare on a square, chords under it, a drum roll into the last held chord.
        // Defeat: a slow falling phrase on a soft triangle over sinking chords.
        static float[] MakeStinger(bool victory)
        {
            int bpm = victory ? 160 : 80;
            int eighth = Rate * 60 / bpm / 2;
            // (MIDI note, eighths)
            int[,] lead = victory
                ? new[,] { { 76, 1 }, { 79, 1 }, { 84, 2 }, { 83, 1 }, { 84, 1 }, { 86, 1 }, { 88, 5 } }
                : new[,] { { 69, 2 }, { 65, 2 }, { 64, 1 }, { 62, 4 } };
            // (root, third, fifth, eighths); the bass plays the root an octave down.
            int[,] chords = victory
                ? new[,] { { 60, 64, 67, 4 }, { 55, 59, 62, 3 }, { 60, 64, 67, 5 } }
                : new[,] { { 62, 65, 69, 2 }, { 58, 62, 65, 2 }, { 57, 61, 64, 1 }, { 50, 53, 57, 4 } };
            int eighths = 0;
            for (int n = 0; n < lead.GetLength(0); n++) eighths += lead[n, 1];
            int tail = Rate * 6 / 10;
            var output = new float[eighths * eighth + tail];

            int at = 0;
            for (int n = 0; n < lead.GetLength(0); n++)
            {
                double freq = Frequency(lead[n, 0]);
                bool lastNote = n == lead.GetLength(0) - 1;
                int len = lead[n, 1] * eighth + (lastNote ? tail : 0);
                float decay = lastNote ? 2.5f : 3f;
                if (victory) AddOnce(output, at, len, i => Square(freq, i) * Pluck(i, len, decay) * 0.16f);
                else AddOnce(output, at, len, i => TriangleAt(Phase(freq, i, true)) * Pluck(i, len, decay) * 0.24f);
                at += lead[n, 1] * eighth;
            }
            at = 0;
            for (int c = 0; c < chords.GetLength(0); c++)
            {
                bool lastChord = c == chords.GetLength(0) - 1;
                int len = chords[c, 3] * eighth + (lastChord ? tail : 0);
                for (int v = 0; v < 3; v++)
                {
                    double freq = Frequency(chords[c, v]);
                    AddOnce(output, at, len, i => Triangle(freq, i) * Pluck(i, len, 1.5f) * 0.07f);
                }
                double bass = Frequency(chords[c, 0] - 12);
                AddOnce(output, at, len, i => (Triangle(bass, i) * 0.7f + Square(bass, i) * 0.3f) * Pluck(i, len, 2.5f) * 0.2f);
                at += chords[c, 3] * eighth;
            }
            if (victory)
            {
                // A snare roll under the climb, then a kick and a crash on the last chord.
                var noise = new Random(7);
                int roll = Rate / 20;
                int lastAt = (eighths - lead[lead.GetLength(0) - 1, 1]) * eighth;
                for (int r = 0; r * eighth / 2 < lastAt; r++)
                {
                    float loud = 0.06f + 0.1f * r * eighth / 2 / lastAt;
                    AddOnce(output, r * eighth / 2, roll, i => (float)(noise.NextDouble() * 2 - 1) * Pluck(i, roll, 7f) * loud);
                }
                int kick = Rate / 6;
                AddOnce(output, lastAt, kick, i => (float)Math.Sin(2 * Math.PI * (55 + 90 * Math.Exp(-i / (double)(Rate / 60))) * i / Rate) * Pluck(i, kick, 5f) * 0.4f);
                int crash = Rate * 7 / 10;
                AddOnce(output, lastAt, crash, i => (float)(noise.NextDouble() * 2 - 1) * Pluck(i, crash, 6f) * 0.1f);
            }
            Normalize(output, 0.55f);
            return output;
        }

        // A few seconds of the place's sound, looping: filtered noise shaped by slow swells (and for the harbor, waves;
        // for the camp, a low fire with crackles).
        public static float[] MakeAmbience(Ambience ambience)
        {
            if (ambience == Ambience.None) return new float[1];
            int length = Rate * 8;
            var output = new float[length];
            var random = new Random(41 + (int)ambience);
            float smooth = ambience switch { Ambience.Harbor => 0.02f, Ambience.Snow => 0.08f, Ambience.Rift => 0.01f, Ambience.Tomb => 0.05f, Ambience.Camp => 0.015f, _ => 0.04f };
            double swell = ambience switch { Ambience.Harbor => 0.18, Ambience.Snow => 0.35, Ambience.Rift => 0.07, Ambience.Camp => 0.12, _ => 0.25 };
            float last = 0;
            for (int i = 0; i < length; i++)
            {
                float noise = (float)(random.NextDouble() * 2 - 1);
                last += (noise - last) * smooth;
                // Whole cycles over the loop so it joins without a seam.
                double phase = 2 * Math.PI * i / length;
                float gain = (float)(0.55 + 0.45 * Math.Sin(phase * Math.Round(swell * 8)));
                float hum = ambience == Ambience.Rift ? (float)Math.Sin(2 * Math.PI * 55 * i / Rate) * 0.25f : 0;
                output[i] = last * gain + hum * gain;
            }
            if (ambience == Ambience.Camp)
            {
                // Crackles: short bright pops of noise, a few a second, some louder (kept off the loop's ends).
                int pops = 8 * 7;
                for (int p = 0; p < pops; p++)
                {
                    int at = Rate / 5 + random.Next(length - Rate / 2);
                    int len = Rate / 400 + random.Next(Rate / 150);
                    float loud = 0.15f + (float)random.NextDouble() * (random.Next(4) == 0 ? 0.5f : 0.2f);
                    for (int i = 0; i < len; i++) output[at + i] += (float)(random.NextDouble() * 2 - 1) * loud * (float)Math.Exp(-5.0 * i / len);
                }
            }
            // Fade the very ends together.
            int fade = Rate / 10;
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                output[i] *= t;
                output[length - 1 - i] *= t;
            }
            Normalize(output, 0.35f);
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

        // Cycles played by sample i; with vibrato the pitch wavers about a tenth of a semitone, five and a half times
        // a second.
        static double Phase(double freq, int i, bool vibrato)
        {
            double t = i / (double)Rate;
            if (!vibrato) return freq * t;
            const double w = 2 * Math.PI * 5.5;
            return freq * (t + 0.006 * (1 - Math.Cos(w * t)) / w);
        }

        static float TriangleAt(double phase)
        {
            phase -= Math.Floor(phase);
            return (float)(phase < 0.5 ? phase * 4 - 1 : 3 - phase * 4);
        }

        static float Triangle(double freq, int i) => TriangleAt(freq * i / Rate);

        static float Square(double freq, int i) => freq * i / Rate % 1.0 < 0.5 ? 0.6f : -0.6f;

        // A quarter-wide pulse: thinner and reedier than the square.
        static float Pulse(double phase) => phase - Math.Floor(phase) < 0.25 ? 0.6f : -0.2f;

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

        // Adds a sound into the loop, wrapping past the end to the start (so tails ring into the loop's beginning).
        static void Add(float[] output, int start, int length, Func<int, float> sample)
        {
            for (int i = 0; i < length; i++)
            {
                int at = (start + i) % output.Length;
                output[at] += sample(i);
            }
        }

        // Adds a sound that stops at the end (the stingers do not loop).
        static void AddOnce(float[] output, int start, int length, Func<int, float> sample)
        {
            for (int i = 0; i < length && start + i < output.Length; i++) output[start + i] += sample(i);
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
