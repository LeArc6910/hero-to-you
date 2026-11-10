using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // The sentences Core writes come from Data/Text/*.csv in either language (Common/Lines.cs, Docs/Localization.md).
    public class LinesTests
    {
        static GameData English => _english ??= GameData.Load(path => File.ReadAllText(Path.Combine(TestData.DataFolder, path)), "en");
        static GameData _english;

        [Test]
        public void TemplatesTakeValuesAndKoreanParticles()
        {
            Assert.That(Lines.Format("{0:이} {1:을} 지킨다", true, "기사", "사냥꾼"), Is.EqualTo("기사가 사냥꾼을 지킨다"));
            Assert.That(Lines.Format("{0:은} {1:과} {2:으로}", true, "검", "방패", "길"), Is.EqualTo("검은 방패와 길로"));
            Assert.That(Lines.Format("{0:이} {1}", false, "Knight", 3), Is.EqualTo("Knight 3"));
            Assert.That(TestData.Data.Line("common.example", "기사", "사냥꾼"), Is.EqualTo("기사가 사냥꾼을 지킨다"));
            Assert.That(English.Line("common.example", "Knight", "Hunter"), Is.EqualTo("Knight guards Hunter"));
            Assert.That(TestData.Data.Line("no.such.line"), Is.EqualTo("no.such.line"), "없는 id는 그대로 보인다");
        }

        // Every sentence id the Core code names is in the sheets, in Korean and English, with the same values.
        [Test]
        public void EverySentenceTheCodeUsesIsInTheSheetsInBothLanguages()
        {
            string core = Path.GetFullPath(Path.Combine(TestData.DataFolder, "..", "..", "Scripts", "Core"));
            Assert.That(Directory.Exists(core), Is.True, core);
            var used = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\.Line\(\s*""([A-Za-z0-9_.]+)""").Cast<Match>().Select(m => m.Groups[1].Value))
                .Distinct().ToList();
            var ko = TestData.Data.Lines;
            var en = English.Lines;
            foreach (string id in used)
            {
                Assert.That(ko.Has(id), Is.True, $"시트에 없는 문장 id: {id}");
                Assert.That(Values(en.Template(id)), Is.EquivalentTo(Values(ko.Template(id))), $"{id}: 한국어와 영어의 값 자리가 다르다");
            }
            foreach (string id in ko.Ids)
                Assert.That(en.Template(id).Any(c => c >= 0xAC00 && c <= 0xD7A3), Is.False, $"{id}: 영어 칸이 비었거나 한글이 섞였다");
        }

        static string[] Values(string template) =>
            Regex.Matches(template, @"\{(\d+)(:[^}]*)?\}").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(v => v).ToArray();
    }
}
