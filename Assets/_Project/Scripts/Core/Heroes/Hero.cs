using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // One of the party's three heroes for the whole adventure: health, abilities, equipment, chosen upgrades,
    // resolve and grit carry from battle to event to battle (Docs/Dice.md, Docs/Combat.md, Docs/Classes.md).
    public sealed class Hero
    {
        // Total experience for each level: level 2 at 10, level 3 at 25 and so on (Docs/Combat.md 보상).
        public static readonly int[] LevelXp = { 0, 10, 25, 45, 70, 100, 140, 190, 250, 320 };
        public const int MaxLevel = 10;
        public const int LevelHp = 4;
        public const int LevelStat = 3;
        public const int StatCap = 95;

        // The basic attack's skill id: whichever weapon is held.
        public const string AttackId = "attack";
        public const int StartReagents = 3;
        public const int ReagentCap = 6;

        readonly int[] _baseStats;
        readonly List<UpgradeDef> _upgrades = new List<UpgradeDef>();
        readonly Dictionary<string, int> _specials = new Dictionary<string, int>();
        int _baseMaxHp;
        int _upgradeArmor;
        int _upgradeResolve;
        int _upgradeGrit;
        List<SkillDef> _skills;

        public Hero(ClassDef cls, int seat = 0)
        {
            Class = cls ?? throw new ArgumentNullException(nameof(cls));
            Seat = seat;
            _baseStats = (int[])cls.Stats.Clone();
            _baseMaxHp = cls.Hp;
            Weapon = cls.StartWeapon;
            Hp = MaxHp;
            Resolve = MaxResolve;
            Potions = 1;
            Reagents = cls.Id == "alchemist" ? StartReagents : 0;
        }

        public ClassDef Class { get; }
        public string Name => Class.Name;

        // Which player controls this hero (0..2). One person playing alone has all three.
        public int Seat { get; set; }
        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int Hp { get; internal set; }
        public int Resolve { get; internal set; }
        public int Grit { get; internal set; }
        public int Potions { get; internal set; }

        // The alchemist's reagents (Docs/Classes.md), kept between battles.
        public int Reagents { get; internal set; }
        public int ReagentMax => ReagentCap + Special("reagent_cap");

        // Sure successes waiting for the hero's next check: the cleric's blessing (1 each) and the gambler's coin (2).
        public int Blessing { get; internal set; }
        public int Luck { get; internal set; }

        public ItemDef Weapon { get; private set; }
        public ItemDef Armor { get; private set; }
        public ItemDef Trinket { get; private set; }
        public IEnumerable<ItemDef> Equipment => new[] { Weapon, Armor, Trinket }.Where(i => i != null);
        public IReadOnlyList<UpgradeDef> Upgrades => _upgrades;

        public int MaxHp => _baseMaxHp + Equipment.Sum(i => i.Hp);
        public int ArmorValue => Class.Armor + _upgradeArmor + Equipment.Sum(i => i.Armor);
        public int MaxResolve => Class.Resolve + _upgradeResolve + Equipment.Sum(i => i.Resolve);
        public int GritCap => Math.Max(2, Class.GritCap + _upgradeGrit);

        public bool Down => Hp <= 0;

        public int Stat(Stat stat) => Math.Max(0, _baseStats[(int)stat] + Equipment.Sum(i => i.StatBonus[(int)stat]));

        // A class-specific number an upgrade raised (Docs/Classes.md: "special combo_cap +2"); 0 when none did.
        public int Special(string key) => _specials.TryGetValue(key, out int value) ? value : 0;

        // Docs/Combat.md 행동 순서표: 8 + agility / 10.
        public int Speed => 8 + Stat(Core.Stat.Agility) / 10;
        public int Evasion => Stat(Core.Stat.Agility) / 10;

        public int XpToNext => Level >= MaxLevel ? 0 : LevelXp[Level] - Xp;

        // The weapon's attack first, then the class's skills, each with this hero's upgrades applied.
        public IReadOnlyList<SkillDef> Skills => _skills ??= BuildSkills();

        public SkillDef Skill(string id) => Skills.FirstOrDefault(s => s.Id == id);

        List<SkillDef> BuildSkills()
        {
            var list = new List<SkillDef>();
            if (Weapon?.Attack != null)
            {
                var attack = Weapon.Attack.Clone();
                attack.ClassId = Class.Id;
                list.Add(attack);
            }
            foreach (var skill in Class.Skills) list.Add(skill.Clone());
            foreach (var effect in _upgrades.SelectMany(u => u.Effects))
            {
                var skill = list.FirstOrDefault(s => s.Id == effect.Target);
                if (skill == null) continue;
                switch (effect.Verb)
                {
                    case "dice":
                        skill.Dice = Math.Max(1, Math.Min(8, skill.Dice + effect.Amount));
                        break;
                    case "power":
                        skill.Power = Math.Max(0, skill.Power + effect.Amount);
                        break;
                    case "floor":
                        skill.Floor = Math.Max(0, Math.Min(90, skill.Floor + effect.Amount));
                        break;
                    case "cooldown":
                        skill.Cooldown = Math.Max(0, skill.Cooldown + effect.Amount);
                        break;
                    case "delay":
                        skill.Delay = Math.Max(30, skill.Delay + effect.Amount);
                        break;
                }
            }
            return list;
        }

        // Whether the hero can hold the item (weapons by the class's kinds; anyone wears armor and trinkets).
        public bool CanEquip(ItemDef item) => item.Slot != ItemSlot.Weapon || Class.WeaponKinds.Contains(item.Kind);

        // Puts the item on and returns what was in that slot (null for an empty slot). Health keeps its share.
        internal ItemDef Equip(ItemDef item)
        {
            ItemDef old;
            int lost = MaxHp - Hp;
            switch (item.Slot)
            {
                case ItemSlot.Weapon:
                    old = Weapon;
                    Weapon = item;
                    break;
                case ItemSlot.Armor:
                    old = Armor;
                    Armor = item;
                    break;
                default:
                    old = Trinket;
                    Trinket = item;
                    break;
            }
            _skills = null;
            if (!Down) Hp = Math.Max(1, MaxHp - lost);
            Resolve = Math.Min(Resolve, MaxResolve);
            return old;
        }

        // Empties the armor or trinket slot.
        internal void Unequip(ItemSlot slot)
        {
            int lost = MaxHp - Hp;
            if (slot == ItemSlot.Armor) Armor = null;
            else if (slot == ItemSlot.Trinket) Trinket = null;
            _skills = null;
            if (!Down) Hp = Math.Max(1, MaxHp - lost);
            Resolve = Math.Min(Resolve, MaxResolve);
        }

        internal void ApplyUpgrade(UpgradeDef upgrade)
        {
            _upgrades.Add(upgrade);
            foreach (var effect in upgrade.Effects)
            {
                switch (effect.Verb)
                {
                    case "hp":
                        _baseMaxHp += effect.Amount;
                        if (!Down) Hp += effect.Amount;
                        break;
                    case "stat":
                        if (Stats.TryParse(effect.Target, out var stat)) _baseStats[(int)stat] = Math.Min(StatCap, _baseStats[(int)stat] + effect.Amount);
                        break;
                    case "resolve":
                        _upgradeResolve += effect.Amount;
                        Resolve += effect.Amount;
                        break;
                    case "armor":
                        _upgradeArmor += effect.Amount;
                        break;
                    case "gritcap":
                        _upgradeGrit += effect.Amount;
                        break;
                    case "special":
                        _specials[effect.Target] = Special(effect.Target) + effect.Amount;
                        break;
                }
            }
            _skills = null;
        }

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

        internal void GainReagents(int amount) => Reagents = Math.Max(0, Math.Min(ReagentMax, Reagents + amount));

        // Adds experience and returns the levels reached, each with its growth applied: more health and the two
        // main abilities up. The upgrade to pick for each level is the adventure's (Adventure.LevelUps).
        internal List<int> GainXp(int amount)
        {
            var reached = new List<int>();
            if (amount <= 0) return reached;
            Xp += amount;
            while (Level < MaxLevel && Xp >= LevelXp[Level])
            {
                Level++;
                _baseMaxHp += LevelHp;
                if (!Down) Hp += LevelHp;
                foreach (var stat in Class.MainStats) _baseStats[(int)stat] = Math.Min(StatCap, _baseStats[(int)stat] + LevelStat);
                reached.Add(Level);
            }
            return reached;
        }

        public override string ToString() => $"{Name} Lv{Level} {Hp}/{MaxHp}";
    }
}
