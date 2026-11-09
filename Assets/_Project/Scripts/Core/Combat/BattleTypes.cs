using System.Collections.Generic;

namespace Game.Core
{
    public enum BattleEventKind
    {
        // A unit's turn starts.
        Turn,
        // A check was rolled (Check, Skill).
        Roll,
        Damage,
        Miss,
        Heal,
        Down,
        Revive,
        StatusOn,
        StatusOff,
        // A turn passes without acting (stunned).
        Skip,
        // A monster shows its next intent.
        Intent,
        // A boss enters its second phase (Text is its line).
        Phase,
        // Grit filled up into resolve (Amount).
        ResolveGained,
        Guard,
        Potion,
        Victory,
        Defeat,
    }

    // Something that happened in a battle, in order, for the screen to play back and for the log (Text, in Korean).
    public sealed class BattleEvent
    {
        public BattleEventKind Kind { get; internal set; }
        public int Actor { get; internal set; } = -1;
        public int Target { get; internal set; } = -1;
        public int Amount { get; internal set; }
        public string Skill { get; internal set; }
        public string SkillName { get; internal set; }
        public CheckResult Check { get; internal set; }
        public StatusKind Status { get; internal set; }
        public bool Perfect { get; internal set; }
        public string Text { get; internal set; }

        public override string ToString() => Text ?? Kind.ToString();
    }

    // A hero's action: the skill (or Potion), the target unit, and how much resolve to spend on sure successes.
    public sealed class BattleCommand
    {
        public const string Potion = "potion";

        public BattleCommand(int actor, string skill, int target, int resolve = 0)
        {
            Actor = actor;
            Skill = skill;
            Target = target;
            Resolve = resolve;
        }

        public int Actor { get; }
        public string Skill { get; }
        public int Target { get; }
        public int Resolve { get; }

        public override string ToString() => $"{Actor}:{Skill}->{Target} r{Resolve}";
    }

    public enum BattleState
    {
        // Waiting for the current hero's command.
        HeroTurn,
        Won,
        Lost,
    }

    // What a skill would do, shown before rolling (Docs/Dice.md 굴리기 전에 보여 주는 것).
    public sealed class SkillPreview
    {
        public SkillDef Skill { get; internal set; }

        // Null when the command could be given; otherwise why not.
        public string Error { get; internal set; }
        public int Dice { get; internal set; }
        public int Chance { get; internal set; }

        // Sure successes that cost nothing, with why (표식 1, 협공 1…); resolve comes on top.
        public int Free { get; internal set; }
        public List<string> Reasons { get; } = new List<string>();

        // Resolve that could go into this check, and how much the preview was made with.
        public int ResolveRoom { get; internal set; }
        public int Resolve { get; internal set; }
        public CheckOdds Odds { get; internal set; }

        // For each number of successes, the effect on the target after armor (damage, healing or health back).
        public int[] ValueBySuccesses { get; internal set; } = new int[0];
        public double Expected { get; internal set; }

        public bool Ok => Error == null;
        public int Guaranteed => Odds?.Guaranteed ?? 0;
    }
}
