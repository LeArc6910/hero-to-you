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
        static readonly Dictionary<char, string> Colors = new Dictionary<char, string>
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
        };

        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Hero(string classId, string color) => Make("hero:" + classId, Heroes.TryGetValue(classId, out var rows) ? rows : Wanderer, color);

        public static Texture2D Monster(string monsterId, string color) => Make("monster:" + monsterId, Monsters.TryGetValue(monsterId, out var rows) ? rows : Monsters["goblin"], color);

        public static Texture2D Make(string key, string[] rows, string color)
        {
            key += color;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var main = Palette.Hex(color ?? "#888888");
            var shade = Color.Lerp(main, Color.black, 0.35f);
            int height = rows.Length;
            int width = rows[0].Length;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                string row = rows[height - 1 - y];
                for (int x = 0; x < width; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    Color pixel = c switch
                    {
                        '.' => Color.clear,
                        'c' => main,
                        'd' => shade,
                        _ => Colors.TryGetValue(c, out string hex) ? Palette.Hex(hex) : Color.magenta,
                    };
                    pixels[y * width + x] = pixel;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Cache[key] = texture;
            return texture;
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

        public static Texture2D Icon(string name) => name switch
        {
            "heart" => Make("icon:heart", Heart, null),
            "coin" => Make("icon:coin", Coin, null),
            _ => null,
        };
    }
}
