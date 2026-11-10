using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    // The one palette the world is drawn in (user decision 2026-11-10: pixel art, and all colours from a fixed
    // palette so classes, monsters and places look like one world): Resources/Data/Ui/palette.csv, colours in ramps
    // from dark to light, each with the letter the pictures use (PixelArt's letter rows). Shading goes one step along
    // a colour's own ramp instead of mixing toward black or white. The screen's interface colours (Palette) are apart.
    static class GamePalette
    {
        const string Resource = "Data/Ui/palette";

        static List<List<Color32>> _ramps;
        static readonly List<(Color32 color, int ramp, int step)> All = new List<(Color32, int, int)>();
        static readonly Dictionary<char, Color32> Letters = new Dictionary<char, Color32>();
        static readonly Dictionary<int, int> Index = new Dictionary<int, int>();
        static readonly Dictionary<int, Color32> Snapped = new Dictionary<int, Color32>();

        static void Load()
        {
            if (_ramps != null) return;
            _ramps = new List<List<Color32>>();
            var asset = Resources.Load<TextAsset>(Resource);
            if (asset == null) return;
            var rampOf = new Dictionary<string, int>();
            foreach (var row in CsvTable.Read(asset.text).Rows)
            {
                if (!ColorUtility.TryParseHtmlString(row["hex"], out var parsed)) continue;
                Color32 color = parsed;
                if (!rampOf.TryGetValue(row["ramp"], out int ramp))
                {
                    ramp = _ramps.Count;
                    rampOf[row["ramp"]] = ramp;
                    _ramps.Add(new List<Color32>());
                }
                Index[Key(color)] = All.Count;
                All.Add((color, ramp, _ramps[ramp].Count));
                _ramps[ramp].Add(color);
                string letter = row["char"];
                if (letter.Length == 1) Letters[letter[0]] = color;
            }
        }

        static int Key(Color32 c) => (c.r << 16) | (c.g << 8) | c.b;

        public static bool Letter(char letter, out Color32 color)
        {
            Load();
            return Letters.TryGetValue(letter, out color);
        }

        public static Color32 Ink => Letter('k', out var ink) ? ink : new Color32(27, 22, 34, 255);

        // The palette colour nearest to any colour (a weighted RGB distance that follows the eye fairly well).
        public static Color32 Snap(Color32 c)
        {
            Load();
            int key = Key(c);
            if (Index.ContainsKey(key)) return new Color32(c.r, c.g, c.b, 255);
            if (Snapped.TryGetValue(key, out var known)) return known;
            var best = c;
            long bestDistance = long.MaxValue;
            foreach (var (color, _, _) in All)
            {
                int rm = (c.r + color.r) / 2;
                int dr = c.r - color.r, dg = c.g - color.g, db = c.b - color.b;
                long distance = (512 + rm) * dr * dr + 1024 * dg * dg + (767 - rm) * db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = color;
                }
            }
            Snapped[key] = best;
            return best;
        }

        public static Color32 Snap(Color c) => Snap((Color32)c);

        // One step along the colour's own ramp (the colour is snapped first); the ramp's end stays where it is.
        public static Color32 Step(Color32 c, int by)
        {
            var snapped = Snap(c);
            if (!Index.TryGetValue(Key(snapped), out int i)) return snapped;
            var (_, ramp, step) = All[i];
            var colors = _ramps[ramp];
            return colors[Mathf.Clamp(step + by, 0, colors.Count - 1)];
        }

        public static Color32 Darker(Color32 c) => Step(c, -1);

        public static Color32 Lighter(Color32 c) => Step(c, 1);
    }
}
