using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class BattleTests
    {
        static Unit HeroUnit(Battle battle, string classId) => battle.Heroes.First(h => h.Hero.Class.Id == classId);

        // Makes `unit` act right after the current turn.
        static void MakeNext(Battle battle, Unit unit) => unit.Ready = battle.Units.Where(u => !u.Down).Min(u => u.Ready) - 1;

        // Lets the current hero act on the first enemy with its first skill (a plain attack).
        static void Attack(Battle battle, int resolve = 0)
        {
            var actor = battle.Current;
            var target = battle.Monsters.First(m => !m.Down);
            Assert.That(battle.Do(new BattleCommand(actor.Id, Hero.AttackId, target.Id, resolve)), Is.Null);
        }

        static Battle Duel(string[] classes, MonsterDef monster, ulong seed = 1) =>
            new Battle(TestData.Party(classes), new List<MonsterDef> { monster }, seed, false, TestData.Data);

        [Test]
        public void TheSameSeedAndCommandsGiveTheSameBattle()
        {
            string Run()
            {
                var battle = new Battle(TestData.Party("knight", "hunter", "cleric"), TestData.Monsters("goblin", "goblin", "goblin_archer"), 99, false, TestData.Data);
                var random = new GameRandom(5);
                for (int i = 0; i < 200 && !battle.Over; i++) Assert.That(battle.Do(BattleBot.Choose(battle, random)), Is.Null);
                return string.Join("\n", battle.Events.Select(e => e.Text));
            }
            string first = Run();
            Assert.That(Run(), Is.EqualTo(first));
            Assert.That(first, Does.Contain("승리").Or.Contain("쓰러졌다"));
        }

        [Test]
        public void TheFirstTurnIsAHerosAndTheTimelineStartsWithIt()
        {
            var battle = new Battle(TestData.Party("knight", "hunter", "cleric"), TestData.Monsters("goblin"), 3, false, TestData.Data);
            Assert.That(battle.State, Is.EqualTo(BattleState.HeroTurn));
            var timeline = battle.Timeline(8);
            Assert.That(timeline.Count, Is.EqualTo(8));
            Assert.That(timeline[0], Is.SameAs(battle.Current));
        }

        [Test]
        public void MonstersShowWhatTheyWillDo()
        {
            var battle = new Battle(TestData.Party("knight", "hunter", "cleric"), TestData.Monsters("bandit", "goblin_archer"), 4, false, TestData.Data);
            foreach (var monster in battle.Monsters)
            {
                Assert.That(monster.Intent, Is.Not.Null);
                Assert.That(battle.IntentTarget(monster).IsHero, Is.True);
            }
            var archer = battle.Monsters.First(m => m.Monster.Id == "goblin_archer");
            var weakest = battle.Heroes.OrderBy(h => h.Hp).First();
            Assert.That(archer.Intent.Target, Is.EqualTo(weakest.Id), "궁수는 가장 약한 쪽을 노린다");
        }

        [Test]
        public void AMarkGivesEveryAttackerASureSuccess()
        {
            var battle = Duel(new[] { "hunter", "knight" }, TestData.Dummy());
            var hunter = HeroUnit(battle, "hunter");
            var knight = HeroUnit(battle, "knight");
            var dummy = battle.Monsters.First();
            MakeNext(battle, hunter);
            if (battle.Current != hunter) Attack(battle);
            Assert.That(battle.Current, Is.SameAs(hunter));
            MakeNext(battle, knight);
            Assert.That(battle.Do(new BattleCommand(hunter.Id, "hunt_mark", dummy.Id)), Is.Null);
            Assert.That(dummy.Has(StatusKind.Mark), Is.True);
            Assert.That(battle.Current, Is.SameAs(knight));
            var preview = battle.Preview(new BattleCommand(knight.Id, Hero.AttackId, dummy.Id));
            Assert.That(preview.Reasons, Does.Contain("표식 1"));
            Assert.That(preview.Free, Is.EqualTo(1));
        }

        [Test]
        public void HittingTheSameEnemyRightAfterAnAllyIsAComboAttack()
        {
            var battle = Duel(new[] { "knight", "berserker" }, TestData.Dummy());
            var first = battle.Current;
            var second = battle.Heroes.First(h => h != first);
            var dummy = battle.Monsters.First();
            MakeNext(battle, second);
            Attack(battle);
            Assert.That(battle.Current, Is.SameAs(second));
            var combo = battle.Preview(new BattleCommand(second.Id, Hero.AttackId, dummy.Id));
            Assert.That(combo.Reasons, Does.Contain("협공 1"));
        }

        [Test]
        public void AGuardedAllysAttackerHitsTheKnight()
        {
            var goblinDef = TestData.Data.Monsters["goblin"];
            var sturdy = new MonsterDef { Id = "goblin", Name = "고블린", Hp = 300, Speed = goblinDef.Speed, Accuracy = 60, Pattern = goblinDef.Pattern, Color = "#FFFFFF", Size = 1 };
            var battle = Duel(new[] { "knight", "elementalist" }, sturdy);
            var knight = HeroUnit(battle, "knight");
            var mage = HeroUnit(battle, "elementalist");
            var goblin = battle.Monsters.First();
            MakeNext(battle, knight);
            if (battle.Current != knight) Attack(battle);
            if (battle.Current != knight)
            {
                MakeNext(battle, knight);
                Attack(battle);
            }
            Assert.That(battle.Current, Is.SameAs(knight));
            goblin.Intent.Target = mage.Id;
            MakeNext(battle, mage);
            Assert.That(battle.Do(new BattleCommand(knight.Id, "knight_guard", mage.Id)), Is.Null);
            Assert.That(battle.IntentTarget(goblin), Is.SameAs(knight));
            Assert.That(knight.Armor, Is.EqualTo(knight.BaseArmor + Battle.GuardArmor));

            // The goblin acts right after the mage, still during the guard.
            Assert.That(battle.Current, Is.SameAs(mage));
            goblin.Ready = battle.Units.Where(u => !u.Down).Min(u => u.Ready) - 1;
            Attack(battle);
            var attack = battle.Events.Last(e => e.Kind == BattleEventKind.Roll && e.Actor == goblin.Id);
            Assert.That(attack.Target, Is.EqualTo(knight.Id), "기사가 대신 맞는다");
        }

        [Test]
        public void AGuardingKnightStrikesBackAtWhoeverHitsIt()
        {
            var goblinDef = TestData.Data.Monsters["goblin"];
            var sturdy = new MonsterDef { Id = "goblin", Name = "고블린", Hp = 300, Speed = goblinDef.Speed, Accuracy = 200, Pattern = goblinDef.Pattern, Color = "#FFFFFF", Size = 1 };
            var battle = Duel(new[] { "knight", "elementalist" }, sturdy);
            var knight = HeroUnit(battle, "knight");
            var mage = HeroUnit(battle, "elementalist");
            var goblin = battle.Monsters.First();
            Assert.That(Battle.Riposte(mage), Is.Zero, "기사만 받아친다");
            Assert.That(Battle.Riposte(knight), Is.EqualTo(Battle.RiposteBase + knight.Hero.Level));
            MakeNext(battle, knight);
            if (battle.Current != knight) Attack(battle);
            Assert.That(battle.Current, Is.SameAs(knight));
            goblin.Intent.Target = mage.Id;
            MakeNext(battle, mage);
            Assert.That(battle.Do(new BattleCommand(knight.Id, "knight_guard", mage.Id)), Is.Null);
            Assert.That(Battle.Standing(knight), Is.True);

            // The goblin's blow at the mage lands on the guarding knight, and the shield answers it.
            goblin.Ready = battle.Units.Where(u => !u.Down).Min(u => u.Ready) - 1;
            int goblinHp = goblin.Hp;
            Attack(battle);
            Assert.That(battle.Events.Any(e => e.Kind == BattleEventKind.Damage && e.Actor == goblin.Id && e.Target == knight.Id), Is.True, "기사가 대신 맞는다");
            var riposte = battle.Events.LastOrDefault(e => e.Kind == BattleEventKind.Damage && e.Actor == knight.Id && e.Target == goblin.Id && e.Text.Contains("방패 반격"));
            Assert.That(riposte, Is.Not.Null, "방패 반격");
            Assert.That(riposte.Amount, Is.EqualTo(Battle.Riposte(knight)), "방어도 0인 적에게 그대로");
        }

        [Test]
        public void AKnightThatIsNotStandingGuardDoesNotStrikeBack()
        {
            var goblinDef = TestData.Data.Monsters["goblin"];
            var sturdy = new MonsterDef { Id = "goblin", Name = "고블린", Hp = 300, Speed = goblinDef.Speed, Accuracy = 200, Pattern = goblinDef.Pattern, Color = "#FFFFFF", Size = 1 };
            var battle = Duel(new[] { "knight" }, sturdy);
            var knight = battle.Current;
            var goblin = battle.Monsters.First();
            for (int i = 0; i < 6 && !battle.Over; i++) Attack(battle);
            Assert.That(battle.Events.Any(e => e.Kind == BattleEventKind.Damage && e.Actor == goblin.Id && e.Target == knight.Id), Is.True);
            Assert.That(battle.Events.Any(e => e.Kind == BattleEventKind.Damage && e.Text.Contains("방패 반격")), Is.False);
        }

        // The smart bot gives every class's own skills a turn when they are worth one (Docs/Classes.md 2026-11-11 2차):
        // the support skills that only lift others or the party's tempo included.
        [Test]
        public void TheSmartBotUsesEveryClassesSupportSkills()
        {
            var used = new HashSet<string>();
            var parties = new[]
            {
                new[] { "knight", "hunter", "cleric" }, new[] { "monk", "gambler", "bard" }, new[] { "engineer", "mnemonist", "berserker" },
                new[] { "rogue", "beastmaster", "seer" }, new[] { "alchemist", "necromancer", "elementalist" }, new[] { "hunter", "bard", "mnemonist" }, new[] { "gambler", "cleric", "mnemonist" },
            };
            var encounters = new[] { new[] { "bandit", "goblin", "goblin_archer" }, new[] { "wolf", "wolf", "cutthroat" }, new[] { "bandit_chief", "bandit" }, new[] { "cultist", "ash_moth", "ash_moth" } };
            ulong seed = 1;
            foreach (var party in parties)
                foreach (var foes in encounters)
                    for (int run = 0; run < 12; run++)
                    {
                        var heroes = TestData.Party(party);
                        foreach (var hero in heroes) hero.GainXp(Hero.LevelXp[2]);
                        var battle = new Battle(heroes, TestData.Monsters(foes), seed++, false, TestData.Data);
                        var random = new GameRandom(seed);
                        for (int i = 0; i < 300 && !battle.Over; i++)
                        {
                            var command = BattleBot.Choose(battle, random);
                            string echoed = command.Skill == Battle.EchoId ? battle.SkillOf(battle.Current, command.Skill).Effect : null;
                            Assert.That(battle.Do(command), Is.Null);
                            used.Add(command.Skill);
                            if (echoed != null) used.Add("echo:" + echoed);
                        }
                    }
            var missing = new[] { "knight_guard", "knight_taunt", "knight_bash", "cler_bless", "monk_meditate", "gamb_coin", "bard_encore", "engi_snare", "mnem_forget", "beast_command" }
                .Where(s => !used.Contains(s)).ToList();
            Assert.That(missing, Is.Empty, string.Join(", ", used));
            var supports = new[] { "encore", "mark", "song_courage", "song_rest", "lucky", "bless", "guard", "foretell" };
            Assert.That(used.Any(s => s.StartsWith("echo:") && supports.Contains(s.Substring(5))), Is.True, "기억술사가 지원 기술도 되새긴다: " + string.Join(", ", used.Where(s => s.StartsWith("echo:"))));
        }

        [Test]
        public void TheBotsBardTurnsToTheSongOfRestOnceThePartyIsWornDown()
        {
            var battle = Duel(new[] { "bard", "knight", "hunter" }, TestData.Dummy());
            var bard = HeroUnit(battle, "bard");
            MakeNext(battle, bard);
            if (battle.Current != bard) Attack(battle);
            Assert.That(battle.Current, Is.SameAs(bard));
            var random = new GameRandom(1);
            Assert.That(BattleBot.Choose(battle, random).Skill, Is.EqualTo("bard_courage"), "다치지 않은 파티에는 용기");
            Assert.That(battle.Do(new BattleCommand(bard.Id, "bard_courage", bard.Id)), Is.Null);
            for (int i = 0; i < 20 && battle.Current != bard; i++) Attack(battle);
            Assert.That(battle.Current, Is.SameAs(bard));
            Assert.That(battle.Song, Is.EqualTo(Song.Courage));
            foreach (var other in battle.Heroes.Where(h => h != bard)) other.Hp = other.MaxHp / 3;
            Assert.That(BattleBot.Choose(battle, random).Skill, Is.EqualTo("bard_rest"), "둘이 반 아래면 안식");
        }

        [Test]
        public void ATauntTurnsEveryIntentToTheKnight()
        {
            var battle = new Battle(TestData.Party("knight", "hunter", "cleric"), TestData.Monsters("goblin", "goblin", "goblin_archer"), 8, false, TestData.Data);
            var knight = HeroUnit(battle, "knight");
            for (int i = 0; i < 20 && battle.Current != knight; i++) Attack(battle);
            Assert.That(battle.Current, Is.SameAs(knight));
            MakeNext(battle, battle.Heroes.First(h => h != knight && !h.Down));
            Assert.That(battle.Do(new BattleCommand(knight.Id, "knight_taunt", knight.Id)), Is.Null);
            foreach (var monster in battle.Monsters.Where(m => !m.Down)) Assert.That(monster.Intent.Target, Is.EqualTo(knight.Id));
        }

        [Test]
        public void AFireballThatLandsBurns()
        {
            var battle = Duel(new[] { "elementalist" }, TestData.Dummy());
            var mage = battle.Current;
            var dummy = battle.Monsters.First();
            mage.Hero.Blessing = 3;
            int before = dummy.Hp;
            Assert.That(battle.Do(new BattleCommand(mage.Id, "elem_fire", dummy.Id)), Is.Null);
            int power = mage.Hero.Skill("elem_fire").Power;
            Assert.That(dummy.Hp, Is.EqualTo(before - Dice.Percent(power, 150)), "확정 3개면 완벽: 위력의 1.5배");
            Assert.That(dummy.Has(StatusKind.Burn), Is.True);
        }

        [Test]
        public void TheElementCycleResonates()
        {
            var battle = Duel(new[] { "elementalist" }, TestData.Dummy());
            var mage = battle.Current;
            var dummy = battle.Monsters.First();
            Assert.That(battle.Preview(new BattleCommand(mage.Id, "elem_frost", dummy.Id)).Reasons, Is.Empty);
            Assert.That(battle.Do(new BattleCommand(mage.Id, "elem_fire", dummy.Id)), Is.Null);
            Assert.That(battle.Current, Is.SameAs(mage));
            Assert.That(battle.Preview(new BattleCommand(mage.Id, "elem_frost", dummy.Id)).Reasons, Does.Contain("공명 1"));
            Assert.That(battle.Preview(new BattleCommand(mage.Id, "elem_chain", dummy.Id)).Reasons, Is.Empty);
        }

        [Test]
        public void ASkillWaitsOutItsCooldown()
        {
            var battle = Duel(new[] { "berserker" }, TestData.Dummy());
            var hero = battle.Current;
            var dummy = battle.Monsters.First();
            Assert.That(battle.Do(new BattleCommand(hero.Id, "bers_frenzy", dummy.Id)), Is.Null);
            var skill = hero.Hero.Skill("bers_frenzy");
            Assert.That(battle.Cooldown(hero, skill), Is.EqualTo(2));
            Assert.That(battle.Do(new BattleCommand(hero.Id, "bers_frenzy", dummy.Id)), Is.Not.Null);
            Attack(battle);
            Assert.That(battle.Cooldown(hero, skill), Is.EqualTo(1));
            Attack(battle);
            Assert.That(battle.Cooldown(hero, skill), Is.Zero);
            Assert.That(battle.Do(new BattleCommand(hero.Id, "bers_frenzy", dummy.Id)), Is.Null);
        }

        [Test]
        public void RageRaisesTheBerserkersChance()
        {
            var party = TestData.Party("berserker");
            party[0].Hp = party[0].MaxHp / 2;
            var battle = new Battle(party, new List<MonsterDef> { TestData.Dummy() }, 1, false, TestData.Data);
            var hero = battle.Current;
            var chop = hero.Hero.Skill(Hero.AttackId);
            Assert.That(Battle.Rage(hero), Is.EqualTo(16));
            Assert.That(battle.ChanceOf(hero, chop), Is.EqualTo(75 + 16));
        }

        [Test]
        public void TheGamblersChanceIsAlwaysFifty()
        {
            var battle = Duel(new[] { "gambler" }, TestData.Dummy());
            var hero = battle.Current;
            foreach (var skill in hero.Hero.Skills.Where(s => s.Rolls)) Assert.That(battle.ChanceOf(hero, skill), Is.EqualTo(50));
        }

        [Test]
        public void ResolveBuysSureSuccessesAndCostsResolve()
        {
            var battle = Duel(new[] { "knight" }, TestData.Dummy());
            var hero = battle.Current;
            var dummy = battle.Monsters.First();
            var preview = battle.Preview(new BattleCommand(hero.Id, Hero.AttackId, dummy.Id, 2));
            Assert.That(preview.Ok, Is.True);
            Assert.That(preview.Odds.Guaranteed, Is.EqualTo(2));
            Assert.That(battle.Preview(new BattleCommand(hero.Id, Hero.AttackId, dummy.Id, 4)).Ok, Is.False);
            int had = hero.Hero.Resolve;
            Attack(battle, 2);
            Assert.That(hero.Hero.Resolve, Is.EqualTo(had - 2));
            var roll = battle.Events.Last(e => e.Kind == BattleEventKind.Roll);
            Assert.That(roll.Check.Successes, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void TheClericRaisesAFallenAlly()
        {
            var party = TestData.Party("cleric", "knight");
            party[1].Hp = 0;
            var battle = new Battle(party, new List<MonsterDef> { TestData.Dummy() }, 2, false, TestData.Data);
            var cleric = battle.Current;
            var knight = HeroUnit(battle, "knight");
            Assert.That(knight.Down, Is.True);
            Assert.That(battle.Do(new BattleCommand(cleric.Id, "cler_revive", knight.Id)), Is.Null);
            Assert.That(knight.Down, Is.False);
            Assert.That(knight.Hp, Is.GreaterThanOrEqualTo(knight.MaxHp * 12 / 100));
        }

        [Test]
        public void APotionHealsFifteenOnce()
        {
            var party = TestData.Party("hunter");
            party[0].Hp = 5;
            var battle = new Battle(party, new List<MonsterDef> { TestData.Dummy() }, 2, false, TestData.Data);
            var hero = battle.Current;
            Assert.That(battle.Do(new BattleCommand(hero.Id, BattleCommand.Potion, hero.Id)), Is.Null);
            Assert.That(hero.Hp, Is.EqualTo(5 + Battle.PotionHeal));
            Assert.That(battle.Do(new BattleCommand(hero.Id, BattleCommand.Potion, hero.Id)), Is.Not.Null);
        }

        [Test]
        public void TheBossChangesItsPatternAtHalfHealth()
        {
            var battle = new Battle(TestData.Party("knight", "hunter", "cleric"), TestData.Monsters("ash_warden"), 5, false, TestData.Data);
            var boss = battle.Monsters.First();
            boss.Hp = boss.MaxHp / 2 + 2;
            var hero = battle.Current;
            hero.Hero.Blessing = 5;
            Assert.That(battle.Do(new BattleCommand(hero.Id, Hero.AttackId, boss.Id)), Is.Null);
            Assert.That(boss.Phase, Is.EqualTo(1));
            Assert.That(battle.Events.Any(e => e.Kind == BattleEventKind.Phase), Is.True);
            Assert.That(boss.Monster.Pattern2.Select(s => s.Id), Does.Contain(boss.Intent.Skill.Id));
        }

        [Test]
        public void KillingEveryMonsterWins()
        {
            var battle = Duel(new[] { "knight" }, TestData.Dummy(hp: 1));
            battle.Current.Hero.Blessing = 3;
            Attack(battle);
            Assert.That(battle.State, Is.EqualTo(BattleState.Won));
            Assert.That(battle.Current, Is.Null);
        }

        [Test]
        public void FailedDiceFillGritIntoResolve()
        {
            var battle = Duel(new[] { "gambler" }, TestData.Dummy());
            var hero = battle.Current;
            hero.Hero.Resolve = 0;
            int gained = 0;
            for (int i = 0; i < 10; i++)
            {
                Attack(battle);
                gained += battle.Events.Where(e => e.Kind == BattleEventKind.ResolveGained).Sum(e => e.Amount);
                if (gained > 0) break;
            }
            Assert.That(gained, Is.GreaterThan(0), "실패한 주사위가 끈기를 채워 결의가 된다");
        }

        // 2026-11-11 3차: 새 몬스터의 표식(균열 사냥개)과 동료 치료(물마녀, 얼어붙은 원장).
        static MonsterDef Using(string skill, int accuracy = 95, int speed = 30)
        {
            var monster = TestData.Dummy(500, speed);
            monster.Accuracy = accuracy;
            monster.Pattern = new List<MonsterSkillDef> { TestData.Data.MonsterSkills[skill] };
            return monster;
        }

        [Test]
        public void AHoundsScentMarksTheHeroAndItsNextBlowIsSure()
        {
            var battle = Duel(new[] { "knight" }, Using("hound_rend"));
            var knight = battle.Current;
            var hound = battle.Monsters.First();
            for (int i = 0; i < 20 && !knight.Has(StatusKind.Mark); i++) Attack(battle);
            Assert.That(knight.Has(StatusKind.Mark), Is.True, "물린 영웅에게 표식이 남는다");
            int rolls = battle.Events.Count(e => e.Kind == BattleEventKind.Roll && e.Actor == hound.Id);
            for (int i = 0; i < 20 && battle.Events.Count(e => e.Kind == BattleEventKind.Roll && e.Actor == hound.Id) == rolls; i++) Attack(battle);
            var next = battle.Events.Where(e => e.Kind == BattleEventKind.Roll && e.Actor == hound.Id).Skip(rolls).First();
            Assert.That(next.Check.Guaranteed, Is.GreaterThanOrEqualTo(1), "표식이 남은 영웅을 노린 공격은 확정 성공 +1");
        }

        [Test]
        public void TheTideWitchMendsTheMostWoundedOnItsSide()
        {
            var witch = Using("witch_mend");
            var battle = new Battle(TestData.Party("knight"), new List<MonsterDef> { witch, TestData.Dummy() }, 2, false, TestData.Data);
            var hurt = battle.Monsters.Last();
            hurt.Hp = 100;
            var mender = battle.Monsters.First();
            for (int i = 0; i < 20 && !battle.Events.Any(e => e.Kind == BattleEventKind.Heal && e.Actor == mender.Id); i++)
            {
                var knight = battle.Current;
                Assert.That(battle.Do(new BattleCommand(knight.Id, Hero.AttackId, mender.Id)), Is.Null);
            }
            var heal = battle.Events.First(e => e.Kind == BattleEventKind.Heal && e.Actor == mender.Id);
            Assert.That(heal.Target, Is.EqualTo(hurt.Id), "가장 많이 다친 동료");
            Assert.That(heal.Amount, Is.EqualTo(500 * TestData.Data.MonsterSkills["witch_mend"].Power / 100));
        }

        [Test]
        public void EveryBossHasAMoveOnlyItsSecondPhaseUses()
        {
            foreach (string id in new[] { "ash_warden", "bell_ringer", "frozen_abbess", "sern", "nameless" })
            {
                var boss = TestData.Data.Monsters[id];
                Assert.That(boss.Pattern2.Select(s => s.Id).Except(boss.Pattern.Select(s => s.Id)), Is.Not.Empty, id);
            }
        }
    }
}
