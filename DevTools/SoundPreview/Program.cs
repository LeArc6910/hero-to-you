using System;
using System.IO;
using System.Linq;
using Game.Client;

// dotnet run --project DevTools/SoundPreview -- <folder>: one WAV per sound and per music tune, and a line each with
// its length and peak.
// Fails (exit 1) when a sound is silent, clips or has NaN.
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
            bool ok = !nan && peak > 0.05f && peak <= 1f;
            if (!ok) bad++;
            Console.WriteLine($"{sound,-8} {samples.Length / (double)SoundArt.Rate,5:0.00}s peak {peak:0.00} {(ok ? "" : "BAD")}");
            Write(Path.Combine(folder, sound.ToString().ToLowerInvariant() + ".wav"), samples);
        }
        foreach (Tune tune in Enum.GetValues(typeof(Tune)))
        {
            if (tune == Tune.None) continue;
            float[] samples = MusicArt.Make(tune);
            float peak = samples.Select(Math.Abs).DefaultIfEmpty(0).Max();
            bool nan = samples.Any(float.IsNaN);
            bool ok = !nan && peak > 0.05f && peak <= 1f;
            if (!ok) bad++;
            Console.WriteLine($"music {tune,-7} {samples.Length / (double)MusicArt.Rate,5:0.00}s peak {peak:0.00} {(ok ? "" : "BAD")}");
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
