using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // The smithy, the hardcore mode, map variants, achievements and trying a chapter again.
    public class ProgressTests
    {
        static Adventure OnMap(ulong seed = 1, string[] classes = null, bool hardcore = false)
        {
            var adventure = new Adventure(TestData.Data, classes ?? new[] { "knight", "hunter", "cleric" }, seed, null, Difficulty.Normal, hardcore);
            adventure.Continue();
            return adventure;
        }

        static void Arrive(Adventure adventure, Hex hex) =>
            typeof(Adventure).GetMethod("Arrive", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(adventure, new object[] { hex });

        static void ToTown(Adventure a)
        {
            var town = a.Map.Tile(Site.Town).Hex;
            a.Teleport(a.MapHero, town);
            Arrive(a, town);
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Town));
        }

        [Test]
        public void TheSmithyForgesUpToThreeLevels()
        {
            var a = OnMap();
            ToTown(a);
            a.Gold = 2000;
            var knight = a.Heroes[0];
            int power = knight.Skill(Hero.AttackId).Power;
            Assert.That(a.Forge(0, ItemSlot.Weapon), Is.Null);
            Assert.That(knight.Weapon.Plus, Is.EqualTo(1));
            Assert.That(knight.Weapon.Name, Does.EndWith("+1"));
            Assert.That(knight.Skill(Hero.AttackId).Power, Is.EqualTo(power + 2));
            a.Forge(0, ItemSlot.Weapon);
            a.Forge(0, ItemSlot.Weapon);
            Assert.That(knight.Weapon.Name, Does.EndWith("+3"));
            Assert.That(a.Forge(0, ItemSlot.Weapon), Is.Not.Null, "+3까지");
            Assert.That(TestData.Data.Items[knight.Weapon.Id].Plus, Is.Zero, "시트의 아이템은 그대로");
            Assert.That(a.Log.Last(), Is.EqualTo("forge 0 weapon"));
        }

        [Test]
        public void TheEngineerBargainsAtTheSmithy()
        {
            var plain = OnMap(1, new[] { "knight", "hunter", "cleric" });
            var engineer = OnMap(1, new[] { "knight", "hunter", "engineer" });
            var sword = plain.Heroes[0].Weapon;
            Assert.That(engineer.ForgePrice(sword), Is.LessThan(plain.ForgePrice(sword)));
        }

        [Test]
        public void InHardcoreAHeroWhoFallsWithoutHopeIsGoneAndANewOneJoins()
        {
            var a = OnMap(3, null, true);
            a.Hope = 0;
            var enemy = a.Map.Tiles.First(t => t.Content == Content.Enemy);
            a.RollMove();
            a.Teleport(0, enemy.Hex);
            Arrive(a, enemy.Hex);
            a.Heroes[0].Hp = 0;
            a.Join(new[] { 0 });
            Assert.That(a.Heroes[0].Dead, Is.True);
            a.Continue();
            ToTown(a);
            var recruits = a.Recruits();
            Assert.That(recruits.Select(c => c.Id), Does.Not.Contain("knight").And.Not.Contain("hunter"));
            Assert.That(a.Recruit("monk"), Is.Null);
            Assert.That(a.Heroes[0].Class.Id, Is.EqualTo("monk"));
            Assert.That(a.Heroes[0].Dead, Is.False);
            Assert.That(a.Log.Last(), Is.EqualTo("recruit monk"));
            Assert.That(a.Save(), Does.Contain("\nhardcore\n"));
            var fresh = Adventure.Load(TestData.Data, new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 3, null, Difficulty.Normal, true).Save(), out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(fresh.Hardcore, Is.True);
            Assert.That(a.ChapterStartSave(), Is.Null, "하드코어는 다시 하기가 없다");
        }

        [Test]
        public void OutsideHardcoreNobodyDies()
        {
            var a = OnMap(3);
            a.Hope = 0;
            var enemy = a.Map.Tiles.First(t => t.Content == Content.Enemy);
            a.RollMove();
            a.Teleport(0, enemy.Hex);
            Arrive(a, enemy.Hex);
            a.Heroes[0].Hp = 0;
            a.Join(new[] { 0 });
            Assert.That(a.Heroes[0].Dead, Is.False);
            Assert.That(a.Heroes[0].Wounds, Is.EqualTo(1));
        }

        [Test]
        public void EachChapterHasThreeMapsAndTheSeedPicksOne()
        {
            foreach (var chapter in TestData.Data.Chapters) Assert.That(chapter.Maps.Count, Is.EqualTo(3), chapter.Id);
            var seen = new System.Collections.Generic.HashSet<string>();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var a = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, seed);
                seen.Add(a.Map.Tile(Site.Town).Hex.ToString());
            }
            Assert.That(seen.Count, Is.EqualTo(3), "세 지도가 모두 나온다");
            var b = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 5);
            var c = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 5);
            Assert.That(b.Map.Def, Is.SameAs(c.Map.Def), "같은 시드는 같은 지도");
        }

        [Test]
        public void TheJournalKeepsAchievementsAndClassRecords()
        {
            var a = OnMap();
            int chapters = TestData.Data.Chapters.Count;
            for (int i = 0; i < chapters; i++)
            {
                if (a.Screen == AdventureScreen.Story) a.Continue();
                a.EndChapterForTest();
                a.Continue();
            }
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Victory));
            var journal = new Journal();
            journal.Record(a);
            Assert.That(journal.NewAchievements.Select(x => x.Id), Does.Contain("the_end").And.Contain("first_seal").And.Contain("untouched"));
            Assert.That(journal.WinsWith("knight"), Is.EqualTo(1));
            Assert.That(journal.RunsWith("cleric"), Is.EqualTo(1));
            var back = Journal.Parse(journal.ToText());
            Assert.That(back.Achieved, Is.EquivalentTo(journal.Achieved));
            Assert.That(back.WinsWith("hunter"), Is.EqualTo(1));
            journal.Record(a);
            Assert.That(journal.NewAchievements, Is.Empty, "업적은 한 번만");
        }

        [Test]
        public void ALostChapterCanBeTriedAgainFromItsStart()
        {
            var a = OnMap();
            for (int i = 0; i < 6; i++) a.Stay();
            Assert.That(a.ChapterStart, Is.Zero);
            var save = a.ChapterStartSave();
            var again = Adventure.Load(TestData.Data, save, out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(again.Screen, Is.EqualTo(AdventureScreen.Story), "장의 첫 이야기부터");
            Assert.That(again.Log.Count, Is.Zero);
            a.EndChapterForTest();
            Assert.That(a.Continue(), Is.Null);
            Assert.That(a.Chapter.Id, Is.EqualTo("chapter2"));
            Assert.That(a.ChapterStart, Is.EqualTo(a.Log.Count), "다음 장은 그 장의 이야기부터");
        }

        [Test]
        public void HeroesGrowPastLevelTen()
        {
            var hero = TestData.Hero("knight");
            hero.GainXp(Hero.LevelXp[Hero.MaxLevel - 1]);
            Assert.That(hero.Level, Is.EqualTo(Hero.MaxLevel));
            Assert.That(Hero.MaxLevel, Is.GreaterThan(10));
        }
    }
}
