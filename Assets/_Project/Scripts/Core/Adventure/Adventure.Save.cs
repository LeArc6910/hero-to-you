using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Game.Core
{
    // Saving (Docs/Saving.md): the seed, the party and the log of every accepted command. Loading makes a new
    // adventure with the same seed and party and gives it the same commands again; since everything in the Core is
    // decided by the seed and the commands, it comes back to the same state. The sheets' fingerprint is kept too: when
    // the rules changed since saving, the replay may come out a little different (or stop, and the save is refused).
    public sealed partial class Adventure
    {
        public const string SaveHeader = "hero-to-you save";
        public const int SaveVersion = 3;

        public string Save()
        {
            var text = new StringBuilder();
            text.Append(SaveHeader).Append(' ').Append(SaveVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("seed ").Append(Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("party ").Append(string.Join(",", ClassIds)).Append('\n');
            text.Append("perks ").Append(string.Join(",", Perks)).Append('\n');
            text.Append("level ").Append(Difficulty.ToString().ToLowerInvariant()).Append('\n');
            if (Hardcore) text.Append("hardcore\n");
            if (FirstChapter > 0 || LastChapter < Data.Chapters.Count - 1) text.Append($"chapters {FirstChapter} {LastChapter}\n");
            text.Append("sheets ").Append(Data.Fingerprint).Append('\n');
            foreach (string line in _log) text.Append(line).Append('\n');
            return text.ToString();
        }

        // The adventure a save holds, or null with why not. `note` says when the sheets changed since it was saved.
        public static Adventure Load(GameData data, string save, out string error, out string note)
        {
            error = null;
            note = null;
            var lines = (save ?? "").Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
            if (lines.Count < 3 || !lines[0].StartsWith(SaveHeader))
            {
                error = data.Line("adventure.save.not_a_save");
                return null;
            }
            if (!int.TryParse(lines[0].Substring(SaveHeader.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) || version != SaveVersion)
            {
                error = data.Line("adventure.save.old_version");
                return null;
            }
            if (!lines[1].StartsWith("seed ") || !ulong.TryParse(lines[1].Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed) || !lines[2].StartsWith("party "))
            {
                error = data.Line("adventure.save.broken_header");
                return null;
            }
            // The rest of the header, in any order: the journal perks, the difficulty, the sheets' fingerprint.
            int first = 3;
            var perks = new List<string>();
            var difficulty = Difficulty.Normal;
            bool hardcore = false;
            int firstChapter = 0, lastChapter = -1;
            string sheets = null;
            for (; first < lines.Count; first++)
            {
                string line = lines[first];
                if (line.StartsWith("perks")) perks = line.Substring(5).Trim().Split(',').Where(p => p.Length > 0).ToList();
                else if (line.StartsWith("level ")) Enum.TryParse(line.Substring(6), true, out difficulty);
                else if (line.StartsWith("sheets ")) sheets = line.Substring(7);
                else if (line == "hardcore") hardcore = true;
                else if (line.StartsWith("chapters "))
                {
                    string[] range = line.Split(' ');
                    int.TryParse(range[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out firstChapter);
                    if (range.Length > 2) int.TryParse(range[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out lastChapter);
                }
                else break;
            }
            Adventure adventure;
            try
            {
                adventure = new Adventure(data, lines[2].Substring(6).Split(','), seed, perks, difficulty, hardcore, firstChapter, lastChapter);
            }
            catch (ArgumentException e)
            {
                error = data.Line("adventure.save.bad_party", e.Message);
                return null;
            }
            if (sheets != null && sheets != data.Fingerprint) note = data.Line("adventure.save.sheets_changed");
            for (int i = first; i < lines.Count; i++)
            {
                string failed;
                try
                {
                    failed = adventure.Run(lines[i]);
                }
                catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException || e is ArgumentException || e is KeyNotFoundException || e is InvalidOperationException)
                {
                    failed = e.Message;
                }
                if (failed == null) continue;
                // A command that no longer fits (the data changed under the save): keep everything before it
                // rather than losing the whole run. The next command given starts a new tail.
                int kept = i - first;
                if (kept == 0)
                {
                    error = data.Line("adventure.save.first_failed", lines[i], failed);
                    return null;
                }
                note = data.Line("adventure.save.partial", lines.Count - first, kept);
                return adventure;
            }
            return adventure;
        }

        // One logged command, given again. Returns null when it went through, or why not.
        public string Run(string line)
        {
            string[] w = line.Split(' ');
            int N(int i) => int.Parse(w[i], NumberStyles.Integer, CultureInfo.InvariantCulture);
            switch (w[0])
            {
                case "continue": return Continue();
                case "roll": return RollMove();
                case "move": return Move(new Hex(N(1), N(2)));
                case "camp": return Camp();
                case "stay": return Stay();
                case "join": return Join(w[1].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList());
                case "fight": return Fight(new BattleCommand(N(1), w[2], N(3), N(4)));
                case "choose": return Choose(N(1), N(2), N(3));
                case "rest": return Rest();
                case "reroll": return Reroll();
                case "avoid": return Avoid();
                case "scare": return Scare();
                case "rumor": return Rumor();
                case "donate": return Donate();
                case "row": return SetRow(N(1), w[2] == "back");
                case "delve": return Delve();
                case "leavedungeon": return LeaveDungeon();
                case "contract": return TakeContract();
                case "forge": return Forge(N(1), (ItemSlot)Enum.Parse(typeof(ItemSlot), w[2], true));
                case "recruit": return Recruit(w[1]);
                case "use": return UseItem(N(1), w[2]);
                case "buy": return Buy(N(1), w.Length > 2 ? N(2) : -1);
                case "sell": return Sell(N(1));
                case "elder": return Elder();
                case "leave": return LeaveTown();
                case "equip": return Equip(N(1), N(2));
                case "unequip": return Unequip(N(1), (ItemSlot)Enum.Parse(typeof(ItemSlot), w[2], true));
                case "upgrade": return ChooseUpgrade(N(1));
                default: return Data.Line("adventure.save.unknown_command", w[0]);
            }
        }
    }
}
