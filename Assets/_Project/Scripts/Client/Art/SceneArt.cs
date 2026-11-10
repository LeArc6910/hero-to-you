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
        // A wide picture (256×96, horizon on row 64) drawn with square points: as wide as the rect allows, cropped at the
        // sides when the rect is tall, and placed so the horizon sits at `horizon` (0 top .. 1 bottom) of the rect where
        // the picture reaches. `rows` limits the picture to its top rows (a battle shows only what is above its ground).
        public static void DrawWide(Rect r, Texture2D picture, float horizon, int rows = 96, Color? tint = null)
        {
            if (picture == null || Event.current.type != EventType.Repaint) return;
            float w = picture.width, h = picture.height, horizonRow = h * 2 / 3;
            float s = Mathf.Max(r.width / w, r.height / Mathf.Min(rows, h));
            float cols = r.width / s, shown = r.height / s;
            float left = (w - cols) / 2;
            float top = Mathf.Clamp(horizonRow - horizon * shown, 0, Mathf.Max(0, Mathf.Min(rows, h) - shown));
            var uv = new Rect(left / w, (h - top - shown) / h, cols / w, shown / h);
            var old = GUI.color;
            GUI.color = tint ?? Color.white;
            GUI.DrawTextureWithTexCoords(Ui.ToPixels(r), picture, uv);
            GUI.color = old;
        }

        // The place behind a battle: the picture as wide as the rect, its horizon (row 64) on `horizonY`, the sky carried
        // up past its top in a darker band and its ground carried down past its bottom. False when there is no picture.
        public static bool DrawBackdrop(Rect r, string scene, float horizonY, Color tint)
        {
            var picture = PixelArt.Picture(scene);
            if (picture == null) return false;
            float w = picture.width, h = picture.height, horizonRow = h * 2 / 3;
            float s = r.width / w;
            float top = horizonY - horizonRow * s, bottom = top + h * s;
            var old = GUI.color;
            GUI.color = tint;
            if (top > r.y)
            {
                var sky = PixelArt.PictureEdge(scene, true);
                float above = top - r.y;
                Ui.Image(new Rect(r.x, r.y, r.width, above + 1), PixelArt.Sky(GamePalette.Darker(sky), sky, r.width / Mathf.Max(1, above)), tint);
            }
            if (bottom < r.yMax) Ui.Fill(new Rect(r.x, bottom - 1, r.width, r.yMax - bottom + 1), tint * (Color)PixelArt.PictureEdge(scene, false));
            float y0 = Mathf.Max(r.y, top), y1 = Mathf.Min(r.yMax, bottom);
            if (y1 > y0 && Event.current.type == EventType.Repaint)
            {
                float from = (y0 - top) / s, to = (y1 - top) / s;
                GUI.DrawTextureWithTexCoords(Ui.ToPixels(new Rect(r.x, y0, r.width, y1 - y0)), picture, new Rect(0, (h - to) / h, 1, (to - from) / h));
            }
            GUI.color = old;
            return true;
        }

        public static void Draw(Rect r, string scene, IReadOnlyList<(string id, string color)> party, float time)
        {
            var picture = PixelArt.Picture(scene);
            if (picture != null)
            {
                DrawPicture(r, scene, picture, party, time);
                return;
            }
            var (top, bottom, ground, mark, extra) = scene switch
            {
                "forest" => (Palette.Hex("#3B3243"), Palette.Hex("#9C8A5E"), Palette.Hex("#3E5430"), "tree", "stones"),
                "harbor" => (Palette.Hex("#1E3448"), Palette.Hex("#8DBAC6"), Palette.Hex("#3E7A6E"), "wave", "bell"),
                "snow" => (Palette.Hex("#5A6376"), Palette.Hex("#C4D2EA"), Palette.Hex("#A0AABB"), "peak", "snowflake"),
                "rift" => (Palette.Hex("#2A1020"), Palette.Hex("#76282C"), Palette.Hex("#2E1D18"), "stones", "boss"),
                "tomb" => (Palette.Hex("#2E1D18"), Palette.Hex("#AD7C4E"), Palette.Hex("#9C8A5E"), "stones", "crown"),
                "gate" => (Palette.Hex("#1B1622"), Palette.Hex("#3B3243"), Palette.Hex("#28222F"), "crack", "boss"),
                "dawn" => (Palette.Hex("#DC9E78"), Palette.Hex("#F8EAB0"), Palette.Hex("#6E9E4F"), "tree", "sun"),
                "dusk" => (Palette.Hex("#28222F"), Palette.Hex("#6C6467"), Palette.Hex("#3B3243"), "stones", "ash"),
                "reunion" => (Palette.Hex("#2A1F40"), Palette.Hex("#AD7C4E"), Palette.Hex("#4A2F24"), "tree", "waiting"),
                "shrine" => (Palette.Hex("#28222F"), Palette.Hex("#4C7A96"), Palette.Hex("#45574A"), "stones", "glow"),
                "camp" => (Palette.Hex("#1B1622"), Palette.Hex("#3B3243"), Palette.Hex("#2E1D18"), "tree", "fire"),
                _ => (Palette.Hex("#28222F"), Palette.Hex("#6C6467"), Palette.Hex("#3B3243"), null, null),
            };
            float skyH = r.height * 0.68f;
            Ui.Image(new Rect(r.x, r.y, r.width, skyH + 1), PixelArt.Sky(top, bottom, r.width / Mathf.Max(1, skyH)));
            Ui.Fill(new Rect(r.x, r.y + skyH, r.width, r.height - skyH), ground);
            Ui.Fill(new Rect(r.x, r.y + skyH, r.width, 2), GamePalette.Lighter(ground));

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
                Ui.Image(new Rect(r.x + r.width * 0.62f, r.yMax - fire - 6, fire, fire), PixelArt.Icon("flame"), new Color(1, 1, 1, 0.9f + flicker));
                float who = Mathf.Min(r.height * 0.36f, 56);
                Ui.Image(new Rect(r.x + r.width * 0.62f + fire, r.yMax - who - 4, who, who), PixelArt.Hero("wanderer", "#9A8F88"), null, true);
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

        // The drawn picture of the place, then what moves on it: falling snow or ash, the shrine's pulse, the camp's
        // firelight, and the party walking (or, at the camp, sitting by the fire in the middle).
        static void DrawPicture(Rect r, string scene, Texture2D picture, IReadOnlyList<(string id, string color)> party, float time)
        {
            DrawWide(r, picture, 0.7f);
            if (Settings.LessMotion) time = 0;
            var random = new System.Random(scene.GetHashCode());
            if (scene == "snow" || scene == "dusk")
            {
                var flake = scene == "dusk" ? new Color(0.7f, 0.68f, 0.72f, 0.55f) : new Color(1, 1, 1, 0.75f);
                for (int i = 0; i < 24 && !Settings.LessMotion; i++)
                {
                    float x = r.x + (float)random.NextDouble() * r.width;
                    float y = r.y + ((float)random.NextDouble() * r.height + time * (10 + i % 5 * 4)) % r.height;
                    Ui.Fill(new Rect(x, y, 2, 2), flake);
                }
            }
            if (scene == "shrine" && !Settings.LessMotion)
                Ui.Fill(r, new Color(0.7f, 0.82f, 1f, 0.05f + Mathf.Abs(Mathf.Sin(time * 2)) * 0.05f));
            if ((scene == "camp" || scene == "reunion") && !Settings.LessMotion)
                Ui.Fill(r, new Color(1f, 0.6f, 0.3f, 0.04f + Mathf.Abs(Mathf.Sin(time * 9)) * 0.025f));
            if (party == null) return;
            float hero = Mathf.Min(r.height * 0.36f, 56);
            if (scene == "camp")
            {
                // Seated on both sides of the fire in the middle of the picture.
                for (int i = 0; i < party.Count; i++)
                {
                    bool left = i % 2 == 0;
                    float x = r.center.x + (left ? -hero * (1.4f + i / 2 * 0.9f) : hero * (0.4f + i / 2 * 0.9f));
                    float breathe = Mathf.Abs(Mathf.Sin(time * 1.2f + i)) * hero * 0.02f;
                    Ui.Image(new Rect(x, r.yMax - hero - 4 - breathe, hero, hero), PixelArt.Hero(party[i].id, party[i].color), null, !left);
                }
                return;
            }
            float walk = (time * 18) % (r.width * 0.25f);
            for (int i = 0; i < party.Count; i++)
            {
                float x = r.x + r.width * 0.12f + walk + i * hero * 0.95f;
                var pose = (int)(time * 6 + i) % 2 == 0 ? PixelArt.Pose.Walk1 : PixelArt.Pose.Walk2;
                Ui.Image(new Rect(x, r.yMax - hero - 4, hero, hero), PixelArt.Hero(party[i].id, party[i].color, Settings.LessMotion ? PixelArt.Pose.Stand : pose));
            }
        }
    }
}
