using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    // The screens' own words, by id, from Resources/Data/Ui/strings.csv (id, ko, en; Docs/Localization.md). The
    // language is the settings' choice; a word missing in it falls back to Korean, and an id missing from the sheet
    // shows the id, so a gap is easy to see. The story, events and the rules' messages stay in their own sheets and
    // in Korean for now.
    static class Strings
    {
        const string Resource = "Data/Ui/strings";
        static Dictionary<string, (string ko, string en)> _words;

        static Dictionary<string, (string ko, string en)> Words
        {
            get
            {
                if (_words != null) return _words;
                _words = new Dictionary<string, (string, string)>();
                var asset = Resources.Load<TextAsset>(Resource);
                if (asset == null) return _words;
                foreach (var row in CsvTable.Read(asset.text).Rows) _words[row["id"]] = (row["ko"], row["en"]);
                return _words;
            }
        }

        public static string Get(string id)
        {
            if (!Words.TryGetValue(id, out var word)) return id;
            return Settings.Language == "en" && !string.IsNullOrEmpty(word.en) ? word.en : word.ko;
        }

        // Get with {0}, {1}… filled in.
        public static string Format(string id, params object[] values) => string.Format(Get(id), values);

        // Whether the sheet has the id (Tips use it to find their cards).
        public static bool Has(string id) => Words.ContainsKey(id);
    }
}
