using System.Collections.Generic;

namespace Game.Core
{
    // What the sheets in Assets/_Project/Resources/Data describe (Docs/Classes.md, Docs/Combat.md, Docs/Narrative.md). GameData
    // reads and checks them; nothing changes them during play.

    public sealed class ClassDef
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Role { get; internal set; }
        public int Hp { get; internal set; }
        public int Armor { get; internal set; }
        public int Resolve { get; internal set; }
        public int GritCap { get; internal set; }
        public int[] Stats { get; internal set; } = new int[Game.Core.Stats.Count];
        // The class's own skills; the basic attack comes from the weapon (ItemDef.Attack).
        public IReadOnlyList<SkillDef> Skills { get; internal set; } = new List<SkillDef>();

        // The weapon every hero of the class starts with, and the kinds of weapon it can hold.
        public ItemDef StartWeapon { get; internal set; }
        public IReadOnlyList<string> WeaponKinds { get; internal set; } = new List<string>();
        public IReadOnlyList<UpgradeDef> Upgrades { get; internal set; } = new List<UpgradeDef>();
        public string Color { get; internal set; }
        public string Mechanic { get; internal set; }
        public string Blurb { get; internal set; }

        // Whether it can be picked yet (Docs/Classes.md: the M5 classes are listed but not playable).
        public bool Playable { get; internal set; }

        public int Stat(Stat stat) => Stats[(int)stat];

        // The two highest abilities, which grow on level up. Ties go to the earlier ability.
        public Stat[] MainStats
        {
            get
            {
                int first = 0;
                for (int i = 1; i < Game.Core.Stats.Count; i++)
                    if (Stats[i] > Stats[first]) first = i;
                int second = first == 0 ? 1 : 0;
                for (int i = 0; i < Game.Core.Stats.Count; i++)
                    if (i != first && Stats[i] > Stats[second]) second = i;
                return new[] { (Stat)first, (Stat)second };
            }
        }
    }

    public enum SkillKind
    {
        Physical,
        Magic,
        Heal,
        Revive,
        Support,
    }

    public enum SkillTarget
    {
        Enemy,
        AllEnemies,
        Ally,
        OtherAlly,
        DownedAlly,
        Self,
    }

    public sealed class SkillDef
    {
        public string Id { get; internal set; }
        public string ClassId { get; internal set; }
        public string Name { get; internal set; }

        // The ability its dice use; FixedChance instead when the chance never changes (the gambler's 50%).
        public Stat? Stat { get; internal set; }
        public int FixedChance { get; internal set; }
        public int Dice { get; internal set; }
        public int Power { get; internal set; }
        public int Floor { get; internal set; }
        public SkillKind Kind { get; internal set; }
        public SkillTarget Target { get; internal set; }
        public int Delay { get; internal set; }
        public int Cooldown { get; internal set; }

        // fire, frost, lightning or empty (the elementalist's cycle).
        public string Element { get; internal set; }

        // burn, chill, lifesteal, guard, taunt, mark, bless, lucky, backfire or empty.
        public string Effect { get; internal set; }

        // crit150, crit200, crit300, stun, armor1 or empty.
        public string Perfect { get; internal set; }

        // What using it spends besides the turn: "reagent" or "soul" (CostAmount of them), or empty.
        public string CostKind { get; internal set; } = "";
        public int CostAmount { get; internal set; }
        public string Text { get; internal set; }

        // A copy to change for one hero (level-up upgrades, Hero.Skills).
        internal SkillDef Clone() => (SkillDef)MemberwiseClone();

        public bool Rolls => Dice > 0;
        public bool Damages => Kind == SkillKind.Physical || Kind == SkillKind.Magic;

        // The damage percent a perfect check gives (100 when the perfect effect is not a multiplier).
        public int PerfectPercent => Perfect switch
        {
            "crit150" => 150,
            "crit200" => 200,
            "crit300" => 300,
            _ => 100,
        };

        public string PerfectText => Perfect switch
        {
            "crit150" => "완벽: 1.5배",
            "crit200" => "완벽: 2배",
            "crit300" => "완벽: 3배",
            "stun" => "완벽: 기절",
            "armor1" => "완벽: 방어도 +1",
            _ => "",
        };
    }

    public enum MonsterAim
    {
        Random,
        Weakest,
        Strongest,
    }

    public enum MonsterTarget
    {
        One,
        All,
        Self,
    }

    public sealed class MonsterSkillDef
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public int Dice { get; internal set; }
        public int Power { get; internal set; }
        public int Floor { get; internal set; }

        // physical, magic or buff.
        public string Kind { get; internal set; }
        public MonsterTarget Target { get; internal set; }
        public MonsterAim Aim { get; internal set; }
        public int Delay { get; internal set; }

        // burn, howl or empty.
        public string Effect { get; internal set; }
        public string Text { get; internal set; }

        public bool Damages => Kind == "physical" || Kind == "magic";
    }

    public sealed class MonsterDef
    {
        internal MonsterDef Clone() => (MonsterDef)MemberwiseClone();

        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public int Hp { get; internal set; }
        public int Speed { get; internal set; }
        public int Accuracy { get; internal set; }
        public int Armor { get; internal set; }
        public int Ward { get; internal set; }
        public int Xp { get; internal set; }
        public int Gold { get; internal set; }
        public IReadOnlyList<MonsterSkillDef> Pattern { get; internal set; } = new List<MonsterSkillDef>();
        public IReadOnlyList<MonsterSkillDef> Pattern2 { get; internal set; } = new List<MonsterSkillDef>();

        // At or below this percent of its health the second pattern starts (0 for none).
        public int Phase2 { get; internal set; }
        public string Phase2Line { get; internal set; }

        // Monsters that join the boss when its second phase starts.
        public IReadOnlyList<MonsterDef> Phase2Summon { get; internal set; } = new List<MonsterDef>();
        public bool Boss { get; internal set; }
        public string Color { get; internal set; }

        // 1 for an ordinary monster, 2 for a big one.
        public int Size { get; internal set; }
        public string Text { get; internal set; }
    }

    public sealed class EncounterDef
    {
        public string Id { get; internal set; }
        public IReadOnlyList<MonsterDef> Monsters { get; internal set; } = new List<MonsterDef>();

        // Where the map puts it (terrain codes) and how strong it is: 1 early, 2 once the rift has grown, 0 never by
        // itself (events and the boss).
        public IReadOnlyList<string> Terrains { get; internal set; } = new List<string>();
        public int Danger { get; internal set; }
        public string Text { get; internal set; }
    }

    public sealed class StoryDef
    {
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public string Text { get; internal set; }
    }

    // One result command of an event choice (Docs/Narrative.md): "gold 10" is Verb gold, Amount 10.
    public sealed class Outcome
    {
        public string Verb { get; internal set; }
        public int Amount { get; internal set; }
        public string Arg { get; internal set; }

        public override string ToString() => Arg != null ? $"{Verb} {Arg}" : $"{Verb} {Amount}";
    }

    public sealed class EventChoice
    {
        public int Index { get; internal set; }
        public string Label { get; internal set; }

        // The class that must be in the party, alive, to see this choice; null for anyone.
        public string ClassId { get; internal set; }

        // A flag the adventure must have (or, with a leading !, must not have) for the choice to show.
        public string Requires { get; internal set; }
        public Stat? Stat { get; internal set; }
        public int FixedChance { get; internal set; }
        public int Dice { get; internal set; }
        public int Need { get; internal set; }
        public IReadOnlyList<Outcome> Success { get; internal set; } = new List<Outcome>();
        public IReadOnlyList<Outcome> Fail { get; internal set; } = new List<Outcome>();
        public string SuccessText { get; internal set; }
        public string FailText { get; internal set; }

        public bool HasCheck => Dice > 0;
    }

    public sealed class EventCard
    {
        public string Id { get; internal set; }
        public string Pool { get; internal set; }

        // A class's own event: only drawn when that class is in the party.
        public string ClassId { get; internal set; }
        public string Title { get; internal set; }
        public string Text { get; internal set; }
        public string Journal { get; internal set; }
        public List<EventChoice> Choices { get; } = new List<EventChoice>();
    }
}

namespace Game.Core
{
    public enum ItemSlot
    {
        Weapon,
        Armor,
        Trinket,
    }

    // A weapon, armor or trinket (Data/Items/items.csv). A weapon's Attack is the holder's basic attack.
    public sealed class ItemDef
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public ItemSlot Slot { get; internal set; }
        public string Kind { get; internal set; }
        public int Tier { get; internal set; }
        public int Price { get; internal set; }
        public SkillDef Attack { get; internal set; }
        public int Armor { get; internal set; }
        public int Hp { get; internal set; }
        public int Resolve { get; internal set; }
        public int[] StatBonus { get; internal set; } = new int[Stats.Count];
        public string Text { get; internal set; }

        // "힘 +5 · 방어도 +2", what it gives besides an attack.
        public string Bonuses
        {
            get
            {
                var parts = new List<string>();
                if (Armor != 0) parts.Add($"방어도 +{Armor}");
                if (Hp != 0) parts.Add($"생명력 +{Hp}");
                if (Resolve != 0) parts.Add($"결의 +{Resolve}");
                for (int i = 0; i < Stats.Count; i++)
                    if (StatBonus[i] != 0) parts.Add($"{Stats.Name((Stat)i)} {(StatBonus[i] > 0 ? "+" : "")}{StatBonus[i]}");
                return string.Join(" · ", parts);
            }
        }
    }

    // One change a level-up upgrade makes: "power attack +3" is Verb power, Target attack, Amount 3.
    public sealed class UpgradeEffect
    {
        public string Verb { get; internal set; }
        public string Target { get; internal set; }
        public int Amount { get; internal set; }
    }

    // A choice offered on level up (Data/Classes/upgrades.csv, Docs/Classes.md).
    public sealed class UpgradeDef
    {
        public string Id { get; internal set; }
        public string ClassId { get; internal set; }
        public string Name { get; internal set; }
        public IReadOnlyList<UpgradeEffect> Effects { get; internal set; } = new List<UpgradeEffect>();
        public string Text { get; internal set; }
    }
}
