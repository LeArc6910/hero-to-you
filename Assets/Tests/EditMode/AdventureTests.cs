using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class AdventureTests
    {
        // Plays a whole run with the battle bot and the first choice of every event (tried by its first hero).
        static Adventure Play(ulong seed, params string[] classes)
        {
            var adventure = new Adventure(TestData.Data, classes, seed);
            var random = new GameRandom(seed, 77);
            for (int guard = 0; guard < 2000; guard++)
            {
                switch (adventure.Screen)
                {
                    case AdventureScreen.Victory:
                    case AdventureScreen.Defeat:
                        return adventure;
                    case AdventureScreen.Battle:
                        Assert.That(adventure.Fight(BattleBot.Choose(adventure.Battle, random)), Is.Null);
                        break;
                    case AdventureScreen.Event:
                        var choice = adventure.Choices().First(c => adventure.Affordable(c));
                        int hero = adventure.Heroes.IndexOf(adventure.CanAttempt(choice).First());
                        Assert.That(adventure.Choose(choice.Index, hero), Is.Null);
                        break;
                    default:
                        adventure.Continue();
                        break;
                }
            }
            Assert.Fail("The run did not end.");
            return adventure;
        }

        [Test]
        public void ARunEndsAndReplaysTheSame()
        {
            var a = Play(12, "knight", "hunter", "cleric");
            var b = Play(12, "knight", "hunter", "cleric");
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Victory).Or.EqualTo(AdventureScreen.Defeat));
            Assert.That(b.Screen, Is.EqualTo(a.Screen));
            Assert.That(b.Gold, Is.EqualTo(a.Gold));
            Assert.That(b.Heroes.Select(h => h.Hp), Is.EqualTo(a.Heroes.Select(h => h.Hp)));
        }

        [Test]
        public void TheStoryStartsIt()
        {
            var adventure = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 1);
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Story));
            Assert.That(adventure.Story.Id, Is.EqualTo("prologue"));
            adventure.Continue();
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Battle));
            Assert.That(adventure.Battle.Monsters.Count(), Is.EqualTo(3));
        }

        [Test]
        public void AClassChoiceShowsOnlyWithThatClass()
        {
            var withHunter = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 1);
            var withoutHunter = new Adventure(TestData.Data, new[] { "knight", "berserker", "cleric" }, 1);
            var chest = TestData.Data.Events.First(e => e.Id == "chest");
            SetEvent(withHunter, chest);
            SetEvent(withoutHunter, chest);
            Assert.That(withHunter.Choices().Any(c => c.ClassId == "hunter"), Is.True);
            Assert.That(withoutHunter.Choices().Any(c => c.ClassId == "hunter"), Is.False);
            var disarm = withHunter.Choices().First(c => c.ClassId == "hunter");
            Assert.That(withHunter.CanAttempt(disarm).Select(h => h.Class.Id), Is.EqualTo(new[] { "hunter" }));
        }

        [Test]
        public void AChoiceThatCostsGoldNeedsTheGold()
        {
            var adventure = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 1);
            SetEvent(adventure, TestData.Data.Events.First(e => e.Id == "peddler"));
            var buy = adventure.Choices().First(c => Adventure.Cost(c) == 10);
            adventure.Gold = 5;
            Assert.That(adventure.Choose(buy.Index, 0), Does.Contain("금화"));
            adventure.Gold = 10;
            Assert.That(adventure.Choose(buy.Index, 0), Is.Null);
            Assert.That(adventure.Gold, Is.Zero);
            Assert.That(adventure.Heroes[0].Potions, Is.EqualTo(2));
        }

        [Test]
        public void AFailedShortcutStartsAnAmbush()
        {
            for (ulong seed = 1; seed < 200; seed++)
            {
                var adventure = new Adventure(TestData.Data, new[] { "knight", "berserker", "cleric" }, seed);
                SetEvent(adventure, TestData.Data.Events.First(e => e.Id == "crossroads"));
                var shortcut = adventure.Choices().First(c => c.HasCheck);
                Assert.That(adventure.Choose(shortcut.Index, 0), Is.Null);
                if (adventure.Result.Success) continue;
                adventure.Continue();
                Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Battle));
                Assert.That(adventure.Encounter.Id, Is.EqualTo("ambush"));
                return;
            }
            Assert.Fail("No seed failed the check.");
        }

        [Test]
        public void RestingRaisesTheFallenAndRefillsResolve()
        {
            var adventure = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 1);
            adventure.Heroes[0].Hp = 0;
            adventure.Heroes[1].Resolve = 0;
            int rest = TestData.Data.Path.ToList().FindIndex(n => n.Kind == PathKind.Rest);
            adventure.JumpTo(rest);
            Assert.That(adventure.Screen, Is.EqualTo(AdventureScreen.Rest));
            Assert.That(adventure.Heroes[0].Down, Is.False);
            Assert.That(adventure.Heroes[1].Resolve, Is.EqualTo(adventure.Heroes[1].MaxResolve));
        }

        static void SetEvent(Adventure adventure, EventCard card)
        {
            int step = TestData.Data.Path.ToList().FindIndex(n => n.Kind == PathKind.Event && n.Arg == card.Pool);
            for (int i = 0; i < 50; i++)
            {
                adventure.JumpTo(step);
                if (adventure.Event == card) return;
            }
            Assert.Fail($"Could not reach the event {card.Id}.");
        }
    }
}
