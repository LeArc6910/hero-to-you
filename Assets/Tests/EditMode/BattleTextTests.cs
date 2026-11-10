using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // The battles some tests make without the sheets write their log from the Korean sheets (Battle.TextSheets), so
    // those tests can read it.
    [SetUpFixture]
    public class BattleTextSetUp
    {
        [OneTimeSetUp]
        public void WriteBattlesWithoutSheetsFromTheSheets() => Battle.TextSheets = TestData.Data;

        [OneTimeTearDown]
        public void Forget() => Battle.TextSheets = null;
    }

    // The battle log and the battle's refusals come from Data/Text/battle.csv in the sheets' language (Docs/Localization.md).
    public class BattleTextTests
    {
        static GameData English => _english ??= GameData.Load(path => File.ReadAllText(Path.Combine(TestData.DataFolder, path)), "en");
        static GameData _english;

        static bool Hangul(string text) => text != null && text.Any(c => c >= 0xAC00 && c <= 0xD7A3);

        // The knight guards the hunter; also what the hunter is told when it tries to act out of turn.
        static (string guard, string notYourTurn) KnightGuardsHunter(GameData data)
        {
            var heroes = new List<Hero> { new Hero(data.Classes["knight"]), new Hero(data.Classes["hunter"]) };
            var battle = new Battle(heroes, new List<MonsterDef> { TestData.Dummy() }, 3, false, data);
            var knight = battle.Heroes.First(h => h.Hero.Class.Id == "knight");
            var hunter = battle.Heroes.First(h => h.Hero.Class.Id == "hunter");
            var dummy = battle.Monsters.First();
            for (int i = 0; i < 10 && battle.Current != knight; i++)
                Assert.That(battle.Do(new BattleCommand(battle.Current.Id, Hero.AttackId, dummy.Id)), Is.Null);
            Assert.That(battle.Current, Is.SameAs(knight));
            string error = battle.Preview(new BattleCommand(hunter.Id, Hero.AttackId, dummy.Id)).Error;
            Assert.That(battle.Do(new BattleCommand(knight.Id, "knight_guard", hunter.Id)), Is.Null);
            return (battle.Events.Last(e => e.Kind == BattleEventKind.Guard).Text, error);
        }

        [Test]
        public void TheLogIsWrittenInTheSheetsLanguage()
        {
            var ko = KnightGuardsHunter(TestData.Data);
            Assert.That(ko.guard, Is.EqualTo($"기사가 사냥꾼을 지킨다 (방어도 +{Battle.GuardArmor})"));
            Assert.That(ko.notYourTurn, Is.EqualTo("지금은 이 영웅의 차례가 아닙니다."));
            var en = KnightGuardsHunter(English);
            Assert.That(en.guard, Is.EqualTo($"Knight guards Hunter (armor +{Battle.GuardArmor})"));
            Assert.That(en.notYourTurn, Is.EqualTo("It is not this hero's turn."));
        }

        // Every class against every encounter in English: once the names from the sheets are taken out (a row may
        // leave its English cell empty), no Korean is left in the log, the refusals or the reasons for sure successes.
        [Test]
        public void AnEnglishBattleWritesNoKoreanOfItsOwn()
        {
            var data = English;
            var names = data.Classes.Values.Select(c => c.Name)
                .Concat(data.Skills.Values.Select(s => s.Name))
                .Concat(data.MonsterSkills.Values.SelectMany(s => new[] { s.Name, s.Text }))
                .Concat(data.Monsters.Values.SelectMany(m => new[] { m.Name, m.Phase2Line }))
                .Concat(data.Items.Values.Select(i => i.Name))
                .Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderByDescending(n => n.Length).ToList();
            string Own(string text)
            {
                foreach (string name in names) text = text.Replace(name, "");
                return text;
            }

            var classes = data.PlayableClasses.Select(c => c.Id).ToList();
            var consumables = data.ItemList.Where(i => i.Consumable).Select(i => i.Id).ToList();
            int run = 0;
            foreach (var encounter in data.Encounters.Values.OrderBy(e => e.Id))
            {
                run++;
                var heroes = Enumerable.Range(0, 3).Select(i => new Hero(data.Classes[classes[(run * 3 + i * 5) % classes.Count]])).ToList();
                foreach (var hero in heroes)
                    foreach (string item in consumables) hero.AddConsumable(item, 1);
                var battle = new Battle(heroes, encounter.Monsters, (ulong)run, run % 3 == 0, data);
                var random = new GameRandom((ulong)run, 3);
                for (int turn = 0; turn < 200 && !battle.Over; turn++)
                {
                    var actor = battle.Current;
                    var tries = actor.Hero.Skills.Select(s => s.Id).Concat(consumables.Select(BattleCommand.Use)).Append(BattleCommand.Flee);
                    foreach (string skill in tries)
                        foreach (var target in battle.Units)
                        {
                            var preview = battle.Preview(new BattleCommand(actor.Id, skill, target.Id, turn % 2 == 0 ? 0 : 9));
                            Assert.That(Hangul(Own(preview.Error ?? "")), Is.False, preview.Error);
                            foreach (string reason in preview.Reasons) Assert.That(Hangul(Own(reason)), Is.False, reason);
                        }
                    Assert.That(battle.Do(BattleBot.Choose(battle, random, turn % 2 == 0)), Is.Null);
                }
                foreach (var e in battle.Events) Assert.That(Hangul(Own(e.Text ?? "")), Is.False, e.Text);
            }
        }
    }
}
