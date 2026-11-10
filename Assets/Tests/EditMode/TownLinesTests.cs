using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // Texts made from the sheets while reading them (a weapon's attack, a perfect roll, an item's bonuses) and the
    // town's sentences come in the sheets' language (Data/Text/common.csv, town.csv).
    public class TownLinesTests
    {
        static GameData English => _english ??= GameData.Load(path => File.ReadAllText(Path.Combine(TestData.DataFolder, path)), "en");
        static GameData _english;

        [Test]
        public void ItemAndSkillTextsFollowTheLanguage()
        {
            Assert.That(TestData.Data.Items["sword_old"].Attack.Text, Is.EqualTo("낡은 장검의 기본 공격."));
            Assert.That(English.Items["sword_old"].Attack.Text, Is.EqualTo("Basic attack with the Old Longsword."));
            Assert.That(TestData.Data.Items["sword_old"].Attack.PerfectText, Is.EqualTo("완벽: 1.5배"));
            Assert.That(English.Items["sword_old"].Attack.PerfectText, Is.EqualTo("Perfect: 1.5× damage"));
            Assert.That(TestData.Data.Items["armor_chain"].Bonuses, Is.EqualTo("방어도 +2 · 민첩 -5"));
            Assert.That(English.Items["armor_chain"].Bonuses, Is.EqualTo("Armor +2 · Agility -5"));
            Assert.That(English.Items["armor_chain"].Clone().Bonuses, Is.EqualTo("Armor +2 · Agility -5"), "벼린 사본도 같은 언어");
            foreach (var skill in English.Skills.Values.Where(s => s.Perfect.Length > 0))
                Assert.That(skill.PerfectText, Does.StartWith("Perfect: "), skill.Id);
        }

        [Test]
        public void TownRefusalsFollowTheLanguage()
        {
            var adventure = new Adventure(English, new[] { "knight", "hunter", "cleric" }, 1);
            Assert.That(adventure.Equip(99, 0), Is.EqualTo("You can't change gear right now.").Or.EqualTo("There is no such item in the bag."));
            Assert.That(adventure.UseItem(0, "no_such_item"), Is.EqualTo("You can't use that right now.").Or.EqualTo("You don't have that item."));
        }
    }
}
