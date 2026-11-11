using UnityEngine;

namespace Game.Client
{
    // The UI's colors: dark stone and gold (user choice 2026-11-11, UiKit), all from the palette's dusk, gold, fire
    // and wine ramps.
    static class Palette
    {
        public static readonly Color Back = Hex("#1b1622");
        public static readonly Color BackLight = Hex("#28222f");
        public static readonly Color Ground = Hex("#3b3243");
        public static readonly Color Panel = Hex("#28222fF4");
        public static readonly Color Edge = Hex("#55465a");
        public static readonly Color Button = Hex("#3b3243");
        public static readonly Color Quiet = Hex("#28222f");
        public static readonly Color Accent = Hex("#b8433a");
        public static readonly Color Selected = Hex("#55465a");
        public static readonly Color Danger = Hex("#4a1a2a");
        public static readonly Color Ink = Hex("#ece4d6");
        public static readonly Color Dim = Hex("#9a8f88");
        public static readonly Color Gold = Hex("#e3c766");
        public static readonly Color GoldLight = Hex("#f8eab0");
        public static readonly Color GoldDark = Hex("#a27b36");
        public static readonly Color DangerInk = Hex("#f5a04a");
        public static readonly Color Hp = Hex("#b8433a");
        public static readonly Color HpHero = Hex("#6e9e4f");
        public static readonly Color BarBack = Hex("#2a1020");
        public static readonly Color Resolve = Hex("#e3c766");
        public static readonly Color Grit = Hex("#7c6cd6");
        public static readonly Color Success = Hex("#62C15E");
        public static readonly Color Fail = Hex("#6B6272");
        public static readonly Color Sure = Hex("#e3c766");
        public static readonly Color Damage = Hex("#FF6E5A");
        public static readonly Color Heal = Hex("#7CE07A");
        public static readonly Color Intent = Hex("#E59A6B");
        public static readonly Color Journal = Hex("#D9C7A0");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }

        public static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
    }
}
