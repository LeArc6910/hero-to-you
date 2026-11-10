using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Core
{
    // The sentences Core writes for the player (the battle log, reports, refusals) in the language the sheets were
    // loaded in, from Data/Text/*.csv (`id, ko, en`; Docs/Localization.md Core의 문장). A template takes values by
    // number: {0}. In Korean {0:이} {0:을} {0:은} {0:과} {0:으로} add the particle that fits the value (기사가, 사냥꾼이);
    // in other languages the particle mark is dropped and only the value is written. An id the sheets do not have
    // is written as itself, so a missing line shows on the screen. Text never steers the rules.
    public sealed class Lines
    {
        readonly Dictionary<string, string> _templates = new Dictionary<string, string>();

        internal Lines(string language)
        {
            Language = string.IsNullOrEmpty(language) ? "ko" : language;
        }

        public string Language { get; }

        public bool Korean => Language == "ko";

        public IEnumerable<string> Ids => _templates.Keys;

        public bool Has(string id) => _templates.ContainsKey(id);

        internal void Add(string id, string template) => _templates[id] = template;

        public string Template(string id) => _templates.TryGetValue(id, out string t) ? t : id;

        public string Get(string id, params object[] args) => Format(Template(id), Korean, args);

        public static string Format(string template, bool korean, params object[] args)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var text = new StringBuilder(template.Length + 16);
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                int close = c == '{' ? template.IndexOf('}', i + 1) : -1;
                if (close < 0)
                {
                    text.Append(c);
                    continue;
                }
                string token = template.Substring(i + 1, close - i - 1);
                int colon = token.IndexOf(':');
                string number = colon < 0 ? token : token.Substring(0, colon);
                if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    text.Append(c);
                    continue;
                }
                string value = index >= 0 && args != null && index < args.Length ? Show(args[index]) : "";
                string particle = colon < 0 ? "" : token.Substring(colon + 1);
                text.Append(korean && particle.Length > 0 ? Particle(value, particle) : value);
                i = close;
            }
            return text.ToString();
        }

        static string Show(object value) => value switch
        {
            null => "",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        static string Particle(string word, string particle) => particle switch
        {
            "이" or "가" => Game.Core.Korean.Subject(word),
            "을" or "를" => Game.Core.Korean.Object(word),
            "은" or "는" => Game.Core.Korean.Topic(word),
            "과" or "와" => Game.Core.Korean.With(word),
            "으로" or "로" => Game.Core.Korean.Toward(word),
            _ => word + particle,
        };
    }
}
