using System;
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
        // The rogue in the shadows: no enemy can aim at it.
        Hidden,
        // Armor −2 (the alchemist's acid).
        Corrode,
        // The next attack's chance is lower (the seer's prophecy).
        Foretold,
        // Falls with it and the necromancer gets two more souls.
        DeathMark,
        // One die fewer on the next check (the sunken bell ringing in the ears).
        Daze,
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
            StatusKind.Hidden => "숨음",
            StatusKind.Corrode => "부식",
            StatusKind.Foretold => "예언",
            StatusKind.DeathMark => "죽음의 표",
            StatusKind.Daze => "멍함",
            _ => kind.ToString(),
        };
    }

    // Which side a unit fights on. Summons (the skeleton, the wolf) are monsters on the party's side.
    public enum Side
    {
        Party,
        Foe,
    }

    // What a monster (or a summon) will do on its next turn, shown over its head (Docs/Combat.md 적의 의도).
    public sealed class Intent
    {
        public Intent(MonsterSkillDef skill, int target)
        {
            Skill = skill;
            Target = target;
        }

        public MonsterSkillDef Skill { get; }

        // The unit it aims at; -1 for every opponent, the unit's own id for a self buff.
        public int Target { get; internal set; }
    }

    // A hero, a monster or a summon in one battle. A hero's health is the hero's own, so it carries out of the battle.
    public sealed class Unit
    {
        int _hp;
        readonly int _maxHp;

        internal Unit(int id, Hero hero)
        {
            Id = id;
            Hero = hero;
            Side = Side.Party;
            Name = hero.Name;
            _maxHp = hero.MaxHp;
            BaseArmor = hero.ArmorValue;
            Speed = hero.Speed;
        }

        internal Unit(int id, MonsterDef monster, string name, Side side = Side.Foe, int owner = -1, int hp = 0)
        {
            Id = id;
            Monster = monster;
            Side = side;
            Owner = owner;
            Name = name;
            _maxHp = hp > 0 ? hp : monster.Hp;
            _hp = _maxHp;
            BaseArmor = monster.Armor;
            Ward = monster.Ward;
            Speed = monster.Speed;
            Accuracy = monster.Accuracy;
        }

        public int Id { get; }
        public Hero Hero { get; }
        public MonsterDef Monster { get; }
        public Side Side { get; }
        public bool IsHero => Hero != null;
        public bool IsFoe => Side == Side.Foe;

        // A summon on the party's side: the hero unit it belongs to.
        public int Owner { get; } = -1;
        public bool IsSummon => !IsHero && Side == Side.Party;
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

        public int MaxHp => IsHero ? Hero.MaxHp : _maxHp;
        public bool Down => Hp <= 0;

        public int BaseArmor { get; }
        public int Ward { get; }
        public int Speed { get; }
        public int Accuracy { get; }
        public int Evasion => IsHero ? Hero.Evasion : 0;

        // Bonus power for every attack (the beastmaster's upgrades for the wolf).
        public int PowerBonus { get; internal set; }

        // Armor now: the base, +2 while guarding someone, + the taunt's successes, −2 while corroded.
        public int Armor => Math.Max(0, BaseArmor + (GuardTarget >= 0 ? 2 : 0) + TauntArmor - (Has(StatusKind.Corrode) ? 2 : 0));

        // When it acts next, on the battle's clock (Docs/Combat.md 행동 순서표).
        public long Ready { get; internal set; }
        public int TurnsTaken { get; internal set; }

        // Status → turns left (of this unit's own turns). Howl holds its bonus power, Foretold its chance cut.
        internal Dictionary<StatusKind, int> Statuses { get; } = new Dictionary<StatusKind, int>();
        public IReadOnlyDictionary<StatusKind, int> Status => Statuses;
        public bool Has(StatusKind kind) => Statuses.ContainsKey(kind);

        // Guarding: the unit standing in for this one (-1 for none) and whose turn ends it; for a guard, whom it guards.
        public int GuardedBy { get; internal set; } = -1;
        internal int GuardUntil { get; set; } = -1;
        public int GuardTarget { get; internal set; } = -1;
        public int TauntArmor { get; internal set; }

        // Class resources held for this battle: the rogue's combo, the necromancer's souls.
        public int Combo { get; internal set; }
        public int Souls { get; internal set; }

        // The monk's ki (Docs/Classes.md 수도승): one for each blow that lands, spent by its skills.
        public int Ki { get; internal set; }

        // Skill id → the turn (TurnsTaken) it can be used again.
        internal Dictionary<string, int> AvailableAt { get; } = new Dictionary<string, int>();

        // Skills a boss took away ("seal" or "erase"), until the hero uses them again (Battle.BlockedBy).
        internal Dictionary<string, string> Blocked { get; } = new Dictionary<string, string>();

        // How many of this unit's turns the skill still has to wait, counting from the turn `turn` (0 = usable then).
        internal int CooldownFrom(string skill, int turn) =>
            AvailableAt.TryGetValue(skill, out int at) && at > turn ? at - turn : 0;

        // Monsters and summons: what they will do next, and where they are in their pattern.
        public Intent Intent { get; internal set; }
        internal int PatternIndex { get; set; }
        public int Phase { get; internal set; }

        // The elementalist's last element this battle, for the cycle.
        public string LastElement { get; internal set; } = "";

        public override string ToString() => $"{Name}#{Id} {Hp}/{MaxHp}";
    }
}
