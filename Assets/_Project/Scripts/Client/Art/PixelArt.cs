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
        // Pictures drawn at full size (24 rows or more) use the palette's own letters (Resources/Data/Ui/palette.csv).
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

        // Pictures of 24 rows or more (24×24 icons, 32×32 heroes and monsters, 48×48 bosses) are drawn in the palette's
        // letters and shaded by hand. Smaller ones are the older letter rows: snapped to the palette, and with `finish`
        // (a small hero or monster) drawn at twice the size with the finishing pass below. Every colour comes from the
        // palette: `c` is the class or monster colour, `d` one step darker on its ramp, `C` one step lighter.
        public static Texture2D Make(string key, string[] rows, string color, bool finish = false)
        {
            key += color + (finish ? "+" : "");
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            int height = rows.Length;
            int width = rows[0].Length;
            bool full = height >= 24;
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
                "................................",
                "..........kkkk..................",
                "........kkCCCCkk.........k......",
                ".......kCCcccccdk.......kik.....",
                "......kCcdkkCccdk.......kZmk....",
                ".......kkkkkkccdkkkkk...kZmk....",
                "..........kiiiiimmmmMk..kZmk....",
                ".........kiiimmmmmmmMMk.kZmk....",
                ".........kiZimmmmmmmMMk.kZmk....",
                ".........kiZimmmmmmmMMk.kimk....",
                ".........kiimmmmmmmmMMk.kimk....",
                ".........kimmMkkkkkkkk..kimk....",
                ".........kimmMkkkkkkkk..kimk....",
                ".........kimmmmmmmmmMMk.kimk....",
                ".........kimmmmmmmmmMMk.kimk....",
                ".....kkkkkmMMMMMMMMM88k.kimk....",
                "....kiiimMiCCCCCCCfimmMkkimkk...",
                "...kiuuuwwMCffffffdmmmMgyyggjk..",
                "..kiuuwwwwhMfffgffd8immMiimMk...",
                "..kiuwwgwwhMffgyjfd8immMimmMk...",
                "..kiwwgyjwhMfffgffd8mMMMmmMMk...",
                "..kiwwwjwhhMfffjffdmMMkkkgjk....",
                "..kmwwwwhhhMwwwgjwwwhhk..kk.....",
                "...kmhhhhhMCffffffffdMk.........",
                "....kMMMM8kCffdkCfffdk..........",
                ".....kkkkkkmmMMkkimmMk..........",
                "..........kmmMMkkimmMk..........",
                "..........kMMM8kkimmMk..........",
                "..........kMMMM8kmmmMMk.........",
                "..........k88888kMMMMMk.........",
                "...........kkkkk.kkkkk..........",
                "................................",
            },
            ["berserker"] = new[]
            {
                "................................",
                "...........k....k.......kkkk....",
                ".......k..kwk..khk.....kimmik...",
                "......kwk.kwhkkwwk..k..kmMMmik..",
                "......kwhkkwhhkwhhkkwk.kuwMmiik.",
                ".....kwwhhwwhhhwhhhww5kkuwMmiZk.",
                "....kwwhhhhhhhhhhhhhh5kkuwMmiZk.",
                "....kwhhhhhhhhhhhhhhh5kkuwMmmZk.",
                "...kkCccccccccccccccddkkuwMmmik.",
                "..kCckhhhhhhhtttttttSk.kuwMMmik.",
                "..kcdkwhhhhh5tsshhshhk.kuwMMmk..",
                "...kkwwhhhhh5tsssksskSkkuw8Mk...",
                ".....khhhhhh5tsssksskSkkuwkk....",
                "......khhhhh5tsttwwww5kkuwk.....",
                ".......khhh55twwwwhh55kkuwk.....",
                ".......kk555kkwhhhh55kkkuwk.....",
                "......ktttCCctt55555tttkuwk.....",
                ".....kttssCcccSS555ctssSuwk.....",
                ".....ktssScccccccccctsssuwk.....",
                ".....ktssScccccccccccwhsstSk....",
                ".....kwhhhccccccccccchhsssSk....",
                ".....khhh5cccccccccccdSSSSSk....",
                ".....ktssSuwwwwgjwwwhhkkuwk.....",
                ".....ktssScccdccccdccdkkuwk.....",
                "......kSSSddddqdddd1ddkkuwk.....",
                ".......kkkk1QQqkk1QQqk.kuwk.....",
                "..........kqqqqkkqqqqk.kuwk.....",
                "..........kXxxXkkXxxXk.khhk.....",
                "..........kuwwwhkuwwwhk.kk......",
                "..........khhhhhkhhhhhk.........",
                "...........kkkkk.kkkkk..........",
                "................................",
            },
            ["hunter"] = new[]
            {
                "................................",
                "................................",
                "..........kk............k.......",
                ".........kCCkkkkkk.....kuk......",
                ".........kCccCCCCCkk....Xuk.....",
                ".........kCccccccccCk...Xkuk....",
                ".....k.kkCCcccccccccck..Xkuk....",
                "....kWkrkCcccccccccccdk.X.kuk...",
                "....kWrWkCcccccccccccdk.X.kuk...",
                "....kXTXkCcccccwwhhttdk.X..kuk..",
                "...khhhhkCccccttsssssSk.X..kuk..",
                "....kwwwhCcccctssksskSk.X..kuk..",
                "....kuwwwCcccctssksskSk.X...kuk.",
                ".....kwwwCcccctttsssSSk.X...kwk.",
                ".....kuwwCcccctssssSSdk.X...kwk.",
                ".....kuwwddddddSSSSSdkkkXkkktSk.",
                "......kCCdCCCCCCCCCCCCCduuhttSk.",
                "......kCcdCccccccchCcccduwhtsSk.",
                "......kCcdCcccccchccddddhhhSSSk.",
                "......kCcdCccccchccccdkkXkkkkwk.",
                "......kCcdCcccchcccccdk.X...kwk.",
                ".....kLddduwwwwgjwwwhhk.X..kwk..",
                ".....kLttSCccccccccccdk.X..kwk..",
                ".....kLSSSddddddddddddk.X..kwk..",
                ".....kLVVVVuuuhkkuuuhk..X.kwk...",
                ".....kLVVVvuwwhkkuwwhk..X.kwk...",
                ".....kLVVvvhhhhkkhhhhk..Xkwk....",
                "......kvvvkwwwwkkwwwwk..Xkwk....",
                ".......kkkkwhhh5kwhhh5k.Xhk.....",
                "..........k55555k55555kkhk......",
                "...........kkkkk.kkkkk..k.......",
                "................................",
            },
            ["elementalist"] = new[]
            {
                "................................",
                "........kkk..............kkkk...",
                ".......kCCCkk...........koooek..",
                "........kcccCkk........ko**ooek.",
                ".........kccccCkk......ko*oooek.",
                "..........kcccccCkk....kooooerk.",
                ".......kkkkyggggggjkkkkkeoeerrk.",
                "......kCccccccccccccccddkrrrTk..",
                ".......kdddddddddddddPPk.kuwk...",
                "........kkXXXaSSSSSSSkk..kuwk...",
                ".........kXxxttssssssSk..kuwk...",
                "........kXXxxtsssksskSk..kuwk...",
                "........kXxxxtsssksskSk..kuwk...",
                "........kXxxatsttsssSSk..kuwk...",
                "........kXxxatsssssSSk...kuwk...",
                "........kkxaakSSSSSSkkk..kuwk...",
                ".......kCCCCCCCCCCCCCCCk.kuwk...",
                "......kCCcCccccccccCcccckkuwk...",
                "......kCccCccccccccCccccdttSk...",
                ".....kCCccCcccccccccccccdtsSk...",
                ".....kCccdyggggggggggddddSSSk...",
                ".....kCccCCcccccdcccccdkkkuwk...",
                ".....kCccCccccccdcccccdk.kuwk...",
                ".....kdddCccccccdcccccdk.kuwk...",
                ".....ksSCCccccccdcccccdk.kuwk...",
                "......kCCcccccccdcccccdk.kuwk...",
                "......kCccccccccdcccccdk.kuwk...",
                "......kCccccccccdcccccdk.kuwk...",
                "......kygggggggggggggjjk.kuwk...",
                ".......kkkkwhh5kkwhh5kk..khhk...",
                "...........kkkk..kkkk.....kk....",
                "................................",
            },
            ["cleric"] = new[]
            {
                "................................",
                "............kkkkkkkk......kk....",
                "...........kyyyyyyygk....kygk...",
                "..........kggkkkkkkjjk..kkygkk..",
                "...........kk......kk..kyyyyygk.",
                ".......................kgggjjjk.",
                "............kkkkkkkk....kkgjkk..",
                "..........kkZZZZZZZZk....kgjk...",
                ".........kZZZWWWWWWWWk...kgjk...",
                "........kZZWWWWwwhhttXk..kgjk...",
                "........kZWWWWttsssssSk..kuwk...",
                "........kZWWWWtssksskSk..kuwk...",
                "........kZWWWWtssksskSk..kuwk...",
                "........kZWWWWtttsssSSk..kuwk...",
                "........kZWWWWtssssSSXk..kuwk...",
                "........kXXXXXXSSSSSXXk..kuwk...",
                ".......kZZZZyggggggZZZZk.kuwk...",
                "......kZZWZWWWWgjWWZWWWWkkuwk...",
                "......kZWWZWWWWgjWWZWWWWXttSk...",
                ".....kZZWWZWWWWgjWWWWWWWXtsSk...",
                ".....kZWWXyggggggggggXXXXSSSk...",
                ".....kZWWZZWWWWgjWWWWWXkkkuwk...",
                ".....kZWWZWWWWWgjWWWWWXk.kuwk...",
                ".....kXXXZWWWWWgjWWWWWXk.kuwk...",
                ".....ksSZZWWWWWgjWWWWWXk.kuwk...",
                "......kZZWWWWWWgjWWWWWXk.kuwk...",
                "......kZWWWWWWWgjWWWWWXk.kuwk...",
                "......kZWWWWWWWgjWWWWWXk.kuwk...",
                "......kygggggggggggggjjk.kuwk...",
                ".......kkkkuwwhkkuwwhkk..khhk...",
                "...........kkkk..kkkk.....kk....",
                "................................",
            },
            ["gambler"] = new[]
            {
                "................................",
                "...........kkkkkkkkk............",
                "..........k1111111WXk...........",
                "..........k1QQQQQQrWk...........",
                "..........k1QQQQQqWXk...........",
                "........kkkCcccccccdkkkk........",
                ".......k111111111111111Qk.......",
                "........kqqqqqqqqqqqqqqk........",
                ".........khhhhSSSSSSSSk.........",
                ".........kwhhhssssssssk.........",
                ".........kwhhhssssssssk.........",
                ".........kwhhsssssksksk.........",
                ".........kwhhSssssksksk..kkk....",
                ".........khhhhssstsssSkkkZZZkk..",
                "..........khhhSssssShhkZZxkWxZk.",
                ".........kkkkkSSssSSkkZrWxWxrWk.",
                "........kCCCCCCZWWXCccWWWxWxWWk.",
                "........kCcdccCWrrXdcccWWxxWWWk.",
                "........kCcdccCWrTXdcccdWxxWWk..",
                "........kCcdccCWTWXdcccdWxWWk...",
                "........kCcdcccCWXdccddWsssk....",
                "........kCcdccccCdcccddWSSk.....",
                "......kkWWWdccccdcccddkkkk......",
                ".....kygssccccccdccccddk........",
                ".....kgjsScccccdkccccddk........",
                "......kkddddddddkddddddk........",
                "........kkkkQQqkkQQqkkk.........",
                "...........kQQqkkQQqk...........",
                "..........kwwwhkkwwwhk..........",
                "..........kh555kkh555k..........",
                "...........kkkk..kkkk...........",
                "................................",
            },
            ["rogue"] = new[]
            {
                "................................",
                "................................",
                "................................",
                "............kkkkkk..............",
                "..........kkcccccckk............",
                ".........kcccdddddddk...........",
                "........kccddddddddddk..........",
                ".......kcdddddddddddddk.........",
                "......kcddddddddddddqqqk........",
                ".....kcdddddddqSSSSSSSk.........",
                ".....kddddddddqtssssssk.........",
                "......kdddddddqssskskSk.........",
                ".......kddddddqSsskskSk.........",
                "......kkqqqqqCCCCCCCCCk.........",
                "....kkCCcccccccccccccdk.........",
                "...kCCccdkccccccccccdkk.........",
                "..kCcddkccqddddddddddcck........",
                "..kdkkkkcdquhuuuuuwccdk..k......",
                "...k...kcdquwhwwwwwhccdkkgkkkk..",
                ".......kcdquwwhwwwwhdwwssgiiiik.",
                ".......kcdquwwwhwwwhqhhSSjmmMk..",
                ".......khhquwwwwhwwwhkkkkjkkk...",
                ".......kss55555g55555k...k......",
                ".......ksSkcccccccccdk..........",
                "......kjgjjkccckkccdk...........",
                ".......kimkkccdkkccdk...........",
                ".......kimkkccdkkccdk...........",
                ".......kikkuuuukkuuuuk..........",
                "........k.kwwwhkkwwwhk..........",
                "..........khh55kkhh55k..........",
                "...........kkkk..kkkk...........",
                "................................",
            },
            ["bard"] = new[]
            {
                "................................",
                ".....kkk........................",
                "....keeek...kkkkkk..............",
                "...kerrrekkkCCCCCCkk............",
                "....kTTrrreCcccccccckkk.........",
                ".....kkTTrCcccccccccccdk........",
                ".......kkddddddddddddddk........",
                ".........kuuuuuwwSSSSSk.........",
                "........kuuwwwwussssssk.........",
                "........kuwwwwssssssssk.........",
                "........kuwwwsssssssssk....kk...",
                "........kuwwwsssssksksk...khwk..",
                "........kwwwwSssssksksk..kXhhhk.",
                "........kwwhwwssstsssSk.kXhkkk..",
                ".........kwhwhSssssTSk.kXhk.....",
                "........kkhhkkkSSSSkkCkssk......",
                ".......kCCCdCZWXWXWXCCcsSk......",
                ".......kCccdcccccccCCcddk.......",
                ".......kCccdcccccccdcddk........",
                ".......kCCddccuuuuuXhdk.........",
                ".......kCcdccuuwwwwXdk..........",
                ".......kdddssuwh5wXhhk..........",
                "........kkCsSww55Xhhhdk.........",
                ".........kdduwwwwhhhhdk.........",
                ".........kdddhhhhhhhddk.........",
                "..........kk[[hhhhh)kk..........",
                "...........k[[)kk[[)k...........",
                "..........kuuuhkkuuuhk..........",
                "..........kuwwhkkuwwhk..........",
                "..........khhhhkkhhhhk..........",
                "...........kkkk..kkkk...........",
                "................................",
            },
            ["alchemist"] = new[]
            {
                "................................",
                "................................",
                "................................",
                "...........kk...k...............",
                "..........kuwkkkwkkk............",
                ".........kuwwuwwuwwwkk..........",
                "........kuwwwwmmwwmmwhk.........",
                "........kuwqqmRbMmRbMqk.........",
                "........kuwwwSMMSSMMSSk.........",
                "........kuwwwsssssssssk.........",
                "........kuwwhsssssssssk...kk....",
                "........kuwhwsssssksksk..k=lk...",
                "........kwhhwSssssksksk..klLk...",
                "........kwhhhhssstsssSk..kkk....",
                ".........kwhhhSssssSSk..klllk...",
                "........kkkkkkSSssSSk....k=Ik...",
                ".......kCCCdCZZZZZZCCk...k=Ik...",
                ".......kCccdcZWWWWWCcck.kZ=IIk..",
                ".......kCccdcZWWWWWCccdk=llLIk..",
                ".......kCccdcZWWWWWXcccslLLLVk..",
                ".......kddddcZWWWWWXdddSLLLVVk..",
                ".......ksshhhhhhjhhhhhkkkLVVk...",
                ".......ksScucZuWWWuXddk..kkk....",
                "........kCcecZlWWW+Xddk.........",
                "........kddrdXLXXXpXddk.........",
                "........kddddddkkdddddk.........",
                ".........kkkQQqkkQQqkk..........",
                "..........kwww5kkwww5k..........",
                "..........kwhh5kkwhh5k..........",
                "..........k5555kk5555k..........",
                "...........kkkk..kkkk...........",
                "................................",
            },
            ["necromancer"] = new[]
            {
                "................................",
                "........................kkkk....",
                ".......................kZWWXk...",
                "......................kZWWWWXk..",
                ".............kkkkkk...kWWkWkXk..",
                "..........kkkcccccckk.kXWWXWxk..",
                ".........kccccddddddqk.kXkXkk...",
                "........kccddddddddddqk.khk.....",
                ".......kccdddddddddddqk.kwk.....",
                "......kccddddddddkkkkkqkkwk.....",
                ".....kccddddddddqkqqqqqkkwk.....",
                "....kqcdddddddddqq*q*qqkkwk.....",
                ".....kqddddddddddqoqoqqkkwk.....",
                "......kkdddddddddqqqqqqkkwk.....",
                ".......kcddddddddddddqqkkhk.....",
                ".......kcdddddddddddddqkkhk.....",
                ".......kcdddddddddddddqkkhk.....",
                ".......kqqqqqqqqqqqqqqqkkhk.....",
                "......kcccdccccccccdcccdXhxk....",
                "......kcccdccccccccdccddXhxk....",
                "......kccddccccccccddddkxhak....",
                "......kccddXxxXxxxxxxxdkkhk.....",
                "......kdddCccxccdccccddkkhk.....",
                ".......kCccccXccdccccddkkhk.....",
                ".......kCcccxcccdccccddkkhk.....",
                "......kCcccccccdcccccdddkhk.....",
                "......kCcccccccdcccccdddkhk.....",
                "......kCcccccccdcccccdddkhk.....",
                "......kCcccccccdccccddddkhk.....",
                ".....kddddkdddkddddkddddk5k.....",
                "......kkkk.kkk.kkkk.kkkk.k......",
                "................................",
            },
            ["seer"] = new[]
            {
                "................................",
                "............kk..................",
                "...........kCCk.................",
                "..........kCCcckk...............",
                "..........kCcyccCk..............",
                ".........kCCccccyckkk...........",
                "......kkkkdddddygddddkkkkk......",
                ".....kCCCCCCCCCCCCCCCCCCCCk.....",
                "....kddddddddddddddddddddddk....",
                ".....kkkkkWXXXSSSSSSSSkkkkk.....",
                ".........kWXxXXtssssssk.........",
                ".........kWXxXttskssksk.....R...",
                ".........kWXxXtsskssksskkkkRZR..",
                ".........kWXxXtsssssSsk====kR...",
                ".........kWXxXxssssSSk=Z=IINk...",
                ".........kWXxXxSSSSSSk=IIIINk...",
                "........kcc0CCCCCCCdkk=IIIINk...",
                "........kcd0ccccccCCkk=IIINNk...",
                ".......kccd0cccccCccckkNNNNk....",
                ".......kcdd0cccccCcccctssSk.....",
                ".......kcdd0ccccccccccckkk......",
                ".......kcdd0ggggggddddddk.......",
                "......kccdd0jccccccgjdkk........",
                "......kcddddjcdccccgjdk.........",
                "......k000000cdccccgjdk.........",
                ".......kkCccccdcyccgjdk.........",
                ".......kCCcccdccccccgjdk........",
                ".......kddddddddddddgjdk........",
                ".......kddddddddddddgjdk........",
                ".......kggggggggggggjjjjk.......",
                "........kkkkkkkkkkkkkkkk........",
                "................................",
            },
            ["beastmaster"] = new[]
            {
                "................................",
                ".........................k......",
                "........................kik.....",
                ".......................kimMk....",
                ".............kkkkk.....kimMk....",
                "...........kkXXXXXkk....kMk.....",
                ".......kk.kXXXxxxxxXk..krrrk....",
                "......kXXkXXxxxxxxxxak..kTk.....",
                ".....kaXxxXxxxxxttttxk..kuk.....",
                "......kkxxaxxxxttssssak.kuk.....",
                ".......kXxaxxxttxxsxxsk.kuk.....",
                "......kXXxxaxxtsskssksk.kuk.....",
                ".....kaaxxxaxxtsskssksskkuk.....",
                "......kkkxxaxxxXXXXXXak.kuk.....",
                "........kaxxxxxXxxaaxak.kuk.....",
                ".......ka444444Xxxxxaak.kuk.....",
                ".......k44cccccXxxxaadkkkuk.....",
                ".......k4ccccdccxxaacdtSkwk.....",
                ".......k4dccddcdcaaccdwStssk....",
                ".......kcddddhddhddSddhSssSk....",
                ".......kcddwhhhhhhhh5kkkkwk.....",
                ".......kcddwhhhhhhhh5k..kwk.....",
                ".......kcddw55555j5555k.kwk.....",
                "......kccddwhhhhhhhh5k..kwk.....",
                "......kcdhd5555555555k..kwk.....",
                "......khhhkh@@?kk@@?k...kwk.....",
                ".......kkk.k@@?kk@@?kk..kwk.....",
                "..........k444ukk444u5k.kwk.....",
                "..........kwhh5kkwhhh5k.khk.....",
                "..........k5555kk55555k..k......",
                "...........kkkk..kkkkk..........",
                "................................",
            },
            ["monk"] = new[]
            {
                "................................",
                "................................",
                "................................",
                "................................",
                ".............kkkkk..............",
                "...........kktttttkk............",
                "..........ktttttttttk...........",
                ".........ktttttsssssSk..........",
                ".........ktttssssssssk..........",
                "........ktttsssssssssSk.........",
                "........ktttSsssSSsSSsk.........",
                "........kttssSssskssksk.........",
                "........kttssSsssksskssk........",
                ".........ktsSsssssssSsk.........",
                ".........ktssssssssSSk..........",
                "..........kSsssssSSSk....kkk....",
                "........kkkCCSSSSSCCdkkkkWWxk...",
                ".......kCCdCcccCdccCCdXtSxXxk...",
                ".......kCcdCcccCdccCcdXSSxxxk...",
                ".......kCcdCccccCdcdddkkkkkk....",
                ".......kWWxCccccCdccdk..........",
                ".......kxxxCcccccCdcdk..........",
                "........kkrrrrrrerrrrrk.........",
                ".......krrTTTTTTTTTTTTk.........",
                ".......krTHH77kkkJJJJk..........",
                "......kDDHHJ7k...kJJJ7k.........",
                ".......kXXXX7k...k7XXXXk........",
                ".......kxxxxk.....kxxxxk........",
                "......ksssssk.....ksssssk.......",
                "......kSSSSSk.....kSSSSSk.......",
                ".......kkkkk.......kkkkk........",
                "................................",
            },
            ["engineer"] = new[]
            {
                "................................",
                "................................",
                "................................",
                "................................",
                "..........kk.kkkkk..............",
                ".......kkkwwkwwwwwkk............",
                "......kwwwwhhwhhhhhwkkk.........",
                ".......khhhhhhjggjhjggjk........",
                "........khhhhhjRbjhjRbjk........",
                "........kwhhhhhjjtttjjk.........",
                "........kwhhhhhttsssssk.........",
                "........kwhhhhttskssksk.........",
                "........kwhhhhtssksskssk........",
                ".........khhhhtsssssSsk....k....",
                "..........k5hhhssssSSk....kMk...",
                "...........kk555SSSSk....kWmMk..",
                "........kkkEEEEEEEEEdkk.kWkmMk..",
                ".......kEEdEcccccdcEEEdkWkkmMk..",
                ".......kEcdEcccccdjEccuuuuumMik.",
                ".......kEcdEcccccdcdddtswwwMMk..",
                ".......kEcdEcccccdjcdkssWkkMMk..",
                ".......kdddEcccccdccdkkkkWkMMk..",
                ".......kssSuuhhuhhhghhk..kWMk...",
                ".......kmkkuwhchhcccdk....kk....",
                ".......kMkkhhhdddddddk..........",
                ".......kMk.kQQqkkQQqk...........",
                "......kmMmkkQQqkkQQqkk..........",
                "......kmkmkuuuukkuuuu5k.........",
                ".......k.kkwhh5kkwhhh5k.........",
                "..........k5555kk55555k.........",
                "...........kkkk..kkkkk..........",
                "................................",
            },
            ["mnemonist"] = new[]
            {
                "................................",
                "................................",
                "................................",
                "................................",
                "............kkkkkkk.............",
                "..........kkCCCCcCCk............",
                ".........kCCCccccccdk...........",
                "........kCCcccccccccdk..........",
                ".......kCCccccccccccdk..........",
                "......kCCccccccQQQQQQdk.........",
                ".......kccccc$QttsssQdk.........",
                ".......kCcccc$ttskssksk.........",
                ".......kdcccc$tsskssksk..+......",
                "........kkccc$tsssssSsk.+Z+.....",
                ".........kCcc$cssssSSk...+......",
                ".........kkdd$ddSSSSkk..p.kk....",
                "........kccPdddddddCZZkkkkWWk...",
                "........kcdPcccdc+CZZZZZxWWWWk..",
                "........kcdPccccc+CZXXXZxWXXxk..",
                ".......kccdPccccc+C+pppp$$$$$k..",
                ".......kcddPccccc+$dtsdkkpkkk...",
                ".......kcddPccccc+pccdk.kpk.....",
                ".......kcddPccccc+p$cdk.k$k.....",
                ".......kPPPPccccc+p$cdk..k......",
                "........kCCccdccc+p$cdk.........",
                "........kCcccdccc+p$cdk.........",
                "........kCccdcccc+p$cdk.........",
                "........kCccdcccc$$$cdk.........",
                "........kdddddddddddddk.........",
                "........kpppp$$$$$$$$$k.........",
                ".........kkkkkkkkkkkkk..........",
                "................................",
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
                "................................................",
                "...............kk..kkk..........................",
                "..............kCdkkCCdk...kk....................",
                "..............kCdkkdddkkkkcAk...................",
                "..............kddCCCCCCCCCAAk...................",
                "..............kdddddddddddddk...................",
                "..............kCcccccccccddAk...................",
                "..............kddddddddddddAk...................",
                "..............kAAAddAAAAAddAk...................",
                "..............ke*edde*eAAddAk...................",
                "..............krorddrorAAddAk...................",
                ".......kkkkkk.kCcccccccccddAk.......kkkkk.......",
                "......k%&%%CCkkdAAAAAAAddddAk.....kk&%C%%kk.....",
                ".....k%CCCC@%CCdddddddddddAAkkkkkkC%Cc@cc@Ck....",
                "....kCCCCCccc@cAAAAAAAAAAAAACCCCACCccccccccdk...",
                "...kCCCCcccccccddCCrCCCCdCCCCCCACCcccccccccddk..",
                "...kCCCccccccccddcceccccdccccccACccccccdcccddk..",
                "...kCCcccccccccddcceccccdccccccACcccccccdccddk..",
                "...kCCceocccccdddccocccccccccccACccccccccddddk..",
                "...kCCcceecccddddccecccccccccccACccccccccddddk..",
                "....kdccccedddddAccceccccccoeeerAdcccccdddddk...",
                "....kAdddddrdddAccccrrrrrceccccccAdddddddddk....",
                "....kCAAdddddAAccccr****ercccccccdAAdddddAk.....",
                ".....kccAAAAAACccccr*Z*oercccccccdddAAAAAAk.....",
                ".....kCccccddACccccr**ooercccccccdddddddAAk.....",
                ".....kCddccddACccccr*oooerccccccddddddddAAk.....",
                ".....kCccddddAddddcreeeeerccccddddddddddAAk.....",
                ".....kCccccddACcccecrrrrrcccccccddddcdddAAk.....",
                ".....kCccccddkCcceccccccceccccccdddkcdddAAk.....",
                ".....kdddddddkAArAAAAAAAeAAAAAAAAAAkcdddAAk.....",
                ".....kdAAAAAdkccccccccccecccccccccAkcdddAAk.....",
                ".....kkcCccckkcddddAdddddddddAddddAkAAAAAAk.....",
                "....kCccdAdCdAAddddAdddddddddAddddAkAAAccAk.....",
                "....kcAddAddAAAAAAAAAAAAAAAAAAAAAAAkkcccddck....",
                "...kccAddAddAAAAcccddkkkkkkCccccddkkccAddAddk...",
                "...kcdAddAddAAAAcccddk....kCcdccddkccdAddAddAk..",
                "...kcdAddAddAAAAdccddk....kCccddddkcddAddAddAk..",
                "....kdAddAAAAAAccddddk....kCccccddkcddAddAdAAk..",
                "....kAAAAAAAAAAccccddk....kCccccddkkddAdddAAk...",
                ".....kkAAAAAkkAAAAAAAk...kkAAAAAAAkkkAddAAAk....",
                ".......kkkkccccccccccAk.kccccccccccAkkkAAkk.....",
                ".........kccdddddddddAkkccdddddddddAk..kk.......",
                ".........kcddAdddddddAkkcddAdddddddAk...........",
                ".........kcddAdddddddAkkcddAdddddddAk...........",
                ".........kcddAdddddddAkkcddAdddddddAk...........",
                ".........kAAAAAAAAAAAAkkAAAAAAAAAAAAk...........",
                "..........kkkkkkkkkkkk..kkkkkkkkkkkk............",
                "................................................",
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
                "................................................",
                "................................................",
                ".............................kkkkkk.............",
                "...........................kkCCCCCCk............",
                "..........................kCCCCCCCCdk...........",
                ".........................kCCCCCccccddk..........",
                "........................kCCCUUcccccddk..........",
                "........................kCUUUUUcccccdk..........",
                ".......................kCnnnUUncccccdk..........",
                ".......................kCUUUUUncccccddk.........",
                "...............kkkkkkk.kC==U=Incccccddk.........",
                "..............k===NCCCkkCUUUUUncccccddk.........",
                "...........kkkk=IINCcccCCCIIIUncccccddk.........",
                "..........kHHHH=NNNCcccccccIIUUccccdddk.........",
                "..........kHJkJNNNNcccccccccNUcccccdddk.........",
                "........kkk77777kkkdcdcccccccddddddddk..........",
                ".......kJHYHHJJ777kkkcdccccccddddddddk..........",
                "......kJHYHHJJJ//;7kkkcddccccdccccLcdkkk........",
                ".....kJHHYHHJJJ|//;7k.kccdcccdccccLVddCdk.......",
                ".....kJHHYHHJJJJ7777k..kcccdddccccLVddccdk......",
                ".....kJHHYHHJJJJ7777k..kBdddBBcccccVcddcdk......",
                ".....k777777777777777kkCCBBBcccccccVcddcddk.....",
                ".....kHHHHHHHHHHHHHHHkkCCCcccccdCcLVcddccdk.....",
                ".....kJHHYHHJJJJ7777k.kCCccccccdCcVcccddcdk.....",
                ".....kJHHYHHJJJJ7777k.kCCccccccdCcVcccddcddk....",
                "....kJHHYHHH//JJJ7/77kkCCccccccdCcVcdcddccdk....",
                "....kJHHYHHHJ/;JJ7|/7kCCCccccccCccVcdcdddcdk....",
                "....kJHHYHHHJJJJJ77/7kCCCccccccCccVcdCdddcddk...",
                "....kJHHYHHHJJJJJ77;7kCCcccccccCcccLdCcddccdk...",
                "....kJHHYHHHJJJJJ7777kCCcccccccCcccVcCcddccdk...",
                "....k77777777777777777CCcdcccccCcccVcdCddccddk..",
                "....kHHHHHHHHHHHHHHHHHCCcdccccdccccccdCddddddk..",
                "...kJHHHYHHHJJJJJ77777CCcdccccdccccccdCddddddk..",
                "..kJJHHYY/HHJJJJJ777//7CcdccccdccccccdCcddddk...",
                ".kJJHHYYHH/;JJJJJJ77|/;7cdccccdccccccdccddkkk...",
                ".kJHHHHHHHHHHHHHHHHHHHHHdcccccdccccccdccd=kk=k..",
                ".kJ7777777777777777777777cccccdccccccdccdIdkIk..",
                ".kJJJJJJJJJJJJJJJJJJJJJJdcccccdcccccccdcdddkk...",
                "..kkLkkkkkkkkkkkkkkkLCCcdcccccdcccccccdccddk....",
                "...kVk..k..kHH7k...kVCCddcccdddccddcccddcddk....",
                "...kVk.k=k.k777k.k.kVCddddcLdddcddddcLdddddk....",
                "....k..kIk..kkk.k=kkdVdddddVdddddddddVddddVdk...",
                "........k.......kIkkdkkdddkVdddkkdddkVdddkVdk...",
                ".................kIIIIIIdIIIIdIIIIdIIIIdIIIInk..",
                "................kIINNNNNNNNNNNNNNNNNNNNNNNNNnk..",
                "................knnnnnnnnnnnnnnnnnnnnnnnnnnnnk..",
                ".................kkkkkkkkkkkkkkkkkkkkkkkkkkkk...",
                "................................................",
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
                "................................................",
                "....................k...k...k...................",
                ".......kk..........kZk.kZk.kZk..................",
                "......kZZk......kkkkZkkkZbkkZkkkk...............",
                ".....kZZ==k....kZ=bZ=biZ=RbZ=bZ=bk..............",
                "...kkZZZZ==kkk.kZRRRZRRRZRRRZRRRbk..............",
                "..k==Z=Z==RRRRk.kbbbbbbbbbbbbbbbk...............",
                "...kk====RRkkk..kiiFFFFFFFFFFffk................",
                ".....k==RRk....kiiffffffFFFFFffk................",
                "......kRRk.....kiFZWWWX9FFFFfiffk...............",
                "......kiMk.....kiZXWWXX9FFFFfiffk...............",
                "......kiMk.....kiZBWWBW9FFFFfiffk...............",
                "......kiMk.....kiZBWWBW9FFFFFfifk...............",
                "......kiMk.....kiZWWWRW9FFFFFfifk...............",
                "......kiMk.....kiFZWWRW9FFFFFfiffk..............",
                "......kiMk.....kiFZxxWX9FFfFFfiffk..............",
                "......kiMk.....kiFFZXXffFFfFFfiffk..............",
                "......kiMk.....kiFCCNFFFFFfFFFfifk..............",
                "......kiMk.....kfCCccNfffffffffifkk.............",
                "......kiMk.....kCCccddNffffffffiffCk............",
                "......kiMk...kkCCcccdddNffffffffffdck...........",
                "......kiMk..kCCCccccddNyygjNccccccdddk..........",
                "......kiMk.kCCcccccdddNygjjNccccccddcdk.........",
                "......kZZXkCCccccccdddNijjfNcccccccdddk.........",
                "......kZWXCCcccccccddNNiFFfNcccccccddcdk........",
                "......kXXXCcccccccdddNNiFFfNccccccccdcck........",
                "......kiMCccccccccdddNNiFFfNcccccdccddcdk.......",
                "......kiMkccccccccddNdNiFFfNcccccdcccdccdk......",
                "......kiMkCccNNNNNNdNdNiFFfNccccCdcccddcdk......",
                "......kiMkddddddddddNcNiFFfNccccCcdccddccdk.....",
                "......kiMkkddddddddNdcNiFFFNcccccCdcccddddk.....",
                "......kiMkkkkkCCcccCdNiiFFFfNccccCdcccdddddk....",
                "......kiMkkk=CC=cc=cdNiFFFFfNccccCdccccdddk.....",
                "......kiMkZkRCCRccRdcNiFFFFfNcccccCdcccddkZk....",
                "......kiMkZkkCCRccCdcNiFFFFfNcccccCdccc=d==k....",
                "......kiMZ=bkCCcccCdcNiFFFFfNcccccCdcccRdR=bk...",
                "......kiMZ=bkCCccCcdcNiFFFFfNcccccCdccccdR=bk...",
                "......kiMZ=bCCCccCcdcNiFFFFfNccccccCccccZ==bk...",
                "......kiMZ=bCCCZcCdccNiFFFFfNccccccCdcccZ==bk...",
                "......kiM==RbCZ=bcdccNiFFFFfNcccccccdZccZ==Rbk..",
                "......kiM==RbCZ=bcdccNiFFFFfNcccccccZ=bcZ==Rbk..",
                "......kiM==RbCZ=bcdccNiFFFFfNcccccccZ=bZ===Rbk..",
                "......kiM==RbZ==RbZ=bNiFFFFfNdddddddZ=bZ===Rbk..",
                "......kiM==RRZ==RbZ=bNiFFFFfNdddddddZ=bZ===RRbk.",
                "......kiM==RZ===RRZ=bNffffffNddddddZ==Rb===RRbk.",
                "......kMM==RZ===RZ==RbkkkkkkkkkkkkkZ==Rb===RRbk.",
                ".......kkkkkkkkkkkkkkk.............kkkkkkkkkkk..",
                "................................................",
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
                "................................................",
                "....k..............k..k..k..k...................",
                "...kik............kekkekkekkek......kkkkk.......",
                "...kimk...........kekkekkekkek.....kZZZZZk......",
                "...kimk...........kerkerkerkerk....kZxxxXk......",
                "...kimMk..........keeeeeeeeeTk.....kZWWWXk......",
                "....kimMk.........kT*TT*TT*TTk.....kZxxWXk......",
                "....kimMk.........kqqqqqqqQQQk.....kZWWWXk......",
                ".....kimMk........kqQsssqqQQQqk....kZXXXk.......",
                ".....kimMk........khssshsqQQQqk.....kkkk........",
                "......kimMk.......kthshssqQQQqk.................",
                "......kimMk.......ktktsksqQQQqk..........kkkk...",
                ".......kimMk.....kSskstkSqQQQQk.........kZZZZk..",
                ".......kimMk......kSsssssqQQQQqkkk......kZxxXk..",
                "........kimMk.kkkkkksSSsSqQQQQqrrrk.....kZWWXk..",
                "........kimMkkCCCCCdCsssSqQQQQqCCTTk....kZxWXk..",
                ".........kimMC]]]]]dCCSS(C1QQQqcccTDk...kZXXk...",
                ".........kimMCCCcccddccc(cqqqqqcccdTDk...kkk....",
                "..........kimMCcccdddccc(cccddccccddTk..........",
                "..........kimMcccddddccc(cccddccccddTTk.........",
                "...........kimMddddd(ccc(cccddcccdddTTDk........",
                "...........kimMCCdd(]ccc(cccddcccdddTTTDk.......",
                "............kimMcdgyc]cc(cccdddddddTDTTDk.......",
                "............kimMggjjc]cc(cccdddddddTDTTDDk......",
                "............kkggjjCccccc(cccddTTTrTTDTTTDk......",
                "...........kggjjddCccccc(cccddTTTrTTTDTTDk......",
                "..........kgjjimm8Cccccc(cccddTTTrTTTDTTDDk.....",
                "..........k6kkmMM8TTTTTTgTTTTTTTTrTTTDTTTDk.....",
                "...........k.k8888w((((DjDDDDDDTTrTTTDTTTDk.....",
                "..............kkChwimm8(CCCCCCdTTTrTTDTTTDDk....",
                "...............k]]hmMM8(]]]]]]]TTTrTTDTTTTDk....",
                "..............kCCch8888(c(ccc(ddTTrTTTDTTTDk....",
                "...kkkkk......kCccgc(cccc(ccc(ddTTrTTTDTTTDDk...",
                "..kZZZZZk.....kCccjc(cccc(ccc(cdTTrTTTDTTTTDk...",
                ".kZxxxXk......kCccc(ccccc(cccc(dTTrTTTDTTTTDk...",
                ".kZWWWXk.....kddddd(ddddd(dddd(ddTrTTTDTTTTDDk..",
                ".kZxxWXk.....kddddd(ddddd(dddd(ddTrTTTDTTTTDDk..",
                ".kZWWWXk......kkkCcccdkkkCcccdkkrTTrTTTDTTTDDk..",
                ".kZXXXk.........kC]ccdk.kC]ccdkkrTTrTTTDTTTDDk..",
                "..kkkk..kkkk....kC[ccdk.kC[ccdkkrTTrTTTDTTTDDk..",
                ".......kZZZZk...kCcccdk.kCcccdk.kTTrDTTTDDTDDk..",
                ".......kZxxXk...kCcccdk.kCcccdk.krTrDDTTDDDDDk..",
                ".......kZWWXk...kCcccdk.kCcccdk.krDDDkkDDDkkDk..",
                ".......kZXXk...k]]CCCdkk]]CCCdk..kDDDk.kDDkkDk..",
                "........kkk...k]Cccccdk]Cccccdk...kDk...kk..k...",
                ".............kddddddddddddddddk....k............",
                "..............kkkkkkkkkkkkkkkk..................",
                "................................................",
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
                "................................................",
                "....................kkkkk............kk.........",
                ".................kkk11111kkk........k11k........",
                "................k111QQQQQ111k......k1Q$k........",
                "...............k1QQQCCCCCQQQ1k....k1Q$k....k....",
                "..............k1QCCCcccccCCCQ1k...k1Q$k...k1k...",
                ".............k1QCcc211111cccCQ1kkk1QP$k..k1Q1k..",
                ".............k1QC22cccccc1cccCQ$+$QCP$k.k1Q$k+k.",
                "............k1QC2ccccccccc1cccCQPQCCP$kk1QP$kk..",
                ".....k......k1Q2cccccccc$+$QcccCQCcC+$k1QP$k....",
                "....k+k.....k1Q2cccccccccPcQccccCcccCQ1QCP$k....",
                ".....k.....k1Q2ppppccccccccQcccccccccCQCCP$k....",
                "...........k12pZkk+pcccccccQccccccccccCCP$k.....",
                "..........k1Qp++kk++pccccccQWccc1111cccCP$k.....",
                ".........k1QCcppkkppcccccccQcccc1QQQccccCQ1k....",
                ".........k1QCccppppccccccccQccc1kQkQ1cccCP$k....",
                ".........k1QCccccccccccccccQccc1QQQQQcccCP$k....",
                "........k1QQcccccccccccccccQccc1QQQQQccccCQ1k...",
                "........k1QQcccccccccccccccQcccc1kQQcc$+$CP$k...",
                "........k1QQc$$$$$$$$$$$cccccccc1QQQcccPcCP$k...",
                ".......k1QCQPXXkXXkXXkXXPccccccccccccccccCP$k...",
                ".......k1QCQPkk2kk2kk2kPcccccccccccccccccCP$k...",
                ".......k1QCQcc$$$$$$$$$cccccccccccccccccccCQ1k..",
                "...k...k1QCQccccccccccccccccc$+$cccccccccWCP$k..",
                "..k+k..k1QCQccccccccccccccccccPcccccccccccCP$k..",
                "..k2k...k1QQccccccccc$+$ccccccccccccc111ccCP$k..",
                "...k2k..k1QQccccccccccPcccccpccccccc1QQQ1cCP$k..",
                "...k2kkkk1Q1ccccWccccccccccpZpcccccckQkQQcCP$k..",
                "....k221k11Qccccccccccccccccpccccccc1QQQQcCP$k..",
                "...k22111QQQCcccccccccccc111cccccccc1QQQQcCP$k..",
                "..k2k211Qk1QCccccccccccc1QQQ1cccccccckQQcCP$k...",
                ".k+kk2k2k.k1QCcccccccccckQkQQccccc$+$ccccCP$k...",
                "..kk2kk2k..k1QCcc$+$cccc1QQQQccccccPcccccCP$k...",
                "...k2kk2k..k1QCcccPccccc1QQQQccccccccccccCP$k...",
                "..k2k.k2k...k1QCccccccccckQQcccccccccccc$+$$k...",
                "..k+k.k2k...k1QCcccccccccccccccccccccccccPP$k...",
                "...k..k+k...k1QCccccc+ccccccccccccccccccccCQ1k..",
                ".......k.....k1QCcccccccpCccccc$+$CCccccccCP$k..",
                ".............k1QCcCCcccpZpCcCCccPCPPCccCCcCP$k..",
                "..............k1QCPPCcCPp$QCPPCcCP$$+CCPPCCP$k..",
                "..............k1QP$$QCP$kk1Q$$QCP$kk1QP$$QCP$k..",
                ".........k.....k1$kk1QP$kk1$kk1QP$kk1Q$kk1QP$k..",
                "........k+k....k1$kk1QP$k.kk.k1QP$kk1$k.k1Q$k...",
                ".........k......kk.k1Q$k.....k1Q$k..kk..k1Q$k...",
                "....................k1$k......k1$k.......k1$k...",
                "....................k1$k......k1$k.......k1k....",
                ".....................kk........kk.........k.....",
                "................................................",
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
