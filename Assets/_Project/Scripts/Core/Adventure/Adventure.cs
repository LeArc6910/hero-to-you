using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    public enum AdventureScreen
    {
        Story,
        Map,
        Join,
        Battle,
        BattleResult,
        Event,
        EventResult,
        Town,
        // Between two rooms of the dungeon: go deeper or leave.
        DungeonRest,
        Victory,
        Defeat,
    }

    // How hard the run is (Docs/Map.md 난이도): easy monsters have a quarter less health and the party starts with
    // one more hope and a little gold. The journal's 가시밭길 makes it harder on top.
    public enum Difficulty
    {
        Easy,
        Normal,
    }

    // What an event choice did (Docs/Narrative.md).
    public sealed class EventResult
    {
        public EventChoice Choice { get; internal set; }
        public Hero Hero { get; internal set; }
        public CheckResult Check { get; internal set; }
        public bool Success { get; internal set; }
        public string Text { get; internal set; }
        public List<string> Changes { get; } = new List<string>();
    }

    // A level-up waiting for its upgrade: two of the class's upgrades not taken yet (Docs/Classes.md).
    public sealed class LevelUpChoice
    {
        internal LevelUpChoice(int hero, int level, List<UpgradeDef> options)
        {
            Hero = hero;
            Level = level;
            Options = options;
        }

        public int Hero { get; }
        public int Level { get; }
        public IReadOnlyList<UpgradeDef> Options { get; }
    }

    // A thing the town's shop sells today: an item (gear goes to the bag, a consumable to the hero in town), or reagents.
    public sealed class ShopItem
    {
        public ItemDef Item { get; internal set; }

        // "reagent" for reagents; null for an item.
        public string Goods { get; internal set; }
        public string Name => Item?.Name ?? "연금 재료 2";
        public int Price { get; internal set; }
        public bool Sold { get; internal set; }
    }

    // A campaign of chapters for a party of three (Docs/Map.md): each chapter is a hex map with map turns hero by hero
    // and a movement roll, what the hexes hold (enemies, events, treasure, the town, the shrine, the quest site, the
    // clue, the middle boss's camp, the boss), fog that lifts around the heroes, days and the rift, battles with whoever
    // is near (or avoided), levels with their upgrades, the bag, consumables and equipment. The client shows Screen
    // and calls the method for what the player chose; every accepted call is written to Log, so the seed, the party,
    // the journal perks and the log replay the same run (Load, Docs/Saving.md). Dice come from the adventure's Fate;
    // each battle gets a seed from the adventure's. The parts: Adventure.Map.cs, .Battle.cs, .Events.cs, .Town.cs,
    // .Save.cs.
    public sealed partial class Adventure
    {
        public const int StartHope = 3;
        public const int StartGold = 15;
        public const int DoomMax = 10;
        public const int HopeRevivePercent = 30;
        public const int TimesOfDay = 4;

        static readonly string[] TimeNames = { "아침", "낮", "저녁", "밤" };

        // Separate streams for separate things (Docs/Saving.md): when a sheet changes (one more event, a new
        // item), only the draws of that kind come out differently, and a save's commands still fit the rest.
        readonly GameRandom _eventRandom;
        readonly GameRandom _lootRandom;
        readonly GameRandom _levelRandom;
        readonly GameRandom _shopRandom;
        readonly GameRandom _talkRandom;
        GameRandom _mapRandom;
        readonly Fate _moveFate;
        readonly Fate _eventFate;
        readonly Fate _avoidFate;
        int _battlesStarted;
        readonly HashSet<string> _flags = new HashSet<string>();
        readonly HashSet<string> _seen = new HashSet<string>();
        readonly List<string> _log = new List<string>();

        // What a story page leads to when continued.
        enum Then
        {
            Map,
            NextChapter,
            Defeat,
        }

        Then _then;

        public Adventure(GameData data, IReadOnlyList<string> classIds, ulong seed, IReadOnlyList<string> perks = null, Difficulty difficulty = Difficulty.Normal, bool hardcore = false, int firstChapter = 0, int lastChapter = -1)
        {
            Hardcore = hardcore;
            FirstChapter = Math.Max(0, Math.Min(firstChapter, data.Chapters.Count - 1));
            LastChapter = lastChapter < 0 ? data.Chapters.Count - 1 : Math.Max(FirstChapter, Math.Min(lastChapter, data.Chapters.Count - 1));
            Data = data ?? throw new ArgumentNullException(nameof(data));
            if (classIds == null || classIds.Count != 3) throw new ArgumentException("A party is three heroes.", nameof(classIds));
            Seed = seed;
            ClassIds = classIds.ToList();
            Perks = (perks ?? new string[0]).Where(p => p.Length > 0).ToList();
            Difficulty = difficulty;
            _eventRandom = new GameRandom(seed, 20);
            _lootRandom = new GameRandom(seed, 21);
            _levelRandom = new GameRandom(seed, 22);
            _shopRandom = new GameRandom(seed, 23);
            _talkRandom = new GameRandom(seed, 24);
            _moveFate = new Fate(seed, 11);
            _eventFate = new Fate(seed, 12);
            _avoidFate = new Fate(seed, 13);
            foreach (string id in classIds)
            {
                if (!data.Classes.TryGetValue(id, out var cls) || !cls.Playable) throw new ArgumentException($"No playable class '{id}'.", nameof(classIds));
                Heroes.Add(new Hero(cls, Heroes.Count));
            }
            Hope = StartHope + (difficulty == Difficulty.Easy ? 1 : 0);
            Gold = StartGold + (difficulty == Difficulty.Easy ? 10 : 0);
            StartChapter(FirstChapter);
            StartLate();
            ApplyPerks();
        }

        public GameData Data { get; }
        public ulong Seed { get; }
        public IReadOnlyList<string> ClassIds { get; }

        // The journal perks this run started with (Docs/Journal.md).
        public IReadOnlyList<string> Perks { get; }
        public bool Hard { get; private set; }
        public Difficulty Difficulty { get; }

        // The hardcore mode (Docs/Combat.md 하드코어): a hero who falls with no hope left is gone for good, and a new
        // companion can be taken on in town.
        public bool Hardcore { get; }

        // The chapters this run plays (Docs/Map.md 짧은 판): from FirstChapter to LastChapter, 0-based.
        public int FirstChapter { get; }
        public int LastChapter { get; }
        public bool FullRun => FirstChapter == 0 && LastChapter == Data.Chapters.Count - 1;

        // The level a party starting at a later chapter begins with, and its gold to buy gear.
        public static readonly int[] StartLevels = { 1, 4, 6, 8, 10 };
        public const int LateGold = 30;

        // A party starting at a later chapter comes with the levels, gear (of the chapter's tier, plain pieces in
        // sheet order) and some gold a party would have gathered on the way.
        void StartLate()
        {
            if (FirstChapter == 0) return;
            int level = StartLevels[Math.Min(FirstChapter, StartLevels.Length - 1)];
            int tier = FirstChapter + 1;
            foreach (var h in Heroes)
            {
                GainXp(h, Hero.LevelXp[level - 1]);
                var plain = Data.ItemList.Where(i => i.Price > 0 && !i.Consumable && i.Effect.Length == 0 && i.Tier <= tier).ToList();
                var weapon = plain.Where(i => i.Slot == ItemSlot.Weapon && h.CanEquip(i) && (i.Attack == null || i.Attack.Effect.Length == 0))
                    .OrderByDescending(i => i.Tier).FirstOrDefault();
                string armorKind = h.Back ? "light" : "heavy";
                var armor = plain.Where(i => i.Slot == ItemSlot.Armor && i.Kind == armorKind).OrderByDescending(i => i.Tier).FirstOrDefault();
                var trinket = plain.Where(i => i.Slot == ItemSlot.Trinket).OrderByDescending(i => i.Tier).FirstOrDefault();
                foreach (var item in new[] { weapon, armor, trinket })
                    if (item != null) h.Equip(item.Clone());
                h.Hp = h.MaxHp;
            }
            Gold += LateGold * FirstChapter;
        }

        // What the run did, for the journal's achievements (Docs/Journal.md).
        public int Falls { get; private set; }
        public int DungeonsCleared { get; private set; }
        public int ContractsDone { get; private set; }
        public IReadOnlyList<string> Endings => _endings;
        readonly List<string> _endings = new List<string>();
        public List<Hero> Heroes { get; } = new List<Hero>();
        public int Hope { get; internal set; }
        public int Gold { get; internal set; }

        public ChapterDef Chapter { get; private set; }
        public int ChaptersCleared { get; private set; }
        public int MidbossesBeaten { get; private set; }

        // The rift (Docs/Map.md): one more each day; at DoomMax it opens and the adventure is lost.
        public int Doom { get; internal set; }
        public int Day { get; private set; }
        public int TimeOfDay { get; private set; }
        public string TimeName => TimeNames[TimeOfDay];
        public bool Night => TimeOfDay == TimesOfDay - 1;

        public AdventureScreen Screen { get; private set; }
        public StoryDef Story { get; private set; }

        // What the last step did, a line each ("기사 레벨 2!", "새 날이 밝았다").
        public List<string> Report { get; } = new List<string>();
        public IReadOnlyCollection<string> Flags => _flags;
        public bool HasFlag(string flag) => _flags.Contains(flag);
        public int BattlesWon { get; private set; }
        public IReadOnlyList<string> Log => _log;
        public bool Over => Screen == AdventureScreen.Victory || Screen == AdventureScreen.Defeat;

        void Record(string command) => _log.Add(command);

        string Blocked()
        {
            if (LevelUp != null) return "레벨업 강화를 먼저 고르세요.";
            if (Over) return "모험이 끝났습니다.";
            return null;
        }

        // ---- Journal perks (Docs/Journal.md) ----

        void ApplyPerks()
        {
            foreach (string id in Perks)
            {
                var perk = Data.Perks.FirstOrDefault(p => p.Id == id);
                if (perk == null) throw new ArgumentException($"No journal perk '{id}'.");
                ApplyGift(perk.Effect);
            }
            // The pairings of classes the party has (Docs/Classes.md 조합).
            foreach (var combo in Data.Combos.Where(c => c.Classes.All(cls => ClassIds.Contains(cls))))
            {
                _combos.Add(combo);
                ApplyGift(combo.Effect);
            }
            Report.Clear();
        }

        readonly List<ComboDef> _combos = new List<ComboDef>();

        // ---- The codex (Docs/Journal.md 도감): what this run met, for the journal to keep ----

        readonly HashSet<string> _codexMonsters = new HashSet<string>();
        readonly HashSet<string> _codexItems = new HashSet<string>();
        readonly HashSet<string> _codexStories = new HashSet<string>();

        public IReadOnlyCollection<string> SeenMonsters => _codexMonsters;
        public IReadOnlyCollection<string> ReadStories => _codexStories;

        // Gear found or bought this run, and what the heroes wear now.
        public IReadOnlyCollection<string> FoundItems
        {
            get
            {
                var all = new HashSet<string>(_codexItems);
                foreach (var hero in Heroes)
                    foreach (var item in hero.Equipment)
                        all.Add(item.Id);
                return all;
            }
        }

        void AddToBag(ItemDef item)
        {
            Bag.Add(item);
            _codexItems.Add(item.Id);
        }

        // People an earlier choice saved who came back this run (events with `requires`).
        public int CallbacksMet => Data.Events.Count(e => e.Requires.Length > 0 && !e.Requires.StartsWith("!") && _seen.Contains(e.Id));

        // The class pairings this party makes, with their gifts.
        public IReadOnlyList<ComboDef> Combos => _combos;

        // What a journal perk or a class pairing gives at the start (GameData.PerkVerbs).
        void ApplyGift(UpgradeEffect e)
        {
            switch (e.Verb)
            {
                case "gold":
                    Gold += e.Amount;
                    break;
                case "potion":
                    foreach (var h in Heroes) h.AddConsumable(Hero.PotionId, e.Amount);
                    break;
                case "tonic":
                    foreach (var h in Heroes) h.AddConsumable("tonic", e.Amount);
                    break;
                case "hope":
                    Hope += e.Amount;
                    break;
                case "doom":
                    Doom = Math.Max(0, Doom + e.Amount);
                    break;
                case "xp":
                    foreach (var h in Heroes) GainXp(h, e.Amount);
                    break;
                case "hard":
                    Hard = true;
                    break;
                case "reveal":
                    // The nearest hexes to the start the party does not know yet.
                    var start = Map.Tile(Site.Start).Hex;
                    foreach (var tile in Map.Tiles.Where(t => !Knows(t)).OrderBy(t => t.Hex.Distance(start)).ThenBy(t => t.Hex.Row).ThenBy(t => t.Hex.Col).Take(e.Amount).ToList())
                        _revealed.Add(tile.Hex);
                    break;
                case "bless":
                    foreach (var h in Heroes) h.Blessing += Math.Max(1, e.Amount);
                    break;
                case "forge":
                    foreach (var h in Heroes)
                        for (int n = 0; n < Math.Max(1, e.Amount); n++) ForgeFree(h, ItemSlot.Weapon);
                    break;
                case "renown":
                    Renown += e.Amount;
                    break;
            }
        }

        // Journal pages this run earned, once it is over: three for each chapter cleared, one for each middle boss,
        // one for a kind ending, one just for going; twice as many on the hard road when won.
        public int PagesEarned
        {
            get
            {
                int pages = 1 + ChaptersCleared * 3 + MidbossesBeaten + (_kindEndings > 0 ? _kindEndings : 0);
                if (Hard && Screen == AdventureScreen.Victory) pages *= 2;
                return pages;
            }
        }

        int _kindEndings;

        // ---- Chapters and story pages ----

        // How many commands the log had when this chapter began (Docs/Saving.md 장 처음부터 다시).
        public int ChapterStart { get; private set; }

        // The save of this run as it was when the chapter began, to try the chapter again after losing it. Null in
        // the hardcore mode and on the hard road, where a loss is a loss.
        public string ChapterStartSave()
        {
            if (Hardcore || Hard) return null;
            var lines = Save().Replace("\r", "").Split('\n').ToList();
            int header = lines.FindIndex(l => !(l.StartsWith(SaveHeader) || l.StartsWith("seed ") || l.StartsWith("party ") || l.StartsWith("perks") || l.StartsWith("level ") || l == "hardcore" || l.StartsWith("chapters ") || l.StartsWith("sheets ")));
            if (header < 0) header = lines.Count;
            return string.Join("\n", lines.Take(header + ChapterStart)) + "\n";
        }

        void StartChapter(int index)
        {
            ChapterStart = _log.Count;
            Chapter = Data.Chapters[index];
            // One of the chapter's maps, drawn by the seed alone (Docs/Map.md 지도 그리기).
            var maps = Chapter.Maps.Count > 0 ? Chapter.Maps : new[] { Chapter.Map };
            Map = new WorldMap(maps[new GameRandom(Seed, 300 + (ulong)index).Next(maps.Count)]);
            _mapRandom = new GameRandom(Seed, 100 + (ulong)index);
            var start = Map.Tile(Site.Start).Hex;
            Positions.Clear();
            foreach (var _ in Heroes) Positions.Add(start);
            Day = 1;
            TimeOfDay = 0;
            if (index > 0) Doom = 0;
            _explored.Clear();
            _revealed.Clear();
            _shopDay = 0;
            _rumorDay = 0;
            _donateDay = 0;
            _rerollDay = 0;
            _eventTile = null;
            _elderMet = false;
            DungeonRoom = 0;
            Contract = null;
            Event = null;
            Battle = null;
            if (index > 0)
            {
                // Between chapters the party rests and gathers itself.
                foreach (var hero in Heroes)
                {
                    if (hero.Dead) continue;
                    if (hero.Down) hero.Hp = 1;
                    hero.HealWounds();
                    hero.Heal(hero.MaxHp);
                    hero.GainResolve(hero.MaxResolve);
                }
                Hope = Math.Max(Hope, StartHope);
            }
            Populate();
            UpdateSight();
            ShowStory(Chapter.Prologue, Then.Map);
        }

        void ShowStory(string id, Then then)
        {
            Story = Data.Stories[id];
            _codexStories.Add(id);
            _then = then;
            Screen = AdventureScreen.Story;
        }

        // Goes on from a story page, a battle's or an event's result.
        public string Continue()
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            switch (Screen)
            {
                case AdventureScreen.Story:
                    Record("continue");
                    Report.Clear();
                    switch (_then)
                    {
                        case Then.Map:
                            StartMapTurn(FirstStanding());
                            break;
                        case Then.Defeat:
                            Screen = AdventureScreen.Defeat;
                            break;
                        default:
                            if (Chapter.Index + 1 <= LastChapter) StartChapter(Chapter.Index + 1);
                            else Screen = AdventureScreen.Victory;
                            break;
                    }
                    return null;
                case AdventureScreen.BattleResult:
                case AdventureScreen.EventResult:
                    Record("continue");
                    Report.Clear();
                    if (_pendingBattle != null)
                    {
                        string id = _pendingBattle;
                        _pendingBattle = null;
                        PrepareJoin(_eventTile ?? Map.Tile(Positions[MapHero]), id, JoinKind.Ambush);
                        return null;
                    }
                    if (_backToTown)
                    {
                        _backToTown = false;
                        Screen = AdventureScreen.Town;
                        return null;
                    }
                    EndTurn();
                    return null;
                default:
                    return $"{Screen}에서는 계속할 것이 없습니다.";
            }
        }

        int FirstStanding()
        {
            int index = Heroes.FindIndex(h => !h.Down);
            return index < 0 ? 0 : index;
        }

        // The chapter's boss is down: the ending that fits how the party treated people on the way (the chapter's
        // flag lists, Docs/Narrative.md 결말).
        void EndChapter()
        {
            ChaptersCleared++;
            int kindness = Chapter.KindFlags.Count(HasFlag);
            int grimness = Chapter.GrimFlags.Count(HasFlag);
            bool kind = kindness >= Chapter.KindNeed && Renown >= Chapter.KindRenown;
            string ending = kind ? Chapter.EpilogueKind : grimness >= Chapter.GrimNeed ? Chapter.EpilogueGrim : Chapter.Epilogue;
            if (ending == Chapter.EpilogueKind) _kindEndings++;
            _endings.Add(ending == Chapter.EpilogueKind ? "kind" : ending == Chapter.EpilogueGrim ? "grim" : "plain");
            ShowStory(ending, Then.NextChapter);
        }

        // The chapter's goals for the map screen, in the chapter's words: done or not, in order (Docs/Map.md 본 줄거리).
        public IReadOnlyList<(string text, bool done)> Objectives => new List<(string, bool)>
        {
            (Chapter.GoalQuest + $" (보스 생명력 −{WeakenPercent}%·방어도 −{WeakenArmor})", HasFlag(Chapter.WeakenFlag)),
            (Chapter.GoalClue + " (보스가 부하를 부르지 못한다)", HasFlag(Chapter.NoSummonFlag)),
            (Chapter.GoalCamp + $" (희망 +1·균열 −1·좋은 장비·명성 +{MidbossRenown})", Map.Tile(Site.Camp).Used),
            (Chapter.GoalBoss, Map.Tile(Site.Boss).Used),
        };

        // The next goal not done, in one line.
        public string Objective
        {
            get
            {
                var next = Objectives.FirstOrDefault(o => !o.done);
                return next.text ?? Objectives[Objectives.Count - 1].text;
            }
        }
    }
}
