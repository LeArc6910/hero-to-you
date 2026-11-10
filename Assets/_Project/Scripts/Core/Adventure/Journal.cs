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
        readonly HashSet<string> _achieved = new HashSet<string>();
        readonly Dictionary<string, int> _classRuns = new Dictionary<string, int>();
        readonly Dictionary<string, int> _classWins = new Dictionary<string, int>();

        // Pages not spent yet, and all ever earned.
        public int Pages { get; private set; }
        public int PagesEarned { get; private set; }
        public int Runs { get; private set; }
        public int Victories { get; private set; }
        public int ChaptersCleared { get; private set; }
        public int BestRenown { get; private set; }

        // The furthest chapter (1-based) any run has reached: runs may start at any chapter up to it.
        public int Reached { get; private set; } = 1;

        // For the playtests (Client/Playtest.cs "reached").
        internal void ReachForTest(int chapter) => Reached = Math.Max(1, chapter);
        public IReadOnlyCollection<string> Unlocked => _unlocked;

        // The events whose old notes were found in some run.
        public IReadOnlyCollection<string> Notes => _notes;

        // Achievements earned (Data/Journal/achievements.csv), and those the last recorded run earned.
        public IReadOnlyCollection<string> Achieved => _achieved;
        public List<AchievementDef> NewAchievements { get; } = new List<AchievementDef>();

        // Runs and victories with each class (Docs/Journal.md 기록).
        public int RunsWith(string classId) => _classRuns.TryGetValue(classId, out int n) ? n : 0;
        public int WinsWith(string classId) => _classWins.TryGetValue(classId, out int n) ? n : 0;

        public bool Has(string perk) => _unlocked.Contains(perk);

        // Pages given outside a run (playtests).
        internal void AddPages(int pages) => Pages = Math.Max(0, Pages + pages);

        public bool CanUnlock(PerkDef perk) => perk != null && !Has(perk.Id) && Pages >= perk.Pages && (perk.Requires.Length == 0 || _achieved.Contains(perk.Requires));

        public string Unlock(PerkDef perk)
        {
            if (perk == null) return "그런 장은 없습니다.";
            if (Has(perk.Id)) return "이미 펼친 장입니다.";
            if (Pages < perk.Pages) return $"페이지가 모자랍니다 (필요 {perk.Pages}).";
            if (perk.Requires.Length > 0 && !_achieved.Contains(perk.Requires)) return "이 장은 업적을 먼저 이뤄야 펼칠 수 있습니다.";
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
            // A short run's victory counts its chapters and pages but not as a won campaign (Docs/Journal.md).
            bool won = adventure.Screen == AdventureScreen.Victory && adventure.FullRun;
            if (won) Victories++;
            ChaptersCleared += adventure.ChaptersCleared;
            BestRenown = Math.Max(BestRenown, adventure.Renown);
            Reached = Math.Max(Reached, adventure.Chapter.Index + 1);
            foreach (var card in adventure.Footsteps) _notes.Add(card.Id);
            foreach (var hero in adventure.Heroes)
            {
                _classRuns[hero.Class.Id] = RunsWith(hero.Class.Id) + 1;
                if (won) _classWins[hero.Class.Id] = WinsWith(hero.Class.Id) + 1;
            }
            NewAchievements.Clear();
            foreach (var achievement in adventure.Data.Achievements)
            {
                if (_achieved.Contains(achievement.Id) || !Earned(achievement, adventure, won)) continue;
                _achieved.Add(achievement.Id);
                NewAchievements.Add(achievement);
            }
            return pages;
        }

        bool Earned(AchievementDef a, Adventure run, bool won) => a.Rule switch
        {
            "win" => won,
            "chapters" => run.ChaptersCleared >= a.Amount,
            "kind_ending" => run.Endings.Contains("kind"),
            "grim_ending" => run.Endings.Contains("grim"),
            "kind_all" => won && run.Endings.Count > 0 && run.Endings.All(e => e == "kind"),
            "renown" => run.Renown >= a.Amount,
            "dungeons" => run.DungeonsCleared >= a.Amount,
            "contracts" => run.ContractsDone >= a.Amount,
            "talks" => run.Talks.Count >= a.Amount,
            "notes" => _notes.Count >= a.Amount,
            "hard_win" => won && run.Hard,
            "hardcore_win" => won && run.Hardcore,
            "easy_win" => won && run.Difficulty == Difficulty.Easy,
            "classes" => _classWins.Count >= a.Amount,
            "runs" => Runs >= a.Amount,
            "no_fall" => won && run.Falls == 0,
            _ => false,
        };

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
            Line("reached", Reached);
            text.Append("unlocked ").Append(string.Join(",", _unlocked.OrderBy(s => s, StringComparer.Ordinal))).Append('\n');
            text.Append("notes ").Append(string.Join(",", _notes.OrderBy(s => s, StringComparer.Ordinal))).Append('\n');
            text.Append("achieved ").Append(string.Join(",", _achieved.OrderBy(s => s, StringComparer.Ordinal))).Append('\n');
            text.Append("classruns ").Append(string.Join(",", _classRuns.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value}"))).Append('\n');
            text.Append("classwins ").Append(string.Join(",", _classWins.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value}"))).Append('\n');
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
                    case "reached": journal.Reached = Math.Max(1, n); break;
                    case "unlocked":
                        foreach (string id in value.Split(',').Where(s => s.Length > 0)) journal._unlocked.Add(id);
                        break;
                    case "notes":
                        foreach (string id in value.Split(',').Where(s => s.Length > 0)) journal._notes.Add(id);
                        break;
                    case "achieved":
                        foreach (string id in value.Split(',').Where(s => s.Length > 0)) journal._achieved.Add(id);
                        break;
                    case "classruns":
                    case "classwins":
                        var counts = key == "classruns" ? journal._classRuns : journal._classWins;
                        foreach (string pair in value.Split(',').Where(s => s.Contains(":")))
                        {
                            string[] kv = pair.Split(':');
                            if (int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) counts[kv[0]] = Math.Max(0, count);
                        }
                        break;
                }
            }
            return journal;
        }
    }
}
