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
        Victory,
        Defeat,
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

    // A thing the town's shop sells today: an item for the bag, or a potion or reagents.
    public sealed class ShopItem
    {
        public ItemDef Item { get; internal set; }

        // "potion" or "reagent" for those; null for an item.
        public string Goods { get; internal set; }
        public string Name => Item?.Name ?? (Goods == "potion" ? "회복 물약" : "연금 재료 2");
        public int Price { get; internal set; }
        public bool Sold { get; internal set; }
    }

    // One run of the party over a chapter map (Docs/Map.md): map turns hero by hero with a movement roll, what the
    // hexes hold (enemies, events, treasure, the town, the shrine, the quest stone, the boss), days and the rift,
    // battles with whoever is near, levels with their upgrades, the bag and equipment. The client shows Screen and
    // calls the method for what the player chose; every accepted call is written to Log, so the seed, the party and
    // the log replay the same run (Load, Docs/Saving.md). Dice come from the adventure's Fate; each battle gets a
    // seed from the adventure's.
    public sealed partial class Adventure
    {
        public const int StartHope = 3;
        public const int StartGold = 15;
        public const int DoomMax = 10;
        public const int HopeRevivePercent = 30;
        public const int MoveBase = 2;
        public const int MoveDice = 3;
        public const int JoinRange = 2;
        public const int CampPercent = 20;
        public const int InnPercent = 50;
        public const int InnCost = 10;
        public const int PotionPrice = 8;
        public const int ReagentPrice = 4;
        public const int StartEnemies = 6;
        public const int StartEvents = 10;
        public const int StartTreasures = 3;
        public const int StartHerbs = 3;
        public const int TimesOfDay = 4;

        static readonly string[] TimeNames = { "아침", "낮", "저녁", "밤" };

        readonly GameRandom _random;
        readonly Fate _fate;
        readonly HashSet<string> _flags = new HashSet<string>();
        readonly HashSet<string> _seen = new HashSet<string>();
        readonly List<string> _log = new List<string>();
        readonly Queue<(int hero, int level)> _levels = new Queue<(int, int)>();
        string _pendingBattle;
        MapTile _eventTile;
        bool _backToTown;
        bool _weakNext;
        List<Hero> _fighters = new List<Hero>();
        int _shopDay;

        public Adventure(GameData data, IReadOnlyList<string> classIds, ulong seed)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            if (classIds == null || classIds.Count != 3) throw new ArgumentException("A party is three heroes.", nameof(classIds));
            Seed = seed;
            ClassIds = classIds.ToList();
            _random = new GameRandom(seed, 10);
            _fate = new Fate(seed, 11);
            foreach (string id in classIds)
            {
                if (!data.Classes.TryGetValue(id, out var cls) || !cls.Playable) throw new ArgumentException($"No playable class '{id}'.", nameof(classIds));
                Heroes.Add(new Hero(cls, Heroes.Count));
            }
            Map = new WorldMap(data.Map);
            var start = Map.Tile(Site.Start).Hex;
            foreach (var _ in Heroes) Positions.Add(start);
            Hope = StartHope;
            Gold = StartGold;
            Day = 1;
            Populate();
            ShowStory("prologue");
        }

        public GameData Data { get; }
        public ulong Seed { get; }
        public IReadOnlyList<string> ClassIds { get; }
        public List<Hero> Heroes { get; } = new List<Hero>();
        public int Hope { get; internal set; }
        public int Gold { get; internal set; }

        // The rift (Docs/Map.md): one more each day; at DoomMax it opens and the adventure is lost.
        public int Doom { get; internal set; }
        public int Day { get; private set; }
        public int TimeOfDay { get; private set; }
        public string TimeName => TimeNames[TimeOfDay];
        public bool Night => TimeOfDay == TimesOfDay - 1;

        public WorldMap Map { get; }
        public List<Hex> Positions { get; } = new List<Hex>();

        // Whose map turn it is, and the movement rolled (-1 before rolling).
        public int MapHero { get; private set; }
        public int MovePoints { get; private set; } = -1;
        public CheckResult MoveRoll { get; private set; }

        public AdventureScreen Screen { get; private set; }
        public StoryDef Story { get; private set; }
        public EncounterDef Encounter { get; private set; }
        public Battle Battle { get; private set; }
        public bool BossBattle { get; private set; }
        public IReadOnlyList<Hero> Fighters => _fighters;
        public EventCard Event { get; private set; }
        public EventResult Result { get; private set; }

        // Who may join the battle about to start (Join): living heroes within JoinRange, the mover first.
        public List<int> JoinCandidates { get; } = new List<int>();
        public MapTile JoinTile { get; private set; }

        public List<ItemDef> Bag { get; } = new List<ItemDef>();
        public List<ShopItem> Shop { get; } = new List<ShopItem>();

        // The level-up whose upgrade is to be picked; nothing else can be done until it is.
        public LevelUpChoice LevelUp { get; private set; }

        // What the last step did, a line each ("기사 레벨 2!", "새 날이 밝았다").
        public List<string> Report { get; } = new List<string>();
        public IReadOnlyCollection<string> Flags => _flags;
        public bool HasFlag(string flag) => _flags.Contains(flag);
        public int BattlesWon { get; private set; }
        public IReadOnlyList<string> Log => _log;

        public Hero Current => Heroes[MapHero];

        // The chapter's goal now, for the map screen.
        public string Objective =>
            !HasFlag("read_seal") ? "북동쪽 폐허의 리엔의 비석을 읽는다(봉인석의 파수꾼이 약해진다) · 또는 바로 동쪽 끝 봉인석으로"
            : "동쪽 끝 봉인석에서 잿빛 파수꾼을 쓰러뜨린다";

        void Record(string command) => _log.Add(command);

        string Blocked()
        {
            if (LevelUp != null) return "레벨업 강화를 먼저 고르세요.";
            return null;
        }

        // ---- The map at the start ----

        void Populate()
        {
            var start = Map.Tile(Site.Start).Hex;
            var free = Map.Tiles.Where(t => Map.Def.Passable(t.Hex) && t.Site == Site.None && t.Hex.Distance(start) >= 2).ToList();
            Place(free, StartEnemies, Content.Enemy, t => t.Hex.Distance(start) >= 3, 1);
            Place(free, StartEvents, Content.Event, t => t.Terrain != Terrain.Road, 0);
            Place(free, StartTreasures, Content.Treasure, t => true, 0);
            Place(free, StartHerbs, Content.Herb, t => t.Terrain == Terrain.Forest || t.Terrain == Terrain.Swamp || t.Terrain == Terrain.Plains, 0);
        }

        void Place(List<MapTile> free, int count, Content content, Func<MapTile, bool> fits, int danger)
        {
            for (int i = 0; i < count; i++)
            {
                var options = free.Where(t => t.Content == Content.None && fits(t)).ToList();
                if (options.Count == 0) return;
                var tile = options[_random.Next(options.Count)];
                tile.Content = content;
                if (content == Content.Enemy) tile.Encounter = PickEncounter(tile, danger);
            }
        }

        string PickEncounter(MapTile tile, int danger)
        {
            string terrain = TerrainInfo.Code(tile.Terrain);
            var fitting = Data.Encounters.Values.Where(e => e.Danger == danger && e.Terrains.Contains(terrain)).OrderBy(e => e.Id).ToList();
            if (fitting.Count == 0) fitting = Data.Encounters.Values.Where(e => e.Danger == danger).OrderBy(e => e.Id).ToList();
            return fitting[_random.Next(fitting.Count)].Id;
        }

        // A new group somewhere quiet, away from every hero (each new day).
        void Spawn()
        {
            var candidates = Map.Tiles.Where(t => Map.Def.Passable(t.Hex) && t.Site == Site.None && t.Content == Content.None
                && Positions.All(p => p.Distance(t.Hex) >= 3)).ToList();
            if (candidates.Count == 0) return;
            var tile = candidates[_random.Next(candidates.Count)];
            tile.Content = Content.Enemy;
            tile.Encounter = PickEncounter(tile, Doom >= 4 ? 2 : 1);
            Report.Add(Doom >= 4 ? "균열에서 더 센 무리가 나타났다." : "새 적 무리가 나타났다.");
        }

        // ---- Story pages ----

        void ShowStory(string id)
        {
            Story = Data.Stories[id];
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
                    if (Story.Id == "prologue") StartMapTurn(0);
                    else if (Story.Id == "rift_open") Screen = AdventureScreen.Defeat;
                    else Screen = AdventureScreen.Victory;
                    return null;
                case AdventureScreen.BattleResult:
                case AdventureScreen.EventResult:
                    Record("continue");
                    Report.Clear();
                    if (_pendingBattle != null)
                    {
                        string id = _pendingBattle;
                        _pendingBattle = null;
                        PrepareJoin(_eventTile ?? Map.Tile(Positions[MapHero]), id, false);
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

        // ---- Map turns ----

        void StartMapTurn(int hero)
        {
            MapHero = hero;
            MovePoints = -1;
            MoveRoll = null;
            Screen = AdventureScreen.Map;
        }

        string MapTurn()
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.Map) return "지도 차례가 아닙니다.";
            if (Current.Down) return $"{Korean.Topic(Current.Name)} 쓰러져 있습니다.";
            return null;
        }

        // The movement roll (Docs/Map.md): 2 + successes of three agility dice, one less at night, at least 1.
        public string RollMove()
        {
            string error = MapTurn();
            if (error != null) return error;
            if (MovePoints >= 0) return "이미 굴렸습니다.";
            var hero = Current;
            int sure = Math.Min(MoveDice, hero.Blessing + hero.Luck);
            hero.Blessing = 0;
            hero.Luck = 0;
            MoveRoll = _fate.Roll(MoveDice, Dice.ClampChance(hero.Stat(Stat.Agility)), sure);
            hero.AddGrit(MoveRoll.RolledFailures);
            MovePoints = Math.Max(1, MoveBase + MoveRoll.Successes - (Night ? 1 : 0));
            Record("roll");
            return null;
        }

        // Where the current hero can move with the rolled points, and what each costs.
        public Dictionary<Hex, int> Reachable() =>
            Screen == AdventureScreen.Map && MovePoints > 0 ? Map.Reach(Positions[MapHero], MovePoints) : new Dictionary<Hex, int>();

        public string Move(Hex to)
        {
            string error = MapTurn();
            if (error != null) return error;
            if (MovePoints < 0) return "먼저 이동을 굴리세요.";
            if (!Reachable().ContainsKey(to)) return "그 칸에는 갈 수 없습니다.";
            Positions[MapHero] = to;
            MovePoints = 0;
            Record($"move {to.Col} {to.Row}");
            Report.Clear();
            Arrive(to);
            return null;
        }

        // Rests where the hero stands instead of moving: some health and one resolve.
        public string Camp()
        {
            string error = MapTurn();
            if (error != null) return error;
            if (MovePoints >= 0) return "이동을 굴린 뒤에는 야영할 수 없습니다.";
            var hero = Current;
            int before = hero.Hp;
            hero.Heal(Math.Max(1, hero.MaxHp * CampPercent / 100));
            hero.GainResolve(1);
            Record("camp");
            Report.Clear();
            Report.Add($"{hero.Name}: 야영. 생명력 +{hero.Hp - before}, 결의 +1");
            EndTurn();
            return null;
        }

        // Ends the turn where the hero stands.
        public string Stay()
        {
            string error = MapTurn();
            if (error != null) return error;
            Record("stay");
            Report.Clear();
            EndTurn();
            return null;
        }

        void Arrive(Hex hex)
        {
            foreach (var other in Heroes.Where((h, i) => i != MapHero && h.Down && Positions[i] == hex))
            {
                other.Hp = 1;
                Report.Add($"{Korean.Subject(Current.Name)} {Korean.Object(other.Name)} 부축해 일으켰다");
            }
            var tile = Map.Tile(hex);
            switch (tile.Content)
            {
                case Content.Enemy:
                    PrepareJoin(tile, tile.Encounter, false);
                    return;
                case Content.Event:
                    OpenEvent(PickEvent("wild"), tile);
                    return;
                case Content.Treasure:
                    OpenEvent(PickEvent("chest"), tile);
                    return;
                case Content.Herb:
                    OpenEvent(PickEvent("herb"), tile);
                    return;
            }
            switch (tile.Site)
            {
                case Site.Town:
                    OpenTown();
                    return;
                case Site.Shrine when !tile.Used:
                    OpenEvent(PickEvent("shrine"), tile);
                    return;
                case Site.Quest when !tile.Used:
                    OpenEvent(PickEvent("quest"), tile);
                    return;
                case Site.Boss:
                    PrepareJoin(tile, "warden", true);
                    return;
            }
            EndTurn();
        }

        void EndTurn()
        {
            if (Heroes.All(h => h.Down))
            {
                Screen = AdventureScreen.Defeat;
                return;
            }
            int next = MapHero;
            for (int step = 0; step < Heroes.Count; step++)
            {
                next = (next + 1) % Heroes.Count;
                if (next == 0) AdvanceTime();
                if (Screen == AdventureScreen.Story || Screen == AdventureScreen.Defeat) return;
                if (!Heroes[next].Down) break;
            }
            StartMapTurn(next);
        }

        void AdvanceTime()
        {
            TimeOfDay++;
            if (TimeOfDay < TimesOfDay) return;
            TimeOfDay = 0;
            Day++;
            Doom++;
            Report.Add($"{Day}일째 아침. 균열 {Doom}/{DoomMax}");
            foreach (var hero in Heroes.Where(h => h.Down))
            {
                if (Hope <= 0) continue;
                Hope--;
                hero.Hp = 1;
                Report.Add($"{Korean.Subject(hero.Name)} 희망으로 일어났다 (남은 희망 {Hope})");
            }
            if (Doom >= DoomMax)
            {
                ShowStory("rift_open");
                return;
            }
            Spawn();
        }

        // ---- Battles ----

        void PrepareJoin(MapTile tile, string encounterId, bool boss)
        {
            JoinTile = tile;
            Encounter = Data.Encounters[encounterId];
            BossBattle = boss;
            JoinCandidates.Clear();
            JoinCandidates.Add(MapHero);
            for (int i = 0; i < Heroes.Count; i++)
                if (i != MapHero && !Heroes[i].Down && Positions[i].Distance(tile.Hex) <= JoinRange) JoinCandidates.Add(i);
            Screen = AdventureScreen.Join;
        }

        // Starts the battle with the chosen heroes; the hero whose move started it always fights.
        public string Join(IReadOnlyCollection<int> heroes)
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.Join) return "합류할 전투가 없습니다.";
            if (heroes == null || !heroes.Contains(MapHero)) return $"{Korean.Topic(Current.Name)} 빠질 수 없습니다.";
            if (heroes.Any(h => !JoinCandidates.Contains(h))) return "멀리 있는 영웅은 합류할 수 없습니다.";
            var chosen = JoinCandidates.Where(heroes.Contains).ToList();
            Record("join " + string.Join(",", chosen));
            _fighters = chosen.Select(i => Heroes[i]).ToList();
            var monsters = Encounter.Monsters.ToList();
            Report.Clear();
            if (BossBattle) monsters = monsters.Select(Empower).ToList();
            Battle = new Battle(_fighters, monsters, NextSeed(), _weakNext, Data);
            _weakNext = false;
            Screen = AdventureScreen.Battle;
            return null;
        }

        // The boss as the run made it (Docs/Map.md): weaker for the stone's words, stronger for a grown rift.
        MonsterDef Empower(MonsterDef boss)
        {
            if (!boss.Boss) return boss;
            var copy = boss.Clone();
            if (HasFlag("read_seal"))
            {
                copy.Hp = copy.Hp * 75 / 100;
                copy.Armor = 0;
                Report.Add("리엔의 문장이 봉인석을 누그러뜨린다. 파수꾼의 생명력 −25%, 방어도 0");
            }
            if (Doom >= 6)
            {
                copy.Hp = copy.Hp * 120 / 100;
                Report.Add($"자란 균열(균열 {Doom})이 파수꾼에게 힘을 준다. 생명력 +20%");
            }
            return copy;
        }

        ulong NextSeed() => ((ulong)_random.NextUInt() << 32) | _random.NextUInt();

        // The current hero's battle command. When it ends the battle, the rewards are given and the screen moves on;
        // the battle stays readable for the screen to finish showing it.
        public string Fight(BattleCommand command)
        {
            if (Screen != AdventureScreen.Battle || Battle == null) return "전투 중이 아닙니다.";
            string error = Battle.Do(command);
            if (error != null) return error;
            Record($"fight {command.Actor} {command.Skill} {command.Target} {command.Resolve}");
            if (Battle.Over) FinishBattle();
            return null;
        }

        void FinishBattle()
        {
            Report.Clear();
            if (Battle.State == BattleState.Lost)
            {
                Report.Add($"{string.Join(", ", _fighters.Select(h => h.Name))}: 모두 쓰러졌다.");
                if (Heroes.All(h => h.Down))
                {
                    Screen = AdventureScreen.Defeat;
                    return;
                }
                Report.Add(Hope > 0 ? "쓰러진 영웅은 다음 아침에 희망으로 일어나거나, 동료가 그 칸에 가서 부축하면 일어난다." : "희망이 없다. 동료가 그 칸에 가서 부축해야 일어난다.");
                Screen = AdventureScreen.BattleResult;
                return;
            }
            BattlesWon++;
            int xp = Battle.XpReward;
            int gold = Battle.GoldReward;
            Gold += gold;
            Report.Add($"경험치 {xp}(싸운 영웅만), 금화 {gold}");
            foreach (var hero in _fighters.Where(h => h.Down))
            {
                if (Hope <= 0)
                {
                    Report.Add($"{Korean.Topic(hero.Name)} 쓰러진 채다. 희망이 없다 (동료가 부축하면 일어난다)");
                    continue;
                }
                Hope--;
                hero.Hp = Math.Max(1, hero.MaxHp * HopeRevivePercent / 100);
                Report.Add($"{Korean.Subject(hero.Name)} 희망으로 일어났다 (생명력 {hero.Hp}, 남은 희망 {Hope})");
            }
            foreach (var hero in _fighters)
            {
                if (!hero.Down && hero.Resolve < hero.MaxResolve) hero.GainResolve(1);
                if (hero.Class.Id == "alchemist")
                {
                    hero.GainReagents(1 + hero.Special("reagent_gain"));
                    Report.Add($"{hero.Name}: 재료 {hero.Reagents}/{hero.ReagentMax}");
                }
                GainXp(hero, xp);
            }
            if (JoinTile != null && JoinTile.Content == Content.Enemy && Encounter.Id == JoinTile.Encounter) JoinTile.Content = Content.None;
            if (BossBattle)
            {
                JoinTile.Used = true;
                // The ending remembers how the party treated the people on the way (Docs/Narrative.md).
                int kindness = new[] { "kind", "pilgrim_saved", "child_saved", "spared_bandit", "spared_fox", "knight_banner" }.Count(HasFlag);
                ShowStory(kindness >= 3 ? "epilogue_kind" : "epilogue");
                return;
            }
            Screen = AdventureScreen.BattleResult;
        }

        void GainXp(Hero hero, int xp)
        {
            var levels = hero.GainXp(xp);
            if (levels.Count == 0) return;
            var main = hero.Class.MainStats;
            Report.Add($"{hero.Name} 레벨 {levels[levels.Count - 1]}! 최대 생명력 +{Hero.LevelHp * levels.Count}, {Stats.Name(main[0])}·{Stats.Name(main[1])} +{Hero.LevelStat * levels.Count}");
            foreach (int level in levels) _levels.Enqueue((Heroes.IndexOf(hero), level));
            NextLevelUp();
        }

        void NextLevelUp()
        {
            while (LevelUp == null && _levels.Count > 0)
            {
                var (index, level) = _levels.Dequeue();
                var hero = Heroes[index];
                var left = hero.Class.Upgrades.Where(u => !hero.Upgrades.Contains(u)).ToList();
                if (left.Count == 0) continue;
                var options = new List<UpgradeDef>();
                while (options.Count < 2 && left.Count > 0)
                {
                    int pick = _random.Next(left.Count);
                    options.Add(left[pick]);
                    left.RemoveAt(pick);
                }
                LevelUp = new LevelUpChoice(index, level, options);
            }
        }

        // Takes one of the level-up's upgrades.
        public string ChooseUpgrade(int option)
        {
            if (LevelUp == null) return "고를 레벨업이 없습니다.";
            if (option < 0 || option >= LevelUp.Options.Count) return "그런 강화는 없습니다.";
            var hero = Heroes[LevelUp.Hero];
            var upgrade = LevelUp.Options[option];
            hero.ApplyUpgrade(upgrade);
            Record($"upgrade {option}");
            Report.Add($"{hero.Name}: {upgrade.Name} ({upgrade.Text})");
            LevelUp = null;
            NextLevelUp();
            return null;
        }

        // ---- Events ----

        EventCard PickEvent(string pool)
        {
            var all = Data.Events.Where(e => e.Pool == pool && (e.ClassId == null || HasClass(e.ClassId))).ToList();
            var fresh = all.Where(e => !_seen.Contains(e.Id)).ToList();
            // Each class's own event comes first while it has not been seen (Docs/Narrative.md 개인 이야기).
            var personal = fresh.Where(e => e.ClassId != null).ToList();
            var from = personal.Count > 0 && _random.Next(2) == 0 ? personal : fresh.Count > 0 ? fresh : all;
            var card = from[_random.Next(from.Count)];
            _seen.Add(card.Id);
            return card;
        }

        void OpenEvent(EventCard card, MapTile tile)
        {
            Event = card;
            _eventTile = tile;
            Result = null;
            Screen = AdventureScreen.Event;
        }

        public bool HasClass(string classId) => Heroes.Any(h => !h.Down && h.Class.Id == classId);

        // Heroes near enough to take part in the event (within JoinRange of it).
        public List<Hero> EventHeroes() =>
            Heroes.Where((h, i) => !h.Down && (_eventTile == null || Positions[i].Distance(_eventTile.Hex) <= JoinRange)).ToList();

        // The choices this party sees: a class's own choice needs that class standing near; some need a flag.
        public List<EventChoice> Choices()
        {
            if (Event == null) return new List<EventChoice>();
            var near = EventHeroes();
            return Event.Choices.Where(c => (c.ClassId == null || near.Any(h => h.Class.Id == c.ClassId)) && FlagOk(c.Requires)).ToList();
        }

        bool FlagOk(string requires)
        {
            if (string.IsNullOrEmpty(requires)) return true;
            return requires.StartsWith("!") ? !HasFlag(requires.Substring(1)) : HasFlag(requires);
        }

        // Gold a choice costs up front ("물약을 산다 (금화 10)"): the lost gold of a choice without a check.
        public static int Cost(EventChoice choice) => choice.HasCheck ? 0 : -choice.Success.Where(o => o.Verb == "gold" && o.Amount < 0).Sum(o => o.Amount);

        public bool Affordable(EventChoice choice) => Gold >= Cost(choice);

        // Who may try the choice: its class, or any hero near.
        public List<Hero> CanAttempt(EventChoice choice) => EventHeroes().Where(h => choice.ClassId == null || h.Class.Id == choice.ClassId).ToList();

        public static int ChanceOf(EventChoice choice, Hero hero) =>
            choice.FixedChance > 0 ? choice.FixedChance : Dice.ClampChance(choice.Stat.HasValue ? hero.Stat(choice.Stat.Value) : 50);

        public static int FreeOf(EventChoice choice, Hero hero) => Math.Min(choice.Dice, hero.Blessing + hero.Luck);

        public static int ResolveRoom(EventChoice choice, Hero hero) => Math.Min(hero.Resolve, choice.Dice - FreeOf(choice, hero));

        public static CheckOdds Odds(EventChoice choice, Hero hero, int resolve) =>
            new CheckOdds(choice.Dice, ChanceOf(choice, hero), FreeOf(choice, hero) + Math.Max(0, resolve));

        // Takes a choice with the hero who tries it and the resolve spent. Returns null when done, or why not.
        public string Choose(int choiceIndex, int heroIndex, int resolve = 0)
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.Event) return "사건 중이 아닙니다.";
            var choice = Choices().FirstOrDefault(c => c.Index == choiceIndex);
            if (choice == null) return "고를 수 없는 선택지입니다.";
            if (!Affordable(choice)) return $"금화가 모자랍니다 (필요 {Cost(choice)}).";
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return "영웅을 고르세요.";
            var hero = Heroes[heroIndex];
            if (!CanAttempt(choice).Contains(hero)) return $"{Korean.Topic(hero.Name)} 이 선택지를 할 수 없습니다.";
            if (resolve < 0 || (choice.HasCheck && resolve > ResolveRoom(choice, hero)) || (!choice.HasCheck && resolve > 0))
                return "결의를 그만큼 쓸 수 없습니다.";

            var result = new EventResult { Choice = choice, Hero = hero, Success = true };
            if (choice.HasCheck)
            {
                int guaranteed = FreeOf(choice, hero) + resolve;
                hero.Resolve -= resolve;
                hero.Blessing = 0;
                hero.Luck = 0;
                result.Check = _fate.Roll(choice.Dice, ChanceOf(choice, hero), guaranteed);
                result.Success = result.Check.Successes >= choice.Need;
                int gained = hero.AddGrit(result.Check.RolledFailures);
                if (gained > 0) result.Changes.Add($"{hero.Name}: 끈기가 차서 결의 +{gained}");
            }
            Record($"choose {choiceIndex} {heroIndex} {resolve}");
            result.Text = result.Success ? choice.SuccessText : choice.FailText;
            Report.Clear();
            Apply(result.Success ? choice.Success : choice.Fail, hero, result.Changes);
            foreach (string line in Report) result.Changes.Add(line);
            Report.Clear();
            Result = result;
            UseUp(_eventTile);
            Screen = AdventureScreen.EventResult;
            return null;
        }

        // An event's hex is done: its content goes; a shrine is used; the quest stone once its words are read.
        void UseUp(MapTile tile)
        {
            if (tile == null) return;
            if (tile.Content == Content.Event || tile.Content == Content.Treasure || tile.Content == Content.Herb) tile.Content = Content.None;
            else if (tile.Site == Site.Shrine) tile.Used = true;
            else if (tile.Site == Site.Quest && HasFlag("read_seal")) tile.Used = true;
        }

        void Apply(IEnumerable<Outcome> outcomes, Hero hero, List<string> lines)
        {
            var living = Heroes.Where(h => !h.Down).ToList();
            foreach (var o in outcomes)
            {
                switch (o.Verb)
                {
                    case "gold":
                        int change = Math.Max(-Gold, o.Amount);
                        Gold += change;
                        lines.Add(change >= 0 ? $"금화 +{change}" : $"금화 {change}");
                        break;
                    case "heal":
                        lines.Add(HealLine(hero, o.Amount));
                        break;
                    case "heal_all":
                        foreach (var h in living) lines.Add(HealLine(h, o.Amount));
                        break;
                    case "hurt":
                        lines.Add(HurtLine(hero, o.Amount));
                        break;
                    case "hurt_all":
                        foreach (var h in living) lines.Add(HurtLine(h, o.Amount));
                        break;
                    case "resolve":
                        hero.GainResolve(o.Amount);
                        lines.Add($"{hero.Name}: 결의 {Signed(o.Amount)} ({hero.Resolve}/{hero.MaxResolve})");
                        break;
                    case "resolve_all":
                        foreach (var h in living) h.GainResolve(o.Amount);
                        lines.Add($"모두 결의 {Signed(o.Amount)}");
                        break;
                    case "bless_all":
                        foreach (var h in living) h.Blessing++;
                        lines.Add("모두 축복: 다음 판정 확정 +1");
                        break;
                    case "potion":
                        hero.Potions = Math.Max(0, hero.Potions + o.Amount);
                        lines.Add($"{hero.Name}: 회복 물약 {Signed(o.Amount)} ({hero.Potions}개)");
                        break;
                    case "hope":
                        Hope = Math.Max(0, Hope + o.Amount);
                        lines.Add($"희망 {Signed(o.Amount)} ({Hope})");
                        break;
                    case "xp":
                        lines.Add($"모두 경험치 +{o.Amount}");
                        foreach (var h in Heroes) GainXp(h, o.Amount);
                        break;
                    case "weak_next":
                        _weakNext = true;
                        lines.Add("다음 전투를 약화된 채 시작한다");
                        break;
                    case "flag":
                        _flags.Add(o.Arg);
                        break;
                    case "battle":
                        _pendingBattle = o.Arg;
                        lines.Add("전투가 벌어진다!");
                        break;
                    case "item":
                        var item = Data.Items[o.Arg];
                        Bag.Add(item);
                        lines.Add($"{item.Name}을 가방에 넣었다");
                        break;
                    case "loot":
                        var loot = Loot(o.Amount);
                        if (loot != null)
                        {
                            Bag.Add(loot);
                            lines.Add($"{Korean.Object(loot.Name)} 얻었다 (가방)");
                        }
                        break;
                    case "reagent":
                        var alchemist = Heroes.FirstOrDefault(h => h.Class.Id == "alchemist");
                        if (alchemist != null)
                        {
                            alchemist.GainReagents(o.Amount);
                            lines.Add($"{alchemist.Name}: 재료 {alchemist.Reagents}/{alchemist.ReagentMax}");
                        }
                        break;
                    case "doom":
                        Doom = Math.Max(0, Doom + o.Amount);
                        lines.Add($"균열 {Signed(o.Amount)} ({Doom}/{DoomMax})");
                        break;
                }
            }
        }

        // A random item of the tier, not a starting one.
        ItemDef Loot(int tier)
        {
            var items = Data.ItemList.Where(i => i.Tier == tier && i.Price > 0).ToList();
            return items.Count == 0 ? null : items[_random.Next(items.Count)];
        }

        static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

        static string HealLine(Hero hero, int amount)
        {
            int before = hero.Hp;
            hero.Heal(amount);
            return $"{hero.Name}: 생명력 +{hero.Hp - before} ({hero.Hp}/{hero.MaxHp})";
        }

        // Events hurt but never knock a hero down.
        static string HurtLine(Hero hero, int amount)
        {
            int before = hero.Hp;
            hero.Hp = Math.Max(1, hero.Hp - amount);
            return $"{hero.Name}: 생명력 −{before - hero.Hp} ({hero.Hp}/{hero.MaxHp})";
        }

        // ---- Test and playtest set-up ----

        internal void Teleport(int hero, Hex hex) => Positions[hero] = hex;

        internal void OpenEventForTest(EventCard card) => OpenEvent(card, Map.Tile(Positions[MapHero]));
    }
}
