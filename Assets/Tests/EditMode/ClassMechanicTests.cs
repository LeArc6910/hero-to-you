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
    }
}
