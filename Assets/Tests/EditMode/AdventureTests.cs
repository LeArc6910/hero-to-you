using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class AdventureTests
    {
        static Adventure New(ulong seed = 1, params string[] classes) =>
            new Adventure(TestData.Data, classes.Length == 3 ? classes : new[] { "knight", "hunter", "cleric" }, seed);

        // Past the prologue onto the map.
        static Adventure OnMap(ulong seed = 1, params string[] classes)
        {
            var adventure = New(seed, classes);
            Assert.That(adventure.Continue(), Is.Null);
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Map));
            return adventure;
        }

        [Test]
        public void TheStoryStartsItAndTheMapFollows()
        {
            var adventure = New();
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Story));
            Assert.That(adventure.Story.Id, Is.EqualTo("prologue"));
            adventure.Continue();
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Map));
            Assert.That(adventure.MapHero, Is.Zero);
            var start = adventure.Map.Tile(Site.Start).Hex;
            Assert.That(adventure.Positions.All(p => p == start), Is.True);
        }

        [Test]
        public void TheMapIsFilledByTheSeed()
        {
            var a = New(7);
            var b = New(7);
            var c = New(8);
            string Contents(Adventure x) => string.Join(";", x.Map.Tiles.Where(t => t.Content != Content.None).Select(t => $"{t.Hex}:{t.Content}:{t.Encounter}"));
            Assert.That(Contents(a), Is.EqualTo(Contents(b)));
            Assert.That(Contents(a), Is.Not.EqualTo(Contents(c)));
            Assert.That(a.Map.Tiles.Count(t => t.Content == Content.Enemy), Is.EqualTo(Adventure.StartEnemies));
            Assert.That(a.Map.Tiles.Count(t => t.Content == Content.Event), Is.EqualTo(Adventure.StartEvents));
        }

        [Test]
        public void MovementIsRolledThenSpentOnAReachableHex()
        {
            var adventure = OnMap();
            Assert.That(adventure.Move(adventure.Positions[0]), Is.Not.Null, "굴리기 전에는 못 간다");
            Assert.That(adventure.RollMove(), Is.Null);
            Assert.That(adventure.MovePoints, Is.InRange(Adventure.MoveBase, Adventure.MoveBase + Adventure.MoveDice));
            var reach = adventure.Reachable();
            Assert.That(reach.Values.All(cost => cost <= adventure.MovePoints), Is.True);
            Assert.That(reach.Keys.All(h => adventure.Map.Def.Passable(h)), Is.True);
            var quiet = reach.Keys.FirstOrDefault(h => !adventure.Map.Stops(adventure.Map.Tile(h)));
            Assert.That(adventure.Move(quiet), Is.Null);
            Assert.That(adventure.Positions[0], Is.EqualTo(quiet));
            Assert.That(adventure.MapHero, Is.EqualTo(1), "조용한 칸이면 다음 영웅의 차례");
        }

        [Test]
        public void ADayPassesAfterFourRoundsAndTheRiftGrows()
        {
            var adventure = OnMap();
            for (int i = 0; i < 3 * Adventure.TimesOfDay; i++) Assert.That(adventure.Stay(), Is.Null);
            Assert.That(adventure.Day, Is.EqualTo(2));
            Assert.That(adventure.Doom, Is.EqualTo(1));
            Assert.That(adventure.Map.Tiles.Count(t => t.Content == Content.Enemy), Is.EqualTo(Adventure.StartEnemies + 1), "새 날마다 무리 하나");
        }

        [Test]
        public void TheRiftOpeningLosesTheAdventure()
        {
            var adventure = OnMap();
            adventure.Doom = Adventure.DoomMax - 1;
            for (int i = 0; i < 3 * Adventure.TimesOfDay && adventure.Screen == AdventureScreen.Map; i++) adventure.Stay();
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Story));
            Assert.That(adventure.Story.Id, Is.EqualTo("rift_open"));
            adventure.Continue();
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Defeat));
        }

        [Test]
        public void SteppingOnEnemiesAsksWhoJoinsAndOnlyNearHeroesMay()
        {
            var adventure = OnMap();
            var enemy = adventure.Map.Tiles.First(t => t.Content == Content.Enemy);
            adventure.Teleport(0, enemy.Hex.Neighbors().First(h => adventure.Map.Def.Passable(h)));
            adventure.Teleport(2, adventure.Map.Tile(Site.Boss).Hex);
            adventure.RollMove();
            adventure.Teleport(0, enemy.Hex);
            // Arrive through the public path: a move of 0 is not allowed, so open the join directly.
            typeof(Adventure).GetMethod("Arrive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(adventure, new object[] { enemy.Hex });
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Join));
            Assert.That(adventure.JoinCandidates, Does.Contain(0));
            Assert.That(adventure.JoinCandidates, Does.Not.Contain(2), "멀리 있는 영웅은 못 온다");
            Assert.That(adventure.Join(new[] { 1 }), Is.Not.Null, "움직인 영웅은 빠질 수 없다");
            Assert.That(adventure.Join(new[] { 0 }), Is.Null);
            Assert.That(adventure.Battle.Heroes.Count(), Is.EqualTo(1));
        }

        [Test]
        public void WinningOnlyRewardsTheFightersAndClearsTheHex()
        {
            var adventure = OnMap();
            var enemy = adventure.Map.Tiles.First(t => t.Content == Content.Enemy);
            adventure.RollMove();
            adventure.Teleport(0, enemy.Hex);
            typeof(Adventure).GetMethod("Arrive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(adventure, new object[] { enemy.Hex });
            adventure.Join(new[] { 0 });
            adventure.Heroes[0].Blessing = 50;
            var random = new GameRandom(3);
            for (int i = 0; i < 300 && adventure.Screen == AdventureScreen.Battle; i++)
            {
                adventure.Heroes[0].Hp = adventure.Heroes[0].MaxHp;
                adventure.Heroes[0].Blessing = 9;
                Assert.That(adventure.Fight(BattleBot.Choose(adventure.Battle, random)), Is.Null);
            }
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.BattleResult));
            Assert.That(enemy.Content, Is.EqualTo(Content.None));
            Assert.That(adventure.Heroes[0].Xp, Is.GreaterThan(0));
            Assert.That(adventure.Heroes[1].Xp, Is.Zero, "싸우지 않은 영웅은 경험치가 없다");
        }

        [Test]
        public void AClassChoiceShowsOnlyWithThatClassNear()
        {
            var withHunter = OnMap(1, "knight", "hunter", "cleric");
            var withoutHunter = OnMap(1, "knight", "berserker", "cleric");
            var chest = TestData.Data.Events.First(e => e.Id == "chest");
            withHunter.OpenEventForTest(chest);
            withoutHunter.OpenEventForTest(chest);
            Assert.That(withHunter.Choices().Any(c => c.ClassId == "hunter"), Is.True);
            Assert.That(withoutHunter.Choices().Any(c => c.ClassId == "hunter"), Is.False);
            withHunter.Teleport(1, withHunter.Map.Tile(Site.Boss).Hex);
            Assert.That(withHunter.Choices().Any(c => c.ClassId == "hunter"), Is.False, "멀리 있는 사냥꾼은 못 한다");
        }

        [Test]
        public void AChoiceCanNeedAFlag()
        {
            var adventure = OnMap();
            var elder = TestData.Data.Events.First(e => e.Id == "elder");
            adventure.OpenEventForTest(elder);
            Assert.That(adventure.Choices().Count, Is.EqualTo(1));
            adventure.Choose(adventure.Choices()[0].Index, 0);
            var again = OnMap();
            var child = TestData.Data.Events.First(e => e.Id == "lost_child");
            again.OpenEventForTest(child);
            again.Choose(child.Choices.First(c => c.Label.StartsWith("마을까지")).Index, 0);
            again.OpenEventForTest(elder);
            Assert.That(again.Choices().Count, Is.EqualTo(2), "아이를 데려다준 뒤에는 장로가 다른 이야기를 한다");
        }

        [Test]
        public void TheInnRestsEveryoneInTownForGold()
        {
            var adventure = OnMap();
            var town = adventure.Map.Tile(Site.Town).Hex;
            for (int i = 0; i < 3; i++) adventure.Teleport(i, town);
            adventure.Heroes[1].Hp = 0;
            adventure.Heroes[0].Hp = 3;
            typeof(Adventure).GetMethod("Arrive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(adventure, new object[] { town });
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Town));
            int gold = adventure.Gold;
            Assert.That(adventure.Rest(), Is.Null);
            Assert.That(adventure.Gold, Is.EqualTo(gold - Adventure.InnCost));
            Assert.That(adventure.Heroes[1].Down, Is.False);
            Assert.That(adventure.Heroes[0].Hp, Is.GreaterThan(3));
        }

        [Test]
        public void TheShopSellsIntoTheBagAndGearSwaps()
        {
            var adventure = OnMap();
            var town = adventure.Map.Tile(Site.Town).Hex;
            adventure.Teleport(0, town);
            typeof(Adventure).GetMethod("Arrive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(adventure, new object[] { town });
            adventure.Gold = 500;
            int index = adventure.Shop.FindIndex(s => s.Item != null && adventure.Heroes.Any(h => h.CanEquip(s.Item)));
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            var item = adventure.Shop[index].Item;
            Assert.That(adventure.Buy(index), Is.Null);
            Assert.That(adventure.Bag, Does.Contain(item));
            int who = adventure.Heroes.FindIndex(h => h.CanEquip(item));
            var old = item.Slot == ItemSlot.Weapon ? adventure.Heroes[who].Weapon : null;
            Assert.That(adventure.Equip(adventure.Bag.IndexOf(item), who), Is.Null);
            if (old != null) Assert.That(adventure.Bag, Does.Contain(old), "벗은 무기는 가방으로");
        }

        [Test]
        public void ALevelUpWaitsForItsUpgrade()
        {
            var adventure = OnMap();
            var statue = TestData.Data.Events.First(e => e.Id == "ash_wind");
            adventure.OpenEventForTest(statue);
            adventure.Heroes[0].GainXp(9);
            typeof(Adventure).GetMethod("GainXp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(adventure, new object[] { adventure.Heroes[0], 1 });
            Assert.That(adventure.LevelUp, Is.Not.Null);
            Assert.That(adventure.LevelUp.Options.Count, Is.EqualTo(2));
            Assert.That(adventure.Choose(0, 0), Does.Contain("레벨업"), "고르기 전에는 다른 일을 못 한다");
            var upgrade = adventure.LevelUp.Options[1];
            Assert.That(adventure.ChooseUpgrade(1), Is.Null);
            Assert.That(adventure.Heroes[0].Upgrades, Does.Contain(upgrade));
            Assert.That(adventure.LevelUp, Is.Null);
        }

        [TestCase(11UL, "knight", "hunter", "cleric")]
        [TestCase(12UL, "rogue", "bard", "necromancer")]
        [TestCase(13UL, "alchemist", "seer", "beastmaster")]
        [TestCase(14UL, "berserker", "elementalist", "gambler")]
        public void TheBotPlaysAWholeChapterToAnEnd(ulong seed, string a, string b, string c)
        {
            var adventure = new Adventure(TestData.Data, new[] { a, b, c }, seed);
            AdventureBot.Play(adventure, new GameRandom(seed, 9));
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Victory).Or.EqualTo(AdventureScreen.Defeat));
        }
    }
}

namespace Game.Core.Tests
{
    public class AdventureEdgeTests
    {
        [Test]
        public void ABattleLostBeforeTheFirstTurnStillEnds()
        {
            var adventure = new Adventure(TestData.Data, new[] { "elementalist", "hunter", "cleric" }, 3);
            adventure.Continue();
            var enemy = adventure.Map.Tiles.First(t => t.Content == Content.Enemy);
            adventure.Heroes[0].Hp = 1;
            adventure.RollMove();
            adventure.Teleport(0, enemy.Hex);
            for (int seed = 0; seed < 1; seed++)
            {
                typeof(Adventure).GetMethod("Arrive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(adventure, new object[] { enemy.Hex });
                Assert.That(adventure.Join(new[] { 0 }), Is.Null);
                Assert.That(adventure.Screen == AdventureScreen.Battle && adventure.Battle.Over, Is.False, "끝난 전투에 머물지 않는다");
                Assert.That(adventure.Fight(null), Is.Not.Null);
            }
        }

        [Test]
        public void TheRandomBotPlaysEveryClassWithoutBreaking()
        {
            var ids = TestData.Data.PlayableClasses.Select(c => c.Id).ToList();
            for (int i = 0; i < 4; i++)
            {
                var party = new[] { ids[i * 3], ids[i * 3 + 1], ids[i * 3 + 2] };
                for (ulong seed = 1; seed <= 3; seed++)
                {
                    var adventure = new Adventure(TestData.Data, party, seed * 31 + (ulong)i);
                    AdventureBot.Play(adventure, new GameRandom(seed, 3), smart: false);
                    Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Victory).Or.EqualTo(AdventureScreen.Defeat), string.Join(",", party));
                }
            }
        }
    }
}
