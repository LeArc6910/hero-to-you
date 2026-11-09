using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class DataTests
    {
        [Test]
        public void TheSheetsLoad()
        {
            var data = TestData.Data;
            Assert.That(data.ClassList.Count, Is.GreaterThanOrEqualTo(12), "직업 12종이 목록에 있다 (Docs/Classes.md)");
            Assert.That(data.PlayableClasses.Count(), Is.EqualTo(12), "직업 12종 모두 고를 수 있다 (Docs/Classes.md)");
            Assert.That(data.Path.Count, Is.GreaterThan(5));
        }

        [Test]
        public void EveryPlayableClassHasItsOwnMechanicAndABasicAttack()
        {
            foreach (var cls in TestData.Data.PlayableClasses)
            {
                Assert.That(cls.Skills.Count, Is.GreaterThanOrEqualTo(2), cls.Id);
                Assert.That(cls.StartWeapon.Attack.Damages && cls.StartWeapon.Attack.Cooldown == 0, cls.Id);
                Assert.That(cls.Mechanic, Is.Not.Empty, cls.Id);
                Assert.That(cls.Upgrades.Count, Is.GreaterThanOrEqualTo(6), cls.Id);
                var hero = new Hero(cls);
                Assert.That(hero.Skills[0].Id, Is.EqualTo(Hero.AttackId), cls.Id);
            }
        }

        [Test]
        public void ABrokenSheetSaysWhereItIsBroken()
        {
            var error = Assert.Throws<FormatException>(() => GameData.Load(path =>
            {
                string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.DataFolder, path));
                return path == GameData.SkillsFile ? text.Replace("knight_bash,knight,방패 치기,str,2", "knight_bash,knight,방패 치기,xyz,2") : text;
            }));
            Assert.That(error.Message, Does.Contain("skills.csv"));
            Assert.That(error.Message, Does.Contain("xyz"));
        }
    }
}
