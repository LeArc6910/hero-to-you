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
            Assert.That(data.PlayableClasses.Select(c => c.Id), Is.EquivalentTo(new[] { "knight", "berserker", "hunter", "elementalist", "cleric", "gambler" }));
            Assert.That(data.Path.Count, Is.GreaterThan(5));
        }

        [Test]
        public void EveryPlayableClassHasItsOwnMechanicAndABasicAttack()
        {
            foreach (var cls in TestData.Data.PlayableClasses)
            {
                Assert.That(cls.Skills.Count, Is.GreaterThanOrEqualTo(3), cls.Id);
                Assert.That(cls.Skills[0].Damages && cls.Skills[0].Cooldown == 0, cls.Id);
                Assert.That(cls.Mechanic, Is.Not.Empty, cls.Id);
            }
        }

        [Test]
        public void ABrokenSheetSaysWhereItIsBroken()
        {
            var error = Assert.Throws<FormatException>(() => GameData.Load(path =>
            {
                string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.DataFolder, path));
                return path == GameData.SkillsFile ? text.Replace("knight_slash,knight,베기,str,3", "knight_slash,knight,베기,xyz,3") : text;
            }));
            Assert.That(error.Message, Does.Contain("skills.csv"));
            Assert.That(error.Message, Does.Contain("xyz"));
        }
    }
}
