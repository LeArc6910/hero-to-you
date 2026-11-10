using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // Events (Docs/Narrative.md): drawing a card from the chapter's pool, the choices the party sees, the check, what
    // the outcome changes, and the trail of the old hero's notes the party finds on the way.
    public sealed partial class Adventure
    {
        string _pendingBattle;
        MapTile _eventTile;
        bool _backToTown;
        readonly List<EventCard> _footsteps = new List<EventCard>();

        public EventCard Event { get; private set; }
        public EventResult Result { get; private set; }

        // The party's renown (Docs/Narrative.md 명성): kind deeds and bosses raise it, and with it the title the party
        // goes by, from nobody to a hero.
        public int Renown { get; private set; }
        public RenownDef Rank => Data.Renown.LastOrDefault(r => r.Need <= Renown) ?? Data.Renown[0];
        public RenownDef NextRank => Data.Renown.FirstOrDefault(r => r.Need > Renown);

        // The events whose old notes (the "journal" column) the party found this run, in the order found: the trail of
        // the hero before them (Docs/Narrative.md 발자취).
        public IReadOnlyList<EventCard> Footsteps => _footsteps;

        void GainRenown(int amount, List<string> lines)
        {
            if (amount == 0) return;
            var before = Rank;
            Renown = Math.Max(0, Renown + amount);
            lines.Add($"명성 {Signed(amount)} ({Renown})");
            if (Rank != before && Rank.Need > before.Need) lines.Add($"사람들이 너희를 「{Rank.Title}」라 부르기 시작했다.");
        }

        EventCard PickEvent(string pool)
        {
            var all = Data.Events.Where(e => e.Pool == pool && (e.ClassId == null || HasClass(e.ClassId))).ToList();
            var fresh = all.Where(e => !_seen.Contains(e.Id)).ToList();
            // Each class's own event comes first while it has not been seen (Docs/Narrative.md 개인 이야기).
            var personal = fresh.Where(e => e.ClassId != null).ToList();
            var from = personal.Count > 0 && _eventRandom.Next(2) == 0 ? personal : fresh.Count > 0 ? fresh : all;
            var card = from[_eventRandom.Next(from.Count)];
            _seen.Add(card.Id);
            return card;
        }

        void OpenEvent(EventCard card, MapTile tile)
        {
            Event = card;
            _eventTile = tile;
            Result = null;
            Screen = AdventureScreen.Event;
            if (!string.IsNullOrEmpty(card.Journal) && !_footsteps.Contains(card)) _footsteps.Add(card);
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
                result.Check = _eventFate.Roll(choice.Dice, ChanceOf(choice, hero), guaranteed);
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

        // An event's hex is done: its content goes; a shrine is used; the quest site and the clue once they gave
        // what they hold (failing there can be tried again another day).
        void UseUp(MapTile tile)
        {
            if (tile == null) return;
            if (tile.Content == Content.Event || tile.Content == Content.Treasure || tile.Content == Content.Herb) tile.Content = Content.None;
            else if (tile.Site == Site.Shrine) tile.Used = true;
            else if (tile.Site == Site.Quest && HasFlag(Chapter.WeakenFlag)) tile.Used = true;
            else if (tile.Site == Site.Clue && HasFlag(Chapter.NoSummonFlag)) tile.Used = true;
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
                        if (item.Consumable)
                        {
                            hero.AddConsumable(item.Id, 1);
                            lines.Add($"{hero.Name}: {item.Name} +1 ({hero.Count(item.Id)}개)");
                        }
                        else
                        {
                            Bag.Add(item);
                            lines.Add($"{Korean.Object(item.Name)} 가방에 넣었다");
                        }
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
                    case "renown":
                        GainRenown(o.Amount, lines);
                        break;
                }
            }
        }

        // A random piece of gear of the tier, not a starting one.
        ItemDef Loot(int tier)
        {
            var items = Data.ItemList.Where(i => i.Tier == tier && i.Price > 0 && !i.Consumable).ToList();
            return items.Count == 0 ? null : items[_lootRandom.Next(items.Count)];
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

        internal void OpenEventForTest(EventCard card) => OpenEvent(card, Map.Tile(Positions[MapHero]));
    }
}
