using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // The game's pixel art, drawn in code (no image files to import; user decision 2026-11-10: pixel art): each picture
    // is rows of letters, one letter a point, read through the fixed palette (GamePalette, Resources/Data/Ui/palette.csv).
    // Heroes face right, monsters face left (summons that stand with the heroes face right). 'c', 'd' and 'C' are the
    // class's (or monster's) own colour and one step darker or lighter on its palette ramp. Heroes and monsters are
    // 32×32, chapter bosses 48×48, icons 24×24. DevTools/ArtPreview/preview.py draws these same rows into a PNG to look
    // at without Unity. Textures are made once and sampled to the nearest point, so they stay crisp at any size.
    static class PixelArt
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        // Pictures are asked for many times a frame (every hex, every unit): found by (kind, name, color) without
        // building a key string or a closure each time; made once.
        static readonly Dictionary<(char, string, string), Texture2D> Found = new Dictionary<(char, string, string), Texture2D>();

        static bool Known(char kind, string name, string color, out Texture2D texture) =>
            Found.TryGetValue((kind, name, color), out texture) && texture != null;

        static Texture2D Keep(char kind, string name, string color, Texture2D texture)
        {
            Found[(kind, name, color)] = texture;
            return texture;
        }

        // What a figure is doing: standing, the instant of its blow, the recoil of being struck, the two steps of a walk
        // on the map, lying fallen.
        public enum Pose
        {
            Stand,
            Attack,
            Hit,
            Walk1,
            Walk2,
            Down,
        }

        static string Suffix(Pose pose) => pose switch
        {
            Pose.Attack => "_attack",
            Pose.Hit => "_hit",
            Pose.Walk1 => "_walk1",
            Pose.Walk2 => "_walk2",
            Pose.Down => "_down",
            _ => "",
        };

        static char Kind(Pose pose) => pose switch
        {
            Pose.Attack => 'a',
            Pose.Hit => 'b',
            Pose.Walk1 => 'w',
            Pose.Walk2 => 'x',
            Pose.Down => 'd',
            _ => 'h',
        };

        // Whether the class has its own drawn picture for the pose (a fallen hero without one is drawn lying low).
        public static bool HasPose(string classId, Pose pose) => pose == Pose.Stand || Heroes.ContainsKey(classId + Suffix(pose));

        // A hero in a pose: the class's own drawn picture ("knight_attack", "knight_walk1", "knight_down") where there
        // is one; otherwise the standing picture leaned forward (attack) or back and down (hit), or just standing.
        public static Texture2D Hero(string classId, string color, Pose pose = Pose.Stand)
        {
            char kind = Kind(pose);
            if (Known(kind, classId, color, out var known)) return known;
            var stand = Heroes.TryGetValue(classId, out var rows) ? rows : Wanderer;
            if (pose != Pose.Stand && !Heroes.TryGetValue(classId + Suffix(pose), out rows))
                rows = pose == Pose.Attack || pose == Pose.Hit ? Lean(stand, pose, true) : stand;
            return Keep(kind, classId, color, Make($"hero:{classId}:{kind}", rows ?? stand, color));
        }

        // A monster in a pose: its own drawn attack/hit picture ("goblin_attack") where there is one, otherwise the
        // standing picture leaned (monsters face left; `facingRight` for the summons on the heroes' side).
        public static Texture2D Monster(string monsterId, string color, Pose pose = Pose.Stand, bool facingRight = false)
        {
            if (pose != Pose.Attack && pose != Pose.Hit) pose = Pose.Stand;
            char kind = pose == Pose.Stand ? 'm' : pose == Pose.Attack ? (facingRight ? 'r' : 'n') : (facingRight ? 's' : 'o');
            if (Known(kind, monsterId, color, out var known)) return known;
            var stand = Monsters.TryGetValue(monsterId, out var rows) ? rows : Monsters.TryGetValue("goblin", out var goblin) ? goblin : Wanderer;
            var posed = pose == Pose.Stand ? stand : Monsters.TryGetValue(monsterId + Suffix(pose), out var drawn) ? drawn : Lean(stand, pose, facingRight);
            return Keep(kind, monsterId, color, Make($"monster:{monsterId}:{kind}", posed, color));
        }

        // The picture's rows sheared: the feet stay, the head goes up to two points toward the facing side for a blow,
        // or away from it for a hit, which also sinks the picture one row. Points pushed past the edge are dropped.
        static string[] Lean(string[] rows, Pose pose, bool facingRight)
        {
            int height = rows.Length, width = rows[0].Length;
            int most = Mathf.Max(1, width / 16);
            int toward = (pose == Pose.Attack ? 1 : -1) * (facingRight ? 1 : -1) * most;
            int sink = pose == Pose.Hit ? 1 : 0;
            var leaned = new string[height];
            var line = new char[width];
            for (int y = 0; y < height; y++)
            {
                int from = y - sink;
                for (int x = 0; x < width; x++) line[x] = '.';
                if (from >= 0)
                {
                    int shift = Mathf.RoundToInt(toward * (float)(height - 1 - from) / (height - 1));
                    for (int x = 0; x < width; x++)
                    {
                        int to = x + shift;
                        if (to >= 0 && to < width) line[to] = rows[from][x];
                    }
                }
                leaned[y] = new string(line);
            }
            return leaned;
        }

        // Every colour comes from the palette: `c` is the class or monster colour, `d` one step darker on its ramp, `C`
        // one step lighter; any other letter is the palette's own (magenta shows a letter the palette does not have).
        public static Texture2D Make(string key, string[] rows, string color)
        {
            key += color;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            int height = rows.Length;
            int width = rows[0].Length;
            var main = GamePalette.Snap(Palette.Hex(color ?? "#888888"));
            var shade = GamePalette.Darker(main);
            var light = GamePalette.Lighter(main);
            var missing = new Color32(255, 0, 255, 255);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                string row = rows[height - 1 - y];
                for (int x = 0; x < width; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    Color32 pixel;
                    if (c == '.') pixel = default;
                    else if (c == 'c') pixel = main;
                    else if (c == 'd') pixel = shade;
                    else if (c == 'C') pixel = light;
                    else pixel = GamePalette.Letter(c, out var letter) ? letter : missing;
                    pixels[y * width + x] = pixel;
                }
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return texture;
        }

        // ---- Skies: a gradient in a few bands, an ordered dither where two bands meet, drawn big ----

        static readonly Dictionary<(int, int, int), Texture2D> Skies = new Dictionary<(int, int, int), Texture2D>();
        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        static int Pack(Color c) => ((int)(c.r * 255) << 16) | ((int)(c.g * 255) << 8) | (int)(c.b * 255);

        // `aspect` is the drawn width over height, so the points stay square. Made once per colours and width.
        public static Texture2D Sky(Color top, Color bottom, float aspect)
        {
            const int height = 24;
            const int steps = 6;
            int width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 8, 192);
            var key = (Pack(top), Pack(bottom), width);
            if (Skies.TryGetValue(key, out var made) && made != null) return made;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1) * (steps - 1);
                int band = Mathf.Min((int)t, steps - 2);
                float frac = t - band;
                var a = GamePalette.Snap(Color.Lerp(top, bottom, band / (float)(steps - 1)));
                var b = GamePalette.Snap(Color.Lerp(top, bottom, (band + 1) / (float)(steps - 1)));
                for (int x = 0; x < width; x++)
                    pixels[(height - 1 - y) * width + x] = frac * 16 > Bayer[(y & 3) * 4 + (x & 3)] + 0.5f ? b : a;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Skies[key] = texture;
            return texture;
        }

        // ---- The pictures: data files in Resources/Data/Art (heroes, monsters, icons, ui) ----

        // Each file holds pictures as '!name' followed by its rows of letters ('#' lines are notes). Read once, the first
        // time a picture of the file is wanted; adding a picture needs no code.
        static Dictionary<string, string[]> _heroes, _monsters, _icons, _ui, _scenes, _effects;

        public static Dictionary<string, string[]> Heroes => _heroes ??= Read("heroes");
        public static Dictionary<string, string[]> Monsters => _monsters ??= Read("monsters");
        public static Dictionary<string, string[]> MapIcons => _icons ??= Read("icons");
        static Dictionary<string, string[]> UiIcons => _ui ??= Read("ui");
        static Dictionary<string, string[]> Scenes => _scenes ??= Read("scenes");
        static Dictionary<string, string[]> Effects => _effects ??= Read("effects");

        // A wide picture (scenes.txt: 256×96 places for story pages, events and battles; the 256×144 title), or null.
        public static Texture2D Picture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Known('p', name, null, out var known)) return known;
            return Scenes.TryGetValue(name, out var rows) ? Keep('p', name, null, Make("scene:" + name, rows, null)) : null;
        }

        // The colour most of a picture's top (or bottom) row is, to carry the sky up or the ground down past its edge.
        public static Color32 PictureEdge(string name, bool top)
        {
            if (!Scenes.TryGetValue(name, out var rows) || rows.Length == 0) return GamePalette.Ink;
            string row = top ? rows[0] : rows[rows.Length - 1];
            var counts = new Dictionary<char, int>();
            foreach (char c in row) counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
            char most = '.';
            foreach (var pair in counts)
                if (most == '.' || pair.Value > counts[most]) most = pair.Key;
            return GamePalette.Letter(most, out var color) ? color : GamePalette.Ink;
        }

        // The two colours most of a picture's rows `from`..`to` are (its ground, for the battle's ground plane).
        public static (Color32 most, Color32 next) PictureColors(string name, int from, int to)
        {
            if (!Scenes.TryGetValue(name, out var rows) || rows.Length == 0) return (GamePalette.Ink, GamePalette.Ink);
            var counts = new Dictionary<char, int>();
            for (int y = Mathf.Max(0, from); y < Mathf.Min(rows.Length, to); y++)
                foreach (char c in rows[y])
                    if (c != '.') counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
            char first = '.', second = '.';
            foreach (var pair in counts)
            {
                if (first == '.' || pair.Value > counts[first])
                {
                    second = first;
                    first = pair.Key;
                }
                else if (second == '.' || pair.Value > counts[second]) second = pair.Key;
            }
            var most = GamePalette.Letter(first, out var a) ? a : GamePalette.Ink;
            var next = GamePalette.Letter(second, out var b) ? b : GamePalette.Darker(most);
            return (most, next);
        }

        // An oval lying on the ground (a shadow, or a ring under the unit whose turn it is), white to be tinted.
        public static Texture2D Oval(bool ring)
        {
            if (Known('o', ring ? "ring" : "fill", null, out var known)) return known;
            const int w = 24, h = 8;
            var rows = new string[h];
            for (int y = 0; y < h; y++)
            {
                var row = new char[w];
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f - w / 2f) / (w / 2f), dy = (y + 0.5f - h / 2f) / (h / 2f);
                    float d = dx * dx + dy * dy;
                    row[x] = d <= 1 && (!ring || d >= 0.55f) ? 'W' : '.';
                }
                rows[y] = new string(row);
            }
            return Keep('o', ring ? "ring" : "fill", null, MakeWhite(rows));
        }

        static Texture2D MakeWhite(string[] rows)
        {
            int width = rows[0].Length, height = rows.Length;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[(height - 1 - y) * width + x] = rows[y][x] == '.' ? new Color32(0, 0, 0, 0) : new Color32(255, 255, 255, 255);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // A single picture of effects.txt (a projectile, a die), or null.
        public static Texture2D Still(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Known('f', name, "", out var known)) return known;
            return Effects.TryGetValue(name, out var rows) ? Keep('f', name, "", Make("effect:" + name, rows, null)) : null;
        }

        static readonly string[] Frames = { "_1", "_2", "_3" };

        // One frame (0-2) of a hit effect (effects.txt: slash, pierce, impact, spell, fire, frost, heal), or null.
        public static Texture2D Effect(string name, int frame)
        {
            frame = Mathf.Clamp(frame, 0, Frames.Length - 1);
            if (Known('f', name, Frames[frame], out var known)) return known;
            return Effects.TryGetValue(name + Frames[frame], out var rows) ? Keep('f', name, Frames[frame], Make("effect:" + name + Frames[frame], rows, null)) : null;
        }

        static string[] Wanderer => Heroes.TryGetValue("wanderer", out var rows) ? rows : new[] { "." };

        static Dictionary<string, string[]> Read(string file)
        {
            var pictures = new Dictionary<string, string[]>();
            var asset = Resources.Load<TextAsset>("Data/Art/" + file);
            if (asset == null) return pictures;
            string name = null;
            var rows = new List<string>();
            foreach (string raw in asset.text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] == '!')
                {
                    if (name != null && rows.Count > 0) pictures[name] = rows.ToArray();
                    name = line.Substring(1).Trim();
                    rows.Clear();
                    continue;
                }
                rows.Add(line);
            }
            if (name != null && rows.Count > 0) pictures[name] = rows.ToArray();
            return pictures;
        }

        public static Texture2D Icon(string name)
        {
            if (Known('i', name, null, out var known)) return known;
            var made = UiIcons.TryGetValue(name, out var small) ? Make("icon:" + name, small, null)
                : MapIcons.TryGetValue(name, out var rows) ? Make("map:" + name, rows, null) : null;
            return made == null ? null : Keep('i', name, null, made);
        }

        // ---- Map ground: a hexagon filled with its land's pattern, in the land's palette ramp ----

        static readonly Dictionary<(string, int, int), Texture2D> Grounds = new Dictionary<(string, int, int), Texture2D>();

        // `pattern`: grass, forest, swamp, road, ruins, water, shallows, sand, snow, rift, mountain (anything else is
        // plain). Three variants of each, so neighbouring hexes do not repeat. Made once per pattern, colour, variant.
        public static Texture2D Ground(string pattern, Color color, int variant)
        {
            var key = (pattern, Pack(color), variant);
            if (Grounds.TryGetValue(key, out var made) && made != null) return made;
            const int width = 26;
            const int height = 30;
            var baseColor = GamePalette.Snap(color);
            var dark = GamePalette.Darker(baseColor);
            var darker = GamePalette.Darker(dark);
            var light = GamePalette.Lighter(baseColor);
            var ember = GamePalette.Letter('e', out var e) ? e : light;
            var grid = new Color32[width * height];
            for (int i = 0; i < grid.Length; i++) grid[i] = baseColor;
            // A small fixed hash per point: the same picture on every device.
            uint seed = (uint)(pattern.Length * 131 + variant * 977 + 7);
            int Noise(int x, int y)
            {
                uint h = (uint)(x * 374761393 + y * 668265263) ^ seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (int)((h ^ (h >> 16)) % 100);
            }
            void Put(int x, int y, Color32 c)
            {
                if (x >= 0 && y >= 0 && x < width && y < height) grid[y * width + x] = c;
            }
            // y grows downward here (row 0 is the top); flipped when written.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int n = Noise(x, y);
                    switch (pattern)
                    {
                        case "grass":
                            if (n < 4) { Put(x, y, light); Put(x - 1, y + 1, light); Put(x + 1, y + 1, light); }
                            else if (n < 7) Put(x, y, dark);
                            break;
                        case "forest":
                            if (n < 12) Put(x, y, dark);
                            else if (n < 15) Put(x, y, darker);
                            else if (n < 17) Put(x, y, light);
                            break;
                        case "swamp":
                            if (n < 4) for (int i = 0; i < 4; i++) Put(x + i, y, dark);
                            else if (n < 6) { Put(x, y, light); Put(x, y - 1, light); }
                            break;
                        case "road":
                            // A packed dirt road: pebbles and short dark ruts.
                            if (n < 5) Put(x, y, light);
                            else if (n < 8) { Put(x, y, dark); Put(x + 1, y, dark); Put(x + 2, y, dark); }
                            else if (n < 11) Put(x, y, dark);
                            break;
                        case "ruins":
                            if (n < 3) { int cx = x, cy = y; for (int i = 0; i < 5; i++) { Put(cx, cy, darker); cx += Noise(cx, cy + i) < 50 ? 1 : 0; cy++; } }
                            else if (n < 7) Put(x, y, light);
                            else if (n < 11) Put(x, y, dark);
                            break;
                        case "water":
                        case "shallows":
                            // Short wave crests in rows, light above a dark trough.
                            if ((y + variant * 2) % 6 == 0 && (x + y * 3 + variant * 5) % 9 < 3) { Put(x, y, light); Put(x, y + 1, dark); }
                            else if (pattern == "shallows" && n < 3) Put(x, y, light);
                            break;
                        case "sand":
                            if ((x + y * 2 + variant * 3) % 9 == 0 && n < 70) Put(x, y, light);
                            else if (n < 3) Put(x, y, dark);
                            break;
                        case "snow":
                            if (n < 4) Put(x, y, light);
                            else if (n < 7 && x + y > width) Put(x, y, dark);
                            break;
                        case "rift":
                            if (n < 3) { int cx = x, cy = y; for (int i = 0; i < 6; i++) { Put(cx, cy, darker); cx += Noise(cx + i, cy) < 50 ? 1 : -1; cy++; } }
                            else if (n < 5) Put(x, y, ember);
                            break;
                        case "mountain":
                            if (n < 10) Put(x, y, dark);
                            else if (n < 16) Put(x, y, light);
                            break;
                    }
                }
            var pixels = new Color32[width * height];
            float cxm = (width - 1) / 2f, cym = (height - 1) / 2f, r = height / 2f - 0.4f;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float dx = Mathf.Abs(x - cxm), dy = Mathf.Abs(y - cym);
                    bool inside = dx <= r * 0.866f && dy <= r - dx * 0.577f;
                    pixels[(height - 1 - y) * width + x] = inside ? grid[y * width + x] : default;
                }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Grounds[key] = texture;
            return texture;
        }

        // A pointy-top hexagon, white, to tint for a map hex; or just its rim.
        public static Texture2D Hex(bool rim)
        {
            string key = rim ? "hex:rim" : "hex:fill";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            const int width = 26;
            const int height = 30;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[width * height];
            float cx = (width - 1) / 2f;
            float cy = (height - 1) / 2f;
            float r = height / 2f;
            bool Inside(float x, float y, float shrink)
            {
                float dx = Mathf.Abs(x - cx);
                float dy = Mathf.Abs(y - cy);
                float rr = r - shrink;
                return dx <= rr * 0.866f && dy <= rr - dx * 0.577f;
            }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    bool inside = Inside(x, y, 0.4f);
                    bool core = Inside(x, y, 2.4f);
                    bool on = rim ? inside && !core : inside;
                    pixels[y * width + x] = on ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return texture;
        }
    }
}
