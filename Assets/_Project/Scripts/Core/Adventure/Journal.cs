using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Game.Core
{
    // The journal that outlives the runs (Docs/Journal.md): pages earned by finished adventures, perks bought with them,
    // and the old hero's notes found so far. The client keeps its text on the device next to the save.
    public sealed class Journal
    {
        public const string Header = "hero-to-you journal 1";

        readonly HashSet<string> _unlocked = new HashSet<string>();
        readonly HashSet<string> _notes = new HashSet<string>();

        // Pages not spent yet, and all ever earned.
        public int Pages { get; private set; }
        public int PagesEarned { get; private set; }
        public int Runs { get; private set; }
        public int Victories { get; private set; }
        public int ChaptersCleared { get; private set; }
        public int BestRenown { get; private set; }
        public IReadOnlyCollection<string> Unlocked => _unlocked;

        // The events whose old notes were found in some run.
        public IReadOnlyCollection<string> Notes => _notes;

        public bool Has(string perk) => _unlocked.Contains(perk);

        // Pages given outside a run (playtests).
        internal void AddPages(int pages) => Pages = Math.Max(0, Pages + pages);

        public bool CanUnlock(PerkDef perk) => perk != null && !Has(perk.Id) && Pages >= perk.Pages;

        public string Unlock(PerkDef perk)
        {
            if (perk == null) return "그런 장은 없습니다.";
            if (Has(perk.Id)) return "이미 펼친 장입니다.";
            if (Pages < perk.Pages) return $"페이지가 모자랍니다 (필요 {perk.Pages}).";
            Pages -= perk.Pages;
            _unlocked.Add(perk.Id);
            return null;
        }

        // Writes a finished adventure in: its pages, its notes, how far it went. Returns the pages it gave (0 when it
        // is not over).
        public int Record(Adventure adventure)
        {
            if (adventure == null || !adventure.Over) return 0;
            int pages = adventure.PagesEarned;
            Pages += pages;
            PagesEarned += pages;
            Runs++;
            if (adventure.Screen == AdventureScreen.Victory) Victories++;
            ChaptersCleared += adventure.ChaptersCleared;
            BestRenown = Math.Max(BestRenown, adventure.Renown);
            foreach (var card in adventure.Footsteps) _notes.Add(card.Id);
            return pages;
        }

        public string ToText()
        {
            var text = new StringBuilder();
            text.Append(Header).Append('\n');
            void Line(string key, int value) => text.Append(key).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            Line("pages", Pages);
            Line("earned", PagesEarned);
            Line("runs", Runs);
            Line("victories", Victories);
            Line("chapters", ChaptersCleared);
            Line("renown", BestRenown);
            text.Append("unlocked ").Append(string.Join(",", _unlocked.OrderBy(s => s, StringComparer.Ordinal))).Append('\n');
            text.Append("notes ").Append(string.Join(",", _notes.OrderBy(s => s, StringComparer.Ordinal))).Append('\n');
            return text.ToString();
        }

        // Reads a journal's text; a missing or broken one is a new, empty journal.
        public static Journal Parse(string text)
        {
            var journal = new Journal();
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            if (lines.Length == 0 || lines[0] != Header) return journal;
            foreach (string line in lines.Skip(1))
            {
                int space = line.IndexOf(' ');
                string key = space < 0 ? line : line.Substring(0, space);
                string value = space < 0 ? "" : line.Substring(space + 1);
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
                n = Math.Max(0, n);
                switch (key)
                {
                    case "pages": journal.Pages = n; break;
                    case "earned": journal.PagesEarned = n; break;
                    case "runs": journal.Runs = n; break;
                    case "victories": journal.Victories = n; break;
                    case "chapters": journal.ChaptersCleared = n; break;
                    case "renown": journal.BestRenown = n; break;
                    case "unlocked":
                        foreach (string id in value.Split(',').Where(s => s.Length > 0)) journal._unlocked.Add(id);
                        break;
                    case "notes":
                        foreach (string id in value.Split(',').Where(s => s.Length > 0)) journal._notes.Add(id);
                        break;
                }
            }
            return journal;
        }
    }
}
