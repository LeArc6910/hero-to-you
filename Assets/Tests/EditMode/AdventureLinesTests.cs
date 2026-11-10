using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // The adventure's reports and refusals come from Data/Text/adventure.csv in the language the sheets were loaded in.
    public class AdventureLinesTests
    {
        static GameData English => _english ??= GameData.Load(path => File.ReadAllText(Path.Combine(TestData.DataFolder, path)), "en");
        static GameData _english;

        static bool HasHangul(string text) => text != null && text.Any(c => c >= 0xAC00 && c <= 0xD7A3);

        [Test]
        public void TheAdventureSpeaksTheSheetsLanguage()
        {
            var party = new[] { "knight", "hunter", "cleric" };
            var korean = new Adventure(TestData.Data, party, 7);
            var english = new Adventure(English, party, 7);
            Assert.That(korean.TimeName, Is.EqualTo("아침"));
            Assert.That(english.TimeName, Is.EqualTo("Morning"));
            Assert.That(korean.Move(new Hex(0, 0)), Is.EqualTo("지도 차례가 아닙니다."));
            Assert.That(english.Move(new Hex(0, 0)), Is.EqualTo("It is not a map turn."));
            Assert.That(korean.Continue(), Is.Null);
            Assert.That(english.Continue(), Is.Null);
            Assert.That(korean.Move(new Hex(0, 0)), Is.EqualTo("먼저 이동을 굴리세요."));
            Assert.That(english.Move(new Hex(0, 0)), Is.EqualTo("Roll for movement first."));
            Assert.That(english.Objectives.Select(o => o.text).Where(HasHangul), Is.Empty, "장의 목표 글과 덧붙인 말 모두 영어");
        }

        // The old Unlock without sheets still answers in Korean, the same words as the sheet's.
        [Test]
        public void JournalRefusalsComeFromTheSheets()
        {
            var journal = new Journal();
            var perk = TestData.Data.Perks.First(p => p.Pages > 0 && p.Requires.Length == 0);
            Assert.That(journal.Unlock(null, TestData.Data), Is.EqualTo(journal.Unlock(null)));
            Assert.That(journal.Unlock(perk, TestData.Data), Is.EqualTo(journal.Unlock(perk)));
            Assert.That(journal.Unlock(perk, English), Is.EqualTo($"Not enough pages (need {perk.Pages})."));
            var locked = TestData.Data.Perks.FirstOrDefault(p => p.Requires.Length > 0);
            journal.AddPages(1000);
            if (locked != null)
            {
                Assert.That(journal.Unlock(locked, TestData.Data), Is.EqualTo(journal.Unlock(locked)));
                Assert.That(HasHangul(journal.Unlock(locked, English)), Is.False);
            }
            Assert.That(journal.Unlock(perk, English), Is.Null);
            Assert.That(journal.Unlock(perk, TestData.Data), Is.EqualTo(journal.Unlock(perk)));
            Assert.That(journal.Unlock(perk, English), Is.EqualTo("That perk is already open."));
        }
    }
}
