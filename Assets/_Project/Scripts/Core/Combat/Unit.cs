using System.Collections.Generic;

namespace Game.Core
{
    public enum StatusKind
    {
        Burn,
        Chill,
        Mark,
        Stun,
        Weak,
        Taunt,
        Howl,
    }

    public static class StatusNames
    {
        public static string Name(StatusKind kind) => kind switch
        {
            StatusKind.Burn => "화상",
            StatusKind.Chill => "냉기",
            StatusKind.Mark => "표식",
            StatusKind.Stun => "기절",
            StatusKind.Weak => "약화",
            StatusKind.Taunt => "도발",
            StatusKind.Howl => "울부짖음",
            _ => kind.ToString(),
        };
    }

    // What a monster will do on its next turn, shown over its head (Docs/Combat.md 적의 의도).
    public sealed class Intent
    {
        public Intent(MonsterSkillDef skill, int target)
        {
            Skill = skill;
            Target = target;
        }

        public MonsterSkillDef Skill { get; }

        // The hero unit it aims at; -1 for every hero, the monster's own id for a self buff.
        public int Target { get; internal set; }
    }

    // A hero or a monster in one battle. A hero's health is the hero's own, so it carries out of the battle.
    public sealed class Unit
    {
        int _hp;

        internal Unit(int id, Hero hero)
        {
            Id = id;
            Hero = hero;
            Name = hero.Name;
            MaxHpValue = hero.MaxHp;
            BaseArmor = hero.Armor;
            Speed = hero.Speed;
        }

        internal Unit(int id, MonsterDef monster, string name)
        {
            Id = id;
            Monster = monster;
            Name = name;
            _hp = monster.Hp;
            MaxHpValue = monster.Hp;
            BaseArmor = monster.Armor;
            Ward = monster.Ward;
            Speed = monster.Speed;
            Accuracy = monster.Accuracy;
        }

        public int Id { get; }
        public Hero Hero { get; }
        public MonsterDef Monster { get; }
        public bool IsHero => Hero != null;
        public string Name { get; }

        public int Hp
        {
            get => IsHero ? Hero.Hp : _hp;
            internal set
            {
                if (IsHero) Hero.Hp = value;
                else _hp = value;
            }
        }

        int MaxHpValue { get; }
        public int MaxHp => IsHero ? Hero.MaxHp : MaxHpValue;
        public bool Down => Hp <= 0;

        public int BaseArmor { get; }
        public int Ward { get; }
        public int Speed { get; }
        public int Accuracy { get; }
        public int Evasion => IsHero ? Hero.Evasion : 0;

        // Armor now: the base, +2 while guarding someone, + the taunt's successes.
        public int Armor => BaseArmor + (GuardTarget >= 0 ? 2 : 0) + TauntArmor;

        // When it acts next, on the battle's clock (Docs/Combat.md 행동 순서표).
        public long Ready { get; internal set; }
        public int TurnsTaken { get; internal set; }

        // Status → turns left (of this unit's own turns). Howl holds its bonus power instead.
        internal Dictionary<StatusKind, int> Statuses { get; } = new Dictionary<StatusKind, int>();
        public IReadOnlyDictionary<StatusKind, int> Status => Statuses;
        public bool Has(StatusKind kind) => Statuses.ContainsKey(kind);

        // The knight guarding this unit (-1 for none), and for a knight the ally guarded.
        public int GuardedBy { get; internal set; } = -1;
        public int GuardTarget { get; internal set; } = -1;
        public int TauntArmor { get; internal set; }

        // Skill id → the turn (TurnsTaken) it can be used again.
        internal Dictionary<string, int> AvailableAt { get; } = new Dictionary<string, int>();

        // How many of this unit's turns the skill still has to wait, counting from the turn `turn` (0 = usable then).
        internal int CooldownFrom(string skill, int turn) =>
            AvailableAt.TryGetValue(skill, out int at) && at > turn ? at - turn : 0;

        // Monsters: what they will do next, and where they are in their pattern.
        public Intent Intent { get; internal set; }
        internal int PatternIndex { get; set; }
        public int Phase { get; internal set; }

        // The elementalist's last element this battle, for the cycle.
        public string LastElement { get; internal set; } = "";

        public override string ToString() => $"{Name}#{Id} {Hp}/{MaxHp}";
    }
}
