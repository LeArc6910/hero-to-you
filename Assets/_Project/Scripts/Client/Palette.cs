using UnityEngine;

namespace Game.Client
{
    // The UI's colors: a warm dark board-game look, ash greys with candle gold.
    static class Palette
    {
        public static readonly Color Back = Hex("#14121A");
        public static readonly Color BackLight = Hex("#211D29");
        public static readonly Color Ground = Hex("#2B2631");
        public static readonly Color Panel = Hex("#1D1A24F2");
        public static readonly Color Edge = Hex("#5E5468");
        public static readonly Color Button = Hex("#3A3345");
        public static readonly Color Quiet = Hex("#28232F");
        public static readonly Color Accent = Hex("#B5562F");
        public static readonly Color Selected = Hex("#4E4060");
        public static readonly Color Danger = Hex("#7E2E2E");
        public static readonly Color Ink = Hex("#F1E9DA");
        public static readonly Color Dim = Hex("#8E8597");
        public static readonly Color Gold = Hex("#E8C15A");
        public static readonly Color Hp = Hex("#C8473C");
        public static readonly Color HpHero = Hex("#5DB65A");
        public static readonly Color BarBack = Hex("#0D0B10");
        public static readonly Color Resolve = Hex("#E8C15A");
        public static readonly Color Grit = Hex("#8E7BC8");
        public static readonly Color Success = Hex("#62C15E");
        public static readonly Color Fail = Hex("#6B6272");
        public static readonly Color Sure = Hex("#E8C15A");
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
