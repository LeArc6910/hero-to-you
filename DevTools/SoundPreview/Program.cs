using System;
using System.IO;
using System.Linq;
using Game.Client;

// dotnet run --project DevTools/SoundPreview -- <folder>: one WAV per sound, and a line each with its length and peak.
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
