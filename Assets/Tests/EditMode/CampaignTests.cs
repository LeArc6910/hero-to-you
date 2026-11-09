using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // Chapters, fog, avoiding and fleeing, consumables, rumors, map perks, renown and the journal.
    public class CampaignTests
    {
        static Adventure OnMap(ulong seed = 1, string[] classes = null, string[] perks = null)
        {
            var adventure = new Adventure(TestData.Data, classes ?? new[] { "knight", "hunter", "cleric" }, seed, perks);
            Assert.That(adventure.Continue(), Is.Null);
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Map));
            return adventure;
        }

        static void Arrive(Adventure adventure, Hex hex) =>
            typeof(Adventure).GetMethod("Arrive", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(adventure, new object[] { hex });

        // A quiet hex far from the start (in fog) with quiet hexes around it.
        static Hex FarGround(Adventure a)
        {
            var start = a.Map.Tile(Site.Start).Hex;
            bool Quiet(Hex h) => a.Map.Def.Passable(h) && a.Map.Tile(h).Site == Site.None;
            foreach (var hex in a.Map.Def.Hexes().Where(h => h.Distance(start) >= 6 && Quiet(h)))
                if (hex.Neighbors().Count(Quiet) >= 3) return hex;
            Assert.Fail("안개 속 빈 땅이 없다");
            return default;
        }

        // Hero 0 stands there with the most movement it can roll.
        static void StandAndRoll(Adventure a, Hex at)
        {
            Clear(a, at, 2);
            a.Positions[0] = at;
            a.Heroes[0].Luck = Adventure.MoveDice;
            Assert.That(a.RollMove(), Is.Null);
        }

        static void Clear(Adventure a, Hex around, int radius)
        {
            foreach (var tile in a.Map.Tiles.Where(t => t.Hex.Distance(around) <= radius)) tile.Content = Content.None;
        }

        [Test]
        public void BeatingTheBossOpensTheNextChapterWithTheSameParty()
        {
            var a = OnMap();
            a.Heroes[0].GainXp(30);
            int level = a.Heroes[0].Level;
            a.Gold = 77;
            a.EndChapterForTest();
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Story));
            Assert.That(new[] { "epilogue", "epilogue_kind", "epilogue_grim" }, Does.Contain(a.Story.Id));
            Assert.That(a.Continue(), Is.Null);
            Assert.That(a.Chapter.Id, Is.EqualTo("chapter2"));
            Assert.That(a.Story.Id, Is.EqualTo("prologue2"));
            Assert.That(a.Heroes[0].Level, Is.EqualTo(level), "레벨은 다음 장으로 이어진다");
            Assert.That(a.Gold, Is.EqualTo(77));
            Assert.That(a.Doom, Is.Zero);
            Assert.That(a.Heroes.All(h => h.Hp == h.MaxHp), Is.True, "장 사이에 쉰다");
            Assert.That(a.Continue(), Is.Null);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Map));
            Assert.That(a.Positions.All(p => p == a.Map.Tile(Site.Start).Hex), Is.True);
        }

        [Test]
        public void TheLastChapterWonIsAVictoryWithPages()
        {
            var a = OnMap();
            a.EndChapterForTest();
            a.Continue();
            a.Continue();
            a.EndChapterForTest();
            Assert.That(a.Continue(), Is.Null);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Victory));
            Assert.That(a.ChaptersCleared, Is.EqualTo(2));
            Assert.That(a.PagesEarned, Is.EqualTo(1 + 2 * 3));
        }

        [Test]
        public void KindDeedsChooseTheKindEndingAndGrimOnesTheGrim()
        {
            var kind = OnMap();
            foreach (string flag in kind.Chapter.KindFlags.Take(kind.Chapter.KindNeed)) kind.SetFlag(flag);
            kind.EndChapterForTest();
            Assert.That(kind.Story.Id, Is.EqualTo(kind.Chapter.EpilogueKind));

            var grim = OnMap();
            foreach (string flag in grim.Chapter.GrimFlags.Take(grim.Chapter.GrimNeed)) grim.SetFlag(flag);
            grim.EndChapterForTest();
            Assert.That(grim.Story.Id, Is.EqualTo(grim.Chapter.EpilogueGrim));
        }

        [Test]
        public void FogHidesFarContentsButNotThePlaces()
        {
            var a = OnMap();
            var start = a.Map.Tile(Site.Start).Hex;
            var far = a.Map.Tiles.Where(t => t.Content != Content.None && t.Hex.Distance(start) > Adventure.Sight + 1).ToList();
            Assert.That(far, Is.Not.Empty);
            Assert.That(far.All(t => !a.Knows(t)), Is.True, "멀리 있는 것은 모른다");
            Assert.That(a.Knows(a.Map.Tile(Site.Boss)), Is.True, "장소는 일지의 지도에 있다");
            Assert.That(a.Explored(start), Is.True);

            var seer = OnMap(1, new[] { "seer", "hunter", "cleric" });
            Assert.That(seer.Map.Tiles.Where(t => t.Content == Content.Event).All(seer.Knows), Is.True, "점성술사는 사건을 느낀다");
            Assert.That(seer.Map.Tiles.Where(t => t.Content == Content.Enemy && t.Hex.Distance(start) > 3).Any(seer.Knows), Is.False);
        }

        [Test]
        public void TheHunterSeesFurtherAndWalksForestsLightly()
        {
            var a = OnMap(1, new[] { "knight", "hunter", "cleric" });
            Assert.That(a.Vision(1), Is.EqualTo(a.Vision(0) + 1));
            Assert.That(a.TerrainCost(a.Heroes[1], Terrain.Forest), Is.EqualTo(1));
            Assert.That(a.TerrainCost(a.Heroes[0], Terrain.Forest), Is.EqualTo(2));
        }

        [Test]
        public void WhatFogHidStopsTheWalkOnTheWay()
        {
            var a = OnMap();
            var at = FarGround(a);
            Clear(a, at, 2);
            a.Positions[0] = at;
            var around = at.Neighbors().Where(n => a.Map.Def.Passable(n) && a.Map.Tile(n).Site == Site.None).ToList();
            foreach (var n in around)
            {
                a.Map.Tile(n).Content = Content.Enemy;
                a.Map.Tile(n).Encounter = "goblins";
            }
            Assert.That(around.Any(n => a.Knows(a.Map.Tile(n))), Is.False);
            a.Heroes[0].Luck = Adventure.MoveDice;
            Assert.That(a.RollMove(), Is.Null);
            var twoAway = a.Reachable().Keys.FirstOrDefault(h => h.Distance(at) == 2 && a.Map.Tile(h).Content == Content.None && a.Map.Tile(h).Site == Site.None);
            Assert.That(twoAway.Distance(at), Is.EqualTo(2), "모르는 것은 길을 막지 않는다");
            Assert.That(a.Move(twoAway), Is.Null);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Join));
            Assert.That(a.Positions[0].Distance(at), Is.EqualTo(1), "숨은 무리에서 멈춘다");
        }

        // Hero 0 walks from far ground onto a group it could not see.
        static (Adventure a, Hex from, MapTile enemy) MeetGroup(string[] classes)
        {
            var a = OnMap(1, classes);
            var at = FarGround(a);
            StandAndRoll(a, at);
            var enemy = a.Map.Tile(at.Neighbors().First(n => a.Map.Def.Passable(n) && a.Map.Tile(n).Site == Site.None));
            enemy.Content = Content.Enemy;
            enemy.Encounter = "goblins";
            Assert.That(a.Move(enemy.Hex), Is.Null);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Join));
            return (a, at, enemy);
        }

        [Test]
        public void AvoidingAGroupStepsBack()
        {
            var (a, from, enemy) = MeetGroup(new[] { "knight", "hunter", "cleric" });
            Assert.That(a.CanAvoid, Is.True);
            Assert.That(a.CanScare, Is.False, "겁주기는 광전사만");
            a.Heroes[0].Luck = 3;
            Assert.That(a.Avoid(), Is.Null);
            Assert.That(a.Positions[0], Is.EqualTo(from));
            Assert.That(enemy.Content, Is.EqualTo(Content.Enemy), "무리는 남는다");
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Map));
            Assert.That(a.MapHero, Is.EqualTo(1));
            Assert.That(a.Log.Last(), Is.EqualTo("avoid"));
        }

        [Test]
        public void AFailedAvoidMeansAFightAndNoSecondTry()
        {
            var (a, _, _) = MeetGroup(new[] { "knight", "hunter", "cleric" });
            for (int i = 0; i < 40 && a.Screen == AdventureScreen.Join && a.CanAvoid; i++)
            {
                a.Heroes[0].Luck = 0;
                // Agility 0 keeps the chance at the floor; one try either way.
                a.Avoid();
            }
            if (a.Screen == AdventureScreen.Join)
            {
                Assert.That(a.CanAvoid, Is.False);
                Assert.That(a.Avoid(), Is.Not.Null);
                Assert.That(a.Join(new[] { 0 }), Is.Null);
            }
        }

        [Test]
        public void TheBerserkerScaresAGroupOff()
        {
            var (a, _, enemy) = MeetGroup(new[] { "berserker", "hunter", "cleric" });
            Assert.That(a.CanScare, Is.True);
            a.Heroes[0].Luck = 3;
            Assert.That(a.Scare(), Is.Null);
            Assert.That(enemy.Content, Is.EqualTo(Content.None));
            Assert.That(a.Heroes[0].Xp, Is.GreaterThan(0));
        }

        [Test]
        public void SmokeGetsThePartyOutAndTheGroupStays()
        {
            var (a, from, enemy) = MeetGroup(new[] { "knight", "hunter", "cleric" });
            a.Heroes[0].AddConsumable("smoke", 1);
            Assert.That(a.Join(new[] { 0 }), Is.Null);
            var random = new GameRandom(5);
            for (int i = 0; i < 50 && a.Screen == AdventureScreen.Battle; i++)
            {
                var me = a.Battle.Current;
                a.Heroes[0].Hp = a.Heroes[0].MaxHp;
                Assert.That(a.Fight(new BattleCommand(me.Id, BattleCommand.Use("smoke"), me.Id)), Is.Null);
            }
            Assert.That(a.Battle.State, Is.EqualTo(BattleState.Fled));
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.BattleResult));
            Assert.That(a.Positions[0], Is.EqualTo(from));
            Assert.That(enemy.Content, Is.EqualTo(Content.Enemy));
            Assert.That(a.Heroes[0].Count("smoke"), Is.Zero);
        }

        [Test]
        public void PotionsAndCharmsWorkOnTheMapButSmokeDoesNot()
        {
            var a = OnMap();
            var hero = a.Heroes[0];
            hero.Hp = 5;
            int potions = hero.Potions;
            Assert.That(a.UseItem(0, Hero.PotionId), Is.Null);
            Assert.That(hero.Hp, Is.GreaterThan(5));
            Assert.That(hero.Potions, Is.EqualTo(potions - 1));
            hero.AddConsumable("charm", 1);
            Assert.That(a.UseItem(0, "charm"), Is.Null);
            Assert.That(hero.Luck, Is.EqualTo(2));
            hero.AddConsumable("smoke", 1);
            Assert.That(a.UseItem(0, "smoke"), Is.Not.Null);
            Assert.That(a.UseItem(0, "firebomb"), Is.Not.Null, "없는 것은 못 쓴다");
        }

        [Test]
        public void TheTownSellsConsumablesRumorsAndTheBardBargains()
        {
            var a = OnMap(1, new[] { "bard", "hunter", "cleric" });
            var town = a.Map.Tile(Site.Town).Hex;
            a.Teleport(0, town);
            Arrive(a, town);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Town));
            a.Gold = 200;
            int tonic = a.Shop.FindIndex(s => s.Item != null && s.Item.Id == "tonic");
            Assert.That(tonic, Is.GreaterThanOrEqualTo(0));
            Assert.That(a.Shop[tonic].Price, Is.LessThan(TestData.Data.Items["tonic"].Price), "음유시인은 값을 깎는다");
            Assert.That(a.Buy(tonic, 2), Is.Null);
            Assert.That(a.Heroes[2].Count("tonic"), Is.EqualTo(1));
            Assert.That(a.Shop[tonic].Sold, Is.False, "소모품은 계속 판다");

            var hidden = a.Map.Tiles.Count(t => !a.Knows(t));
            Assert.That(a.CanHearRumors, Is.True);
            Assert.That(a.Rumor(), Is.Null);
            Assert.That(a.Map.Tiles.Count(t => !a.Knows(t)), Is.EqualTo(hidden - System.Math.Min(hidden, Adventure.RumorHexes)));
            Assert.That(a.Rumor(), Is.Not.Null, "소문은 하루 한 번");
        }

        [Test]
        public void TheGamblerRerollsTheMoveOnceADay()
        {
            var a = OnMap(1, new[] { "gambler", "hunter", "cleric" });
            Assert.That(a.Reroll(), Is.Not.Null, "굴리기 전에는 다시 굴릴 수 없다");
            a.RollMove();
            Assert.That(a.CanReroll, Is.True);
            Assert.That(a.Reroll(), Is.Null);
            Assert.That(a.CanReroll, Is.False);
            Assert.That(a.MovePoints, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void TheElderTalksOnceAChapter()
        {
            var a = OnMap();
            var town = a.Map.Tile(Site.Town).Hex;
            a.Teleport(0, town);
            Arrive(a, town);
            Assert.That(a.Elder(), Is.Null);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Event));
            Assert.That(a.Event.Pool, Is.EqualTo(a.Chapter.ElderPool));
            Assert.That(a.CanMeetElder, Is.False);
        }

        [Test]
        public void RenownRaisesThePartysTitle()
        {
            var a = OnMap();
            var first = a.Rank;
            Assert.That(first.Need, Is.Zero);
            a.AddRenown(TestData.Data.Renown[1].Need);
            Assert.That(a.Rank, Is.SameAs(TestData.Data.Renown[1]));
            Assert.That(a.Report.Last(), Does.Contain(a.Rank.Title));
            a.AddRenown(1000);
            Assert.That(a.Rank, Is.SameAs(TestData.Data.Renown.Last()));
            Assert.That(a.NextRank, Is.Null);
        }

        [Test]
        public void EventsLeaveTheOldHerosNotes()
        {
            var a = OnMap();
            var card = TestData.Data.Events.First(e => !string.IsNullOrEmpty(e.Journal) && e.Pool == "wild" && e.ClassId == null);
            a.OpenEventForTest(card);
            Assert.That(a.Footsteps, Does.Contain(card));
        }

        [Test]
        public void JournalPerksStartTheRunAndComeBackWithTheSave()
        {
            var a = OnMap(4, null, new[] { "coin_purse", "spare_potions", "hard_road" });
            Assert.That(a.Gold, Is.EqualTo(Adventure.StartGold + 20));
            Assert.That(a.Heroes.All(h => h.Potions == 2), Is.True);
            Assert.That(a.Hard, Is.True);
            a.RollMove();
            var loaded = Adventure.Load(TestData.Data, a.Save(), out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(loaded.Perks, Is.EqualTo(a.Perks));
            Assert.That(loaded.Gold, Is.EqualTo(a.Gold));
            Assert.That(loaded.MovePoints, Is.EqualTo(a.MovePoints));
            Assert.That(() => new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 1, new[] { "nope" }), Throws.ArgumentException);
        }

        [Test]
        public void TheJournalKeepsPagesUnlocksAndNotes()
        {
            var a = OnMap();
            a.OpenEventForTest(TestData.Data.Events.First(e => !string.IsNullOrEmpty(e.Journal)));
            var journal = new Journal();
            Assert.That(journal.Record(a), Is.Zero, "끝나지 않은 모험은 적지 않는다");
            a.Doom = Adventure.DoomMax;
            a.EndChapterForTest();
            a.Continue();
            a.Continue();
            a.EndChapterForTest();
            a.Continue();
            Assert.That(a.Over, Is.True);
            int pages = journal.Record(a);
            Assert.That(pages, Is.EqualTo(a.PagesEarned));
            Assert.That(journal.Victories, Is.EqualTo(1));
            Assert.That(journal.Notes, Is.Not.Empty);
            var perk = TestData.Data.Perks.First(p => p.Pages <= pages);
            Assert.That(journal.Unlock(perk), Is.Null);
            Assert.That(journal.Unlock(perk), Is.Not.Null, "한 번만");
            Assert.That(journal.Pages, Is.EqualTo(pages - perk.Pages));
            var back = Journal.Parse(journal.ToText());
            Assert.That(back.ToText(), Is.EqualTo(journal.ToText()));
            Assert.That(back.Has(perk.Id), Is.True);
            Assert.That(Journal.Parse("garbage").Pages, Is.Zero);
        }

        [Test]
        public void EveryChapterMapHoldsItsPlaces()
        {
            foreach (var chapter in TestData.Data.Chapters)
            {
                var map = chapter.Map;
                foreach (var site in new[] { Site.Start, Site.Town, Site.Quest, Site.Clue, Site.Camp, Site.Boss })
                    Assert.That(map.Hexes().Count(h => map.SiteAt(h) == site), Is.EqualTo(1), $"{chapter.Id} {site}");
                Assert.That(TestData.Data.Events.Any(e => e.Pool == chapter.QuestPool), Is.True);
            }
        }

        [TestCase(11UL, "knight", "hunter", "cleric")]
        [TestCase(12UL, "berserker", "elementalist", "gambler")]
        [TestCase(13UL, "seer", "necromancer", "bard")]
        public void TheBotPlaysTheWholeCampaignToAnEnd(ulong seed, string a, string b, string c)
        {
            var adventure = new Adventure(TestData.Data, new[] { a, b, c }, seed);
            AdventureBot.Play(adventure, new GameRandom(seed, 2));
            Assert.That(adventure.Over, Is.True);
            var loaded = Adventure.Load(TestData.Data, adventure.Save(), out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(loaded.Screen, Is.EqualTo(adventure.Screen));
            Assert.That(loaded.Renown, Is.EqualTo(adventure.Renown));
        }
    }
}
