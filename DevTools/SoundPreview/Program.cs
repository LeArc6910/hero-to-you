using System;
using System.IO;
using System.Linq;
using Game.Client;

// dotnet run --project DevTools/SoundPreview -- <folder>: one WAV per sound and per music tune, and a line each with
// its length and peak (and for music, its loudness and how long it took to make: the phone's web build makes tunes a
// few milliseconds a frame).
// Fails (exit 1) when a sound is silent, clips (reaches full scale, where SoundArt's mix clamps), has NaN, or starts or
// ends on a jump (a click); or when a looping tune does not join smoothly at its seam.
static class Program
{
    static int Main(string[] args)
    {
        string folder = args.Length > 0 ? args[0] : "sounds";
        Directory.CreateDirectory(folder);
        int bad = 0;
        foreach (Sound sound in Enum.GetValues(typeof(Sound)))
        {
            float[] samples = SoundArt.Make(sound);
            float peak = samples.Select(Math.Abs).DefaultIfEmpty(0).Max();
            bool nan = samples.Any(float.IsNaN);
            int clipped = samples.Count(s => Math.Abs(s) >= 0.999f);
            float edge = Math.Max(Math.Abs(samples[0]), Math.Abs(samples[samples.Length - 1]));
            bool ok = !nan && peak > 0.05f && peak <= 1f && clipped == 0 && edge < 0.05f;
            if (!ok) bad++;
            Console.WriteLine($"{sound,-11} {samples.Length / (double)SoundArt.Rate,5:0.00}s peak {peak:0.00} edge {edge:0.00}{(clipped > 0 ? $" clipped {clipped}" : "")} {(ok ? "" : "BAD")}");
            Write(Path.Combine(folder, sound.ToString().ToLowerInvariant() + ".wav"), samples);
        }
        foreach (Tune tune in Enum.GetValues(typeof(Tune)))
        {
            if (tune == Tune.None) continue;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float[] samples = MusicArt.Make(tune);
            double ms = clock.Elapsed.TotalMilliseconds;
            double rms = Math.Sqrt(samples.Average(s => (double)s * s));
            float peak = samples.Select(Math.Abs).DefaultIfEmpty(0).Max();
            bool nan = samples.Any(float.IsNaN);
            // A loop's seam should be no bigger a step than the tune's own steps; a stinger should end on silence.
            float largest = 0;
            for (int i = 1; i < samples.Length; i++) largest = Math.Max(largest, Math.Abs(samples[i] - samples[i - 1]));
            float seam = MusicArt.Loops(tune) ? Math.Abs(samples[0] - samples[samples.Length - 1]) : Math.Abs(samples[samples.Length - 1]);
            bool seamOk = MusicArt.Loops(tune) ? seam <= largest + 1e-6f : seam < 0.01f;
            bool ok = !nan && peak > 0.05f && peak <= 1f && seamOk;
            if (!ok) bad++;
            Console.WriteLine($"music {tune,-9} {samples.Length / (double)MusicArt.Rate,5:0.00}s peak {peak:0.00} {(MusicArt.Loops(tune) ? "seam" : "end")} {seam:0.000} rms {rms:0.00} made in {ms,4:0}ms {(ok ? "" : "BAD")}");
            Write(Path.Combine(folder, "music_" + tune.ToString().ToLowerInvariant() + ".wav"), samples);
        }
        foreach (Ambience ambience in Enum.GetValues(typeof(Ambience)))
        {
            if (ambience == Ambience.None) continue;
            float[] samples = MusicArt.MakeAmbience(ambience);
            float peak = samples.Select(Math.Abs).DefaultIfEmpty(0).Max();
            bool ok = !samples.Any(float.IsNaN) && peak > 0.05f && peak <= 1f;
            if (!ok) bad++;
            Console.WriteLine($"ambience {ambience,-7} {samples.Length / (double)MusicArt.Rate,5:0.00}s peak {peak:0.00} {(ok ? "" : "BAD")}");
            Write(Path.Combine(folder, "ambience_" + ambience.ToString().ToLowerInvariant() + ".wav"), samples);
        }
        return bad == 0 ? 0 : 1;
    }

    static void Write(string path, float[] samples)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = samples.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + bytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(SoundArt.Rate);
        w.Write(SoundArt.Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(bytes);
        foreach (float s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * 32767));
    }
}
