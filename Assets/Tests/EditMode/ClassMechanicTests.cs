using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    // The six classes added on 2026-11-10 (Docs/Classes.md): each one's own rule.
    public class ClassMechanicTests
    {
        static Battle Solo(string classId, MonsterDef monster = null, params string[] more)
        {
            var party = TestData.Party(new[] { classId }.Concat(more).ToArray());
            return new Battle(party, new List<MonsterDef> { monster ?? TestData.Dummy() }, 3, false, TestData.Data);
        }

        static void Act(Battle battle, string skill, int target = -1, int resolve = 0)
        {
            var actor = battle.Current;
            if (target < 0) target = battle.Monsters.First(m => !m.Down).Id;
            Assert.That(battle.Do(new BattleCommand(actor.Id, skill, target, resolve)), Is.Null);
        }

        [Test]
        public void TheRoguesCombosFeedTheFinisher()
        {
            var battle = Solo("rogue");
            var rogue = battle.Current;
            var finisher = rogue.Hero.Skill("rogue_finisher");
            Assert.That(battle.Preview(new BattleCommand(rogue.Id, "rogue_finisher", battle.Monsters.First().Id)).Ok, Is.False, "연계가 없으면 못 쓴다");
            Act(battle, Hero.AttackId);
            Act(battle, Hero.AttackId);
            Assert.That(rogue.Combo, Is.EqualTo(2));
            Assert.That(battle.DiceOf(rogue, finisher), Is.EqualTo(finisher.Dice + 2));
            Act(battle, "rogue_finisher");
            Assert.That(rogue.Combo, Is.Zero);
        }

        [Test]
        public void AHiddenRogueIsNotAimedAtAndStrikesWithASureDie()
        {
            var goblin = TestData.Data.Monsters["goblin"];
            var battle = new Battle(TestData.Party("rogue", "knight"), new List<MonsterDef> { goblin, goblin }, 4, false, TestData.Data);
            var rogue = battle.Heroes.First(h => h.Hero.Class.Id == "rogue");
            for (int i = 0; i < 20 && battle.Current != rogue; i++) Act(battle, Hero.AttackId);
            Assert.That(battle.Current, Is.SameAs(rogue));
            foreach (var monster in battle.Monsters) monster.Intent.Target = rogue.Id;
            Act(battle, "rogue_hide", rogue.Id);
            foreach (var monster in battle.Monsters.Where(m => !m.Down)) Assert.That(monster.Intent.Target, Is.Not.EqualTo(rogue.Id));
            for (int i = 0; i < 20 && battle.Current != rogue; i++) Act(battle, Hero.AttackId);
            var preview = battle.Preview(new BattleCommand(rogue.Id, Hero.AttackId, battle.Monsters.First(m => !m.Down).Id));
            Assert.That(preview.Reasons, Does.Contain("기습 1"));
        }

        [Test]
        public void TheSongOfCourageRaisesEveryAllysChance()
        {
            var battle = Solo("bard", null, "knight");
            var bard = battle.Heroes.First(h => h.Hero.Class.Id == "bard");
            var knight = battle.Heroes.First(h => h.Hero.Class.Id == "knight");
            int before = battle.ChanceOf(knight, knight.Hero.Skill(Hero.AttackId));
            for (int i = 0; i < 10 && battle.Current != bard; i++) Act(battle, Hero.AttackId);
            Act(battle, "bard_courage", bard.Id);
            Assert.That(battle.Song, Is.EqualTo(Song.Courage));
            Assert.That(battle.ChanceOf(knight, knight.Hero.Skill(Hero.AttackId)), Is.EqualTo(before + Battle.CourageBonus));
        }

        [Test]
        public void TheAlchemistSpendsReagents()
        {
            var battle = Solo("alchemist");
            var hero = battle.Current.Hero;
            Assert.That(hero.Reagents, Is.EqualTo(Hero.StartReagents));
            Act(battle, "alch_bomb", battle.Current.Id);
            Assert.That(hero.Reagents, Is.EqualTo(Hero.StartReagents - 1));
            hero.Reagents = 0;
            Act(battle, Hero.AttackId);
            Act(battle, Hero.AttackId);
            Assert.That(battle.Preview(new BattleCommand(battle.Current.Id, "alch_acid", battle.Monsters.First().Id)).Error, Does.Contain("재료"));
        }

        [Test]
        public void SoulsRaiseASkeletonThatFightsOnThePartysSide()
        {
            var weak = TestData.Dummy(hp: 1);
            var party = TestData.Party("necromancer");
            var battle = new Battle(party, new List<MonsterDef> { weak, TestData.Dummy() }, 5, false, TestData.Data);
            var necro = battle.Current;
            Assert.That(necro.Souls, Is.EqualTo(2), "전투를 영혼 둘로 시작한다");
            necro.Hero.Blessing = 3;
            Act(battle, Hero.AttackId, battle.Monsters.First(m => m.MaxHp == 1).Id);
            Assert.That(necro.Souls, Is.EqualTo(3), "적이 쓰러지면 영혼 +1");
            var skeleton = battle.Summons.Single();
            Assert.That(skeleton.Side, Is.EqualTo(Side.Party), "해골 하나와 함께 전투를 시작한다");
            Assert.That(skeleton.Intent, Is.Not.Null);
            Assert.That(battle.Unit(skeleton.Intent.Target).IsFoe, Is.True, "해골은 적을 노린다");
            Assert.That(battle.Preview(new BattleCommand(necro.Id, "necro_raise", necro.Id)).Ok, Is.False, "서 있는 해골이 있으면 못 일으킨다");
            skeleton.Hp = 0;
            Act(battle, "necro_raise", necro.Id);
            Assert.That(battle.Summons.Single().Hp, Is.GreaterThan(0), "쓰러진 해골을 다시 일으킨다");
            Assert.That(necro.Souls, Is.EqualTo(1));
        }

        [Test]
        public void TheMonkGathersKiAndItsPalmGoesThroughArmor()
        {
            var battle = new Battle(TestData.Party("monk"), new List<MonsterDef> { TestData.Dummy(armor: 5) }, 5, false, TestData.Data);
            var monk = battle.Current;
            Assert.That(monk.Ki, Is.EqualTo(1));
            monk.Hero.Blessing = 3;
            Act(battle, Hero.AttackId, battle.Monsters.First().Id);
            Assert.That(monk.Ki, Is.EqualTo(2), "맞히면 기 +1");
            var preview = battle.Preview(new BattleCommand(monk.Id, "monk_palm", battle.Monsters.First().Id));
            Assert.That(preview.Ok, Is.True);
            int raw = preview.ValueBySuccesses.Last();
            Assert.That(raw, Is.GreaterThan(5), "방어도 5를 꿰뚫는다");
            Act(battle, "monk_palm", battle.Monsters.First().Id);
            Assert.That(monk.Ki, Is.EqualTo(1), "기 2를 쓰고, 맞혀서 다시 1");
            Assert.That(battle.Preview(new BattleCommand(monk.Id, "monk_palm", battle.Monsters.First().Id)).Ok, Is.False, "기가 모자라면 못 쓴다");
        }

        [Test]
        public void TheEngineerBuildsATurretThatStandsInFront()
        {
            var battle = new Battle(TestData.Party("engineer"), new List<MonsterDef> { TestData.Dummy() }, 5, false, TestData.Data);
            var engineer = battle.Current;
            Act(battle, "engi_turret", engineer.Id);
            var turret = battle.Summons.Single();
            Assert.That(turret.Monster.Id, Is.EqualTo(GameData.TurretId));
            Assert.That(Battle.InFront(turret), Is.True);
            Assert.That(Battle.InFront(engineer), Is.False, "기술자는 뒷줄");
        }

        [Test]
        public void TheMnemonistEchoesTheLastAllySkill()
        {
            var battle = new Battle(TestData.Party("cleric", "mnemonist"), new List<MonsterDef> { TestData.Dummy(speed: 1) }, 7, false, TestData.Data);
            var mnemonist = battle.Heroes.First(h => h.Hero.Class.Id == "mnemonist");
            for (int i = 0; i < 20 && battle.Current != null && battle.Current.Hero?.Class.Id != "cleric"; i++)
                Act(battle, Hero.AttackId, battle.Monsters.First().Id);
            if (battle.Current?.Hero?.Class.Id == "mnemonist")
                Assert.That(battle.Preview(new BattleCommand(mnemonist.Id, Battle.EchoId, mnemonist.Id)).Ok, Is.False, "되새길 것이 없으면 못 쓴다");
            var cleric = battle.Heroes.First(h => h.Hero.Class.Id == "cleric");
            var heal = cleric.Hero.Skills.First(s => s.Kind == SkillKind.Heal);
            for (int i = 0; i < 20 && battle.Current != cleric; i++) Act(battle, Hero.AttackId, battle.Monsters.First().Id);
            Act(battle, heal.Id, cleric.Id);
            for (int i = 0; i < 20 && battle.Current != mnemonist; i++) Act(battle, Hero.AttackId, battle.Monsters.First().Id);
            var echo = battle.SkillOf(mnemonist, Battle.EchoId);
            Assert.That(echo.Kind, Is.EqualTo(SkillKind.Heal), "되새김은 동료의 치유가 된다");
            Assert.That(battle.Preview(new BattleCommand(mnemonist.Id, Battle.EchoId, cleric.Id)).Ok, Is.True);
        }

        [Test]
        public void TheSeerSeesTheNextDiceAndCanDropOne()
        {
            var battle = Solo("seer");
            var seer = battle.Current;
            var seen = battle.Foresee();
            Assert.That(seen.Count, Is.EqualTo(Battle.Foresight));
            Act(battle, "seer_twist", seer.Id);
            var after = battle.Foresee();
            Assert.That(after[0], Is.EqualTo(seen[1]), "버린 수 다음 것이 맨 앞으로");
            var roll = battle.Events.Last(e => e.Kind == BattleEventKind.StatusOn && e.Amount == seen[0]);
            Assert.That(roll.Text, Does.Contain("버렸다"));
        }

        [Test]
        public void TheBeastmastersWolfJoinsEveryBattleAndBitesOnCommand()
        {
            var battle = Solo("beastmaster");
            var wolf = battle.Summons.Single();
            Assert.That(wolf.Monster.Id, Is.EqualTo(GameData.CompanionWolfId));
            Assert.That(wolf.Owner, Is.EqualTo(battle.Heroes.First().Id));
            while (battle.Current == null || battle.Current.Hero.Class.Id != "beastmaster") Act(battle, Hero.AttackId);
            int rolls = battle.Events.Count(e => e.Kind == BattleEventKind.Roll && e.Actor == wolf.Id);
            Act(battle, "beast_command");
            int after = battle.Events.Count(e => e.Kind == BattleEventKind.Roll && e.Actor == wolf.Id);
            Assert.That(after, Is.GreaterThan(rolls), "늑대가 바로 같은 적을 문다");
        }

        [Test]
        public void TheSeerRaisesNoSoulsAndTheNecromancerNeedsThem()
        {
            var battle = Solo("necromancer");
            battle.Current.Souls = 0;
            var preview = battle.Preview(new BattleCommand(battle.Current.Id, "necro_raise", battle.Current.Id));
            Assert.That(preview.Error, Does.Contain("영혼"));
        }
            // 2026-11-11 밸런스: 도박사의 판돈, 동전 던지기 같은 결투, 점성술사의 별의 경고와 별의 인도, 원소술사의 얼리는 공명.
        [Test]
        public void TheGamblersFailedDiceBecomeTheStakeAllInCashes()
        {
            var battle = Solo("gambler");
            var gambler = battle.Current;
            for (int i = 0; i < 10 && gambler.Stake == 0; i++) Act(battle, Hero.AttackId);
            Assert.That(gambler.Stake, Is.GreaterThan(0), "실패한 주사위가 판돈이 된다");
            Assert.That(gambler.Stake, Is.LessThanOrEqualTo(battle.StakeMax(gambler)));
            int stake = gambler.Stake;
            var preview = battle.Preview(new BattleCommand(gambler.Id, "gamb_allin", battle.Monsters.First().Id));
            Assert.That(preview.Reasons, Has.Member($"판돈 {stake}"));
            Assert.That(preview.Free, Is.EqualTo(stake));
            Act(battle, "gamb_allin");
            var roll = battle.Events.Last(e => e.Kind == BattleEventKind.Roll && e.Actor == gambler.Id);
            Assert.That(gambler.Stake, Is.EqualTo(System.Math.Min(battle.StakeMax(gambler), roll.Check.RolledFailures)), "올인이 판돈을 모두 쓰고, 그 실패만 다시 쌓인다");
        }

        [Test]
        public void ABlowAtTheGamblerAloneIsACoinToss()
        {
            var sure = TestData.Dummy(500, 30);
            sure.Accuracy = 95;
            sure.Pattern = new List<MonsterSkillDef> { TestData.Data.MonsterSkills["gob_stab"] };
            var battle = new Battle(TestData.Party("gambler"), new List<MonsterDef> { sure }, 5, false, TestData.Data);
            var monster = battle.Monsters.First();
            for (int i = 0; i < 10 && !battle.Events.Any(e => e.Kind == BattleEventKind.Roll && e.Actor == monster.Id); i++) Act(battle, Hero.AttackId);
            var blow = battle.Events.First(e => e.Kind == BattleEventKind.Roll && e.Actor == monster.Id);
            Assert.That(blow.Check.Chance, Is.EqualTo(Battle.GamblerChance));
        }

        [Test]
        public void TheSeerWarnsOfEveryFightAndGuidesEveryone()
        {
            var battle = Solo("seer", null, "knight", "hunter");
            foreach (var monster in battle.Monsters) Assert.That(monster.Status[StatusKind.Foretold], Is.EqualTo(Battle.ForetellCut), "첫 공격이 예언된 채 시작한다");
            while (battle.Current.Hero.Class.Id != "seer") Act(battle, Hero.AttackId);
            var seer = battle.Current;
            Act(battle, "seer_guide", seer.Id);
            foreach (var hero in battle.Heroes) Assert.That(hero.Hero.Blessing, Is.EqualTo(1), hero.Name);
        }

        [Test]
        public void AResonantFrostLanceFreezes()
        {
            var battle = Solo("elementalist", TestData.Dummy(500, 30));
            var elementalist = battle.Current;
            var monster = battle.Monsters.First();
            Act(battle, "elem_fire");
            while (battle.Current != elementalist) Act(battle, Hero.AttackId);
            Assert.That(Battle.Resonates(elementalist, elementalist.Hero.Skill("elem_frost")), Is.True);
            int skips = battle.Events.Count(e => e.Kind == BattleEventKind.Skip && e.Actor == monster.Id);
            Act(battle, "elem_frost");
            Assert.That(battle.Events.Count(e => e.Kind == BattleEventKind.Skip && e.Actor == monster.Id), Is.EqualTo(skips + 1), "얼어서 차례를 쉰다");
        }

        // 2026-11-11 3차: 연금술사의 가방과 재료 거두기, 도박사의 판돈(결투·동전·위력·판 쓸기), 점성술사의 맞은 예언.
        [Test]
        public void TheAlchemistPacksReagentsForEveryFight()
        {
            var party = TestData.Party("alchemist");
            party[0].Reagents = 0;
            new Battle(party, new List<MonsterDef> { TestData.Dummy() }, 3, false, TestData.Data);
            Assert.That(party[0].Reagents, Is.EqualTo(Battle.ReagentFloor));
        }

        [Test]
        public void ABombThatFellsAFoeGivesAReagentBack()
        {
            var battle = new Battle(TestData.Party("alchemist"), new List<MonsterDef> { TestData.Dummy(1), TestData.Dummy(1), TestData.Dummy() }, 3, false, TestData.Data);
            var alchemist = battle.Current;
            int had = alchemist.Hero.Reagents;
            Act(battle, "alch_bomb");
            Assert.That(battle.Monsters.Count(m => m.Down), Is.GreaterThan(0));
            Assert.That(alchemist.Hero.Reagents, Is.EqualTo(had), "재료 1을 쓰고 쓰러진 적에게서 1을 거둔다");
        }

        [Test]
        public void TheGamblersCoinAndAFoesLostDiceGoIntoTheStake()
        {
            var battle = Solo("gambler", null, "knight");
            while (battle.Current.Hero.Class.Id != "gambler") Act(battle, Hero.AttackId);
            var gambler = battle.Current;
            int stake = gambler.Stake;
            Act(battle, "gamb_coin", battle.Heroes.First(h => h.Id != gambler.Id).Id);
            Assert.That(gambler.Stake, Is.EqualTo(stake + 1), "동전도 판돈이 된다");

            var sure = TestData.Dummy(500, 30);
            sure.Accuracy = 95;
            sure.Pattern = new List<MonsterSkillDef> { TestData.Data.MonsterSkills["gob_stab"] };
            var duel = new Battle(TestData.Party("gambler"), new List<MonsterDef> { sure }, 5, false, TestData.Data);
            var lone = duel.Current;
            var monster = duel.Monsters.First();
            bool fed = false;
            for (int i = 0; i < 20 && !fed; i++)
            {
                lone.Stake = 0;
                Assert.That(duel.Do(new BattleCommand(lone.Id, Hero.AttackId, monster.Id)), Is.Null);
                var events = duel.Events.ToList();
                for (int k = 0; k + 1 < events.Count; k++)
                    if (events[k].Kind == BattleEventKind.Roll && events[k].Actor == monster.Id && events[k].Check.RolledFailures > 0 && events[k + 1].Kind == BattleEventKind.StatusOn && events[k + 1].Actor == lone.Id && events[k + 1].Amount > 0)
                        fed = true;
            }
            Assert.That(fed, Is.True, "도박사를 노린 적의 실패한 주사위가 판돈이 된다");
        }

        [Test]
        public void TheStakeRaisesAllInsPowerAndAFellingAllInCanGoAgain()
        {
            var battle = new Battle(TestData.Party("gambler"), new List<MonsterDef> { TestData.Dummy(1), TestData.Dummy() }, 3, false, TestData.Data);
            var gambler = battle.Current;
            var allIn = gambler.Hero.Skill("gamb_allin");
            gambler.Stake = 3;
            Assert.That(battle.PowerOf(gambler, allIn), Is.EqualTo(allIn.Power + 3 * Battle.StakePower));
            Act(battle, "gamb_allin", battle.Monsters.First().Id);
            Assert.That(battle.Monsters.First().Down, Is.True);
            Assert.That(battle.Cooldown(gambler, allIn), Is.Zero, "판을 쓸면 올인을 바로 다시 건다");
        }

        [Test]
        public void AKeptProphecyMarksTheFoeAndAForetoldBlowHitsSofter()
        {
            var weak = TestData.Dummy(500, 30);
            weak.Accuracy = 30;
            weak.Pattern = new List<MonsterSkillDef> { TestData.Data.MonsterSkills["gob_stab"] };
            var battle = Solo("seer", weak);
            var monster = battle.Monsters.First();
            // Its first blow (maybe before the seer's first turn) is the foretold one.
            for (int i = 0; i < 10 && !battle.Events.Any(e => e.Kind == BattleEventKind.Roll && e.Actor == monster.Id); i++) Act(battle, Hero.AttackId);
            var blow = battle.Events.First(e => e.Kind == BattleEventKind.Roll && e.Actor == monster.Id);
            Assert.That(blow.Check.Successes * 2, Is.LessThan(blow.Check.Dice));
            Assert.That(monster.Has(StatusKind.Mark), Is.True, "빗나간 예언된 공격: 별의 표식");

            var sure = TestData.Dummy(500, 30);
            sure.Accuracy = 95;
            sure.Pattern = new List<MonsterSkillDef> { TestData.Data.MonsterSkills["gob_stab"] };
            var second = Solo("seer", sure);
            var foe = second.Monsters.First();
            var seer = second.Current;
            for (int i = 0; i < 10 && !second.Events.Any(e => e.Kind == BattleEventKind.Roll && e.Actor == foe.Id); i++) Act(second, Hero.AttackId);
            int at = second.Events.ToList().FindIndex(e => e.Kind == BattleEventKind.Roll && e.Actor == foe.Id);
            var roll = second.Events[at];
            var hit = second.Events.Skip(at).FirstOrDefault(e => e.Kind == BattleEventKind.Damage && e.Target == seer.Id);
            if (roll.Check.Successes > 0)
            {
                var stab = TestData.Data.MonsterSkills["gob_stab"];
                int soft = Dice.Value(Dice.Percent(stab.Power, Battle.ForetoldPowerPercent), stab.Floor, roll.Check.Successes, roll.Check.Dice);
                Assert.That(hit.Amount, Is.EqualTo(soft - seer.Armor), "예언된 공격은 맞아도 약하다");
            }
        }
    }
}
