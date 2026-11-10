using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // The pictures over the story pages (stories.csv `art`) and the events that matter most (events.csv `art`),
    // composed in code from the pixel art like everything else: a sky, the ground, a few shapes of the place, and the
    // party walking along the bottom. Scene names: forest, harbor, snow, rift, tomb, gate (the chapters), dawn and
    // dusk (kind and grim endings), reunion (someone saved earlier waits by a fire), shrine, camp (the party around a
    // fire under the stars, between two chapters: Docs/Narrative.md 막간). Unknown names draw just
    // the party on a dusk sky, so new stories never break.
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
                "dawn" => (Palette.Hex("#E89A6A"), Palette.Hex("#FCE3B0"), Palette.Hex("#6E8F5A"), "tree", "sun"),
                "dusk" => (Palette.Hex("#23222E"), Palette.Hex("#6A5E6E"), Palette.Hex("#3A3840"), "stones", "ash"),
                "reunion" => (Palette.Hex("#2E2240"), Palette.Hex("#C8784A"), Palette.Hex("#4A3A2A"), "tree", "waiting"),
                "shrine" => (Palette.Hex("#162236"), Palette.Hex("#6E8CA8"), Palette.Hex("#4A5A4A"), "stones", "glow"),
                "camp" => (Palette.Hex("#0E1226"), Palette.Hex("#34304E"), Palette.Hex("#2A281E"), "tree", "fire"),
                _ => (Palette.Hex("#2A2438"), Palette.Hex("#6E5A6E"), Palette.Hex("#3A3440"), null, null),
            };
            float skyH = r.height * 0.68f;
            Ui.Image(new Rect(r.x, r.y, r.width, skyH + 1), PixelArt.Sky(top, bottom, r.width / Mathf.Max(1, skyH)));
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
            if ((extra == "snowflake" || extra == "ash") && !Settings.LessMotion)
            {
                // Snow falling slowly; on a grim ending, grey ash.
                var flake = extra == "ash" ? new Color(0.7f, 0.68f, 0.72f, 0.6f) : new Color(1, 1, 1, 0.8f);
                for (int i = 0; i < 24; i++)
                {
                    float x = r.x + (float)random.NextDouble() * r.width;
                    float y = r.y + ((float)random.NextDouble() * r.height + time * (10 + i % 5 * 4)) % r.height;
                    Ui.Fill(new Rect(x, y, 2, 2), flake);
                }
            }
            else if (extra == "sun")
            {
                // A low sun on the horizon, in rings.
                float sun = s * 0.9f;
                var c = new Vector2(r.xMax - sun * 1.6f, r.y + skyH - sun * 0.35f);
                for (int ring = 3; ring >= 0; ring--)
                {
                    float d = sun * (0.55f + ring * 0.22f);
                    Ui.Fill(new Rect(c.x - d / 2, c.y - d / 2, d, d), new Color(1f, 0.85f, 0.45f, ring == 0 ? 0.95f : 0.18f));
                }
            }
            else if (extra == "waiting")
            {
                // Someone saved in an earlier chapter waits by a fire, facing the party.
                float fire = s * 0.7f;
                float flicker = Settings.LessMotion ? 0 : Mathf.Sin(time * 9) * 0.08f;
                Ui.Image(new Rect(r.x + r.width * 0.62f, r.yMax - fire - 6, fire, fire), PixelArt.Icon("camp"), new Color(1, 1, 1, 0.9f + flicker));
                float who = Mathf.Min(r.height * 0.36f, 56);
                Ui.Image(new Rect(r.x + r.width * 0.62f + fire, r.yMax - who - 4, who, who), PixelArt.Hero("wanderer", "#9A8F86"), null, true);
            }
            else if (extra == "glow")
            {
                // The shrine, with a soft pulse of light.
                float shrine = s * 1.1f;
                float pulse = Settings.LessMotion ? 0.25f : 0.2f + Mathf.Abs(Mathf.Sin(time * 2)) * 0.15f;
                var at = new Rect(r.x + (r.width - shrine) / 2, r.y + skyH - shrine * 0.9f, shrine, shrine);
                Ui.Fill(new Rect(at.x - shrine * 0.3f, at.y - shrine * 0.2f, shrine * 1.6f, shrine * 1.3f), new Color(0.75f, 0.85f, 1f, pulse));
                Ui.Image(at, PixelArt.Icon("shrine"));
            }
            else if (extra == "fire")
            {
                // Stars that twinkle, a fire in the middle with its glow, and the party sitting around it.
                for (int i = 0; i < 28; i++)
                {
                    float x = r.x + (float)random.NextDouble() * r.width;
                    float y = r.y + (float)random.NextDouble() * skyH * 0.7f;
                    float twinkle = Settings.LessMotion ? 0.7f : 0.45f + Mathf.Abs(Mathf.Sin(time * 1.5f + i * 1.7f)) * 0.5f;
                    Ui.Fill(new Rect(x, y, 2, 2), new Color(1f, 0.95f, 0.85f, twinkle));
                }
                // The tent pitched behind, then the fire: its light only on the ground, in a few soft bands.
                float tent = s * 0.75f;
                Ui.Image(new Rect(r.x + r.width * 0.7f, r.y + skyH - tent * 0.8f, tent, tent), PixelArt.Icon("camp"), new Color(1, 1, 1, 0.8f));
                float fire = s * 0.62f;
                float flicker = Settings.LessMotion ? 0 : Mathf.Sin(time * 9) * 0.04f;
                var at = new Rect(r.x + (r.width - fire) / 2, r.yMax - fire - 4, fire, fire);
                float groundTop = r.y + skyH + 2;
                for (int band = 0; band < 3; band++)
                {
                    float w = fire * (4.2f - band * 1.2f);
                    Ui.Fill(new Rect(at.center.x - w / 2, groundTop, w, r.yMax - groundTop), new Color(1f, 0.62f, 0.28f, 0.06f + flicker * 0.5f));
                }
                Ui.Image(at, PixelArt.Icon("flame"), new Color(1, 1, 1, 0.95f));
                if (party == null) return;
                float sitter = Mathf.Min(r.height * 0.34f, 52);
                for (int i = 0; i < party.Count; i++)
                {
                    // Two on the left facing the fire, the rest on the right facing back.
                    bool left = i % 2 == 0;
                    float x = left ? at.x - sitter * (1.05f + i / 2 * 0.9f) : at.xMax + sitter * (0.05f + i / 2 * 0.9f);
                    float breathe = Settings.LessMotion ? 0 : Mathf.Abs(Mathf.Sin(time * 1.2f + i)) * sitter * 0.02f;
                    Ui.Image(new Rect(x, r.yMax - sitter - 4 - breathe, sitter, sitter), PixelArt.Hero(party[i].id, party[i].color), null, !left);
                }
                return;
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
