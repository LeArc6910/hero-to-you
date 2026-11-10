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

        // ---- The battle's ground in perspective (user request 2026-11-10: the field seen at an angle) ----

        // The picture's rows down to a little below its horizon stay as drawn (the far ground); nearer, the ground is a
        // plane in the picture's own ground colours: lines running off to a vanishing point behind the foes, cross
        // lines closer together toward the horizon, and grit that grows with nearness. Made once per place and size.
        const int FarGroundRows = 6;
        static readonly Dictionary<(string, int, int, int), Texture2D> Grounds = new Dictionary<(string, int, int, int), Texture2D>();

        public static bool DrawBattleGround(Rect field, string scene, float horizonY, float vanishingX, Color tint)
        {
            var picture = PixelArt.Picture(scene);
            if (picture == null) return false;
            float w = picture.width, h = picture.height, horizonRow = h * 2 / 3;
            float s = field.width / w;
            float top = horizonY - horizonRow * s;
            int keep = (int)horizonRow + FarGroundRows;
            float stripBottom = top + keep * s;
            if (Event.current.type != EventType.Repaint) return true;
            var old = GUI.color;
            GUI.color = tint;
            if (top > field.y) Ui.Fill(new Rect(field.x, field.y, field.width, top - field.y + 1), tint * (Color)PixelArt.PictureEdge(scene, true));
            float y0 = Mathf.Max(field.y, top);
            if (stripBottom > y0)
            {
                float from = (y0 - top) / s;
                GUI.DrawTextureWithTexCoords(Ui.ToPixels(new Rect(field.x, y0, field.width, stripBottom - y0)), picture, new Rect(0, (h - keep) / h, 1, (keep - from) / h));
            }
            if (field.yMax > stripBottom)
            {
                int gw = (int)w, gh = Mathf.Max(1, Mathf.CeilToInt((field.yMax - stripBottom) / s));
                int vanishing = Mathf.RoundToInt((vanishingX - field.x) / s);
                var ground = Ground(scene, gw, gh, vanishing, (stripBottom - horizonY) / s);
                GUI.DrawTexture(Ui.ToPixels(new Rect(field.x, stripBottom, field.width, gh * s)), ground);
            }
            GUI.color = old;
            return true;
        }

        static Texture2D Ground(string scene, int width, int height, int vanishing, float belowHorizon)
        {
            var key = (scene, width, height, vanishing);
            if (Grounds.TryGetValue(key, out var made) && made != null) return made;
            var (most, next) = PixelArt.PictureColors(scene, 76, 96);
            Color32 lines = GamePalette.Lighter(most), far = GamePalette.Darker(most);
            var pixels = new Color32[width * height];
            float depth = height + belowHorizon;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[(height - 1 - y) * width + x] = most;
            void Set(int x, int y, Color32 c)
            {
                if (x >= 0 && x < width && y >= 0 && y < height) pixels[(height - 1 - y) * width + x] = c;
            }
            // Cross lines, closer together toward the horizon.
            for (int k = 1; k < 12; k++)
            {
                int y = Mathf.RoundToInt(depth * Mathf.Pow(k / 11f, 1.9f) - belowHorizon);
                for (int x = 0; x < width; x += 1) if (((x + k) & 1) == 0) Set(x, y, lines);
            }
            // Lines from the vanishing point, every so often along the bottom edge.
            for (int k = -10; k <= 10; k++)
            {
                float bottomX = vanishing + k * width * 0.16f;
                for (int y = 0; y < height; y++)
                {
                    float t = (y + belowHorizon) / depth;
                    Set(Mathf.RoundToInt(vanishing + (bottomX - vanishing) * t), y, lines);
                }
            }
            // Grit, bigger nearer; the same for the same place.
            var random = new System.Random(scene.Length * 7919 + width);
            int grains = width * height / 90;
            for (int i = 0; i < grains; i++)
            {
                float t = Mathf.Pow((float)random.NextDouble(), 0.7f);
                int y = (int)(t * (height - 1)), x = random.Next(width);
                int size = 1 + (int)(2 * t);
                for (int dy = 0; dy < Mathf.Max(1, size / 2); dy++)
                    for (int dx = 0; dx < size; dx++) Set(x + dx, y + dy, next);
            }
            // The ground nearest the horizon a step darker, dithered into the rest (it reads as farther).
            for (int y = 0; y < Mathf.Min(height, 6); y++)
                for (int x = 0; x < width; x++)
                    if (((x + y) & 1) == 0 && y < 3 || ((x * 3 + y) % 4 == 0 && y < 6)) Set(x, y, far);
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

        // A picture over a whole screen: as big as covering it needs, but never so big that fewer than `minColumns` of
        // its columns show (a phone held upright would see only the middle, blown up). Then it sits at the bottom and
        // its sky goes on above it.
        public static void DrawCover(Rect r, string scene, int minColumns)
        {
            var picture = PixelArt.Picture(scene);
            if (picture == null || Event.current.type != EventType.Repaint) return;
            float w = picture.width, h = picture.height;
            float s = Mathf.Min(Mathf.Max(r.width / w, r.height / h), r.width / Mathf.Min(minColumns, w));
            float shownH = h * s;
            if (shownH >= r.height)
            {
                DrawWide(r, picture, 0.7f, picture.height);
                return;
            }
            float cols = r.width / s;
            // The picture's own top rows already darken upward; past them the sky stays that colour (a dithered band
            // this tall would show as big checks behind the title).
            Ui.Fill(new Rect(r.x, r.y, r.width, r.height - shownH + 1), PixelArt.PictureEdge(scene, true));
            GUI.DrawTextureWithTexCoords(Ui.ToPixels(new Rect(r.x, r.yMax - shownH, r.width, shownH)), picture, new Rect((w - cols) / 2 / w, 0, cols / w, 1));
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
                    // Clear of the fire (about a third of a hero wide each side of the middle) with a little room, then outward.
                    bool left = i % 2 == 0;
                    float x = left ? r.center.x - hero * (1.6f + i / 2 * 0.9f) : r.center.x + hero * (0.6f + i / 2 * 0.9f);
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
