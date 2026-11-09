using System;

namespace Game.Core
{
    // The six abilities every check reads (Docs/Dice.md). A value is the chance in percent that one die succeeds.
    public enum Stat
    {
        Strength,
        Agility,
        Intellect,
        Will,
        Perception,
        Charm,
    }

    public static class Stats
    {
        public const int Count = 6;

        public static string Name(Stat stat) => stat switch
        {
            Stat.Strength => "힘",
            Stat.Agility => "민첩",
            Stat.Intellect => "지능",
            Stat.Will => "의지",
            Stat.Perception => "감각",
            Stat.Charm => "매력",
            _ => stat.ToString(),
        };

        // The short codes the sheets use: str, agi, int, wil, per, cha.
        public static string Code(Stat stat) => stat switch
        {
            Stat.Strength => "str",
            Stat.Agility => "agi",
            Stat.Intellect => "int",
            Stat.Will => "wil",
            Stat.Perception => "per",
            Stat.Charm => "cha",
            _ => throw new ArgumentOutOfRangeException(nameof(stat)),
        };

        public static bool TryParse(string code, out Stat stat)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Code((Stat)i) != code) continue;
                stat = (Stat)i;
                return true;
            }
            stat = default;
            return false;
        }
    }
}
