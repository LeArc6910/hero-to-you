using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // The prototype's pixel art, drawn in code (no image files to import): each picture is rows of letters, one
    // letter a pixel, read through a palette. Heroes face right, monsters face left. 'c' and 'd' are the class's (or
    // monster's) own color and its shade. DevTools/ArtPreview/preview.py draws these same rows into a PNG to look at
    // without Unity. Textures are made once and sampled to the nearest pixel, so they stay crisp at any size.
    static class PixelArt
    {
        // The letters of the small pictures drawn before the fixed palette (16×16 heroes and monsters, 12×12 icons),
        // with the colours they were drawn in; each is snapped to the palette (GamePalette) when the picture is made.
        // Pictures drawn at full size (32 rows or more) use the palette's own letters (Resources/Data/Ui/palette.csv).
        static readonly Dictionary<char, string> Legacy = new Dictionary<char, string>
        {
            ['k'] = "#1B1820",
            ['s'] = "#E8B48A",
            ['S'] = "#B9825E",
            ['h'] = "#4A3424",
            ['H'] = "#C9A15A",
            ['m'] = "#C9CED6",
            ['M'] = "#7D8591",
            ['w'] = "#7A5232",
            ['W'] = "#F4F1EA",
            ['g'] = "#E8C15A",
            ['r'] = "#C8473C",
            ['o'] = "#E8873A",
            ['b'] = "#5C8FD6",
            ['G'] = "#6E8F3A",
            ['l'] = "#9DBA5A",
            ['y'] = "#E6E2B0",
            ['p'] = "#8A6BBE",
            ['a'] = "#9A8F86",
            ['A'] = "#655D57",
            ['e'] = "#FF7A3A",
            ['B'] = "#2E4F7A",
            ['n'] = "#4F8A8A",
            ['N'] = "#D8C08A",
        };

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

        public static Texture2D Hero(string classId, string color)
        {
            if (Known('h', classId, color, out var known)) return known;
            return Keep('h', classId, color, Make("hero:" + classId, Heroes.TryGetValue(classId, out var rows) ? rows : Wanderer, color, true));
        }

        public static Texture2D Monster(string monsterId, string color)
        {
            if (Known('m', monsterId, color, out var known)) return known;
            return Keep('m', monsterId, color, Make("monster:" + monsterId, Monsters.TryGetValue(monsterId, out var rows) ? rows : Monsters["goblin"], color, true));
        }

        // `finish`: small heroes and monsters (under 32 rows) are drawn at twice the size of their letter rows with the
        // finishing pass below; pictures drawn at full size are shaded by hand and kept as they are. Every colour comes
        // from the palette: `c` is the class or monster colour, `d` one step darker on its ramp, `C` one step lighter.
        public static Texture2D Make(string key, string[] rows, string color, bool finish = false)
        {
            key += color + (finish ? "+" : "");
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            int height = rows.Length;
            int width = rows[0].Length;
            bool full = height >= 32;
            finish &= !full;
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
                    else if (full) pixel = GamePalette.Letter(c, out var letter) ? letter : missing;
                    else pixel = Legacy.TryGetValue(c, out string hex) ? GamePalette.Snap(Palette.Hex(hex)) : missing;
                    pixels[y * width + x] = pixel;
                }
            }
            if (finish)
            {
                pixels = Finish(pixels, width, height);
                width *= 2;
                height *= 2;
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

        // ---- The finishing pass (user decision 2026-11-10: the art stays pixel art, drawn in code) ----

        // The letter rows at twice the size, then: the dark outline thinned to one point (half what it was), and in
        // each coloured part a lit edge toward the top left and a shaded edge toward the bottom right, one step along
        // the colour's palette ramp, so the shapes read with volume (three tones from two). Thin parts lit on both sides keep their colour. Shapes stay exactly
        // as drawn (no smoothing: small crosses and blades would melt). Pixels run bottom-up, as Texture2D keeps them.
        static Color32[] Finish(Color32[] small, int width, int height)
        {
            int w = width * 2, h = height * 2;
            var big = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    big[y * w + x] = small[y / 2 * width + x / 2];
            var ink = GamePalette.Ink;
            bool Ink(Color32 c) => c.a > 0 && c.r == ink.r && c.g == ink.g && c.b == ink.b;
            bool Clear(Color32[] p, int x, int y) => x < 0 || y < 0 || x >= w || y >= h || p[y * w + x].a == 0;
            bool InkAt(Color32[] p, int x, int y) => x >= 0 && y >= 0 && x < w && y < h && Ink(p[y * w + x]);
            bool TouchesClear(Color32[] p, int x, int y) => Clear(p, x - 1, y) || Clear(p, x + 1, y) || Clear(p, x, y - 1) || Clear(p, x, y + 1);

            // The outer half of the outline: an ink point on the outside whose inward neighbour is ink too.
            var thin = (Color32[])big.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Ink(big[y * w + x]) || !TouchesClear(big, x, y)) continue;
                    if ((InkAt(big, x - 1, y) && !TouchesClear(big, x - 1, y)) || (InkAt(big, x + 1, y) && !TouchesClear(big, x + 1, y))
                        || (InkAt(big, x, y - 1) && !TouchesClear(big, x, y - 1)) || (InkAt(big, x, y + 1) && !TouchesClear(big, x, y + 1)))
                        thin[y * w + x] = default;
                }
            // A corner the thinning left standing alone (both its ink neighbours went): it goes too.
            var clean = (Color32[])thin.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Ink(thin[y * w + x]) || !Clear(thin, x - 1, y) || !Clear(thin, x + 1, y) || !Clear(thin, x, y - 1) || !Clear(thin, x, y + 1)) continue;
                    if ((InkAt(big, x - 1, y) && Clear(thin, x - 1, y)) || (InkAt(big, x + 1, y) && Clear(thin, x + 1, y))
                        || (InkAt(big, x, y - 1) && Clear(thin, x, y - 1)) || (InkAt(big, x, y + 1) && Clear(thin, x, y + 1)))
                        clean[y * w + x] = default;
                }
            // Light from the top left (y + 1 is up here), shade toward the bottom right.
            bool Edge(int x, int y) => Clear(clean, x, y) || InkAt(clean, x, y);
            var done = (Color32[])clean.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = clean[y * w + x];
                    if (c.a == 0 || Ink(c)) continue;
                    bool lit = Edge(x, y + 1) || Edge(x - 1, y);
                    bool dark = Edge(x, y - 1) || Edge(x + 1, y);
                    if (lit && !dark) done[y * w + x] = GamePalette.Lighter(c);
                    else if (dark && !lit) done[y * w + x] = GamePalette.Darker(c);
                }
            return done;
        }

        // ---- Heroes (16×16, facing right) ----

        static readonly string[] Wanderer =
        {
            "................",
            "......kkkk......",
            ".....kddddk.....",
            "....kddddddk....",
            "....kdsssssk....",
            "....kssksksk....",
            ".....ksssssk....",
            "....kkcccckk....",
            "...kccccccccw...",
            "...kscccccckw...",
            "...kskccccksw...",
            "....kkccccksw...",
            ".....kcddck.....",
            ".....kckkck.....",
            ".....kwk.kwk....",
            ".....kkk.kkk....",
        };

        public static readonly Dictionary<string, string[]> Heroes = new Dictionary<string, string[]>
        {
            ["knight"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kmmmmk.....",
                "....kmmmmmmk....",
                "....kmkkkkmk....",
                "....kmmmmmmk..k.",
                ".....kkmmkk..kmk",
                "....kkcccckk.kmk",
                "...kwkcccccckkmk",
                "..kwwwkccccsk.mk",
                "..kwgwkccdcskkgk",
                "..kwwwkccccck.k.",
                "...kwkkcddck....",
                ".....kckkck.....",
                ".....kMk.kMk....",
                ".....kkk.kkk....",
            },
            ["berserker"] = new[]
            {
                ".............kk.",
                "......kkkk..kmmk",
                ".....khhhhk.kmmk",
                "....khhhhhhkkwmk",
                "....khsssssk.wk.",
                "....kssksksk.wk.",
                "....kshhhhsk.wk.",
                "...kkskhhkskkwk.",
                "..ksssccccsssk..",
                "..kssccccccssk..",
                "...kkccddcck....",
                "....kccccccsk...",
                ".....kwwwwk.....",
                ".....kckkck.....",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["hunter"] = new[]
            {
                "................",
                "......kkkk...k..",
                ".....kcccck..kw.",
                "....kccccdck.k.w",
                "....kcssssk..k.w",
                "....kssksksk.k.w",
                ".....ksssssk.k.w",
                "....kkcccckk.k.w",
                "...kccccccckkkkw",
                "...kscccccsssk.w",
                "...kskwwwwk..k.w",
                "....kkccccc..k.w",
                ".....kcddck..kw.",
                ".....kckkck..k..",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["elementalist"] = new[]
            {
                ".......k........",
                "......kck.......",
                ".....kccck....e.",
                "....kccccck..eoe",
                "...kkddddddk..e.",
                "....kssksksk..w.",
                ".....ksssssk..w.",
                "....kkcccckk..w.",
                "...kccccccccksw.",
                "...kccccccccck..",
                "...kccccccccck..",
                "...kccccccccck..",
                "...kddddddddk...",
                "....kccccccck...",
                "....kddddddk....",
                ".....kkkkkk.....",
            },
            ["cleric"] = new[]
            {
                ".....kkkkkk.....",
                ".....kgggggk....",
                "......kkkkk.....",
                ".....kWWWWWk....",
                "....kWssssWk....",
                "....kWsksksk....",
                ".....ksssssk..g.",
                "....kkWWWWkk.ggg",
                "...kWWWggWWWk.g.",
                "...kWWWggWWWskw.",
                "...kWccccccck.w.",
                "...kWccccccck.w.",
                "...kWWWWWWWWk.w.",
                "....kccccccck...",
                "....kWWWWWWk....",
                ".....kkkkkk.....",
            },
            ["gambler"] = new[]
            {
                "................",
                ".....kkkkkk.....",
                ".....kcccck.....",
                "...kkkkkkkkkk...",
                "....khssssk.....",
                "....kssksksk....",
                ".....ksssssk....",
                "....kkWccWkk.kk.",
                "...kccWrrWcckWWk",
                "...kccWrrWcskrWk",
                "...kccWWWWcckWWk",
                "....kkccccck.kk.",
                ".....kcddck.....",
                ".....kckkck.....",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["rogue"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kddddk.....",
                "....kddddddk....",
                "....kdkssskk....",
                "....kdsksksk....",
                ".....kdssssk....",
                "....kkddddkk....",
                "...kccccccck....",
                "...kscccccsk.k..",
                "...ksk.cckskmk..",
                "....kkcccckkm...",
                ".....kcddck.....",
                ".....kckkck.....",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["bard"] = new[]
            {
                "...........r....",
                "......kkkkrk....",
                ".....kcccccr....",
                "....kkkkkkkkk...",
                "....khssssk.....",
                "....kssksksk....",
                ".....ksssssk....",
                "....kkccccck....",
                "...kccccccck.ww.",
                "...ksccccckwwkw.",
                "...kskccckwwww..",
                "....kkcccckww...",
                ".....kcddck.....",
                ".....kckkck.....",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["alchemist"] = new[]
            {
                "................",
                "......kkkk......",
                ".....khhhhk.....",
                "....khhhhhhk....",
                "....kbkbbkbk....",
                "....kssssssk....",
                ".....ksssssk....",
                "....kkWWWWkk..k.",
                "...kccWccWcckkWk",
                "...kscWWWWcskWlk",
                "...kskccccck.kk.",
                "....kkccccck....",
                ".....kcddck.....",
                ".....kckkck.....",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["necromancer"] = new[]
            {
                "..............W.",
                "......kkkk...WkW",
                ".....kddddk...W.",
                "....kddddddk..w.",
                "....kdkkkkdk..w.",
                "....kdeekeek..w.",
                ".....kdkkkkk..w.",
                "....kkddddkk..w.",
                "...kddccccddksw.",
                "...kdccccccdk.w.",
                "...kdccccccdk...",
                "...kdccccccdk...",
                "...kddddddddk...",
                "....kdddddddk...",
                "....kddddddk....",
                ".....kkkkkk.....",
            },
            ["seer"] = new[]
            {
                "......kk........",
                ".....kcck.......",
                "....kcyccck.....",
                "...kccccccck....",
                "..kkdddddddkk...",
                "....kssksksk....",
                ".....ksssssk....",
                "....kkcccckk.kk.",
                "...kccccccccbbbk",
                "...kccccccccbWbk",
                "...kcyccccccbbbk",
                "...kccccccccckk.",
                "...kddddddddk...",
                "....kccccccck...",
                "....kddddddk....",
                ".....kkkkkk.....",
            },
            ["beastmaster"] = new[]
            {
                "..............m.",
                "......kkkk...kmk",
                ".....kaaaak...w.",
                "....kaAaaAak..w.",
                "....kassssk...w.",
                "....kssksksk..w.",
                ".....ksssssk..w.",
                "....kkaaaakk..w.",
                "...kccccccccksw.",
                "...kscccccccsw..",
                "...kskwwwwk..w..",
                "....kkccccck.w..",
                ".....kcddck.....",
                ".....kckkck.....",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["monk"] = new[]
            {
                "................",
                "................",
                "......kkkk......",
                ".....kssssk.....",
                ".....kskksk.....",
                ".....kssssk.....",
                "......kssk......",
                "....kkccccckk...",
                "...kscccccccsk..",
                "..kssk.cc.kssk..",
                "..kk..kcck..kk..",
                "......kddk......",
                ".....kcddck.....",
                ".....kc..ck.....",
                ".....ks..sk.....",
                ".....kk..kk.....",
            },
            ["engineer"] = new[]
            {
                "................",
                "......kkkk......",
                ".....khhhhk.....",
                "....kgkkkkgk....",
                ".....kssssk.....",
                ".....kskssk.....",
                "......kssk......",
                "....kkccwckk....",
                "...kccwwwcccmmm.",
                "...kcckwkccmMMk.",
                "...ksk.cc.kmkk..",
                "......kddk......",
                ".....kcddck.....",
                ".....kck.kck....",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["mnemonist"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kddddk.....",
                "....kddddddk....",
                "....kdsssssk....",
                "....kssbsbsk....",
                ".....ksssssk....",
                "....kkcccckk....",
                "...kccccccccWW..",
                "...kscccccckWyW.",
                "...kskccccksWWk.",
                "....kkccccck....",
                ".....kcddck.....",
                ".....kcccck.....",
                ".....kdkkdk.....",
                ".....kkk.kkk....",
            },
        };

        // ---- Monsters (facing left) ----

        public static readonly Dictionary<string, string[]> Monsters = new Dictionary<string, string[]>
        {
            ["goblin"] = new[]
            {
                "................",
                "................",
                "................",
                ".kk......kk.....",
                ".kGk.kkkkGk.....",
                "..kGkGGGGkk.....",
                "...kGkeGeGk.....",
                "...kGGGGGGk.....",
                "....kGkkGk......",
                "..m.kkccckk.....",
                ".kmkGcccccGk....",
                "..wkkGcccGkk....",
                "..w..kccck......",
                ".....kGkGk......",
                ".....kGkGk......",
                ".....kkkkk......",
            },
            ["goblin_archer"] = new[]
            {
                "................",
                "................",
                "...w............",
                "..w.k....kk.....",
                ".w..kkkkkGk.....",
                ".w..kGGGGkk.....",
                ".w.kGkeGeGk.....",
                ".wkkGGGGGGk.....",
                ".w..kGkkGk......",
                ".wkkGcccckk.....",
                ".w..kcccccGk....",
                ".w...kcccGkk....",
                "..w..kccck......",
                "...w.kGkGk......",
                ".....kGkGk......",
                ".....kkkkk......",
            },
            ["wolf"] = new[]
            {
                "................",
                "................",
                "................",
                "................",
                "..k.k...........",
                ".kakak..........",
                "kaeaaak.........",
                "kaaaaaakkkkkkk..",
                ".kkkaaaaaaaaaak.",
                "...kaaaaaaaaaaak",
                "...kaAaaaaaaak.k",
                "...kaAkkkkkaak..",
                "...kak.....kak..",
                "...kak.....kak..",
                "...kk......kk...",
                "................",
            },
            ["bandit"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kddddk.....",
                "....kdddddk.....",
                "....kkkssdk.....",
                "....kkskssk.....",
                "....kddddddk....",
                "..m.kccccccck...",
                ".kmkccccccccck..",
                ".kmkscccccccsk..",
                ".kmkskwwwwkksk..",
                "..kgkccccccck...",
                "....kcdddck.....",
                "....kckkck......",
                "...kwk.kwk......",
                "...kkk.kkk......",
            },
            ["cutthroat"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kddddk.....",
                "....kdddddk.....",
                "....krrrrrk.....",
                "....kskskdk.....",
                ".....kddddk.....",
                "....kccccck.....",
                "..mkcccccccck...",
                ".mkscccccccsk...",
                "..kkkcwwwckksk..",
                "....kccccck.mk..",
                ".....kcdck...m..",
                ".....kckck......",
                "....kwkkwk......",
                "....kkk.kk......",
            },
            ["skeleton"] = new[]
            {
                "................",
                "................",
                "......kkkk......",
                ".....kWWWWk.....",
                ".....kWkWkk.....",
                ".....kWWWWk.....",
                "......kWkk......",
                "..m..kkWkkk.....",
                ".kmk.kWkWkWk....",
                "..kmkWkWkWkWk...",
                "...kk.kWWWk.....",
                "......kWkWk.....",
                "......kWkWk.....",
                ".....kWk.kWk....",
                ".....kkk.kkk....",
                "................",
            },
            ["companion_wolf"] = new[]
            {
                "................",
                "................",
                "................",
                "................",
                "...........k.k..",
                "..........kckck.",
                ".........kccccek",
                "..kkkkkkkcccccck",
                ".kcccccccccckkk.",
                "kcccccccccccck..",
                "k.kcccccccdcck..",
                "..kcckkkkkdck...",
                "..kck.....kck...",
                "..kck.....kck...",
                "...kk......kk...",
                "................",
            },
            ["ash_wisp"] = new[]
            {
                "................",
                "......k.........",
                ".....kok....k...",
                "....kooyk..kok..",
                "...kooyyok.kk...",
                "...koyyyyok.....",
                "..kooykyykok....",
                "..koyyyyyyok....",
                "..kooyyyyook....",
                "...kooyyook.....",
                "....kooook......",
                ".....kook..k....",
                "......kk..kok...",
                "...k.......k....",
                "..kok...........",
                "...k............",
            },
            ["bog_lurker"] = new[]
            {
                "................",
                "................",
                ".....kkkkkk.....",
                "...kkcccccckk...",
                "..kcccccccccck..",
                ".kcckeckkecccck.",
                ".kccccccccccdck.",
                "kccdkkkkkkcccddk",
                "kcck.W.W.kccddck",
                "kccckkkkkcccdcck",
                ".kdccccccccdcck.",
                "kccdcccdcccccdck",
                "kckdccdcccdcckck",
                "kk.kccdccdcck.kk",
                "...kkkkkkkkkk...",
                "................",
            },
            ["ash_warden"] = new[]
            {
                "........................",
                ".........kkkkkk.........",
                "........kaaaaaak........",
                ".......kaaAaaAaak.......",
                ".......kaeekkeeak.......",
                ".......kaaaaaaaak.......",
                "........kaAAAAak........",
                "....kkkkkkaaaakkkkkk....",
                "...kaaaaAaaaaaaaAaaak...",
                "..kaaAaakaaeeaakaaAaak..",
                "..kaaaak.kaaeak.kaaaak..",
                "..kaAak..kaaaak..kaAak..",
                "..kaaak..kaAaak..kaaak..",
                "..kAAAk..kaaaak..kAAAk..",
                ".kaaaaak.kaaaak.kaaaaak.",
                ".kAAAAAk.kaAaak.kAAAAAk.",
                "..kkkkk..kaaaak..kkkkk..",
                ".........kaaaak.........",
                "........kaakkaak........",
                ".......kaaak.kaaak......",
                ".......kaaak.kaaak......",
                "......kAAAAk.kAAAAk.....",
                "......kkkkkk.kkkkkk.....",
                "........................",
            },
            ["bandit_chief"] = new[]
            {
                "................",
                ".mm.............",
                "mMMm..kkkkk.....",
                "mMMm.kMMMMMk....",
                ".mmw.kMssssk....",
                "...w.ksksssk....",
                "...w.kssssk.....",
                "...wkkhhhhkk....",
                "...wkcccccccck..",
                "...kscccccccsk..",
                "...kskcddcckSk..",
                "....kkcccccck...",
                ".....kcdddck....",
                ".....kckkkck....",
                ".....khk.khk....",
                ".....kkk.kkk....",
            },
            ["drowned"] = new[]
            {
                "................",
                "................",
                "......kkkk......",
                ".....kccddk.....",
                ".....kkcckk.....",
                ".....kccccdk....",
                "......kcdck.....",
                "....kkccccckk...",
                "...kcckcccckck..",
                "..kcck.kccck.kb.",
                "..kck..kcdck..b.",
                "..b....kccck....",
                ".......kcdck..b.",
                "......kck.kck...",
                "......kck.kck...",
                "......kkk.kkk...",
            },
            ["smuggler"] = new[]
            {
                "................",
                "................",
                "......kkkkk.....",
                ".....krrrrrk....",
                ".....kssssk.....",
                ".....kskssk.....",
                "......ksssk.....",
                ".o..kkcccckk....",
                "kyk.kcccccccw...",
                "kgk.kscccccsw...",
                ".k...kcdcdck....",
                ".....kcccck.....",
                ".....kdddddk....",
                ".....kck.kck....",
                ".....khk.khk....",
                ".....kkk.kkk....",
            },
            ["crab"] = new[]
            {
                "................",
                "................",
                "................",
                "..kk........kk..",
                ".kcck......kcck.",
                ".kcdk......kdck.",
                "..kck.kkkk.kck..",
                "...kkkcccckkk...",
                "...kcWkcckWck...",
                "..kcccccccccck..",
                ".kcccdccccdccck.",
                ".kdcccccccccdk..",
                "..kkdddddddkk...",
                "..k.k.k..k.k.k..",
                ".k..k..k.k..k.k.",
                "................",
            },
            ["gull"] = new[]
            {
                "................",
                "................",
                "................",
                "kk..........kk..",
                ".kck......kck...",
                "..kcck..kcck....",
                "...kcckkcck.....",
                "....kccccck.....",
                "..okkcWcck......",
                "...kcccccck.....",
                "....kdcccdk.....",
                ".....kkkkk......",
                "......o.o.......",
                "................",
                "................",
                "................",
            },
            ["tide_captain"] = new[]
            {
                "................",
                "...kkkkkkkkk....",
                "..kkhhhhhhhkk...",
                "....khhhhhk.....",
                ".....kssssk.....",
                ".....kskssk.....",
                "......ksssk.....",
                "....kkcccckk....",
                ".mm.kcccgccck...",
                "m..mkccgcccck...",
                "m...kcccgccck...",
                ".mm.kkccccckk...",
                ".....kddddk.....",
                ".....kck.kck....",
                ".....kwk.kwk....",
                ".....kkk.kkk....",
            },
            ["bell_ringer"] = new[]
            {
                "........................",
                "..............kkkk......",
                ".............kccddk.....",
                "............kccccddk....",
                "............kckkcddk....",
                "............kcbbcdk.....",
                ".............kccddk.....",
                "...kkkkk....kkccccdkk...",
                "..kgggggk..kcccccccddk..",
                ".kgggWgggk.kcccbcccdddk.",
                ".kggggggggkkcccbccccddk.",
                "kggggggggggkkccbcccccdk.",
                "kgAgggggAggk.kcbccccddk.",
                "kggggggggggk.kccbcccddk.",
                "kkkkkkkkkkkk.kcccbcccdk.",
                ".....kgk.....kccccbccdk.",
                ".....kgk....kcccccbccdk.",
                ".....kkk....kccccccbcdk.",
                "...........kcccccccccddk",
                "...........kccccccccdddk",
                "..........kccccccccdddk.",
                "..........kkkkkkkkkkkkk.",
                "........................",
                "........................",
            },
            ["frost_wolf"] = new[]
            {
                "................",
                "................",
                "................",
                "..W.............",
                ".kWk......kk....",
                "kbWWk....kcck...",
                "kWWWkkkkkccck...",
                ".kkWccccccccck..",
                "...kccccccccWk..",
                "...kcWccccWcck..",
                "....kcck.kcck...",
                "....kck...kck...",
                "....kWk...kWk...",
                "....kkk...kkk...",
                "................",
                "................",
            },
            ["ice_wraith"] = new[]
            {
                "................",
                ".....kkkkk......",
                "....kcccccck....",
                "...kcbkcbkcck...",
                "...kcccccccck...",
                "...kcckkkkcck...",
                "....kcccccck....",
                "...kcccccccck...",
                "..kcWccccccWck..",
                "..kccccccccccck.",
                "...kcccccccck...",
                "....kcc.kccck...",
                ".....kc..kcck...",
                "......k...kck...",
                "...........k....",
                "................",
            },
            ["snow_troll"] = new[]
            {
                "................",
                "....kkkkkk......",
                "...kWWWWWWk.....",
                "..kcckcckcck....",
                "..kcccccccck....",
                "..kcWkkkkWck....",
                ".kkcccccccckk...",
                "kcccccccccccck..",
                "kcWcccccccccWck.",
                "kcckcccccccckck.",
                "kck.kcccccck.kk.",
                ".k..kcccccck....",
                "....kcck.kcck...",
                "....kcck.kcck...",
                "...kkkkk.kkkkk..",
                "................",
            },
            ["cultist"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kccddk.....",
                "....kccddddk....",
                "....kckeekdk....",
                "....kcddddck....",
                ".....kccdck.....",
                "...kkcccccckk...",
                "..kccccrccccck..",
                "..kcckcrcckcck..",
                "..ksk.kcck.ksk..",
                "......kccck.....",
                ".....kcccdck....",
                ".....kccccdk....",
                ".....kkkkkkk....",
                "................",
            },
            ["rift_priest"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kccddk.....",
                "....kcckkcdk....",
                "....kckeekdk....",
                "....kcddddck....",
                ".mm..kccdck.....",
                "m..mkcccccckk...",
                "m..kccccrcccck..",
                ".mmkccrrrrrcck..",
                "...ksccccrccsk..",
                ".....kcccccck...",
                "....kccccccdk...",
                "....kcccccdddk..",
                "....kkkkkkkkkk..",
                "................",
            },
            ["frozen_abbess"] = new[]
            {
                "........................",
                "..........kkkk..........",
                ".........kWWWWk.........",
                "........kWccccWk........",
                "........kcbkkbck........",
                "........kcccccck........",
                "........kWcddcWk........",
                ".......kkWWWWWWkk.......",
                ".....kkcccWWWWcccckk....",
                "....kccccccWWcccccck....",
                "...kcccbccccccccbccck...",
                "..kWcccccccWWccccccWck..",
                "..kcccccccWWWWcccccck...",
                "..kWccccccccccccccccWk..",
                "...kcccccbccccbcccccck..",
                "...kcccccccccccccccck...",
                "..kccccccccccccccccccck.",
                "..kWcccccccccccccccccWk.",
                ".kcccccccbccccbccccccck.",
                ".kccccccccccccccccccccck",
                ".kWccccccccccccccccccWk.",
                ".kkkkkkkkkkkkkkkkkkkkkk.",
                "........................",
                "........................",
            },
            ["turret"] = new[]
            {
                "................",
                "................",
                "................",
                "........mm......",
                "..mmmmmmMMk.....",
                "..kkkkkkMMk.....",
                "......kccck.....",
                ".....kccccck....",
                "....kcWccWcck...",
                "....kccccccck...",
                "...kkkkkkkkkkk..",
                "...k.k.k.k.k.k..",
                "...kkkkkkkkkkk..",
                "................",
                "................",
                "................",
            },
            ["tomb_guard"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kaaaak.....",
                ".....kaeeak.....",
                ".....kaaaak.....",
                ".m...kkaakk.....",
                ".m..kccccccck...",
                ".mkkcccaacccck..",
                ".m.kcckaakcck...",
                ".m.kak.cc.kak...",
                ".m.....cc.......",
                ".....kccccck....",
                ".....kcc.cck....",
                ".....kaa.aak....",
                ".....kkk.kkk....",
                "................",
            },
            ["sand_serpent"] = new[]
            {
                "................",
                "................",
                "..kkkk..........",
                ".kccekk.........",
                ".kcccck.........",
                "rr.kcck.........",
                "....kcck........",
                ".....kcck.......",
                "......kcck..kk..",
                ".......kcckkcck.",
                "........kcccccck",
                ".........kcccck.",
                "..........kkkk..",
                "................",
                "................",
                "................",
            },
            ["order_knight"] = new[]
            {
                "................",
                ".....kkkkk......",
                "....kmmmmmk.....",
                "....kmkkkmk.....",
                "....kmmmmmk.....",
                ".....kmmmk......",
                "m..kkccccckk....",
                "mm.kcccrccccck..",
                ".mmkccrrrccck...",
                "..mkscccrccsk...",
                "....kkccccck....",
                ".....kcdddck....",
                ".....kck.kck....",
                ".....kmk.kmk....",
                ".....kkk.kkk....",
                "................",
            },
            ["red_shade"] = new[]
            {
                "................",
                ".....kkkkk......",
                "....kcccccck....",
                "...kcekcekcck...",
                "...kcccccccck...",
                "...kcckkkkcck...",
                "....kcccccck....",
                "...kcccccccck...",
                "..kcoccccccock..",
                "..kccccccccccck.",
                "...kcccccccck...",
                "....kcc.kccck...",
                ".....kc..kcck...",
                "......k...kck...",
                "...........k....",
                "................",
            },
            ["knight_captain"] = new[]
            {
                "................",
                ".....kkkkk......",
                "....krrrrrk.....",
                "....kmkkkmk.....",
                "....kmmmmmk.....",
                ".mm..kmmmk......",
                "mMMmkkccccckk...",
                ".mm.kcccrcccck..",
                "..wkccrrrrrcck..",
                "..wkscccrcccsk..",
                "..w.kkccccck....",
                ".....kcdddck....",
                ".....kck.kck....",
                ".....kmk.kmk....",
                ".....kkk.kkk....",
                "................",
            },
            ["rift_spawn"] = new[]
            {
                "................",
                "................",
                "................",
                "...kk......kk...",
                "..kcck....kcck..",
                "..kcdckkkkcdck..",
                "...kcccccccck...",
                "..kcekcccckeck..",
                "..kccccccccccck.",
                ".kccdccccccdcck.",
                ".kck.kcccck.kck.",
                ".k...kck.kck..k.",
                "....kk.....kk...",
                "................",
                "................",
                "................",
            },
            ["void_eye"] = new[]
            {
                "................",
                "................",
                ".....kkkkkk.....",
                "...kkccccccckk..",
                "..kcccWWWWccck..",
                ".kccWWbbbbWWcck.",
                ".kcWWbbkkbbWWck.",
                ".kcWWbbkkbbWWck.",
                ".kccWWbbbbWWcck.",
                "..kcccWWWWccck..",
                "...kkccccccckk..",
                ".....kkkkkk.....",
                "......k..k......",
                ".....k....k.....",
                "................",
                "................",
            },
            ["ash_colossus"] = new[]
            {
                "................",
                "....kkkkkkk.....",
                "...kaaaaaaak....",
                "...kaekaaeak....",
                "...kaaaaaaak....",
                ".kkkaaaAaaakkk..",
                "kaaaaaaAaaaaaak.",
                "kaAaaaaaaaaaAak.",
                "kaakaaaAaaakaak.",
                "kAAkaaaaaaakAAk.",
                "kkk.kaaaaak.kkk.",
                "....kaak.aak....",
                "....kaak.aak....",
                "...kAAAk.AAAk...",
                "...kkkkk.kkkkk..",
                "................",
            },
            ["echo_shade"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kccddk.....",
                "....kcckkcdk....",
                "....kcWkkWdk....",
                "....kcddddck....",
                ".....kccdck.....",
                "...kkcccccckk...",
                "..kccccccccccck.",
                "..kc.kccccck.ck.",
                "..k..kccccck..k.",
                "......kccck.....",
                ".......kck......",
                "........k.......",
                "................",
                "................",
            },
            ["rien_shadow"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kcccck.....",
                "....kcccccck....",
                "....kcckkcck....",
                "....kcccccck....",
                ".....kccccck....",
                "...kkccccccckk..",
                "..kccccccccccck.",
                "m.kcckccccckcck.",
                ".mkck.kccck.kck.",
                "..m...kccck.....",
                "......kcdck.....",
                ".....kck.kck....",
                ".....kck.kck....",
                ".....kkk.kkk....",
            },
            ["sern"] = new[]
            {
                "........................",
                "..........kkkk..........",
                ".........krrrrk.........",
                "........kgrrrrgk........",
                "........kssssssk........",
                "........ksksskskk.......",
                "........kssssssk........",
                ".........kssssk.........",
                "......kkkcccccckkk......",
                ".....kcccccrrcccccck....",
                "....kcccccrrrrcccccck...",
                "...kcckcccrrrrccckcck...",
                "...ksk.kcccrrcccck.ksk..",
                "...kk..kccccccccck..kk..",
                ".m.....kcccccccck.......",
                ".mm....kdddddddddk......",
                "..mm...kccc..cccck......",
                "...mm..kccc..cccck......",
                "....mm.kmmk..kmmk.......",
                "......kkkkk..kkkkk......",
                "........................",
                "........................",
                "........................",
                "........................",
            },
            ["ash_moth"] = new[]
            {
                "................",
                "................",
                "..kk........kk..",
                ".kcck......kcck.",
                "kccdck....kcdcck",
                "kcdydck..kcdydck",
                "kccdcckkkkccdcck",
                ".kccckeekkkccck.",
                "..kkckkdkkkckk..",
                "..kccckdkdkccck.",
                ".kcdcck.k.kcdcck",
                ".kccck.....kccck",
                "..kkk.......kkk.",
                "................",
                "................",
                "................",
            },
            ["reef_eel"] = new[]
            {
                "................",
                "................",
                "................",
                "..kkkk..........",
                ".kcecck.........",
                "kcccccck........",
                "kWkWkcck........",
                ".kkkkcck....kk..",
                "....kcck...kcck.",
                "....kccck.kccck.",
                ".....kcccccdck..",
                "......kcdcddck..",
                ".......kkddck...",
                ".........kck....",
                "..........k.....",
                "................",
            },
            ["hollow_monk"] = new[]
            {
                "................",
                "......kkkk......",
                ".....kddddk.....",
                "....kddkkddk....",
                "....kdkekekdk...",
                "....kdkkkkkdk...",
                ".....kdkkkdk....",
                "....kkcccccck...",
                "...kccccccccck..",
                "...kcdcccccdck..",
                "..kaakcccckaak..",
                "...kkcccccccck..",
                "....kcccdcccck..",
                "....kcccdcccck..",
                "...kcccdddcccck.",
                "...kkkkkkkkkkkk.",
            },
            ["scarab"] = new[]
            {
                "................",
                "................",
                "................",
                "................",
                "......kkkkkkk...",
                ".....kcdccdcck..",
                "..kkkccdccdccck.",
                ".kgkkccdccdccck.",
                "kkekkcccddcccck.",
                ".kkkkcccccccccck",
                "..kkkkcccccccckk",
                "...k.kkkkkkkkk..",
                "..k.k.k.k.k.k...",
                ".k.....k.....k..",
                "................",
                "................",
            },
            ["forgotten"] = new[]
            {
                "................",
                ".....kkkkkk.....",
                "....kcccccck....",
                "...kcccccccck...",
                "...kcykcckyck...",
                "...kcccccccck...",
                "....kcckkcck....",
                ".....kccccck....",
                "...kkcccccckk...",
                "..kcccdcdcccck..",
                ".kccckcdckccck..",
                ".kcck.kcdk.kcck.",
                "..kk.kcdcdk.kk..",
                ".....kcdcdk.....",
                "....kcd.k.dck...",
                "....kk.....kk...",
            },
            ["nameless"] = new[]
            {
                "........................",
                "........kkkkkkkk........",
                "......kkccccccccdk......",
                ".....kccccccccccccdk....",
                "....kcccWWccccWWcccdk...",
                "...kcccWbbWccWbbWcccdk..",
                "...kcccWbbWccWbbWcccdk..",
                "...kccccWWccccWWccccdk..",
                "...kcccccccccccccccccdk.",
                "..kccccckkkkkkkkcccccdk.",
                "..kcccckccccccccckcccdk.",
                "..kcccccccccccccccccccdk",
                "..kccbcccccbccccccbcccdk",
                ".kcccccccccccccccccccccd",
                ".kccccbccccccccbccccccdk",
                ".kcccccccccccccccccccccd",
                ".kccccccbccccccccbccccdk",
                "..kcccccccccccccccccccdk",
                "..kccbccccccccbcccccccdk",
                "...kcccccccccccccccccdk.",
                "....kkcccccccccccccdkk..",
                "......kkkkkkkkkkkkkk....",
                "........................",
                "........................",
            },
        };

        // ---- Small icons (8×8) ----

        public static readonly string[] Heart =
        {
            "........",
            ".kk..kk.",
            "krrkkrrk",
            "krrrrrrk",
            "krrrrrrk",
            ".krrrrk.",
            "..krrk..",
            "...kk...",
        };

        public static readonly string[] Coin =
        {
            "........",
            "..kkkk..",
            ".kggggk.",
            "kgggWggk",
            "kggggggk",
            "kgggggk.",
            ".kggggk.",
            "..kkkk..",
        };

        public static Texture2D Icon(string name)
        {
            if (Known('i', name, null, out var known)) return known;
            var made = name switch
            {
                "heart" => Make("icon:heart", Heart, null),
                "coin" => Make("icon:coin", Coin, null),
                _ => MapIcons.TryGetValue(name, out var rows) ? Make("map:" + name, rows, null) : null,
            };
            return made == null ? null : Keep('i', name, null, made);
        }

        // ---- The map (12×12 marks drawn on the hexes) ----

        public static readonly Dictionary<string, string[]> MapIcons = new Dictionary<string, string[]>
        {
            ["tree"] = new[]
            {
                "....kkkk....",
                "...kGGlGk...",
                "..kGlGGGGk..",
                "..kGGGGlGk..",
                ".kGGlGGGGGk.",
                ".kGGGGGlGGk.",
                "..kkGGGGkk..",
                "....kwwk....",
                "....kwwk....",
                "....kkkk....",
                "............",
                "............",
            },
            ["peak"] = new[]
            {
                "............",
                ".....kk.....",
                "....kWWk....",
                "...kWaaAk...",
                "...kaaaAk...",
                "..kaaaaAAk..",
                "..kaaAaaAk..",
                ".kaaaaAaaAk.",
                ".kaaAaaaaAk.",
                "kkkkkkkkkkkk",
                "............",
                "............",
            },
            ["wave"] = new[]
            {
                "............",
                "............",
                "..bb....bb..",
                ".b..b..b..b.",
                "b....bb....b",
                "............",
                "............",
                "..bb....bb..",
                ".b..b..b..b.",
                "b....bb....b",
                "............",
                "............",
            },
            ["reeds"] = new[]
            {
                "............",
                "..w.....w...",
                "..l..w..l...",
                "..l..l..l.w.",
                ".ll..l.ll.l.",
                ".l..ll.l..l.",
                ".l..l..l.ll.",
                "ll.ll.ll.l..",
                "............",
                "............",
                "............",
                "............",
            },
            ["stones"] = new[]
            {
                "............",
                "............",
                "..kk........",
                ".kaak...kk..",
                ".kaAk..kaak.",
                ".kaAk..kaAk.",
                ".kaAk.kkaAk.",
                "kkkkkkkaaAkk",
                "............",
                "............",
                "............",
                "............",
            },
            ["town"] = new[]
            {
                "......k.....",
                ".....krk....",
                "....krrrk...",
                "...krrrrrk..",
                "..krrrrrrrk.",
                "...kWWWWWk..",
                "...kWykWWk..",
                "...kWykWWk..",
                "...kWWkWyk..",
                "...kkkkkkk..",
                "............",
                "............",
            },
            ["shrine"] = new[]
            {
                ".....kk.....",
                ".....ko.....",
                ".....ky.....",
                "....kWWk....",
                "...kaaaak...",
                "...kaAAak...",
                "...kaAAak...",
                "..kaaaaaak..",
                "..kkkkkkkk..",
                "............",
                "............",
                "............",
            },
            ["quest"] = new[]
            {
                "....kkkk....",
                "...kaaaak...",
                "..kaWaWaak..",
                "..kaaaaaak..",
                "..kaWaWWak..",
                "..kaaaaaak..",
                "..kaWWaWak..",
                "..kaaaaaak..",
                ".kkkkkkkkkk.",
                "............",
                "............",
                "............",
            },
            ["boss"] = new[]
            {
                "....kkkk....",
                "...kaaaak...",
                "..kaaeaaak..",
                "..kaaeeaak..",
                "..kaeaaeak..",
                "..kaaeeaak..",
                "..kaaaeaak..",
                "..kaaaaaak..",
                ".kkkkkkkkkk.",
                "............",
                "............",
                "............",
            },
            ["event"] = new[]
            {
                "...kkkkk....",
                "..kgggggk...",
                "..kgkkkgk...",
                "..kkk.kgk...",
                ".....kggk...",
                "....kggk....",
                "....kgk.....",
                "....kkk.....",
                "....kgk.....",
                "....kkk.....",
                "............",
                "............",
            },
            ["chest"] = new[]
            {
                "............",
                "............",
                "..kkkkkkkk..",
                ".kwwwwwwwwk.",
                ".kwwwgwwwwk.",
                ".kkkkgkkkkk.",
                ".kwwwgwwwwk.",
                ".kwwwwwwwwk.",
                ".kkkkkkkkkk.",
                "............",
                "............",
                "............",
            },
            ["herb"] = new[]
            {
                "............",
                "....k..k....",
                "...klkklk...",
                "..klllllk...",
                "...kllllk.k.",
                ".k..kllk.klk",
                "klk..kk.kllk",
                "kllk.kk.kk..",
                ".kk..kk.....",
                ".....kk.....",
                "............",
                "............",
            },
            ["camp"] = new[]
            {
                "............",
                ".....kk.....",
                "....krrk....",
                "...krrrrk...",
                "..krrkkrrk..",
                "..krk..krk..",
                ".krrk..krrk.",
                ".krk....krk.",
                "kkkkkkkkkkkk",
                "............",
                "............",
                "............",
            },
            ["clue"] = new[]
            {
                "....kkkk....",
                "...kaaaak...",
                "...kakkak...",
                "....kaak....",
                "....kaak....",
                "....kAak....",
                "....kaak....",
                "...kaaAak...",
                "...kaaaak...",
                "..kkkkkkkk..",
                "............",
                "............",
            },
            ["dunes"] = new[]
            {
                "............",
                "............",
                "............",
                "...yy.......",
                "..y..y......",
                ".......yy...",
                "......y..y..",
                "............",
                "..yy........",
                ".y..y.......",
                "............",
                "............",
            },
            ["bell"] = new[]
            {
                "....kkkk....",
                "...kaaaak...",
                "..kaaeaaak..",
                "..kgggggak..",
                "..kgWgggak..",
                ".kgggggggak.",
                ".kgggggggak.",
                "kgggggggggak",
                "kkkkkkkkkkkk",
                ".....kk.....",
                "............",
                "............",
            },
            ["cave"] = new[]
            {
                "............",
                "...kkkkkk...",
                "..kaaaaaak..",
                ".kaaAAAAaak.",
                ".kaAkkkkAak.",
                "kaaAk..kAaak",
                "kaAk....kAak",
                "kaAk....kAak",
                "kkkk....kkkk",
                "............",
                "............",
                "............",
            },
            ["snowflake"] = new[]
            {
                "............",
                "............",
                ".....W......",
                "...W.W.W....",
                "....WWW.....",
                "..WWWWWWW...",
                "....WWW.....",
                "...W.W.W....",
                ".....W......",
                "............",
                "............",
                "............",
            },
            ["flag"] = new[]
            {
                "............",
                "...krrrrk...",
                "...krrrrrk..",
                "...krrrrk...",
                "...kk.......",
                "...kw.......",
                "...kw.......",
                "...kw.......",
                "...kw.......",
                "..kkkk......",
                "............",
                "............",
            },
            ["crack"] = new[]
            {
                "............",
                "............",
                "...p........",
                "....p.......",
                "....pp......",
                ".....pp.....",
                "......p.p...",
                ".......pp...",
                "........p...",
                "........pp..",
                "............",
                "............",
            },
            ["crown"] = new[]
            {
                "............",
                "............",
                ".g..g..g....",
                ".gg.gg.gg...",
                ".ggggggggg..",
                ".grgggrggg..",
                ".ggggggggg..",
                ".kkkkkkkkk..",
                "............",
                "............",
                "............",
                "............",
            },
            ["lock"] = new[]
            {
                "............",
                "....kkkk....",
                "...kMMMMk...",
                "...kM..Mk...",
                "...kM..Mk...",
                "..kkkkkkkk..",
                "..kggggggk..",
                "..kgggkggk..",
                "..kggkkggk..",
                "..kgggkggk..",
                "..kkkkkkkk..",
                "............",
            },
            ["erase"] = new[]
            {
                "............",
                "..kkkkkkk...",
                "..kWWWWWk...",
                "..kWaaaWk...",
                "..kWWWWWkk..",
                "..kWaa.WWk..",
                "..kW...aWk..",
                "..kW.k..Wk..",
                "..kWWk.WWk..",
                "..kkkk.kkk..",
                "............",
                "............",
            },
            ["ice"] = new[]
            {
                "............",
                ".....kk.....",
                "....kbbk....",
                "...kbWbbk...",
                "..kbWbbbbk..",
                "..kbbbbWbk..",
                "..kbbbWbbk..",
                "...kbbbbk...",
                "....kbbk....",
                ".....kk.....",
                "............",
                "............",
            },
            ["flame"] = new[]
            {
                "............",
                ".....r......",
                "....rr......",
                "....roe.....",
                "...rooer....",
                "...roeeor...",
                "..rroeyeor..",
                "..roeyyeor..",
                "..roeyyeor..",
                "...roeeor...",
                "....rrrr....",
                "............",
            },
            ["drop"] = new[]
            {
                "............",
                ".....k......",
                "....krk.....",
                "....krk.....",
                "...krrrk....",
                "...krrrk....",
                "..krrWrrk...",
                "..krrrWrk...",
                "..krrrrrk...",
                "...krrrk....",
                "....kkk.....",
                "............",
            },
            ["thorn"] = new[]
            {
                "............",
                ".k......k...",
                "..k....k....",
                "...G..G.....",
                "..GGGGGG....",
                "kGG.GG.GGk..",
                "..GGGGGG....",
                "...G..G.....",
                "..k....k....",
                ".k......k...",
                "............",
                "............",
            },
            ["leaf"] = new[]
            {
                "............",
                "........kk..",
                "......kllk..",
                "....kllllk..",
                "...kllGllk..",
                "..kllGlllk..",
                "..klGlllk...",
                "..kGllkk....",
                "..Gkkk......",
                ".G..........",
                "............",
                "............",
            },
            ["acid"] = new[]
            {
                "............",
                ".....k......",
                "....klk.....",
                "....klk.....",
                "...kllGk....",
                "...klGlk....",
                "..kllGllk...",
                "..klGllGk...",
                "..kllllGk...",
                "...kGllk....",
                "....kkk.....",
                "............",
            },
            ["daze"] = new[]
            {
                "............",
                "...gggggg...",
                "..g......g..",
                ".g..gggg..g.",
                ".g.g....g.g.",
                ".g.g.gg.g.g.",
                ".g.g.g..g.g.",
                ".g..g...g.g.",
                "..g..ggg..g.",
                "...g.....g..",
                "....ggggg...",
                "............",
            },
            ["down"] = new[]
            {
                "............",
                ".....kk.....",
                ".....pp.....",
                ".....pp.....",
                ".....pp.....",
                "..kpppppk...",
                "...kpppk....",
                "....kpk.....",
                ".....k......",
                "............",
                "............",
                "............",
            },
        };

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
