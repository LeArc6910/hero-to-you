using System;
using System.Collections.Generic;

namespace Game.Core
{
    // One of the party's three heroes for the whole adventure: health, abilities, resolve and grit carry from battle
    // to event to battle (Docs/Dice.md, Docs/Combat.md).
    public sealed class Hero
    {
        // Total experience for each level: level 2 at 10, level 3 at 25 and so on (Docs/Combat.md 보상).
        public static readonly int[] LevelXp = { 0, 10, 25, 45, 70, 100, 140, 190, 250, 320 };
        public const int MaxLevel = 10;
        public const int LevelHp = 4;
        public const int LevelStat = 3;
        public const int StatCap = 95;

        public Hero(ClassDef cls, int seat = 0)
        {
            Class = cls ?? throw new ArgumentNullException(nameof(cls));
            Seat = seat;
            MaxHp = cls.Hp;
            Hp = cls.Hp;
            Armor = cls.Armor;
            MaxResolve = cls.Resolve;
            Resolve = cls.Resolve;
            GritCap = cls.GritCap;
            Stats = (int[])cls.Stats.Clone();
            Potions = 1;
        }

        public ClassDef Class { get; }
        public string Name => Class.Name;

        // Which player controls this hero (0..2). One person playing alone has all three.
        public int Seat { get; set; }
        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int MaxHp { get; private set; }
        public int Hp { get; internal set; }
        public int Armor { get; }
        public int[] Stats { get; }
        public int MaxResolve { get; }
        public int Resolve { get; internal set; }
        public int Grit { get; internal set; }
        public int GritCap { get; }
        public int Potions { get; internal set; }
        public int Gold { get; internal set; }

        // Sure successes waiting for the hero's next check: the cleric's blessing (1 each) and the gambler's coin (2).
        public int Blessing { get; internal set; }
        public int Luck { get; internal set; }

        public bool Down => Hp <= 0;
        public int Stat(Stat stat) => Stats[(int)stat];

        // Docs/Combat.md 행동 순서표: 8 + agility / 10.
        public int Speed => 8 + Stat(Core.Stat.Agility) / 10;
        public int Evasion => Stat(Core.Stat.Agility) / 10;

        public int XpToNext => Level >= MaxLevel ? 0 : LevelXp[Level] - Xp;

        internal void Heal(int amount)
        {
            if (Down || amount <= 0) return;
            Hp = Math.Min(MaxHp, Hp + amount);
        }

        // Grit for the rolled dice that failed; each full bar becomes one resolve while resolve has room
        // (Docs/Dice.md 끈기). Returns the resolve gained.
        internal int AddGrit(int failures)
        {
            Grit = Math.Min(GritCap, Grit + Math.Max(0, failures));
            int gained = 0;
            while (Grit >= GritCap && Resolve < MaxResolve)
            {
                Grit -= GritCap;
                Resolve++;
                gained++;
            }
            return gained;
        }

        internal void GainResolve(int amount) => Resolve = Math.Max(0, Math.Min(MaxResolve, Resolve + amount));

        // Adds experience and returns the levels reached, each with its growth applied: more health and the two
        // main abilities up.
        internal List<int> GainXp(int amount)
        {
            var reached = new List<int>();
            if (amount <= 0) return reached;
            Xp += amount;
            while (Level < MaxLevel && Xp >= LevelXp[Level])
            {
                Level++;
                MaxHp += LevelHp;
                if (!Down) Hp += LevelHp;
                foreach (var stat in Class.MainStats) Stats[(int)stat] = Math.Min(StatCap, Stats[(int)stat] + LevelStat);
                reached.Add(Level);
            }
            return reached;
        }

        public override string ToString() => $"{Name} Lv{Level} {Hp}/{MaxHp}";
    }
}
