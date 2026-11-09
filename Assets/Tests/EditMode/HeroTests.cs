using NUnit.Framework;

namespace Game.Core.Tests
{
    public class HeroTests
    {
        [Test]
        public void FiveFailuresBecomeOneResolve()
        {
            var hero = TestData.Hero("knight");
            hero.Resolve = 1;
            Assert.That(hero.AddGrit(4), Is.Zero);
            Assert.That(hero.Grit, Is.EqualTo(4));
            Assert.That(hero.AddGrit(2), Is.EqualTo(1));
            Assert.That(hero.Resolve, Is.EqualTo(2));
            Assert.That(hero.Grit, Is.EqualTo(0));
        }

        [Test]
        public void GritWaitsFullWhileResolveIsFull()
        {
            var hero = TestData.Hero("knight");
            Assert.That(hero.Resolve, Is.EqualTo(hero.MaxResolve));
            Assert.That(hero.AddGrit(9), Is.Zero);
            Assert.That(hero.Grit, Is.EqualTo(hero.GritCap));
            hero.Resolve--;
            Assert.That(hero.AddGrit(0), Is.EqualTo(1));
        }

        [Test]
        public void TheGamblersGritFillsAtThree()
        {
            var hero = TestData.Hero("gambler");
            hero.Resolve = 0;
            Assert.That(hero.AddGrit(3), Is.EqualTo(1));
        }

        [Test]
        public void LevelsRaiseHealthAndTheTwoMainAbilities()
        {
            var hero = TestData.Hero("knight");
            int str = hero.Stat(Stat.Strength);
            int wil = hero.Stat(Stat.Will);
            int agi = hero.Stat(Stat.Agility);
            var levels = hero.GainXp(25);
            Assert.That(levels, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(hero.MaxHp, Is.EqualTo(42 + 8));
            Assert.That(hero.Stat(Stat.Strength), Is.EqualTo(str + 6));
            Assert.That(hero.Stat(Stat.Will), Is.EqualTo(wil + 6));
            Assert.That(hero.Stat(Stat.Agility), Is.EqualTo(agi));
        }
    }
}
