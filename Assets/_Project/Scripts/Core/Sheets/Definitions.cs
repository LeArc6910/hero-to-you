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

        // Where the class stands in battle by default (Docs/Combat.md 줄).
        public bool BackRow { get; internal set; }
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
        // Every hero standing (the alchemist's healing mist).
        AllAllies,
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

        // The sheets it was read from: its texts are in their language (Data/Text/common.csv).
        internal GameData Data { get; set; }

        public string PerfectText => Data == null ? "" : Perfect switch
        {
            "crit150" => Data.Line("common.perfect_crit150"),
            "crit200" => Data.Line("common.perfect_crit200"),
            "crit300" => Data.Line("common.perfect_crit300"),
            "stun" => Data.Line("common.perfect_stun"),
            "armor1" => Data.Line("common.perfect_armor1"),
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

        // How the screen shows the blow (Docs/Combat.md 몬스터 기술): empty is in close (or cast, for magic),
        // "shot" an arrow or bolt, "throw" something thrown. No effect on the rules.
        public string Motion { get; internal set; } = "";
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

        // The chapter whose map may hold it (0 for any).
        public int Chapter { get; internal set; }
        public string Text { get; internal set; }
    }

    public sealed class StoryDef
    {
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public string Text { get; internal set; }

        // The picture the client draws over the page (Client SceneArt: a scene name, or "journal" for a page of the
        // journal being written); empty for none.
        public string Art { get; internal set; }
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

        // A picture over the event (events.csv `art`, the scene names of Client/Art/SceneArt.cs); empty for none.
        public string Art { get; internal set; } = "";

        // A flag the run must have (or, with a leading !, must not have) for this event to be drawn: what an
        // earlier chapter's choice brings back later (Docs/Narrative.md 장을 넘는 이야기).
        public string Requires { get; internal set; } = "";
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
        Consumable,
    }

    // A weapon, armor or trinket (Data/Items/items.csv). A weapon's Attack is the holder's basic attack.
    public sealed class ItemDef
    {
        // Forged levels (Docs/Map.md 대장간): +1 to +3, each a copy of the sheet's item made stronger.
        public int Plus { get; internal set; }

        // Armor and trinkets with something of their own (Docs/Combat.md 장비 효과): "thorns" (a blow's attacker
        // takes EffectAmount back), "regen" (EffectAmount health at each of the hero's turns).
        public string Effect { get; internal set; } = "";
        public int EffectAmount { get; internal set; }

        internal ItemDef Clone()
        {
            var copy = (ItemDef)MemberwiseClone();
            copy.Attack = Attack?.Clone();
            copy.StatBonus = (int[])StatBonus.Clone();
            return copy;
        }

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

        // What a consumable does when used (heal 15, resolve 2, cleanse, flee, bomb 8, luck 2).
        public IReadOnlyList<UpgradeEffect> Use { get; internal set; } = new List<UpgradeEffect>();
        public bool Consumable => Slot == ItemSlot.Consumable;
        public string Text { get; internal set; }

        // The sheets it was read from: its texts are in their language (Data/Text/common.csv).
        internal GameData Data { get; set; }

        // "힘 +5 · 방어도 +2", what it gives besides an attack.
        public string Bonuses
        {
            get
            {
                var parts = new List<string>();
                if (Armor != 0) parts.Add(Data?.Line("common.bonus_armor", Armor));
                if (Hp != 0) parts.Add(Data?.Line("common.bonus_hp", Hp));
                if (Resolve != 0) parts.Add(Data?.Line("common.bonus_resolve", Resolve));
                for (int i = 0; i < Stats.Count; i++)
                    if (StatBonus[i] != 0) parts.Add($"{Stats.Name((Stat)i, Data)} {(StatBonus[i] > 0 ? "+" : "")}{StatBonus[i]}");
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

namespace Game.Core
{
    // A chapter of the campaign (Data/Story/chapters.csv, Docs/Map.md): its map, its boss and middle boss, its stories
    // and the event pools its map draws from, and which flags weaken its boss.
    public sealed class ChapterDef
    {
        public string Id { get; internal set; }
        public int Index { get; internal set; }
        public string Title { get; internal set; }
        public MapDef Map { get; internal set; }
        public string Boss { get; internal set; }
        public string Midboss { get; internal set; }
        public string Town { get; internal set; }
        public string Prologue { get; internal set; }
        public string Epilogue { get; internal set; }
        public string EpilogueKind { get; internal set; }
        public string EpilogueGrim { get; internal set; }
        public string WildPool { get; internal set; }
        public string ChestPool { get; internal set; }
        public string HerbPool { get; internal set; }
        public string QuestPool { get; internal set; }
        public string CluePool { get; internal set; }
        public string ShrinePool { get; internal set; }
        public string ElderPool { get; internal set; }
        // The camp on the road into this chapter, between two chapters of one run (Docs/Narrative.md 막간): an event
        // card of this pool comes before the prologue. Empty for none (the first chapter).
        public string InterludePool { get; internal set; } = "";

        // Reading the quest site sets WeakenFlag (the boss −25% and no armor); the clue site sets NoSummonFlag (no
        // helpers in its second phase).
        public string WeakenFlag { get; internal set; }
        public string NoSummonFlag { get; internal set; }
        public string QuestName { get; internal set; }
        public string ClueName { get; internal set; }
        public string MidbossName { get; internal set; }
        public string BossName { get; internal set; }

        // The story page when the rift fills up and the run is lost.
        public string Rift { get; internal set; }

        // The ending (Docs/Narrative.md 결말): EpilogueKind when at least KindNeed of KindFlags are set, else
        // EpilogueGrim when at least GrimNeed of GrimFlags are, else Epilogue.
        public IReadOnlyList<string> KindFlags { get; internal set; } = new List<string>();
        public IReadOnlyList<string> GrimFlags { get; internal set; } = new List<string>();
        public int KindNeed { get; internal set; }
        public int GrimNeed { get; internal set; }

        // The map screen's goals, in the chapter's words, and the lines the boss battle shows for the quest and clue.
        public string GoalQuest { get; internal set; }
        public string GoalClue { get; internal set; }
        public string GoalCamp { get; internal set; }
        public string GoalBoss { get; internal set; }
        public string WeakenText { get; internal set; }
        public string NoSummonText { get; internal set; }

        // The dungeon's rooms (encounter ids, in order) and its name; empty when the map has none.
        public IReadOnlyList<string> Dungeon { get; internal set; } = new List<string>();
        public string DungeonName { get; internal set; }

        // The tune the client plays on this chapter's map (a Tune name: forest, harbor, snow).
        public string Music { get; internal set; }

        // The chapter's maps: one is drawn for each run by the seed (Map is the first).
        public IReadOnlyList<MapDef> Maps { get; internal set; } = new List<MapDef>();

        // The kind ending also needs this much renown (0: no need), for the last chapter's ending.
        public int KindRenown { get; internal set; }
    }

    // A talk by the campfire (Data/Story/talks.csv, Docs/Narrative.md 동료 대화): two classes in the party, a few lines
    // between them, heard at the dawn of a day. Chapter 0 means any chapter.
    public sealed class TalkDef
    {
        public string Id { get; internal set; }
        public int Chapter { get; internal set; }
        public string A { get; internal set; }
        public string B { get; internal set; }
        public string Requires { get; internal set; }
        public IReadOnlyList<(string speaker, string text)> Lines { get; internal set; } = new List<(string, string)>();
    }

    // An achievement kept in the journal (Data/Journal/achievements.csv, Docs/Journal.md 업적): a rule checked when a
    // run ends, like "win", "chapters 3", "renown 30", "classes 6".
    public sealed class AchievementDef
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Rule { get; internal set; }
        public int Amount { get; internal set; }
        public string Text { get; internal set; }
    }

    // A step of the party's renown (Data/Story/renown.csv, Docs/Narrative.md 명성): the title the party goes by once it
    // has Need renown.
    public sealed class RenownDef
    {
        public int Need { get; internal set; }
        public string Title { get; internal set; }
        public string Text { get; internal set; }
    }

    // A journal page perk (Data/Journal/perks.csv, Docs/Journal.md): bought with pages, chosen at the start of a run.
    // A pairing of classes the party can have, with its name and a small gift at the start (Data/Classes/combos.csv,
    // Docs/Classes.md 조합).
    public sealed class ComboDef
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public IReadOnlyList<string> Classes { get; internal set; } = new List<string>();
        public UpgradeEffect Effect { get; internal set; }
        public string Text { get; internal set; }
    }

    public sealed class PerkDef
    {
        // An achievement that must be earned before the perk can be unlocked; empty for none.
        public string Requires { get; internal set; } = "";
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public int Pages { get; internal set; }
        public UpgradeEffect Effect { get; internal set; }
        public string Text { get; internal set; }
    }
}
