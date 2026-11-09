using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Client
{
    // Just enough JSON for the playtests' answers (Playtest.cs): objects of strings, numbers, booleans and lists.
    sealed class Json
    {
        readonly StringBuilder _text = new StringBuilder();
        readonly Stack<bool> _first = new Stack<bool>();

        public static string Quote(string value)
        {
            if (value == null) return "null";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        void Comma()
        {
            if (_first.Count == 0) return;
            if (!_first.Peek()) _text.Append(',');
            _first.Pop();
            _first.Push(false);
        }

        void Key(string key)
        {
            Comma();
            if (key != null) _text.Append(Quote(key)).Append(':');
        }

        public Json Open(string key = null)
        {
            Key(key);
            _text.Append('{');
            _first.Push(true);
            return this;
        }

        public Json Close()
        {
            _text.Append('}');
            _first.Pop();
            return this;
        }

        public Json List(string key = null)
        {
            Key(key);
            _text.Append('[');
            _first.Push(true);
            return this;
        }

        public Json End()
        {
            _text.Append(']');
            _first.Pop();
            return this;
        }

        public Json Add(string key, string value)
        {
            Key(key);
            _text.Append(Quote(value));
            return this;
        }

        public Json Add(string key, int value)
        {
            Key(key);
            _text.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public Json Add(string key, float value)
        {
            Key(key);
            _text.Append(value.ToString("0.##", CultureInfo.InvariantCulture));
            return this;
        }

        public Json Add(string key, bool value)
        {
            Key(key);
            _text.Append(value ? "true" : "false");
            return this;
        }

        public override string ToString() => _text.ToString();
    }
}
