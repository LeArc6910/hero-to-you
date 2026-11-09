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
            float zoom = Mathf.Clamp(Mathf.Min(w / 1100f, h / 650f), 1f, 2f);
            Scale = ratio * zoom;
            Width = Screen.width / Scale;
            Height = Screen.height / Scale;
            Touch = WebBridge.TouchInput();
            Origins.Clear();
            Origins.Push(Vector2.zero);
            Views.Clear();
            StartProbe();
            TrackPress();
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

        // A panel: dark fill with a light edge.
        public static void Panel(Rect r, Color? fill = null, Color? edge = null)
        {
            Fill(r, fill ?? Palette.Panel);
            Frame(r, edge ?? Palette.Edge, 1.5f);
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
            var px = Px(r);
            // A one-pixel shadow keeps text readable over pictures.
            style.normal.textColor = new Color(0, 0, 0, color.a * 0.6f);
            style.Draw(new Rect(px.x + Scale * 0.6f, px.y + Scale * 0.6f, px.width, px.height), Content, false, false, false, false);
            style.normal.textColor = color;
            style.Draw(px, Content, false, false, false, false);
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
        public static bool Button(Rect r, string label, float size = 16, bool enabled = true, Look look = Look.Normal, string row = null)
        {
            Probe(label, row, r, enabled);
            Face(r, look, enabled);
            Text(new Rect(r.x + 4, r.y, r.width - 8, r.height), label, size, enabled ? Palette.Ink : Palette.Dim, TextAnchor.MiddleCenter, look == Look.Primary);
            return Clicked(r, enabled);
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
            Fill(r, fill);
            Frame(r, look == Look.Selected ? Palette.Gold : Palette.Edge, look == Look.Selected ? 2 : 1);
        }

        // An invisible button over something already drawn (a unit to target, a card).
        public static bool Hit(Rect r, string label, bool enabled = true, string row = null)
        {
            Probe(label, row, r, enabled);
            return Clicked(r, enabled);
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
        static readonly List<Drawn> Drawing = new List<Drawn>();
        static readonly List<string> Writing = new List<string>();

        static void StartProbe()
        {
            if (!Probing || Event.current.type != EventType.Repaint) return;
            LastDrawn = Drawing.ToArray();
            LastWritten = Writing.ToArray();
            Drawing.Clear();
            Writing.Clear();
        }

        static void Probe(string label, string row, Rect r, bool enabled)
        {
            if (!Probing || Event.current.type != EventType.Repaint) return;
            var screen = new Rect(Origins.Peek() + r.position, r.size);
            bool shown = Views.Count == 0 || Views.Peek().Contains(screen.center);
            Drawing.Add(new Drawn(label, row, screen, enabled, shown));
        }
    }
}
