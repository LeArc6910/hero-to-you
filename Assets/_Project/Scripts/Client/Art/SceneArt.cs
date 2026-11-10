using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // The pictures over the story pages (stories.csv `art`), composed in code from the pixel art like everything
    // else: a sky, the ground, a few shapes of the place, and the party walking along the bottom. Scene names:
    // forest, harbor, snow, rift. Unknown names draw just the party on a dusk sky, so new stories never break.
    static class SceneArt
    {
        public static void Draw(Rect r, string scene, IReadOnlyList<(string id, string color)> party, float time)
        {
            var (top, bottom, ground, mark, extra) = scene switch
            {
                "forest" => (Palette.Hex("#3A3530"), Palette.Hex("#8A7A62"), Palette.Hex("#3B5236"), "tree", "stones"),
                "harbor" => (Palette.Hex("#22384A"), Palette.Hex("#7895A0"), Palette.Hex("#3F6E7A"), "wave", "bell"),
                "snow" => (Palette.Hex("#5C6E80"), Palette.Hex("#C9D6E0"), Palette.Hex("#AEB9C4"), "peak", "snowflake"),
                "rift" => (Palette.Hex("#1A0C0C"), Palette.Hex("#6B2A1E"), Palette.Hex("#2A1A18"), "stones", "boss"),
                "tomb" => (Palette.Hex("#3A1A10"), Palette.Hex("#C8784A"), Palette.Hex("#9C8A5E"), "stones", "crown"),
                "gate" => (Palette.Hex("#0C0A18"), Palette.Hex("#4A2A5A"), Palette.Hex("#2A2030"), "crack", "boss"),
                _ => (Palette.Hex("#2A2438"), Palette.Hex("#6E5A6E"), Palette.Hex("#3A3440"), null, null),
            };
            const int bands = 8;
            float skyH = r.height * 0.68f;
            for (int i = 0; i < bands; i++)
                Ui.Fill(new Rect(r.x, r.y + skyH * i / bands, r.width, skyH / bands + 1), Color.Lerp(top, bottom, i / (float)(bands - 1)));
            Ui.Fill(new Rect(r.x, r.y + skyH, r.width, r.height - skyH), ground);
            Ui.Fill(new Rect(r.x, r.y + skyH, r.width, 2), Color.Lerp(ground, Color.white, 0.2f));

            float s = Mathf.Min(r.height * 0.42f, 72);
            var random = new System.Random(scene?.GetHashCode() ?? 0);
            if (mark != null)
            {
                // Far shapes on the horizon, then nearer, bigger ones.
                for (int i = 0; i < 9; i++)
                {
                    float size = s * (0.5f + (float)random.NextDouble() * 0.6f);
                    float x = r.x + (float)random.NextDouble() * (r.width - size);
                    float y = r.y + skyH - size * 0.85f;
                    Ui.Image(new Rect(x, y, size, size), PixelArt.Icon(mark), new Color(1, 1, 1, i < 5 ? 0.35f : 0.7f));
                }
            }
            if (extra == "snowflake" && !Settings.LessMotion)
            {
                // Snow falling slowly.
                for (int i = 0; i < 24; i++)
                {
                    float x = r.x + (float)random.NextDouble() * r.width;
                    float y = r.y + ((float)random.NextDouble() * r.height + time * (10 + i % 5 * 4)) % r.height;
                    Ui.Fill(new Rect(x, y, 2, 2), new Color(1, 1, 1, 0.8f));
                }
            }
            else if (extra != null)
            {
                Ui.Image(new Rect(r.xMax - s * 1.3f, r.y + skyH - s * 1.1f, s, s), PixelArt.Icon(extra), new Color(1, 1, 1, 0.9f));
            }
            // The party walking left to right, a step in time.
            if (party == null) return;
            float hero = Mathf.Min(r.height * 0.36f, 56);
            if (Settings.LessMotion) time = 0;
            float walk = (time * 18) % (r.width * 0.25f);
            for (int i = 0; i < party.Count; i++)
            {
                float x = r.x + r.width * 0.12f + walk + i * hero * 0.95f;
                float bob = Mathf.Abs(Mathf.Sin(time * 5 + i)) * hero * 0.05f;
                Ui.Image(new Rect(x, r.yMax - hero - 4 - bob, hero, hero), PixelArt.Hero(party[i].id, party[i].color));
            }
        }
    }
}
