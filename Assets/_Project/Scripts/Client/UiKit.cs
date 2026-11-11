using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Client
{
    // The interface's parts in the "dark stone and gold" look (user choice 2026-11-11, Docs/WebDemo.md UI 모양):
    // stone frames with a gold hairline, gold corner plates on big panels, bevelled stone buttons with a lip, a blood
    // red plaque for the main action, wine for danger, header bands with swallow-tail ends and gold ornaments,
    // segmented bars with gold end caps, diamond pips. Every part is a small pixel picture made here in palette
    // colours (one art point is two screen points, like the pictures) and stretched in nine parts; the little marks
    // (a pip, the warning sign, ornaments) are letter rows in Resources/Data/Art/ui.txt.
    static partial class Ui
    {
        static Color32 C(char letter) => GamePalette.Letter(letter, out var color) ? color : new Color32(255, 0, 255, 255);

        // A picture at art size, rows from the top, scaled up to whole screen pixels when it becomes a texture.
        sealed class Pix
        {
            public readonly int W, H;
            readonly Color32[] _p;

            public Pix(int w, int h)
            {
                W = w;
                H = h;
                _p = new Color32[w * h];
            }

            public void R(int x0, int y0, int x1, int y1, Color32 c)
            {
                x0 = Mathf.Max(0, x0);
                y0 = Mathf.Max(0, y0);
                x1 = Mathf.Min(W - 1, x1);
                y1 = Mathf.Min(H - 1, y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        _p[y * W + x] = c;
            }

            public void Dot(int x, int y, Color32 c) => R(x, y, x, y, c);

            // A filled rectangle with its corners cut in steps `n` deep.
            public void Notch(int x0, int y0, int x1, int y1, Color32 c, int n)
            {
                for (int i = 0; i < n; i++)
                {
                    R(x0 + n - i, y0 + i, x1 - n + i, y0 + i, c);
                    R(x0 + n - i, y1 - i, x1 - n + i, y1 - i, c);
                }
                R(x0, y0 + n, x1, y1 - n, c);
            }

            public Texture2D Texture(int n, bool repeat = false)
            {
                var pixels = new Color32[W * n * H * n];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        var c = _p[y * W + x];
                        for (int py = 0; py < n; py++)
                            for (int px = 0; px < n; px++)
                                pixels[((H - 1 - y) * n + py) * W * n + x * n + px] = c;
                    }
                var texture = new Texture2D(W * n, H * n, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return texture;
            }
        }

        static readonly Dictionary<string, GUIStyle> Nines = new Dictionary<string, GUIStyle>();

        // Screen pixels to one art point.
        static int ArtPixel => Mathf.Max(1, Mathf.RoundToInt(Scale * 2));

        // Draws picture `key` (made by `make` the first time at this scale) stretched in nine parts over r; the
        // border (in art points) keeps its size and the middle stretches. `outset` art points of the picture lie
        // outside r (corner plates standing out past a frame).
        static void Nine(Rect r, string key, int border, Func<Pix> make, Color? tint = null, int outset = 0)
        {
            if (Event.current.type != EventType.Repaint || r.width <= 0 || r.height <= 0) return;
            int n = ArtPixel;
            string full = key + "@" + n;
            if (!Nines.TryGetValue(full, out var style) || style.normal.background == null)
            {
                var pix = make();
                style = new GUIStyle { border = new RectOffset(border * n, border * n, border * n, border * n) };
                style.normal.background = pix.Texture(n);
                Nines[full] = style;
            }
            var px = Px(r);
            if (outset > 0) px = new Rect(px.x - outset * n, px.y - outset * n, px.width + 2 * outset * n, px.height + 2 * outset * n);
            // A rect smaller than its two borders would fold the picture over itself: keep the corners whole.
            float least = 2 * border * n;
            if (px.width < least) px = new Rect(px.center.x - least / 2, px.y, least, px.height);
            if (px.height < least) px = new Rect(px.x, px.center.y - least / 2, px.width, least);
            var old = GUI.color;
            GUI.color = tint ?? Color.white;
            style.Draw(px, GUIContent.none, false, false, false, false);
            GUI.color = old;
        }

        static string ColorKey(Color c) => Pack(c).ToString("x8");

        // ---- Panels: stone frame, gold hairline, gold corner plates with a red gem when the panel is big ----

        // The frame is three art points (an outline, stone lit along its top, the gold hairline), so a panel's words
        // can start six points in; the corner plates stand two points out past the frame.
        static void StonePanel(Rect r, Color fill, bool gold)
        {
            bool plates = r.width >= 64 && r.height >= 64;
            int size = plates ? 21 : 7, at = plates ? 2 : 0;
            Nine(r, $"panel{plates}{gold}{ColorKey(fill)}", size / 2, () =>
            {
                var p = new Pix(size, size);
                int e = size - 1 - at;
                Color32 k = C('k'), stone = C('Q'), lit = C('1');
                Color32 line = gold ? C('g') : C('j'), shade = gold ? C('j') : C('6');
                p.Notch(at, at, e, e, k, 2);
                p.Notch(at + 1, at + 1, e - 1, e - 1, stone, 1);
                p.R(at + 2, at + 1, e - 2, at + 1, lit);
                p.R(at + 1, at + 2, at + 1, e - 2, lit);
                p.R(at + 2, at + 2, e - 2, e - 2, shade);
                p.R(at + 2, at + 2, e - 2, at + 2, line);
                p.R(at + 2, at + 2, at + 2, e - 2, line);
                p.R(at + 3, at + 3, e - 3, e - 3, fill);
                if (plates)
                {
                    int f = size - 9;
                    Plate(p, 0, 0, false, false);
                    Plate(p, f, 0, true, false);
                    Plate(p, 0, f, false, true);
                    Plate(p, f, f, true, true);
                }
                return p;
            }, null, at);
        }

        // A 9-point gold corner piece with a blood-red gem.
        static void Plate(Pix p, int x, int y, bool flipX, bool flipY)
        {
            Color32 k = C('k'), body = C('j'), lit = C('g'), shade = C('6');
            p.Notch(x, y, x + 8, y + 8, k, 2);
            p.Notch(x + 1, y + 1, x + 7, y + 7, body, 2);
            int top = flipY ? y + 7 : y + 1, bottom = flipY ? y + 1 : y + 7;
            int left = flipX ? x + 7 : x + 1, right = flipX ? x + 1 : x + 7;
            p.R(x + 3, top, x + 5, top, lit);
            p.R(left, y + 3, left, y + 5, lit);
            p.R(x + 3, bottom, x + 5, bottom, shade);
            p.R(right, y + 3, right, y + 5, shade);
            Gem(p, x + 4, y + 4);
        }

        static void Gem(Pix p, int x, int y)
        {
            Color32 k = C('k');
            for (int dy = -2; dy <= 2; dy++)
            {
                int w = 2 - Mathf.Abs(dy);
                p.R(x - w, y + dy, x + w, y + dy, k);
            }
            p.Dot(x, y - 1, C('r'));
            p.R(x - 1, y, x + 1, y, C('r'));
            p.Dot(x, y + 1, C('T'));
            p.Dot(x, y - 1, C('e'));
        }

        // ---- Buttons: a stone block with a lit top row and a two-point lip under it ----

        enum Skin { Normal, Hover, Pressed, Selected, Disabled, Quiet, Primary, PrimaryHover, Danger, DangerHover }

        static void ButtonFace(Rect r, Skin face)
        {
            int size = face == Skin.Primary || face == Skin.PrimaryHover ? 13 : 11;
            Nine(r, "button" + face, size / 2, () =>
            {
                var p = new Pix(size, size);
                int e = size - 1;
                Color32 k = C('k');
                if (face == Skin.Primary || face == Skin.PrimaryHover)
                {
                    bool hover = face == Skin.PrimaryHover;
                    p.Notch(0, 0, e, e, k, 2);
                    p.Notch(1, 1, e - 1, e - 1, C('j'), 2);
                    p.R(3, 1, e - 3, 1, hover ? C('y') : C('g'));
                    p.R(1, 3, 1, e - 3, hover ? C('y') : C('g'));
                    p.R(3, e - 1, e - 3, e - 1, C('6'));
                    p.R(e - 1, 3, e - 1, e - 3, C('6'));
                    p.Notch(2, 2, e - 2, e - 2, k, 1);
                    p.R(3, 3, e - 3, e - 3, hover ? C('e') : C('r'));
                    p.R(3, 3, e - 3, 3, hover ? C('o') : C('e'));
                    p.R(3, e - 4, e - 3, e - 3, C('T'));
                    foreach (var (rx, ry) in new[] { (4, 4), (e - 4, 4) })
                    {
                        p.Dot(rx, ry, C('g'));
                        p.Dot(rx, ry + 1, C('6'));
                    }
                    return p;
                }
                if (face == Skin.Pressed)
                {
                    p.Notch(0, 1, e, e, k, 1);
                    p.R(1, 2, e - 1, e - 1, C('q'));
                    p.R(1, 2, e - 1, 3, k);
                    return p;
                }
                var (outline, ring, body, top, bottom) = face switch
                {
                    Skin.Hover => (k, C('6'), C('1'), C('2'), C('Q')),
                    Skin.Selected => (k, C('g'), C('1'), C('2'), C('Q')),
                    Skin.Disabled => (C('Q'), default, C('q'), C('q'), C('q')),
                    Skin.Quiet => (k, default, C('q'), C('Q'), k),
                    Skin.Danger => (k, C('r'), C(')'), C('['), C('(')),
                    Skin.DangerHover => (k, C('e'), C('['), C(']'), C(')')),
                    _ => (k, default, C('Q'), C('1'), C('q')),
                };
                p.Notch(0, 0, e, e, outline, 1);
                int inner = 1;
                if (ring.a > 0)
                {
                    p.Notch(1, 1, e - 1, e - 1, ring, 1);
                    inner = 2;
                }
                p.R(inner, inner, e - inner, e - inner, body);
                p.R(inner, inner, e - inner, inner, top);
                p.R(inner, e - inner - 1, e - inner, e - inner, bottom);
                return p;
            });
        }

        // ---- Small parts ----

        // A tooltip's card: near black, a gold hairline, tiny gold studs in its corners.
        public static void TipBox(Rect r)
        {
            Nine(r, "tip", 4, () =>
            {
                var p = new Pix(9, 9);
                p.Notch(0, 0, 8, 8, C('k'), 2);
                p.Notch(1, 1, 7, 7, C('6'), 2);
                p.R(3, 1, 5, 1, C('j'));
                p.Notch(2, 2, 6, 6, C('k'), 1);
                p.Dot(3, 3, C('j'));
                p.Dot(5, 3, C('j'));
                p.Dot(3, 5, C('6'));
                p.Dot(5, 5, C('6'));
                return p;
            });
        }

        // A sunken well: a dark inside under a stone edge (light from above).
        public static void Well(Rect r, Color? fill = null)
        {
            var inside = fill ?? (Color)C('k');
            Nine(r, "well" + ColorKey(inside), 3, () =>
            {
                var p = new Pix(7, 7);
                p.Notch(0, 0, 6, 6, C('Q'), 1);
                p.R(1, 0, 5, 0, C('k'));
                p.Notch(1, 1, 5, 5, inside, 1);
                return p;
            });
        }

        public enum Ring { Stone, Gold, Enemy, Wine, Hero }

        // A small plate (a unit's label, a tag): an outline, a coloured ring, a fill with a lit top row.
        public static void Plaque(Rect r, Ring ring = Ring.Stone)
        {
            Nine(r, "plaque" + ring, 3, () =>
            {
                var p = new Pix(7, 7);
                var (edge, fill, top) = ring switch
                {
                    Ring.Gold => (C('g'), C('q'), C('Q')),
                    Ring.Enemy => (C('T'), C('q'), C('Q')),
                    Ring.Wine => (C('T'), C(')'), C('[')),
                    Ring.Hero => (C('1'), C('q'), C('Q')),
                    _ => (C('Q'), C('k'), C('q')),
                };
                p.Notch(0, 0, 6, 6, C('k'), 1);
                p.Notch(1, 1, 5, 5, edge, 1);
                p.R(2, 2, 4, 4, fill);
                p.R(2, 2, 4, 2, top);
                return p;
            });
        }

        // A header band: gold rules above and below a dark middle, the ends cut in a swallow-tail.
        public static void Band(Rect r, bool tails = true)
        {
            Nine(r, "band" + tails, 5, () =>
            {
                const int w = 11, h = 11;
                var p = new Pix(w, h);
                p.R(0, 0, w - 1, h - 1, C('k'));
                p.R(0, 1, w - 1, 1, C('j'));
                p.R(0, 2, w - 1, 2, C('6'));
                p.R(0, h - 3, w - 1, h - 3, C('6'));
                p.R(0, h - 2, w - 1, h - 2, C('j'));
                p.R(0, 3, w - 1, h - 4, C('q'));
                p.R(0, 3, w - 1, 3, C('k'));
                if (tails)
                    for (int y = 0; y < h; y++)
                    {
                        int depth = Mathf.Max(0, 3 - Mathf.Abs(y - h / 2));
                        if (depth == 0) continue;
                        p.R(0, y, depth - 1, y, default);
                        p.R(w - depth, y, w - 1, y, default);
                        p.Dot(depth, y, C('k'));
                        p.Dot(w - 1 - depth, y, C('k'));
                    }
                return p;
            });
        }

        // A screen or section title: gold words on a band between two gold ornaments.
        public static void Title(Rect r, string text, float size = 18, Color? color = null)
        {
            Band(r);
            float wide = Mathf.Min(TextWidth(text, size, true), r.width - 80);
            float mid = r.center.y;
            var left = PixelArt.Icon("orn_left");
            var right = PixelArt.Icon("orn_right");
            if (left != null && r.width >= 140)
            {
                float ow = left.width * 2, oh = left.height * 2;
                Image(new Rect(Mathf.Round(r.center.x - wide / 2 - ow - 8), Mathf.Round(mid - oh / 2), ow, oh), left);
                Image(new Rect(Mathf.Round(r.center.x + wide / 2 + 8), Mathf.Round(mid - oh / 2), ow, oh), right);
            }
            TextFit(new Rect(r.x + 36, r.y, r.width - 72, r.height), text, size, color ?? Palette.Gold, TextAnchor.MiddleCenter, true);
        }

        // A section's heading inside a panel: a gold diamond, the gold words, a gold rule on to the right edge.
        public static void Section(Rect r, string text, float size = 15)
        {
            Mark(new Vector2(r.x + 5, r.center.y), "pip_s");
            float w = Mathf.Min(TextWidth(text, size, true), r.width - 30);
            TextFit(new Rect(r.x + 14, r.y, w + 2, r.height), text, size, Palette.Gold, TextAnchor.MiddleLeft, true);
            float x = r.x + 14 + w + 10;
            if (r.xMax - 4 > x)
            {
                Fill(new Rect(x, Mathf.Round(r.center.y) - 1, r.xMax - 4 - x, 1), Palette.GoldDark);
                Fill(new Rect(x, Mathf.Round(r.center.y), r.xMax - 4 - x, 1), C('6'));
            }
        }

        // A small picture of ui.txt at its own pixel size (two screen points an art point), centred on `at`.
        public static void Mark(Vector2 at, string name, float scale = 2, Color? tint = null)
        {
            var t = PixelArt.Icon(name);
            if (t == null) return;
            Image(new Rect(Mathf.Round(at.x - t.width * scale / 2), Mathf.Round(at.y - t.height * scale / 2), t.width * scale, t.height * scale), t, tint);
        }

        // Resolve as diamonds: full ones gold, spent ones stone sockets. Returns the width used.
        public static float Pips(Vector2 left, int full, int max, bool small = false)
        {
            float step = small ? 9 : 13;
            for (int i = 0; i < max; i++)
                Mark(new Vector2(left.x + step / 2 + i * step, left.y), i < full ? small ? "pip_s" : "pip" : small ? "pip_s_empty" : "pip_empty");
            return step * max;
        }

        // Orange corner brackets just outside r: the target.
        public static void Brackets(Rect r, Color color, float arm = 8, float t = 2)
        {
            r = new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8);
            Fill(new Rect(r.x, r.y, arm, t), color);
            Fill(new Rect(r.x, r.y, t, arm), color);
            Fill(new Rect(r.xMax - arm, r.y, arm, t), color);
            Fill(new Rect(r.xMax - t, r.y, t, arm), color);
            Fill(new Rect(r.x, r.yMax - t, arm, t), color);
            Fill(new Rect(r.x, r.yMax - arm, t, arm), color);
            Fill(new Rect(r.xMax - arm, r.yMax - t, arm, t), color);
            Fill(new Rect(r.xMax - t, r.yMax - arm, t, arm), color);
        }

        // A strip along the top of a screen: stone, lit along its top, closed by a gold rule.
        public static void Strip(Rect r)
        {
            Fill(r, C('q'));
            Fill(new Rect(r.x, r.y, r.width, 2), C('Q'));
            Fill(new Rect(r.x, r.yMax - 3, r.width, 2), C('j'));
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), C('6'));
        }

        // ---- The backdrop: dark stone blocks, faint mortar ----

        static Texture2D _wall;
        static int _wallScale;

        public static void Wall(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            int n = ArtPixel;
            if (_wall == null || _wallScale != n)
            {
                const int w = 20, h = 16;
                var p = new Pix(w, h);
                p.R(0, 0, w - 1, h - 1, C('k'));
                p.R(0, 0, w - 1, 0, C('q'));
                p.R(0, 8, w - 1, 8, C('q'));
                p.R(0, 1, 0, 7, C('q'));
                p.R(10, 9, 10, 15, C('q'));
                _wall = p.Texture(n, true);
                _wallScale = n;
            }
            var px = Px(r);
            GUI.DrawTextureWithTexCoords(px, _wall, new Rect(0, 0, px.width / _wall.width, px.height / _wall.height));
        }

        // ---- Bars: an outline, a wine-dark track, the fill lit along its top and shaded along its bottom ----

        public static void Bar(Rect r, float fraction, Color color, Color? back = null, int ticks = 0, bool caps = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            float t = r.height >= 7 ? 1 : 0.5f;
            Fill(r, C('k'));
            var inner = new Rect(r.x + t, r.y + t, r.width - 2 * t, r.height - 2 * t);
            Fill(inner, back ?? Palette.BarBack);
            Color32 main = color;
            float w = inner.width * Mathf.Clamp01(fraction);
            if (w > 0)
            {
                var full = new Rect(inner.x, inner.y, w, inner.height);
                Fill(full, color);
                if (inner.height >= 3)
                {
                    float band = Mathf.Max(1, Mathf.Floor(inner.height / 4));
                    Fill(new Rect(full.x, full.y, w, band), Palette.WithAlpha(GamePalette.Lighter(GamePalette.Snap(main)), color.a));
                    Fill(new Rect(full.x, full.yMax - band, w, band), Palette.WithAlpha(GamePalette.Darker(GamePalette.Snap(main)), color.a));
                }
            }
            if (ticks > 1 && inner.width / ticks >= 4)
                for (int i = 1; i < ticks; i++)
                {
                    float x = Mathf.Round(inner.x + inner.width * i / ticks);
                    Fill(new Rect(x, inner.y + (inner.height >= 4 ? 1 : 0), Mathf.Max(1, t), inner.height - (inner.height >= 4 ? 1 : 0)),
                        x < inner.x + w ? (Color)GamePalette.Darker(GamePalette.Snap(main)) : (Color)C('k'));
                }
            if (caps)
            {
                Color gold = C('j');
                Fill(new Rect(r.x - 1, r.y - 1, 2, r.height + 2), gold);
                Fill(new Rect(r.xMax - 1, r.y - 1, 2, r.height + 2), gold);
                Fill(new Rect(r.x - 1, r.y - 1, 2, 1), C('g'));
                Fill(new Rect(r.xMax - 1, r.y - 1, 2, 1), C('g'));
            }
        }

        // A hero's or a foe's health: ticks every ten points, gold caps, red when low.
        public static void HpBar(Rect r, int hp, int max, bool hero)
        {
            float f = max > 0 ? (float)hp / max : 0;
            var color = !hero || hp * 4 <= max ? Palette.Hp : Palette.HpHero;
            Bar(r, f, color, null, max > 0 ? Mathf.Clamp(max / 10, 0, 12) : 0, true);
        }
    }
}
