using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // A player stand-in for whole adventures (the balance sandbox, the tests, the playtest's "autoplay"): one step
    // at a time, whatever the screen asks. Smart plays sensibly: reads the quest site and finds the clue first, gets
    // strong on the way, takes the middle boss, heads for the boss together, rests and shops in town, slips past
    // fights when hurt, drinks potions, picks the class's own event choices. It only knows what fog lets the party
    // know. Otherwise it wanders and picks at random. The battles go to BattleBot.
    public static class AdventureBot
    {
        // Does one thing on the current screen. Returns the error the adventure gave, or null.
        public static string Step(Adventure a, GameRandom random, bool smart = true)
        {
            if (a.LevelUp != null) return a.ChooseUpgrade(random.Next(a.LevelUp.Options.Count));
            switch (a.Screen)
            {
                case AdventureScreen.Story:
                case AdventureScreen.BattleResult:
                case AdventureScreen.EventResult:
                    return a.Continue();
                case AdventureScreen.Battle:
                    return a.Fight(BattleBot.Choose(a.Battle, random, smart));
                case AdventureScreen.Join:
                    return JoinOrAvoid(a, smart);
                case AdventureScreen.Event:
                    return ChooseEvent(a, random, smart);
                case AdventureScreen.Town:
                    return Town(a, random, smart);
                case AdventureScreen.Map:
                    return MapTurn(a, random, smart);
                default:
                    return "끝났다";
            }
        }

        static string JoinOrAvoid(Adventure a, bool smart)
        {
            if (smart && a.CanAvoid)
            {
                var hero = a.Current;
                bool hurt = hero.Hp * 2 < hero.MaxHp;
                if (a.CanScare && a.AvoidOdds(true).AtLeast(Adventure.AvoidNeed) >= 0.6) return a.Scare();
                if (hurt && a.AvoidOdds(false).AtLeast(Adventure.AvoidNeed) >= 0.4) return a.Avoid();
            }
            return a.Join(a.JoinCandidates);
        }

        static string ChooseEvent(Adventure a, GameRandom random, bool smart)
        {
            var choices = a.Choices().Where(c => a.Affordable(c) && a.CanAttempt(c).Count > 0).ToList();
            EventChoice choice;
            if (smart)
            {
                // A class's own choice, else the choice whose check is likeliest to pass (no check counts as sure),
                // skipping plain "pass by" unless nothing else is good.
                choice = choices.FirstOrDefault(c => c.ClassId != null)
                    ?? choices.Where(c => c.Success.Count > 0).OrderByDescending(c => c.HasCheck ? Best(a, c) : 0.9).FirstOrDefault()
                    ?? choices[0];
            }
            else choice = choices[random.Next(choices.Count)];
            var hero = a.CanAttempt(choice).OrderByDescending(h => choice.HasCheck ? Adventure.Odds(choice, h, 0).AtLeast(choice.Need) : 0).First();
            return a.Choose(choice.Index, a.Heroes.IndexOf(hero));
        }

        static double Best(Adventure a, EventChoice c) => a.CanAttempt(c).Select(h => Adventure.Odds(c, h, 0).AtLeast(c.Need)).DefaultIfEmpty(0).Max();

        static string Town(Adventure a, GameRandom random, bool smart)
        {
            var here = a.TownHeroes().Select(i => a.Heroes[i]).ToList();
            if (here.Any(h => h.Down || h.Hp * 10 < h.MaxHp * 7) && a.Gold >= a.InnPrice && a.Report.All(l => !l.Contains("결의 가득"))) return a.Rest();
            if (smart)
            {
                // A better weapon or armor for someone, potions for those here, rumors when rich.
                for (int i = 0; i < a.Shop.Count; i++)
                {
                    var item = a.Shop[i];
                    if (item.Sold || item.Price > a.Gold || item.Item == null || item.Item.Consumable) continue;
                    if (a.Heroes.Any(h => Better(h, item.Item))) return a.Buy(i);
                }
                for (int i = 0; i < a.Bag.Count; i++)
                {
                    var bagItem = a.Bag[i];
                    int who = a.Heroes.FindIndex(h => Better(h, bagItem));
                    if (who >= 0) return a.Equip(i, who);
                }
                int potion = a.Shop.FindIndex(s => s.Item != null && s.Item.Id == Hero.PotionId);
                foreach (int i in a.TownHeroes())
                    if (potion >= 0 && a.Heroes[i].Potions < 2 && a.Gold >= a.Shop[potion].Price + a.InnPrice) return a.Buy(potion, i);
                int tonic = a.Shop.FindIndex(s => s.Item != null && s.Item.Id == "tonic");
                if (tonic >= 0 && a.Current.Count("tonic") < 1 && a.Gold >= a.Shop[tonic].Price + 30) return a.Buy(tonic);
                if (a.CanHearRumors && a.Gold >= Adventure.RumorCost + 25) return a.Rumor();
                if (a.CanDonate && a.Gold >= a.DonationCost + 60) return a.Donate();
                if (a.CanMeetElder) return a.Elder();
            }
            return a.LeaveTown();
        }

        // Whether the item would be a step up for the hero in its slot.
        static bool Better(Hero hero, ItemDef item)
        {
            if (!hero.CanEquip(item)) return false;
            var worn = item.Slot == ItemSlot.Weapon ? hero.Weapon : item.Slot == ItemSlot.Armor ? hero.Armor : hero.Trinket;
            return worn == null ? item.Slot != ItemSlot.Weapon : item.Tier > worn.Tier;
        }

        static string MapTurn(Adventure a, GameRandom random, bool smart)
        {
            var hero = a.Current;
            var at = a.Positions[a.MapHero];
            if (a.MovePoints < 0)
            {
                if (smart && hero.Hp * 10 < hero.MaxHp * 4 && hero.Potions > 0) return a.UseItem(a.MapHero, Hero.PotionId);
                // Hurt and far from town: camp instead of walking into trouble.
                if (smart && hero.Hp * 10 < hero.MaxHp * 4 && a.Map.Tile(at).Site != Site.Town && a.Map.Distances(at)[a.Map.Tile(Site.Town).Hex] > 6) return a.Camp();
                return a.RollMove();
            }
            if (smart && a.CanReroll && a.MovePoints <= Adventure.MoveBase) return a.Reroll();
            var reach = a.Reachable();
            if (reach.Count == 0) return a.Stay();
            if (!smart)
            {
                var hexes = reach.Keys.ToList();
                return a.Move(hexes[random.Next(hexes.Count)]);
            }
            var goal = Goal(a, hero, at);
            var distances = a.Map.Distances(goal);
            Hex best = at;
            double bestScore = double.MaxValue;
            foreach (var (hex, _) in reach)
            {
                var tile = a.Map.Tile(hex);
                double score = distances.TryGetValue(hex, out int d) ? d : 999;
                bool known = a.Knows(tile);
                // On the way, take events and treasure, and fights while healthy; keep off the bosses until ready.
                if (known && (tile.Content == Content.Event || tile.Content == Content.Treasure || tile.Content == Content.Herb)) score -= 2;
                if (known && tile.Content == Content.Enemy) score += hero.Hp * 2 > hero.MaxHp && a.Heroes.Count(h => !h.Down && a.Positions[a.Heroes.IndexOf(h)].Distance(hex) <= Adventure.JoinRange) >= 2 ? -1 : 4;
                if (tile.Site == Site.Boss && !ReadyForBoss(a, hex)) score += 50;
                if (tile.Site == Site.Camp && !tile.Used && !ReadyForCamp(a, hex)) score += 50;
                if (tile.Site == Site.Town && hero.Hp * 10 < hero.MaxHp * 6) score -= 3;
                if (!a.Explored(hex)) score -= 0.5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = hex;
                }
            }
            return best == at ? a.Stay() : a.Move(best);
        }

        // Where the hero is heading: town when hurt, the quest site and the clue first, the middle boss when grown,
        // then the boss once strong enough (or late).
        static Hex Goal(Adventure a, Hero hero, Hex at)
        {
            if (hero.Hp * 10 < hero.MaxHp * 4 && a.Gold >= a.InnPrice) return a.Map.Tile(Site.Town).Hex;
            if (!a.HasFlag(a.Chapter.WeakenFlag)) return a.Map.Tile(Site.Quest).Hex;
            if (!a.HasFlag(a.Chapter.NoSummonFlag) && !a.Map.Tile(Site.Clue).Used) return a.Map.Tile(Site.Clue).Hex;
            double level = a.Heroes.Average(h => h.Level);
            var camp = a.Map.Tile(Site.Camp);
            if (!camp.Used && level >= 3 + a.Chapter.Index * 3 && a.Doom < 7) return camp.Hex;
            if (level >= 4 + a.Chapter.Index || a.Doom >= 6) return a.Map.Tile(Site.Boss).Hex;
            // Otherwise the nearest thing worth doing that the party knows of, or the nearest fog.
            var distances = a.Map.Distances(at);
            int Far(Hex h) => distances.TryGetValue(h, out int d) ? d : 999;
            var target = a.Map.Tiles.Where(t => t.Content != Content.None && a.Knows(t)).OrderBy(t => Far(t.Hex)).FirstOrDefault()
                ?? a.Map.Tiles.Where(t => a.Map.Def.Passable(t.Hex) && !a.Explored(t.Hex)).OrderBy(t => Far(t.Hex)).FirstOrDefault();
            return target?.Hex ?? a.Map.Tile(Site.Boss).Hex;
        }

        static bool Together(Adventure a, Hex place)
        {
            int living = a.Heroes.Count(h => !h.Down);
            int near = a.Heroes.Where((h, i) => !h.Down && i != a.MapHero && a.Positions[i].Distance(place) <= Adventure.JoinRange).Count();
            return near >= Math.Min(2, living - 1);
        }

        static bool ReadyForCamp(Adventure a, Hex camp) =>
            Together(a, camp) && a.Heroes.Average(h => h.Level) >= 3 + a.Chapter.Index * 3 && a.Heroes.Where(h => !h.Down).All(h => h.Hp * 10 >= h.MaxHp * 6);

        // The boss only with two others close enough to join (or nobody else left standing).
        static bool ReadyForBoss(Adventure a, Hex boss) => Together(a, boss) && (a.Heroes.Average(h => h.Level) >= 4 + a.Chapter.Index || a.Doom >= 6);

        // Plays a whole adventure to its end (or `limit` steps). Returns the steps taken.
        public static int Play(Adventure a, GameRandom random, bool smart = true, int limit = 20000)
        {
            int steps = 0;
            while (steps < limit && a.Screen != AdventureScreen.Victory && a.Screen != AdventureScreen.Defeat)
            {
                string error = Step(a, random, smart);
                if (error != null) throw new InvalidOperationException($"bot step {steps} on {a.Screen}: {error}");
                steps++;
            }
            return steps;
        }
    }
}
