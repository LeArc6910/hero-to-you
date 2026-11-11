using NUnit.Framework;

namespace Game.Core.Tests
{
    // The whole state of a run in one number (Adventure.StateHash): a save loads back exactly as it was, at many points
    // of a whole campaign played by the bot (Docs/Saving.md). A co-op game will compare the same number between players.
    public class StateHashTests
    {
        [Test]
        public void ASaveLoadsBackToTheSameStateThroughoutARun()
        {
            foreach (ulong seed in new ulong[] { 3, 17, 29, 41 })
            {
                var a = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, seed);
                var bot = new GameRandom(seed, 5);
                int checks = 0;
                for (int step = 0; step < 4000 && !a.Over; step++)
                {
                    Assert.That(AdventureBot.Step(a, bot, true, AdventureBot.HumanMistakes), Is.Null);
                    if (step % 23 != 0 && !a.Over) continue;
                    var loaded = Adventure.Load(TestData.Data, a.Save(), out string error, out _);
                    Assert.That(error, Is.Null, $"seed {seed}, step {step}");
                    Assert.That(loaded.StateHash(), Is.EqualTo(a.StateHash()), $"seed {seed}, step {step}, screen {a.Screen}");
                    checks++;
                }
                Assert.That(checks, Is.GreaterThan(10));
            }
        }

        [Test]
        public void TheHashChangesWithTheState()
        {
            var a = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 9);
            ulong before = a.StateHash();
            Assert.That(a.StateHash(), Is.EqualTo(before), "같은 상태는 같은 값");
            Assert.That(a.Continue(), Is.Null);
            Assert.That(a.StateHash(), Is.Not.EqualTo(before));
            var other = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 10);
            Assert.That(other.StateHash(), Is.Not.EqualTo(before), "다른 시드는 다른 값");
        }
    }
}
