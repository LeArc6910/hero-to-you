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
        public const int SaveVersion = 1;

        public string Save()
        {
            var text = new StringBuilder();
            text.Append(SaveHeader).Append(' ').Append(SaveVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("seed ").Append(Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("party ").Append(string.Join(",", ClassIds)).Append('\n');
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
                error = "저장본이 아닙니다.";
                return null;
            }
            if (!int.TryParse(lines[0].Substring(SaveHeader.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) || version != SaveVersion)
            {
                error = "저장 형식이 바뀌어 이어할 수 없습니다.";
                return null;
            }
            if (!lines[1].StartsWith("seed ") || !ulong.TryParse(lines[1].Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong seed) || !lines[2].StartsWith("party "))
            {
                error = "저장본의 머리말이 깨졌습니다.";
                return null;
            }
            Adventure adventure;
            try
            {
                adventure = new Adventure(data, lines[2].Substring(6).Split(','), seed);
            }
            catch (ArgumentException e)
            {
                error = "저장본의 파티를 만들 수 없습니다: " + e.Message;
                return null;
            }
            int first = 3;
            if (lines.Count > 3 && lines[3].StartsWith("sheets "))
            {
                if (lines[3].Substring(7) != data.Fingerprint) note = "저장한 뒤 게임 데이터가 바뀌어 결과가 조금 다를 수 있습니다.";
                first = 4;
            }
            for (int i = first; i < lines.Count; i++)
            {
                string failed;
                try
                {
                    failed = adventure.Run(lines[i]);
                }
                catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException || e is ArgumentException)
                {
                    failed = e.Message;
                }
                if (failed != null)
                {
                    error = $"저장본을 다시 돌리다 {i - first + 1}번째 명령({lines[i]})에서 어긋났습니다: {failed}";
                    return null;
                }
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
                case "buy": return Buy(N(1));
                case "sell": return Sell(N(1));
                case "elder": return Elder();
                case "leave": return LeaveTown();
                case "equip": return Equip(N(1), N(2));
                case "unequip": return Unequip(N(1), (ItemSlot)Enum.Parse(typeof(ItemSlot), w[2], true));
                case "upgrade": return ChooseUpgrade(N(1));
                default: return $"모르는 명령 '{w[0]}'";
            }
        }
    }
}
