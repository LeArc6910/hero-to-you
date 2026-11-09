using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // A player stand-in for whole adventures (the balance sandbox, the tests, the playtest's "autoplay"): one step
    // at a time, whatever the screen asks. Smart plays sensibly: reads the stone first, gets strong on the way, heads
    // for the boss together, rests and shops in town, picks the class's own event choices. Otherwise it wanders and
    // picks at random. The battles go to BattleBot.
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
                    return a.Join(a.JoinCandidates);
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
            if (here.Any(h => h.Down || h.Hp * 10 < h.MaxHp * 7) && a.Gold >= Adventure.InnCost && a.Report.All(l => !l.Contains("결의 가득"))) return a.Rest();
            if (smart)
            {
                // A better weapon or armor for someone, or a potion for the one here.
                for (int i = 0; i < a.Shop.Count; i++)
                {
                    var item = a.Shop[i];
                    if (item.Sold || item.Price > a.Gold) continue;
                    if (item.Item != null && a.Heroes.Any(h => Better(h, item.Item))) return a.Buy(i);
                }
                for (int i = 0; i < a.Bag.Count; i++)
                {
                    var bagItem = a.Bag[i];
                    int who = a.Heroes.FindIndex(h => Better(h, bagItem));
                    if (who >= 0) return a.Equip(i, who);
                }
                int potion = a.Shop.FindIndex(s => s.Goods == "potion");
                if (potion >= 0 && a.Current.Potions < 2 && a.Gold >= a.Shop[potion].Price + Adventure.InnCost) return a.Buy(potion);
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
                // Hurt and far from town: camp instead of walking into trouble.
                if (smart && hero.Hp * 10 < hero.MaxHp * 4 && a.Map.Tile(at).Site != Site.Town && a.Map.Distances(at)[a.Map.Tile(Site.Town).Hex] > 6) return a.Camp();
                return a.RollMove();
            }
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
                // On the way, take events and treasure, and fights while healthy; keep off the boss until together.
                if (tile.Content == Content.Event || tile.Content == Content.Treasure || tile.Content == Content.Herb) score -= 2;
                if (tile.Content == Content.Enemy) score += hero.Hp * 2 > hero.MaxHp && a.Heroes.Count(h => !h.Down && a.Positions[a.Heroes.IndexOf(h)].Distance(hex) <= Adventure.JoinRange) >= 2 ? -1 : 4;
                if (tile.Site == Site.Boss && !ReadyForBoss(a, hex)) score += 50;
                if (tile.Site == Site.Town && hero.Hp * 10 < hero.MaxHp * 6) score -= 3;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = hex;
                }
            }
            return best == at ? a.Stay() : a.Move(best);
        }

        // Where the hero is heading: town when hurt, the stone first, then the boss once strong enough (or late).
        static Hex Goal(Adventure a, Hero hero, Hex at)
        {
            if (hero.Hp * 10 < hero.MaxHp * 4 && a.Gold >= Adventure.InnCost) return a.Map.Tile(Site.Town).Hex;
            if (!a.HasFlag("read_seal")) return a.Map.Tile(Site.Quest).Hex;
            double level = a.Heroes.Average(h => h.Level);
            if (level >= 4 || a.Doom >= 6) return a.Map.Tile(Site.Boss).Hex;
            // Otherwise the nearest thing worth doing.
            var distances = a.Map.Distances(at);
            var targets = a.Map.Tiles.Where(t => t.Content != Content.None).OrderBy(t => distances.TryGetValue(t.Hex, out int d) ? d : 999).ToList();
            return targets.Count > 0 ? targets[0].Hex : a.Map.Tile(Site.Boss).Hex;
        }

        // The boss only with two others close enough to join (or nobody else left standing).
        static bool ReadyForBoss(Adventure a, Hex boss)
        {
            int living = a.Heroes.Count(h => !h.Down);
            int near = a.Heroes.Where((h, i) => !h.Down && i != a.MapHero && a.Positions[i].Distance(boss) <= Adventure.JoinRange).Count();
            return near >= Math.Min(2, living - 1) && (a.Heroes.Average(h => h.Level) >= 4 || a.Doom >= 6);
        }

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
