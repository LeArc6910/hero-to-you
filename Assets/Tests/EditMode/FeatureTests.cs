using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // Wounds, rows, the dungeon, contracts, campfire talks and the difficulty.
    public class FeatureTests
    {
        static Adventure OnMap(ulong seed = 1, string[] classes = null, Difficulty difficulty = Difficulty.Normal)
        {
            var adventure = new Adventure(TestData.Data, classes ?? new[] { "knight", "hunter", "cleric" }, seed, null, difficulty);
            adventure.Continue();
            return adventure;
        }

        static void Arrive(Adventure adventure, Hex hex) =>
            typeof(Adventure).GetMethod("Arrive", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(adventure, new object[] { hex });

        // Plays the battle on screen with the bot, keeping hero 0 alive and sure of every die.
        static void WinBattle(Adventure a)
        {
            var random = new GameRandom(3);
            for (int i = 0; i < 400 && a.Screen == AdventureScreen.Battle && a.Battle.State == BattleState.HeroTurn; i++)
            {
                foreach (var h in a.Fighters)
                {
                    h.Blessing = 9;
                    if (!h.Down) h.Hp = h.MaxHp;
                }
                Assert.That(a.Fight(BattleBot.Choose(a.Battle, random)), Is.Null);
            }
        }

        [Test]
        public void AFallLeavesAWoundThatTheInnHeals()
        {
            var hero = TestData.Hero("knight");
            int full = hero.MaxHp;
            hero.AddWound();
            Assert.That(hero.Wounds, Is.EqualTo(1));
            Assert.That(hero.MaxHp, Is.EqualTo(full * (100 - Hero.WoundPercent) / 100));
            for (int i = 0; i < 5; i++) hero.AddWound();
            Assert.That(hero.Wounds, Is.EqualTo(Hero.MaxWounds));
            Assert.That(hero.Hp, Is.LessThanOrEqualTo(hero.MaxHp));

            var a = OnMap();
            a.Heroes[0].AddWound();
            var town = a.Map.Tile(Site.Town).Hex;
            a.Teleport(0, town);
            Arrive(a, town);
            a.Gold = 100;
            Assert.That(a.Rest(), Is.Null);
            Assert.That(a.Heroes[0].Wounds, Is.Zero);
        }

        [Test]
        public void FallenFightersAreWoundedAfterTheBattle()
        {
            var a = OnMap();
            var enemy = a.Map.Tiles.First(t => t.Content == Content.Enemy);
            a.RollMove();
            a.Teleport(0, enemy.Hex);
            Arrive(a, enemy.Hex);
            // The battle is over at once with its only hero down.
            a.Heroes[0].Hp = 0;
            Assert.That(a.Join(new[] { 0 }), Is.Null);
            Assert.That(a.Battle.State, Is.EqualTo(BattleState.Lost));
            Assert.That(a.Heroes[0].Wounds, Is.EqualTo(1));
        }

        [Test]
        public void RandomBlowsLandOnTheFrontRowAndTheBackHitsSofterUpClose()
        {
            var heroes = TestData.Party("knight", "elementalist", "cleric");
            Assert.That(heroes[0].Back, Is.False, "기사는 앞줄");
            Assert.That(heroes[1].Back, Is.True, "원소술사는 뒷줄");
            var wolf = TestData.Data.Monsters["wolf"];
            var hits = new Dictionary<string, int>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var party = TestData.Party("knight", "elementalist", "cleric");
                var battle = new Battle(party, new[] { wolf }, seed, false, TestData.Data);
                foreach (var e in battle.Events.Where(e => e.Kind == BattleEventKind.Damage))
                {
                    string name = battle.Unit(e.Target).Name;
                    hits[name] = hits.TryGetValue(name, out int n) ? n + 1 : 1;
                }
            }
            Assert.That(hits.Keys, Is.EquivalentTo(new[] { "기사" }), "늑대의 무작위 공격은 앞줄(기사)에게만");

            var knight = TestData.Hero("knight");
            var battle2 = new Battle(new[] { knight }, new[] { TestData.Dummy() }, 1, false, TestData.Data);
            var me = battle2.Heroes.First();
            var attack = knight.Skill(Hero.AttackId);
            int front = battle2.Preview(new BattleCommand(me.Id, attack.Id, battle2.Monsters.First().Id)).ValueBySuccesses.Last();
            knight.Back = true;
            int back = battle2.Preview(new BattleCommand(me.Id, attack.Id, battle2.Monsters.First().Id)).ValueBySuccesses.Last();
            Assert.That(back, Is.LessThan(front), "뒷줄의 근접 공격은 약하다");
        }

        [Test]
        public void TheRowIsChosenOnTheMapAndSaved()
        {
            var a = OnMap();
            Assert.That(a.SetRow(1, false), Is.Null);
            Assert.That(a.Heroes[1].Back, Is.False);
            Assert.That(a.SetRow(1, false), Is.Not.Null);
            var loaded = Adventure.Load(TestData.Data, a.Save(), out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(loaded.Heroes[1].Back, Is.False);
        }

        // One room of the dungeon, whatever it is: a battle won, or an event taken (its first choice someone near can
        // afford) and read, with the fight it may start won.
        static void ClearRoom(Adventure a)
        {
            while (a.LevelUp != null) a.ChooseUpgrade(0);
            if (a.Screen == AdventureScreen.Event)
            {
                Assert.That(a.DungeonEvent, Is.True);
                var choice = a.Choices().First(c => a.Affordable(c) && a.CanAttempt(c).Count > 0);
                Assert.That(a.Choose(choice.Index, a.Heroes.IndexOf(a.CanAttempt(choice)[0])), Is.Null);
                Assert.That(a.Continue(), Is.Null);
                while (a.LevelUp != null) a.ChooseUpgrade(0);
                if (a.Screen != AdventureScreen.Join) return;
            }
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Join));
            Assert.That(a.Join(a.JoinCandidates), Is.Null);
            WinBattle(a);
        }

        [Test]
        public void TheDungeonGoesRoomByRoomAndRemembersHowFar()
        {
            var a = OnMap();
            var dungeon = a.Map.Tiles.First(t => t.Site == Site.Dungeon);
            Assert.That(a.DungeonRooms, Is.GreaterThanOrEqualTo(2));
            a.RollMove();
            a.Teleport(0, dungeon.Hex);
            Arrive(a, dungeon.Hex);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Join));
            Assert.That(a.Kind, Is.EqualTo(Adventure.JoinKind.Dungeon));
            Assert.That(a.CanAvoid, Is.False, "던전은 피할 수 없다");
            Assert.That(a.Chapter.Dungeon[0], Does.Not.StartWith(Adventure.EventRoom), "첫 방은 싸움");
            a.Join(new[] { 0 });
            WinBattle(a);
            while (a.LevelUp != null) a.ChooseUpgrade(0);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.DungeonRest));
            Assert.That(a.DungeonRoom, Is.EqualTo(1));
            Assert.That(a.LeaveDungeon(), Is.Null);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Map));
            Assert.That(dungeon.Used, Is.False);
            // Back in: the next room, not the first again.
            a.Teleport(a.MapHero, dungeon.Hex);
            Arrive(a, dungeon.Hex);
            if (a.Chapter.Dungeon[1].StartsWith(Adventure.EventRoom)) Assert.That(a.Event.Pool, Is.EqualTo(a.Chapter.Dungeon[1].Substring(Adventure.EventRoom.Length)));
            else Assert.That(a.Encounter.Id, Is.EqualTo(a.Chapter.Dungeon[1]));
        }

        [Test]
        public void TheLastRoomGivesTheTreasureAndClosesTheDungeon()
        {
            var a = OnMap();
            var dungeon = a.Map.Tiles.First(t => t.Site == Site.Dungeon);
            a.RollMove();
            a.Teleport(0, dungeon.Hex);
            Arrive(a, dungeon.Hex);
            int bag = a.Bag.Count;
            for (int room = 0; room < a.DungeonRooms; room++)
            {
                while (a.LevelUp != null) a.ChooseUpgrade(0);
                if (room > 0) Assert.That(a.Delve(), Is.Null);
                ClearRoom(a);
            }
            Assert.That(dungeon.Used, Is.True);
            Assert.That(a.Bag.Count, Is.GreaterThan(bag));
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.BattleResult));
        }

        [Test]
        public void AContractMarksAGroupAndPaysInTown()
        {
            var a = OnMap();
            var town = a.Map.Tile(Site.Town).Hex;
            a.Teleport(0, town);
            Arrive(a, town);
            Assert.That(a.CanTakeContract, Is.True);
            Assert.That(a.TakeContract(), Is.Null);
            var target = a.Contract.Tile;
            Assert.That(a.Knows(target), Is.True, "의뢰한 무리는 지도에 보인다");
            Assert.That(a.TakeContract(), Is.Not.Null, "하나씩");
            a.LeaveTown();
            // Hero 1 beats the group.
            for (int i = 0; i < 10 && a.MapHero != 1; i++) a.Stay();
            Assert.That(a.MapHero, Is.EqualTo(1));
            a.RollMove();
            a.Teleport(1, target.Hex);
            Arrive(a, target.Hex);
            a.Join(new[] { 1 });
            WinBattle(a);
            Assert.That(a.Contract.Done, Is.True);
            int gold = a.Gold;
            int renown = a.Renown;
            for (int i = 0; i < 20 && (a.Screen != AdventureScreen.Map || a.LevelUp != null); i++)
            {
                if (a.LevelUp != null) a.ChooseUpgrade(0);
                else a.Continue();
            }
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Map));
            a.Teleport(a.MapHero, town);
            Arrive(a, town);
            Assert.That(a.Contract, Is.Null);
            Assert.That(a.Gold, Is.GreaterThan(gold));
            Assert.That(a.Renown, Is.GreaterThan(renown));
        }

        [Test]
        public void TwoHeroesTalkAtDawn()
        {
            var a = OnMap(1, new[] { "knight", "cleric", "hunter" });
            int resolve = a.Heroes[0].Resolve;
            a.Heroes[0].Resolve = 0;
            a.Heroes[1].Resolve = 0;
            for (int i = 0; i < 3 * Adventure.TimesOfDay; i++) a.Stay();
            Assert.That(a.Day, Is.EqualTo(2));
            Assert.That(a.Talks.Count, Is.EqualTo(1));
            var talk = a.Talks[0].talk;
            Assert.That(new[] { talk.A, talk.B }, Is.SubsetOf(new[] { "knight", "cleric", "hunter" }));
            Assert.That(talk.Lines.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void EasyMonstersHaveLessHealthAndTheSaveKeepsIt()
        {
            var easy = OnMap(2, null, Difficulty.Easy);
            var normal = OnMap(2);
            Assert.That(easy.Hope, Is.EqualTo(normal.Hope + 1));
            var enemy = easy.Map.Tiles.First(t => t.Content == Content.Enemy);
            easy.RollMove();
            easy.Teleport(0, enemy.Hex);
            Arrive(easy, enemy.Hex);
            easy.Join(new[] { 0 });
            int hp = easy.Battle.Monsters.First().MaxHp;
            Assert.That(hp, Is.LessThan(easy.Encounter.Monsters[0].Hp));
            var loaded = Adventure.Load(TestData.Data, easy.Save(), out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(loaded.Difficulty, Is.EqualTo(Difficulty.Easy));
        }

        [Test]
        public void EveryChapterHasItsDungeonAndTalksAreWellFormed()
        {
            foreach (var chapter in TestData.Data.Chapters)
            {
                Assert.That(chapter.Dungeon.Count, Is.GreaterThan(0), chapter.Id);
                Assert.That(chapter.Map.Hexes().Count(h => chapter.Map.SiteAt(h) == Site.Dungeon), Is.EqualTo(1), chapter.Id);
            }
            Assert.That(TestData.Data.Talks.Count, Is.GreaterThanOrEqualTo(12));
        }

        [TestCase(31UL, "knight", "hunter", "cleric")]
        [TestCase(32UL, "alchemist", "necromancer", "seer")]
        public void TheHumanLikeBotPlaysTheCampaignToAnEnd(ulong seed, string a, string b, string c)
        {
            var adventure = new Adventure(TestData.Data, new[] { a, b, c }, seed);
            AdventureBot.Play(adventure, new GameRandom(seed, 2), true, 30000, AdventureBot.HumanMistakes);
            Assert.That(adventure.Over, Is.True);
            var loaded = Adventure.Load(TestData.Data, adventure.Save(), out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(loaded.Screen, Is.EqualTo(adventure.Screen));
        }
    }
}
