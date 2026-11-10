using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // Added on 2026-11-10: class pairings, the new journal perks, short runs, boss rules, gear effects, the casters'
    // new skills and the people who come back (Docs/Classes.md, Docs/Journal.md, Docs/Map.md, Docs/Combat.md,
    // Docs/Narrative.md).
    public class RunShapeTests
    {
        static Adventure Start(string[] classes, IReadOnlyList<string> perks = null, int first = 0, int last = -1, ulong seed = 1) =>
            new Adventure(TestData.Data, classes, seed, perks, Difficulty.Normal, false, first, last);

        static void Act(Battle battle, string skill, int target = -1)
        {
            var actor = battle.Current;
            if (target < 0) target = battle.Monsters.First(m => !m.Down).Id;
            Assert.That(battle.Do(new BattleCommand(actor.Id, skill, target)), Is.Null);
        }

        static MonsterDef Striker(string skill) => new MonsterDef
        {
            Id = "striker",
            Name = "시험꾼",
            Hp = 500,
            Speed = 60,
            Accuracy = 95,
            Pattern = new List<MonsterSkillDef> { TestData.Data.MonsterSkills[skill] },
            Color = "#FFFFFF",
            Size = 1,
        };

        [Test]
        public void APartyWithNeitherFrontNorHealerIsMarkedAsAChallenge()
        {
            var data = TestData.Data;
            Assert.That(PartyShape.Fragile(PartyShape.Of(data, new[] { "seer", "elementalist", "bard" })), Is.True);
            Assert.That(PartyShape.Fragile(PartyShape.Of(data, new[] { "knight", "hunter", "cleric" })), Is.False);
            // One of the two is enough to lose the mark: a healer in the back row, or a front-liner who cannot heal.
            Assert.That(PartyShape.Fragile(PartyShape.Of(data, new[] { "seer", "elementalist", "cleric" })), Is.False);
            Assert.That(PartyShape.HasHealer(PartyShape.Of(data, new[] { "knight", "seer", "elementalist" })), Is.False);
            Assert.That(PartyShape.HasFront(PartyShape.Of(data, new[] { "knight", "seer", "elementalist" })), Is.True);
            Assert.That(PartyShape.IsHealer(data.Classes["alchemist"]) && PartyShape.IsHealer(data.Classes["mnemonist"]), Is.True);
            Assert.That(Start(new[] { "seer", "elementalist", "bard" }).FragileParty, Is.True);
            Assert.That(Start(new[] { "knight", "hunter", "cleric" }).FragileParty, Is.False);
            Assert.That(data.Achievements.Any(a => a.Rule == "fragile_win"), Is.True, "the challenge has an achievement");
        }

        [Test]
        public void PairedClassesBringTheirGift()
        {
            var plain = Start(new[] { "knight", "rogue", "bard" });
            Assert.That(plain.Combos, Is.Empty);
            var paired = Start(new[] { "knight", "hunter", "cleric" });
            Assert.That(paired.Combos.Select(c => c.Id), Is.EquivalentTo(new[] { "oath_and_bow", "oath_and_prayer", "old_road" }));
            Assert.That(paired.Hope, Is.EqualTo(plain.Hope + 1), "옛 길의 셋: 희망 +1");
            Assert.That(paired.Heroes.All(h => h.Blessing == 1), Is.True, "맹세와 기도: 축복");
            int known = paired.Map.Tiles.Count(paired.Knows);
            var unpaired = Start(new[] { "knight", "rogue", "cleric" });
            Assert.That(known, Is.GreaterThan(unpaired.Map.Tiles.Count(unpaired.Knows)), "방패 뒤의 활: 지도가 밝다");
        }

        [Test]
        public void NewPerksForgeBlessRevealAndBringRenown()
        {
            var party = new[] { "knight", "rogue", "bard" };
            var plain = Start(party);
            var gifted = Start(party, new[] { "fine_edge", "blessed_start", "known_names", "map_page" });
            Assert.That(gifted.Heroes.All(h => h.Weapon.Plus == 1), Is.True);
            Assert.That(gifted.Heroes[0].Weapon.Attack.Power, Is.EqualTo(plain.Heroes[0].Weapon.Attack.Power + 2));
            Assert.That(gifted.Heroes.All(h => h.Blessing == 1), Is.True);
            Assert.That(gifted.Renown, Is.EqualTo(plain.Renown + 2));
            Assert.That(gifted.Map.Tiles.Count(gifted.Knows), Is.EqualTo(plain.Map.Tiles.Count(plain.Knows) + 6));
            var back = Adventure.Load(TestData.Data, gifted.Save(), out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(back.Heroes[0].Weapon.Name, Is.EqualTo(gifted.Heroes[0].Weapon.Name));
        }

        [Test]
        public void SomePerksWaitForAnAchievement()
        {
            var purse = TestData.Data.Perks.Single(p => p.Id == "deep_purse");
            Assert.That(purse.Requires, Is.EqualTo("for_hire"));
            var journal = Journal.Parse(Journal.Header + "\npages 30\n");
            Assert.That(journal.CanUnlock(purse), Is.False);
            Assert.That(journal.Unlock(purse), Is.Not.Null);
            journal = Journal.Parse(Journal.Header + "\npages 30\nachieved for_hire\n");
            Assert.That(journal.CanUnlock(purse), Is.True);
            Assert.That(journal.Unlock(purse), Is.Null);
        }

        [Test]
        public void AShortRunStartsLaterStrongerAndEndsAtItsLastChapter()
        {
            var a = Start(new[] { "knight", "rogue", "bard" }, null, 2, 2);
            Assert.That(a.Chapter.Index, Is.EqualTo(2));
            Assert.That(a.FullRun, Is.False);
            Assert.That(a.Heroes.All(h => h.Level == Adventure.StartLevels[2]), Is.True);
            Assert.That(a.Gold, Is.GreaterThanOrEqualTo(Adventure.LateGold * 2));
            for (int i = 0; i < 40 && a.LevelUp != null; i++) Assert.That(a.ChooseUpgrade(0), Is.Null);
            var back = Adventure.Load(TestData.Data, a.Save(), out string error, out _);
            Assert.That(error, Is.Null);
            Assert.That(back.FirstChapter, Is.EqualTo(2));
            Assert.That(back.LastChapter, Is.EqualTo(2));
            Assert.That(back.Heroes[0].Level, Is.EqualTo(a.Heroes[0].Level));
            if (a.Screen == AdventureScreen.Story) a.Continue();
            a.EndChapterForTest();
            a.Continue();
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Victory), "마지막 장에서 끝난다");
            var journal = new Journal();
            journal.Record(a);
            Assert.That(journal.Reached, Is.EqualTo(3));
            Assert.That(journal.Victories, Is.Zero, "짧은 판은 캠페인 승리가 아니다");
            Assert.That(Journal.Parse(journal.ToText()).Reached, Is.EqualTo(3));
        }

        [Test]
        public void SernSealsTheBestSkillAndTheNamelessErasesThemAll()
        {
            for (ulong seed = 1; seed < 30; seed++)
            {
                var battle = new Battle(TestData.Party("knight"), new List<MonsterDef> { Striker("sern_seal") }, seed, false, TestData.Data);
                var knight = battle.Heroes.Single();
                if (!battle.Events.Any(e => e.Text != null && e.Text.Contains("빼앗았다"))) continue;
                // Each blow that lands takes one skill: the strongest still free.
                int seals = battle.Events.Count(e => e.Text != null && e.Text.Contains("빼앗았다"));
                var sealedSkills = knight.Hero.Skills.Where(s => s.Id != Hero.AttackId && battle.Cooldown(knight, s) > 0).ToList();
                Assert.That(sealedSkills.Count, Is.EqualTo(System.Math.Min(seals, knight.Hero.Skills.Count - 1)));
                Assert.That(sealedSkills.Max(s => battle.Cooldown(knight, s)), Is.GreaterThanOrEqualTo(Battle.SealTurns));
                break;
            }
            for (ulong seed = 1; seed < 30; seed++)
            {
                var battle = new Battle(TestData.Party("knight"), new List<MonsterDef> { Striker("nameless_erase") }, seed, false, TestData.Data);
                var knight = battle.Heroes.Single();
                if (!battle.Events.Any(e => e.Text != null && e.Text.Contains("흐려졌다"))) continue;
                Assert.That(knight.Hero.Skills.Where(s => s.Id != Hero.AttackId).All(s => battle.Cooldown(knight, s) > 0), Is.True, "기본 공격만");
                Assert.That(battle.Preview(new BattleCommand(knight.Id, Hero.AttackId, battle.Monsters.First().Id)).Ok, Is.True);
                return;
            }
            Assert.Fail("지우기가 한 번도 맞지 않았다");
        }

        [Test]
        public void TheWardensBrandBurnsResolveAndTheBellDazes()
        {
            for (ulong seed = 1; seed < 30; seed++)
            {
                var knight = TestData.Hero("knight");
                int resolve = knight.Resolve;
                var battle = new Battle(new List<Hero> { knight }, new List<MonsterDef> { Striker("warden_brand") }, seed, false, TestData.Data);
                if (!battle.Events.Any(e => e.Text != null && e.Text.Contains("낙인"))) continue;
                Assert.That(knight.Resolve, Is.LessThan(resolve));
                Assert.That(battle.Heroes.Single().Has(StatusKind.Burn), Is.True);
                break;
            }
            for (ulong seed = 1; seed < 30; seed++)
            {
                var battle = new Battle(TestData.Party("knight"), new List<MonsterDef> { Striker("bell_daze") }, seed, false, TestData.Data);
                var knight = battle.Heroes.Single();
                if (!knight.Has(StatusKind.Daze)) continue;
                var attack = knight.Hero.Skill(Hero.AttackId);
                Assert.That(battle.DiceOf(knight, attack), Is.EqualTo(attack.Dice - 1), "멍함: 주사위 하나 덜");
                // The daze goes with the hero's next action.
                Act(battle, Hero.AttackId);
                Assert.That(knight.Has(StatusKind.Daze) && battle.Current != knight, Is.False);
                return;
            }
            Assert.Fail("종이 한 번도 맞지 않았다");
        }

        [Test]
        public void ABossTakenSkillShowsWhoTookIt()
        {
            for (ulong seed = 1; seed < 30; seed++)
            {
                var battle = new Battle(TestData.Party("knight"), new List<MonsterDef> { Striker("sern_seal") }, seed, false, TestData.Data);
                var knight = battle.Heroes.Single();
                var taken = knight.Hero.Skills.FirstOrDefault(s => battle.BlockedBy(knight, s) == "seal");
                if (taken == null) continue;
                Assert.That(battle.Cooldown(knight, taken), Is.GreaterThan(0));
                Assert.That(battle.BlockedBy(knight, knight.Hero.Skill(Hero.AttackId)), Is.Null);
                return;
            }
            Assert.Fail("봉인이 한 번도 맞지 않았다");
        }

        [Test]
        public void TheJournalKeepsACodexAndTheNewAchievements()
        {
            var a = Start(new[] { "knight", "hunter", "cleric" }, null, 0, 0, 3);
            var bot = new GameRandom(3, 9);
            for (int i = 0; i < 400 && !a.Over && a.BattlesWon < 2; i++) Assert.That(AdventureBot.Step(a, bot), Is.Null);
            bool ended = false;
            for (int guard = 0; guard < 20 && !a.Over; guard++)
            {
                for (int i = 0; i < 40 && a.LevelUp != null; i++) a.ChooseUpgrade(0);
                if (a.Screen == AdventureScreen.Story && ended) a.Continue();
                else if (a.Screen == AdventureScreen.Story && a.Story.Id == a.Chapter.Prologue) a.Continue();
                else
                {
                    a.EndChapterForTest();
                    ended = true;
                }
            }
            Assert.That(a.Screen, Is.EqualTo(AdventureScreen.Victory));
            var journal = new Journal();
            journal.Record(a);
            Assert.That(journal.Codex("monsters"), Is.Not.Empty, "싸운 몬스터");
            Assert.That(journal.Codex("items"), Is.Not.Empty, "든 장비");
            Assert.That(journal.Codex("combos").Count, Is.EqualTo(3));
            Assert.That(journal.Codex("stories"), Does.Contain(TestData.Data.Chapters[0].Prologue));
            Assert.That(journal.EndingsSeen(TestData.Data), Is.EqualTo(1));
            Assert.That(journal.NewAchievements.Select(x => x.Id), Does.Contain("short_road").And.Contain("three_bonds"));
            var back = Journal.Parse(journal.ToText());
            Assert.That(back.Codex("monsters"), Is.EquivalentTo(journal.Codex("monsters")));
            Assert.That(back.Codex("stories"), Is.EquivalentTo(journal.Codex("stories")));
        }

        [Test]
        public void ThornsHurtTheAttackerAndRegenHealsEachTurn()
        {
            var knight = TestData.Hero("knight");
            knight.Equip(TestData.Data.Items["mail_thorn"].Clone());
            knight.Equip(TestData.Data.Items["charm_spring"].Clone());
            for (ulong seed = 1; seed < 30; seed++)
            {
                knight.Heal(999);
                var battle = new Battle(new List<Hero> { knight }, new List<MonsterDef> { Striker("sern_blade") }, seed, false, TestData.Data);
                var monster = battle.Monsters.Single();
                if (monster.Hp == monster.MaxHp) continue;
                Assert.That((monster.MaxHp - monster.Hp) % 3, Is.Zero, "맞을 때마다 가시 3");
                var hero = battle.Heroes.Single();
                int before = hero.Hp;
                Act(battle, Hero.AttackId);
                Assert.That(battle.Events.Any(e => e.Text != null && e.Text.Contains("되살아나는 힘")) || before == hero.MaxHp, Is.True);
                return;
            }
            Assert.Fail("한 번도 맞지 않았다");
        }

        [Test]
        public void TheMistHealsEveryoneAndTheBoneWallDrawsTheBlows()
        {
            var battle = new Battle(TestData.Party("alchemist", "knight", "necromancer"), new List<MonsterDef> { TestData.Dummy(), TestData.Dummy() }, 3, false, TestData.Data);
            for (int i = 0; i < 10 && battle.Current.Hero?.Class.Id != "alchemist"; i++) Act(battle, Hero.AttackId);
            var alchemist = battle.Current;
            Assert.That(alchemist.Hero.Class.Id, Is.EqualTo("alchemist"));
            foreach (var h in battle.Heroes) h.Hp = h.MaxHp / 2;
            alchemist.Hero.Blessing = 2;
            Act(battle, "alch_mist", alchemist.Id);
            Assert.That(battle.Heroes.All(h => h.Hp > h.MaxHp / 2), Is.True, "모두 회복");

            for (int i = 0; i < 10 && battle.Current.Hero?.Class.Id != "necromancer"; i++) Act(battle, Hero.AttackId);
            var necro = battle.Current;
            Assert.That(necro.Hero.Class.Id, Is.EqualTo("necromancer"));
            var skeleton = battle.Summons.Single(s => s.Owner == necro.Id);
            Act(battle, "necro_wall", necro.Id);
            Assert.That(skeleton.Has(StatusKind.Taunt), Is.True);
            Assert.That(skeleton.Armor, Is.GreaterThanOrEqualTo(Battle.BoneWallArmor));
            skeleton.Hp = 0;
            Assert.That(battle.Preview(new BattleCommand(necro.Id, "necro_wall", necro.Id)).Ok, Is.False);
        }

        [Test]
        public void TheSheetsReadInEnglishAndASaveCarriesOver()
        {
            // The real sheets, with an English name for the knight added here when the sheet has none yet.
            string Read(string path)
            {
                string text = System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.DataFolder, path));
                if (path != "Classes/classes.csv" || text.Split('\n')[0].Contains("name_en")) return text;
                var lines = text.TrimEnd('\n').Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                lines[0] += ",name_en";
                for (int i = 1; i < lines.Count; i++) lines[i] += lines[i].StartsWith("knight,") ? ",Knight" : ",";
                return string.Join("\n", lines) + "\n";
            }
            var ko = GameData.Load(Read);
            var en = GameData.Load(Read, "en");
            Assert.That(en.Fingerprint, Is.EqualTo(ko.Fingerprint), "언어는 저장의 시트 지문을 바꾸지 않는다");
            Assert.That(ko.Classes["knight"].Name, Is.EqualTo("기사"));
            Assert.That(en.Classes["knight"].Name, Is.EqualTo("Knight"));
            Assert.That(en.Classes.Count, Is.EqualTo(ko.Classes.Count));
            // The same save replays the same way in either language.
            var a = new Adventure(ko, new[] { "knight", "hunter", "cleric" }, 9);
            var bot = new GameRandom(9, 9);
            for (int i = 0; i < 300 && !a.Over; i++) Assert.That(AdventureBot.Step(a, bot), Is.Null);
            var back = Adventure.Load(en, a.Save(), out string error, out string note);
            Assert.That(error, Is.Null);
            Assert.That(note, Is.Null.Or.Empty, "시트가 바뀌었다는 알림이 없다");
            Assert.That(back.Log.Count, Is.EqualTo(a.Log.Count));
            Assert.That(back.Gold, Is.EqualTo(a.Gold));
            Assert.That(back.Heroes.Select(h => h.Hp), Is.EqualTo(a.Heroes.Select(h => h.Hp)));
        }

        [Test]
        public void TheEventsThatMatterHaveAPicture()
        {
            var events = TestData.Data.Events.ToDictionary(e => e.Id);
            Assert.That(events["back_fox"].Art, Is.EqualTo("reunion"));
            Assert.That(events["shrine"].Art, Is.EqualTo("shrine"));
            Assert.That(events["seal_stone"].Art, Is.EqualTo("forest"));
            Assert.That(events["deserter"].Art, Is.Empty, "보통 사건은 그림이 없다");
            Assert.That(TestData.Data.Stories["epilogue_kind"].Art, Is.EqualTo("journal+dawn"));
        }

        [Test]
        public void SomeoneSavedEarlierComesBackLater()
        {
            var events = TestData.Data.Events.Where(e => e.Requires.Length > 0).ToList();
            Assert.That(events.Count, Is.GreaterThanOrEqualTo(6));
            var a = Start(new[] { "knight", "rogue", "bard" }, null, 3, 3);
            var pick = typeof(Adventure).GetMethod("PickEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var seen = new HashSet<string>();
            for (int i = 0; i < 30; i++) seen.Add(((EventCard)pick.Invoke(a, new object[] { "wild4" })).Id);
            Assert.That(seen.Any(id => id.StartsWith("back_")), Is.False, "구한 사람이 없으면 아무도 오지 않는다");

            var b = Start(new[] { "knight", "rogue", "bard" }, null, 3, 3);
            b.SetFlag("spared_bandit");
            var first = new List<string>();
            for (int i = 0; i < 6; i++) first.Add(((EventCard)pick.Invoke(b, new object[] { "wild4" })).Id);
            Assert.That(first, Does.Contain("back_bandit"));
            Assert.That(b.CallbacksMet, Is.EqualTo(1));
            Assert.That(TestData.Data.Chapters[4].KindFlags, Does.Contain("old_friend"));
        }
    }
}
