using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game.Client
{
    public enum Look
    {
        Normal,
        Primary,
        Selected,
        Danger,
        Quiet,
    }

    // Drawing for every screen, in points. A browser draws the game at the device's full pixel density (about three
    // pixels a point on phones), and big monitors get a little zoom, so one point is Scale pixels. Everything is
    // drawn straight in pixels (the GUI matrix stays identity) with the font asked for at its pixel size, so text is
    // as sharp as the screen allows; IMGUI would otherwise draw letters at the point size and stretch them.
    // Screens lay out with explicit rects (no GUILayout). Every button goes through Button so the cloud's playtests
    // can find and press it with a real click or tap (Docs/Playtest.md): while Probing, the last repaint's buttons and
    // texts are kept in LastDrawn and LastWritten.
    static class Ui
    {
        const string FontPath = "Fonts/NotoSansKR-Game";

        static Font _font;
        static GUIStyle _text;
        static Texture2D _white;
        static readonly GUIContent Content = new GUIContent();

        public static float Scale { get; private set; } = 1;
        public static float Width { get; private set; } = 800;
        public static float Height { get; private set; } = 600;

        // A finger was the last thing to touch the page: bigger targets, no hover.
        public static bool Touch { get; private set; }
        public static bool Portrait => Height > Width * 1.05f;

        // Short like a phone held sideways.
        public static bool Short => Height < 460;

        // Call at the start of every OnGUI.
        public static void Begin()
        {
            if (_font == null) _font = Resources.Load<Font>(FontPath);
            if (_text == null)
            {
                _text = new GUIStyle { font = _font, richText = false, clipping = TextClipping.Overflow };
                _text.normal.textColor = Color.white;
            }
            GUI.matrix = Matrix4x4.identity;
            float ratio = WebBridge.PixelRatio();
            float w = Screen.width / ratio;
            float h = Screen.height / ratio;
            float zoom = Mathf.Clamp(Mathf.Min(w / 1100f, h / 650f), 1f, 2f) * Settings.TextSize / 100f;
            Scale = ratio * zoom;
            Width = Screen.width / Scale;
            Height = Screen.height / Scale;
            Touch = WebBridge.TouchInput();
            Origins.Clear();
            Origins.Push(Vector2.zero);
            Views.Clear();
            StartProbe();
            TrackPress();
            TrackKey();
        }

        // ---- Keys (PC, Docs/WebDemo.md 키보드): the key pressed in this event, taken by the first thing asking ----

        static KeyCode _key;

        static void TrackKey()
        {
            var e = Event.current;
            _key = e.type == EventType.KeyDown ? e.keyCode : KeyCode.None;
        }

        // True once when `key` was pressed (Space also answers Enter). Nothing under a modal window answers.
        public static bool Key(KeyCode key)
        {
            if (key == KeyCode.None || _key == KeyCode.None || !GUI.enabled) return false;
            bool match = _key == key || (key == KeyCode.Space && (_key == KeyCode.Return || _key == KeyCode.KeypadEnter))
                || (key == KeyCode.Minus && _key == KeyCode.KeypadMinus) || (key == KeyCode.Equals && _key == KeyCode.KeypadPlus);
            if (!match) return false;
            _key = KeyCode.None;
            Event.current.Use();
            return true;
        }

        // The number key for the n-th (0-based) item of a list: 1 to 9, then none.
        public static KeyCode Number(int index) => index >= 0 && index < 9 ? KeyCode.Alpha1 + index : KeyCode.None;

        static string KeyName(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
            if (key >= KeyCode.A && key <= KeyCode.Z) return key.ToString();
            switch (key)
            {
                case KeyCode.Space: return "Space";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Tab: return "Tab";
                case KeyCode.Minus: return "−";
                case KeyCode.Equals: return "+";
                default: return "";
            }
        }

        // ---- Glow: the button a guide wants pressed (GameRoot.Tutorial.cs), by its label's start or its row ----

        public static string GlowLabel;
        public static string GlowRow;
        public static string[] GlowRows;

        static bool Glows(string label, string row) =>
            (GlowLabel != null && label != null && label.StartsWith(GlowLabel, System.StringComparison.Ordinal))
            || (GlowRow != null && row == GlowRow)
            || (GlowRows != null && row != null && System.Array.IndexOf(GlowRows, row) >= 0);

        static void Glow(Rect r, string label, string row)
        {
            if (!Glows(label, row) || !GUI.enabled) return;
            float t = Settings.LessMotion ? 1 : (Mathf.Sin(Time.unscaledTime * 5) + 1) / 2;
            Frame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Color.Lerp(Palette.Gold, Color.white, t * 0.6f), 2 + t);
        }

        // The key's name in a corner of a button, for mouse players (phones have no keys).
        public static void KeyHint(Rect r, KeyCode key)
        {
            if (Touch || key == KeyCode.None || r.width < 40) return;
            string name = KeyName(key);
            if (name.Length == 0) return;
            Text(new Rect(r.xMax - 40, r.y + 1, 37, 12), name, 8.5f, new Color(1, 1, 1, 0.42f), TextAnchor.UpperRight);
        }

        public static Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                return _white;
            }
        }

        static Rect Px(Rect r) => new Rect(r.x * Scale, r.y * Scale, r.width * Scale, r.height * Scale);

        // A rect in points as screen pixels, for drawing with GUI directly.
        public static Rect ToPixels(Rect r) => Px(r);

        // The pointer in points, within the current area.
        public static Vector2 Pointer => Event.current.mousePosition / Scale;

        public static bool Hovered(Rect r) => !Touch && r.Contains(Pointer);

        public static void Fill(Rect r, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(Px(r), White);
            GUI.color = old;
        }

        public static void Frame(Rect r, Color color, float thickness = 1)
        {
            Fill(new Rect(r.x, r.y, r.width, thickness), color);
            Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            Fill(new Rect(r.x, r.y, thickness, r.height), color);
            Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        // A panel: dark fill inside a pixel frame whose lit edge is `edge`.
        public static void Panel(Rect r, Color? fill = null, Color? edge = null)
        {
            var inside = fill ?? Palette.Panel;
            PixelFrame(r, inside, Palette.Back, edge ?? Palette.Edge, Color.Lerp(inside, Color.black, 0.35f));
        }

        // ---- Pixel frames (user decision 2026-11-10: the game is pixel art, the screen's frames too) ----

        // A small pixel picture stretched in nine parts: an outline with its corners cut, a one-point bevel lit along the
        // top and left and shaded along the bottom and right, the fill inside. A frame point is two screen points, as big
        // as a picture's point. One style per colours and screen scale, made the first time it is drawn.
        static readonly Dictionary<(int, int, int, int, int), GUIStyle> Frames = new Dictionary<(int, int, int, int, int), GUIStyle>();

        static int Pack(Color c) => ((int)(c.r * 255) << 24) | ((int)(c.g * 255) << 16) | ((int)(c.b * 255) << 8) | (int)(c.a * 255);

        public static void PixelFrame(Rect r, Color fill, Color outline, Color lit, Color shaded)
        {
            if (Event.current.type != EventType.Repaint) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(Scale * 2));
            var key = (Pack(fill), Pack(outline), Pack(lit), Pack(shaded), n);
            if (!Frames.TryGetValue(key, out var style) || style.normal.background == null)
            {
                const int size = 7;
                var texture = new Texture2D(size * n, size * n, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var pixels = new Color32[size * n * size * n];
                for (int ay = 0; ay < size; ay++)
                    for (int ax = 0; ax < size; ax++)
                    {
                        bool corner = (ax == 0 || ax == size - 1) && (ay == 0 || ay == size - 1);
                        Color c = corner ? Color.clear
                            : ax == 0 || ay == 0 || ax == size - 1 || ay == size - 1 ? outline
                            : (ay == 1 && ax < size - 2) || (ax == 1 && ay < size - 2) ? lit
                            : ay == size - 2 || ax == size - 2 ? shaded
                            : fill;
                        Color32 c32 = c;
                        // Rows run bottom-up in a texture; ay counts from the top.
                        for (int py = 0; py < n; py++)
                            for (int px = 0; px < n; px++)
                                pixels[((size - 1 - ay) * n + py) * size * n + ax * n + px] = c32;
                    }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                style = new GUIStyle { border = new RectOffset(2 * n, 2 * n, 2 * n, 2 * n) };
                style.normal.background = texture;
                Frames[key] = style;
            }
            var old = GUI.color;
            GUI.color = Color.white;
            style.Draw(Px(r), GUIContent.none, false, false, false, false);
            GUI.color = old;
        }

        public static void Image(Rect r, Texture texture, Color? tint = null, bool flip = false)
        {
            if (texture == null || Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = tint ?? Color.white;
            var px = Px(r);
            if (flip) GUI.DrawTextureWithTexCoords(px, texture, new Rect(1, 0, -1, 1));
            else GUI.DrawTexture(px, texture, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        public static void Bar(Rect r, float fraction, Color color, Color? back = null)
        {
            Fill(r, back ?? Palette.BarBack);
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), color);
        }

        static GUIStyle Style(float size, TextAnchor anchor, bool bold, bool wrap)
        {
            _text.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * Scale));
            _text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            _text.alignment = anchor;
            _text.wordWrap = wrap;
            return _text;
        }

        // Text in a rect. With wrap, lines break between words (Wrap).
        public static void Text(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false, bool wrap = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (Event.current.type != EventType.Repaint) return;
            if (Probing) Writing.Add(text);
            var style = Style(size, anchor, bold, wrap);
            if (wrap) text = Wrap(text, size, r.width, bold);
            style.normal.textColor = color;
            Content.text = text;
            // For the playtests: a line wider than its place spills over its neighbours (Docs/Playtest.md 글자 넘침).
            if (Probing && !wrap && style.CalcSize(Content).x / Scale > r.width + 2) Spilling.Add(text);
            var px = Px(r);
            // A one-pixel shadow keeps text readable over pictures.
            style.normal.textColor = new Color(0, 0, 0, color.a * 0.6f);
            style.Draw(new Rect(px.x + Scale * 0.6f, px.y + Scale * 0.6f, px.width, px.height), Content, false, false, false, false);
            style.normal.textColor = color;
            style.Draw(px, Content, false, false, false, false);
        }

        static readonly Dictionary<(string, int, int, bool), (string text, float size)> Fitted = new Dictionary<(string, int, int, bool), (string, float)>();

        // One line that has to stay in its place (a button, a label, a crowded strip): smaller, down to `min`, then
        // cut short with an ellipsis. English and the biggest text size are where this matters.
        public static void TextFit(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false, float min = 8.5f)
        {
            if (string.IsNullOrEmpty(text) || Event.current.type != EventType.Repaint) return;
            var key = (text, Mathf.RoundToInt(size * 10), Mathf.RoundToInt(r.width * Scale), bold);
            if (!Fitted.TryGetValue(key, out var fit))
            {
                fit = (text, size);
                float wide = TextWidth(text, size, bold);
                if (wide > r.width)
                {
                    fit.size = Mathf.Max(min, Mathf.Floor(size * r.width / wide * 2) / 2);
                    if (TextWidth(text, fit.size, bold) > r.width)
                    {
                        int n = text.Length - 1;
                        while (n > 1 && TextWidth(text.Substring(0, n).TrimEnd() + "…", fit.size, bold) > r.width) n--;
                        fit.text = text.Substring(0, n).TrimEnd() + "…";
                    }
                }
                if (Fitted.Count > 2000) Fitted.Clear();
                Fitted[key] = fit;
            }
            Text(r, fit.text, fit.size, color, anchor, bold);
        }

        public static float TextWidth(string text, float size, bool bold = false)
        {
            var style = Style(size, TextAnchor.UpperLeft, bold, false);
            Content.text = text;
            return style.CalcSize(Content).x / Scale;
        }

        public static float TextHeight(string text, float size, float width, bool bold = false)
        {
            string wrapped = Wrap(text, size, width, bold);
            var style = Style(size, TextAnchor.UpperLeft, bold, true);
            Content.text = wrapped;
            return style.CalcHeight(Content, width * Scale) / Scale;
        }

        static readonly Dictionary<(string, int, int, bool), string> Wrapped = new Dictionary<(string, int, int, bool), string>();

        // Line breaks between words so each line fits the width: Unity breaks Korean anywhere, even inside a word.
        // A word wider than a line is left for Unity to break.
        public static string Wrap(string text, float size, float width, bool bold = false)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return text;
            var key = (text, Mathf.RoundToInt(size * Scale), Mathf.RoundToInt(width * Scale), bold);
            if (Wrapped.TryGetValue(key, out string done)) return done;
            if (Wrapped.Count > 3000) Wrapped.Clear();
            var style = Style(size, TextAnchor.UpperLeft, bold, false);
            float limit = width * Scale - 3;
            var result = new StringBuilder(text.Length + 8);
            var line = new StringBuilder();
            string[] paragraphs = text.Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0) result.Append('\n');
                line.Clear();
                foreach (string word in paragraphs[p].Split(' '))
                {
                    if (line.Length > 0)
                    {
                        Content.text = line + " " + word;
                        if (style.CalcSize(Content).x > limit)
                        {
                            result.Append(line).Append('\n');
                            line.Clear();
                        }
                        else line.Append(' ');
                    }
                    line.Append(word);
                }
                result.Append(line);
            }
            done = result.ToString();
            Wrapped[key] = done;
            return done;
        }

        // ---- Buttons ----

        public const float MinTouch = 44;

        // A button with its label. Returns true when pressed (released over it). `row` names what the button is
        // about for the playtests when the label alone is not enough ("기사" for a hero's [고르기]).
        public static bool Button(Rect r, string label, float size = 16, bool enabled = true, Look look = Look.Normal, string row = null, KeyCode key = KeyCode.None)
        {
            Probe(label, row, r, enabled);
            Face(r, look, enabled);
            TextFit(new Rect(r.x + 4, r.y, r.width - 8, r.height), label, size, enabled ? Palette.Ink : Palette.Dim, TextAnchor.MiddleCenter, look == Look.Primary);
            if (enabled) KeyHint(r, key);
            if (enabled) Glow(r, label, row);
            return Clicked(r, enabled) || (enabled && Key(key));
        }

        // A button's background and edge, for buttons that draw their own label (Hit makes them pressable).
        public static void Face(Rect r, Look look = Look.Normal, bool enabled = true)
        {
            bool hover = enabled && Hovered(r);
            Color fill = look switch
            {
                Look.Primary => Palette.Accent,
                Look.Selected => Palette.Selected,
                Look.Danger => Palette.Danger,
                Look.Quiet => Palette.Quiet,
                _ => Palette.Button,
            };
            if (!enabled) fill = Color.Lerp(fill, Palette.Panel, 0.6f);
            else if (hover) fill = Color.Lerp(fill, Color.white, 0.12f);
            fill.a = 1;
            var outline = look == Look.Selected ? Palette.Gold : Palette.Back;
            PixelFrame(r, fill, outline, Color.Lerp(fill, Color.white, enabled ? 0.22f : 0.1f), Color.Lerp(fill, Color.black, 0.32f));
        }

        // An invisible button over something already drawn (a unit to target, a card).
        public static bool Hit(Rect r, string label, bool enabled = true, string row = null, KeyCode key = KeyCode.None)
        {
            Probe(label, row, r, enabled);
            if (enabled) KeyHint(r, key);
            if (enabled) Glow(r, label, row);
            return Clicked(r, enabled) || (enabled && Key(key));
        }

        static bool Clicked(Rect r, bool enabled)
        {
            bool was = GUI.enabled;
            GUI.enabled = was && enabled;
            bool pressed = GUI.Button(Px(r), GUIContent.none, GUIStyle.none);
            GUI.enabled = was;
            return pressed;
        }

        // ---- Tooltips: hover with a mouse, hold a finger still on a phone ----

        const float HoldSeconds = 0.45f;
        static float _pressAt = -1;
        static Vector2 _pressFrom;
        static string _tip;
        static Rect _tipAt;

        // The tooltip shown this frame, if any (for the playtests).
        internal static string ShownTip { get; private set; }

        static void TrackPress()
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    _pressAt = Time.unscaledTime;
                    _pressFrom = e.mousePosition;
                    break;
                case EventType.MouseDrag when (e.mousePosition - _pressFrom).magnitude > 10 * Scale:
                case EventType.MouseUp:
                    _pressAt = -1;
                    break;
                case EventType.Repaint:
                    _tip = null;
                    break;
            }
        }

        // Explains what is under the pointer: shown while a mouse is over r, or once a finger has held still on it
        // for a moment (that press then does not count as a tap). Drawn on top by DrawTip at the end of the frame.
        public static void Tip(Rect r, string text)
        {
            if (string.IsNullOrEmpty(text) || Event.current.type != EventType.Repaint || !r.Contains(Pointer)) return;
            bool held = _pressAt >= 0 && Time.unscaledTime - _pressAt >= HoldSeconds;
            if (Touch ? !held : !Hovered(r)) return;
            if (held) GUIUtility.hotControl = 0;
            _tip = text;
            _tipAt = new Rect(Origins.Peek() + r.position, r.size);
        }

        public static void DrawTip()
        {
            ShownTip = _tip;
            if (_tip == null || Event.current.type != EventType.Repaint) return;
            float w = Mathf.Min(300, Width - 16);
            float h = TextHeight(_tip, 12, w - 16) + 12;
            float x = Mathf.Clamp(_tipAt.center.x - w / 2, 8, Width - w - 8);
            float y = _tipAt.y - h - 6;
            if (y < 6) y = Mathf.Min(_tipAt.yMax + 6, Height - h - 6);
            var box = new Rect(x, y, w, h);
            Fill(box, new Color(0.06f, 0.05f, 0.08f, 0.96f));
            Frame(box, Palette.Gold, 1);
            Text(new Rect(x + 8, y + 6, w - 16, h - 12), _tip, 12, Palette.Ink, TextAnchor.UpperLeft, false, true);
        }

        // ---- Areas: a clipped part of the screen with its own origin, scrolled by dragging or the wheel ----

        static readonly Stack<Vector2> Origins = new Stack<Vector2>();
        static readonly Stack<Rect> Views = new Stack<Rect>();

        public static void BeginArea(Rect r, float scrollY = 0)
        {
            GUI.BeginGroup(Px(r));
            var origin = Origins.Peek() + r.position;
            Views.Push(new Rect(origin, r.size));
            Origins.Push(origin - new Vector2(0, scrollY));
            if (scrollY != 0) GUI.BeginGroup(new Rect(0, -scrollY * Scale, r.width * Scale, (r.height + scrollY) * Scale + 100000));
        }

        public static void EndArea(float scrollY = 0)
        {
            if (scrollY != 0) GUI.EndGroup();
            GUI.EndGroup();
            Origins.Pop();
            Views.Pop();
        }

        // Drag or wheel over an area to scroll its content (taller than the area by `overflow` points).
        public static float Scroll(Rect area, float scroll, float overflow, ref bool dragging, ref float lastY)
        {
            if (overflow <= 0) return 0;
            var e = Event.current;
            var p = Pointer;
            switch (e.type)
            {
                case EventType.ScrollWheel when area.Contains(p):
                    scroll += e.delta.y * 12;
                    e.Use();
                    break;
                case EventType.MouseDown when area.Contains(p):
                    dragging = true;
                    lastY = p.y;
                    break;
                case EventType.MouseDrag when dragging:
                    float dy = p.y - lastY;
                    lastY = p.y;
                    if (Mathf.Abs(dy) > 0.5f)
                    {
                        scroll -= dy;
                        // A drag is not a press: let go of the button the finger started on.
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    dragging = false;
                    break;
            }
            return Mathf.Clamp(scroll, 0, overflow);
        }

        // ---- What the playtests read (Playtest.cs) ----

        internal readonly struct Drawn
        {
            public Drawn(string label, string row, Rect rect, bool enabled, bool shown)
            {
                Label = label;
                Row = row;
                Rect = rect;
                Enabled = enabled;
                Shown = shown;
            }

            public string Label { get; }
            public string Row { get; }

            // On the screen, in points.
            public Rect Rect { get; }
            public bool Enabled { get; }
            public bool Shown { get; }
        }

        internal static bool Probing { get; set; }
        internal static IReadOnlyList<Drawn> LastDrawn { get; private set; } = new List<Drawn>();
        internal static IReadOnlyList<string> LastWritten { get; private set; } = new List<string>();
        internal static IReadOnlyList<string> LastSpilled { get; private set; } = new List<string>();
        static readonly List<string> Spilling = new List<string>();
        static readonly List<Drawn> Drawing = new List<Drawn>();
        static readonly List<string> Writing = new List<string>();

        static void StartProbe()
        {
            if (!Probing || Event.current.type != EventType.Repaint) return;
            LastDrawn = Drawing.ToArray();
            LastWritten = Writing.ToArray();
            LastSpilled = Spilling.ToArray();
            Drawing.Clear();
            Writing.Clear();
            Spilling.Clear();
        }

        static void Probe(string label, string row, Rect r, bool enabled)
        {
            if (!Probing || Event.current.type != EventType.Repaint) return;
            // Under a modal card nothing can be pressed, whatever the button itself says.
            enabled &= GUI.enabled;
            var screen = new Rect(Origins.Peek() + r.position, r.size);
            bool shown = Views.Count == 0 || Views.Peek().Contains(screen.center);
            // Inside a scrolled list only the part in view can be pressed: that is the button's rect for the tests.
            if (shown && Views.Count > 0)
            {
                var view = Views.Peek();
                float x0 = Mathf.Max(screen.xMin, view.xMin), y0 = Mathf.Max(screen.yMin, view.yMin);
                float x1 = Mathf.Min(screen.xMax, view.xMax), y1 = Mathf.Min(screen.yMax, view.yMax);
                if (x1 > x0 && y1 > y0) screen = Rect.MinMaxRect(x0, y0, x1, y1);
            }
            Drawing.Add(new Drawn(label, row, screen, enabled, shown));
        }
    }
}
