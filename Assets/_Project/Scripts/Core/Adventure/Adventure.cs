using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    public enum AdventureScreen
    {
        Story,
        Battle,
        BattleResult,
        Event,
        EventResult,
        Rest,
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

    // One run of the party along the path of Data/Story/path.csv (the M1 slice, Docs/Design.md 9): stories, battles,
    // events and rests in order. The client shows Screen and calls the method for what the player chose. Event dice
    // come from the adventure's own Fate; each battle gets a seed from the adventure's, so a seed and the choices made
    // replay the same run.
    public sealed class Adventure
    {
        public const int StartHope = 3;
        public const int RestPercent = 40;
        public const int HopeRevivePercent = 30;
        public const int StartGold = 10;

        readonly GameRandom _random;
        readonly Fate _fate;
        readonly HashSet<string> _flags = new HashSet<string>();
        readonly HashSet<string> _seen = new HashSet<string>();
        string _pendingBattle;
        bool _weakNext;

        public Adventure(GameData data, IReadOnlyList<string> classIds, ulong seed)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            if (classIds == null || classIds.Count != 3) throw new ArgumentException("A party is three heroes.", nameof(classIds));
            Seed = seed;
            _random = new GameRandom(seed, 10);
            _fate = new Fate(seed, 11);
            foreach (string id in classIds)
            {
                if (!data.Classes.TryGetValue(id, out var cls) || !cls.Playable) throw new ArgumentException($"No playable class '{id}'.", nameof(classIds));
                Heroes.Add(new Hero(cls));
            }
            Hope = StartHope;
            Gold = StartGold;
            Enter(0);
        }

        public GameData Data { get; }
        public ulong Seed { get; }
        public List<Hero> Heroes { get; } = new List<Hero>();
        public int Hope { get; internal set; }

        // The party's purse (shared in M1).
        public int Gold { get; internal set; }
        public int Step { get; private set; }
        public PathNode Node => Step < Data.Path.Count ? Data.Path[Step] : null;
        public AdventureScreen Screen { get; private set; }
        public StoryDef Story { get; private set; }
        public EncounterDef Encounter { get; private set; }
        public Battle Battle { get; private set; }
        public bool BossBattle { get; private set; }
        public EventCard Event { get; private set; }
        public EventResult Result { get; private set; }

        // What the last battle's end or the rest did, a line each ("기사 레벨 2!").
        public List<string> Report { get; } = new List<string>();
        public IReadOnlyCollection<string> Flags => _flags;
        public int BattlesWon { get; private set; }

        // ---- Moving along ----

        // Goes on from a story, a battle's or an event's result, or a rest.
        public void Continue()
        {
            if (Screen == AdventureScreen.Battle || Screen == AdventureScreen.Event || Screen == AdventureScreen.Victory || Screen == AdventureScreen.Defeat)
                throw new InvalidOperationException($"Cannot continue from {Screen}.");
            if (_pendingBattle != null)
            {
                string id = _pendingBattle;
                _pendingBattle = null;
                StartBattle(Data.Encounters[id], false);
                return;
            }
            Enter(Step + 1);
        }

        void Enter(int step)
        {
            Step = step;
            Story = null;
            Event = null;
            Result = null;
            Report.Clear();
            var node = Node;
            if (node == null)
            {
                Screen = AdventureScreen.Victory;
                return;
            }
            switch (node.Kind)
            {
                case PathKind.Story:
                    Story = Data.Stories[node.Arg];
                    Screen = AdventureScreen.Story;
                    break;
                case PathKind.Battle:
                case PathKind.Boss:
                    StartBattle(Data.Encounters[node.Arg], node.Kind == PathKind.Boss);
                    break;
                case PathKind.Event:
                    Event = PickEvent(node.Arg);
                    Screen = AdventureScreen.Event;
                    break;
                case PathKind.Rest:
                    Rest();
                    Screen = AdventureScreen.Rest;
                    break;
            }
        }

        EventCard PickEvent(string pool)
        {
            var all = Data.Events.Where(e => e.Pool == pool).ToList();
            var fresh = all.Where(e => !_seen.Contains(e.Id)).ToList();
            var from = fresh.Count > 0 ? fresh : all;
            var card = from[_random.Next(from.Count)];
            _seen.Add(card.Id);
            return card;
        }

        ulong NextSeed() => ((ulong)_random.NextUInt() << 32) | _random.NextUInt();

        // ---- Battles ----

        void StartBattle(EncounterDef encounter, bool boss)
        {
            Encounter = encounter;
            BossBattle = boss;
            Battle = new Battle(Heroes, encounter.Monsters, NextSeed(), _weakNext, Data);
            _weakNext = false;
            Screen = AdventureScreen.Battle;
        }

        // The current hero's battle command. When it ends the battle, the rewards are given and the screen moves to
        // the result (or the defeat); the battle stays readable for the screen to finish showing it.
        public string Fight(BattleCommand command)
        {
            if (Screen != AdventureScreen.Battle || Battle == null) return "전투 중이 아닙니다.";
            string error = Battle.Do(command);
            if (error != null) return error;
            if (Battle.Over) FinishBattle();
            return null;
        }

        void FinishBattle()
        {
            Report.Clear();
            if (Battle.State == BattleState.Lost)
            {
                Screen = AdventureScreen.Defeat;
                return;
            }
            BattlesWon++;
            int xp = Battle.XpReward;
            int gold = Battle.GoldReward;
            Gold += gold;
            Report.Add($"경험치 {xp}, 금화 {gold}");
            foreach (var hero in Heroes.Where(h => h.Down))
            {
                if (Hope <= 0)
                {
                    Report.Add($"{Korean.Topic(hero.Name)} 쓰러진 채다. 희망이 없다 (쉼터에서 일어난다)");
                    continue;
                }
                Hope--;
                hero.Hp = Math.Max(1, hero.MaxHp * HopeRevivePercent / 100);
                Report.Add($"{Korean.Subject(hero.Name)} 희망으로 일어났다 (생명력 {hero.Hp}, 남은 희망 {Hope})");
            }
            foreach (var hero in Heroes)
            {
                if (!hero.Down && hero.Resolve < hero.MaxResolve) hero.GainResolve(1);
                LevelLines(hero, hero.GainXp(xp));
            }
            Screen = AdventureScreen.BattleResult;
        }

        void LevelLines(Hero hero, List<int> levels)
        {
            if (levels.Count == 0) return;
            var main = hero.Class.MainStats;
            Report.Add($"{hero.Name} 레벨 {levels[levels.Count - 1]}! 최대 생명력 +{Hero.LevelHp * levels.Count}, {Stats.Name(main[0])}·{Stats.Name(main[1])} +{Hero.LevelStat * levels.Count}");
        }

        // ---- Rest ----

        void Rest()
        {
            foreach (var hero in Heroes)
            {
                if (hero.Down)
                {
                    hero.Hp = 1;
                    Report.Add($"{Korean.Subject(hero.Name)} 정신을 차렸다");
                }
                int before = hero.Hp;
                hero.Heal(Math.Max(1, hero.MaxHp * RestPercent / 100));
                hero.GainResolve(hero.MaxResolve);
                Report.Add($"{hero.Name}: 생명력 +{hero.Hp - before} ({hero.Hp}/{hero.MaxHp}), 결의 가득");
            }
        }

        // ---- Events ----

        public bool HasClass(string classId) => Heroes.Any(h => !h.Down && h.Class.Id == classId);

        // The choices this party sees: a class's own choice needs that class standing.
        public List<EventChoice> Choices() =>
            Event == null ? new List<EventChoice>() : Event.Choices.Where(c => c.ClassId == null || HasClass(c.ClassId)).ToList();

        // Gold a choice costs up front ("물약을 산다 (금화 10)"): the lost gold of a choice without a check.
        public static int Cost(EventChoice choice) => choice.HasCheck ? 0 : -choice.Success.Where(o => o.Verb == "gold" && o.Amount < 0).Sum(o => o.Amount);

        public bool Affordable(EventChoice choice) => Gold >= Cost(choice);

        // Who may try the choice: its class, or any hero standing.
        public List<Hero> CanAttempt(EventChoice choice) =>
            Heroes.Where(h => !h.Down && (choice.ClassId == null || h.Class.Id == choice.ClassId)).ToList();

        public static int ChanceOf(EventChoice choice, Hero hero) =>
            choice.FixedChance > 0 ? choice.FixedChance : Dice.ClampChance(choice.Stat.HasValue ? hero.Stat(choice.Stat.Value) : 50);

        public static int FreeOf(EventChoice choice, Hero hero) => Math.Min(choice.Dice, hero.Blessing + hero.Luck);

        public static int ResolveRoom(EventChoice choice, Hero hero) => Math.Min(hero.Resolve, choice.Dice - FreeOf(choice, hero));

        public static CheckOdds Odds(EventChoice choice, Hero hero, int resolve) =>
            new CheckOdds(choice.Dice, ChanceOf(choice, hero), FreeOf(choice, hero) + Math.Max(0, resolve));

        // Takes a choice with the hero who tries it and the resolve spent. Returns null when done, or why not.
        public string Choose(int choiceIndex, int heroIndex, int resolve = 0)
        {
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
            result.Text = result.Success ? choice.SuccessText : choice.FailText;
            Report.Clear();
            Apply(result.Success ? choice.Success : choice.Fail, hero, result.Changes);
            Report.AddRange(result.Changes);
            Result = result;
            Screen = AdventureScreen.EventResult;
            return null;
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
                        foreach (var h in Heroes)
                        {
                            int had = Report.Count;
                            LevelLines(h, h.GainXp(o.Amount));
                            for (int i = had; i < Report.Count; i++) lines.Add(Report[i]);
                            Report.RemoveRange(had, Report.Count - had);
                        }
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
                }
            }
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

        internal void JumpTo(int step) => Enter(Math.Max(0, Math.Min(step, Data.Path.Count)));
    }
}
