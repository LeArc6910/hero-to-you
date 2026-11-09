using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class DiceTests
    {
        [TestCase(8, 0, 0, 3, 0)]
        [TestCase(8, 0, 3, 3, 8)]
        [TestCase(8, 0, 2, 3, 5)]
        [TestCase(8, 0, 1, 3, 3)]
        [TestCase(14, 50, 0, 3, 7)]
        [TestCase(14, 50, 3, 3, 14)]
        [TestCase(7, 25, 0, 2, 2)]
        [TestCase(10, 0, 0, 0, 10)]
        public void ValueScalesWithSuccessesAboveTheFloor(int power, int floor, int successes, int dice, int expected)
        {
            Assert.That(Dice.Value(power, floor, successes, dice), Is.EqualTo(expected));
        }

        [Test]
        public void ChancesStayBetweenTenAndNinetyFive()
        {
            Assert.That(Dice.ClampChance(-20), Is.EqualTo(10));
            Assert.That(Dice.ClampChance(70), Is.EqualTo(70));
            Assert.That(Dice.ClampChance(140), Is.EqualTo(95));
        }

        [Test]
        public void TheDistributionAddsUpAndCountsSureSuccesses()
        {
            var odds = new CheckOdds(3, 70, 1);
            Assert.That(odds.BySuccesses.Sum(), Is.EqualTo(1).Within(1e-9));
            Assert.That(odds.BySuccesses[0], Is.EqualTo(0));
            Assert.That(odds.Perfect, Is.EqualTo(0.49).Within(1e-9));
            Assert.That(odds.AtLeast(1), Is.EqualTo(1).Within(1e-9));
            Assert.That(new CheckOdds(3, 70, 0).None, Is.EqualTo(0.027).Within(1e-9));
        }

        [Test]
        public void TheSameSeedGivesTheSameRolls()
        {
            var a = new Fate(42);
            var b = new Fate(42);
            for (int i = 0; i < 100; i++) Assert.That(a.Roll(4, 60, 1).Faces, Is.EqualTo(b.Roll(4, 60, 1).Faces));
        }

        [Test]
        public void PeekingShowsWhatComesWithoutUsingIt()
        {
            var fate = new Fate(7);
            int[] ahead = Enumerable.Range(0, 5).Select(fate.Peek).ToArray();
            Assert.That(fate.Drawn, Is.EqualTo(0));
            int[] drawn = Enumerable.Range(0, 5).Select(_ => fate.Next()).ToArray();
            Assert.That(drawn, Is.EqualTo(ahead));
        }

        [Test]
        public void SureSuccessesDrawNothingAndCountAsHits()
        {
            var fate = new Fate(3);
            var check = fate.Roll(3, 10, 3);
            Assert.That(check.Successes, Is.EqualTo(3));
            Assert.That(check.Perfect, Is.True);
            Assert.That(check.RolledFailures, Is.Zero);
            Assert.That(fate.Drawn, Is.Zero);
        }

        [Test]
        public void ADieSucceedsBelowTheChance()
        {
            var fate = new Fate(11);
            int value = fate.Peek(0);
            var check = fate.Roll(1, value + 1, 0);
            Assert.That(check.Successes, Is.EqualTo(1));
            var fate2 = new Fate(11);
            Assert.That(fate2.Roll(1, value, 0).Successes, Is.Zero);
        }
    }
}

namespace Game.Core.Tests
{
    public class KoreanTests
    {
        [TestCase("기사", "기사가")]
        [TestCase("사냥꾼", "사냥꾼이")]
        [TestCase("고블린 A", "고블린 A가")]
        [TestCase("고블린 L", "고블린 L이")]
        public void TheSubjectParticleFollowsTheLastSound(string word, string expected)
        {
            Assert.That(Korean.Subject(word), Is.EqualTo(expected));
        }

        [Test]
        public void TheObjectAndTopicParticlesToo()
        {
            Assert.That(Korean.Object("원소술사"), Is.EqualTo("원소술사를"));
            Assert.That(Korean.Object("성직자"), Is.EqualTo("성직자를"));
            Assert.That(Korean.Object("광전사"), Is.EqualTo("광전사를"));
            Assert.That(Korean.Topic("도박사"), Is.EqualTo("도박사는"));
            Assert.That(Korean.Topic("사냥꾼"), Is.EqualTo("사냥꾼은"));
        }
    }
}
