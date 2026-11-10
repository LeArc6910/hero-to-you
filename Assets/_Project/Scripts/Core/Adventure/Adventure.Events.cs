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
            lines.Add(Data.Line("adventure.renown", Signed(amount), Renown));
            if (Rank != before && Rank.Need > before.Need) lines.Add(Data.Line("adventure.renown.rank", Rank.Title));
        }

        EventCard PickEvent(string pool)
        {
            var all = Data.Events.Where(e => e.Pool == pool && (e.ClassId == null || HasClass(e.ClassId)) && FlagOk(e.Requires)).ToList();
            var fresh = all.Where(e => !_seen.Contains(e.Id)).ToList();
            // Someone an earlier choice saved comes back (an event with `requires`): two times in three while unseen
            // (Docs/Narrative.md 다시 만나는 사람들).
            var callback = fresh.Where(e => e.Requires.Length > 0 && !e.Requires.StartsWith("!")).ToList();
            if (callback.Count > 0 && _eventRandom.Next(3) > 0)
            {
                var back = callback[_eventRandom.Next(callback.Count)];
                _seen.Add(back.Id);
                return back;
            }
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

        // Sure successes a hero brings to an event check: blessing and luck, and the mnemonist's memory (one more).
        public static int FreeOf(EventChoice choice, Hero hero) => Math.Min(choice.Dice, hero.Blessing + hero.Luck + (hero.Class.Id == "mnemonist" ? 1 : 0));

        public static int ResolveRoom(EventChoice choice, Hero hero) => Math.Min(hero.Resolve, choice.Dice - FreeOf(choice, hero));

        public static CheckOdds Odds(EventChoice choice, Hero hero, int resolve) =>
            new CheckOdds(choice.Dice, ChanceOf(choice, hero), FreeOf(choice, hero) + Math.Max(0, resolve));

        // Takes a choice with the hero who tries it and the resolve spent. Returns null when done, or why not.
        public string Choose(int choiceIndex, int heroIndex, int resolve = 0)
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.Event) return Data.Line("adventure.error.not_in_event");
            var choice = Choices().FirstOrDefault(c => c.Index == choiceIndex);
            if (choice == null) return Data.Line("adventure.error.choice_unavailable");
            if (!Affordable(choice)) return Data.Line("adventure.error.not_enough_gold", Cost(choice));
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return Data.Line("adventure.error.pick_hero");
            var hero = Heroes[heroIndex];
            if (!CanAttempt(choice).Contains(hero)) return Data.Line("adventure.error.hero_cannot_choose", hero.Name);
            if (resolve < 0 || (choice.HasCheck && resolve > ResolveRoom(choice, hero)) || (!choice.HasCheck && resolve > 0))
                return Data.Line("adventure.error.resolve_too_much");

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
                if (gained > 0) result.Changes.Add(Data.Line("adventure.event.grit", hero.Name, gained));
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
                        lines.Add(Data.Line("adventure.event.gold", Signed(change)));
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
                        lines.Add(Data.Line("adventure.event.resolve", hero.Name, Signed(o.Amount), hero.Resolve, hero.MaxResolve));
                        break;
                    case "resolve_all":
                        foreach (var h in living) h.GainResolve(o.Amount);
                        lines.Add(Data.Line("adventure.event.resolve_all", Signed(o.Amount)));
                        break;
                    case "bless_all":
                        foreach (var h in living) h.Blessing++;
                        lines.Add(Data.Line("adventure.event.bless_all"));
                        break;
                    case "potion":
                        hero.Potions = Math.Max(0, hero.Potions + o.Amount);
                        lines.Add(Data.Line("adventure.event.potion", hero.Name, Signed(o.Amount), hero.Potions));
                        break;
                    case "hope":
                        Hope = Math.Max(0, Hope + o.Amount);
                        lines.Add(Data.Line("adventure.event.hope", Signed(o.Amount), Hope));
                        break;
                    case "xp":
                        lines.Add(Data.Line("adventure.event.xp", o.Amount));
                        foreach (var h in Heroes) GainXp(h, o.Amount);
                        break;
                    case "weak_next":
                        _weakNext = true;
                        lines.Add(Data.Line("adventure.event.weak_next"));
                        break;
                    case "flag":
                        _flags.Add(o.Arg);
                        break;
                    case "battle":
                        _pendingBattle = o.Arg;
                        lines.Add(Data.Line("adventure.event.battle"));
                        break;
                    case "item":
                        var item = Data.Items[o.Arg];
                        if (item.Consumable)
                        {
                            hero.AddConsumable(item.Id, 1);
                            lines.Add(Data.Line("adventure.event.consumable", hero.Name, item.Name, hero.Count(item.Id)));
                        }
                        else
                        {
                            AddToBag(item);
                            lines.Add(Data.Line("adventure.event.to_bag", item.Name));
                        }
                        break;
                    case "loot":
                        var loot = Loot(o.Amount);
                        if (loot != null)
                        {
                            AddToBag(loot);
                            lines.Add(Data.Line("adventure.loot", loot.Name));
                        }
                        break;
                    case "reagent":
                        var alchemist = Heroes.FirstOrDefault(h => h.Class.Id == "alchemist");
                        if (alchemist != null)
                        {
                            alchemist.GainReagents(o.Amount);
                            lines.Add(Data.Line("adventure.reagents", alchemist.Name, alchemist.Reagents, alchemist.ReagentMax));
                        }
                        break;
                    case "doom":
                        Doom = Math.Max(0, Doom + o.Amount);
                        lines.Add(Data.Line("adventure.event.doom", Signed(o.Amount), Doom, DoomMax));
                        break;
                    case "renown":
                        GainRenown(o.Amount, lines);
                        break;
                }
            }
        }

        // A random piece of gear of the tier (or the best tier there is, past it), not a starting one.
        ItemDef Loot(int tier)
        {
            var gear = Data.ItemList.Where(i => i.Price > 0 && !i.Consumable).ToList();
            if (gear.Count == 0) return null;
            tier = Math.Min(tier, gear.Max(i => i.Tier));
            var items = gear.Where(i => i.Tier == tier).ToList();
            return items.Count == 0 ? null : items[_lootRandom.Next(items.Count)];
        }

        static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

        string HealLine(Hero hero, int amount)
        {
            int before = hero.Hp;
            hero.Heal(amount);
            return Data.Line("adventure.heal", hero.Name, hero.Hp - before, hero.Hp, hero.MaxHp);
        }

        // Events hurt but never knock a hero down.
        string HurtLine(Hero hero, int amount)
        {
            int before = hero.Hp;
            hero.Hp = Math.Max(1, hero.Hp - amount);
            return Data.Line("adventure.hurt", hero.Name, before - hero.Hp, hero.Hp, hero.MaxHp);
        }

        internal void OpenEventForTest(EventCard card) => OpenEvent(card, Map.Tile(Positions[MapHero]));
    }
}
