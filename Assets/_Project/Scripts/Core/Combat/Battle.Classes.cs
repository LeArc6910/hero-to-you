using System;
using System.Linq;

namespace Game.Core
{
    // The classes' own rules (Docs/Classes.md): the berserker's rage, the gambler's fixed chance, the elementalist's
    // cycle, and the effects of support skills and attacks with something extra.
    public sealed partial class Battle
    {
        public const int RageCap = 25;
        public const int GuardArmor = 2;

        // A hero's chance for one die of the skill: the gambler's fixed 50%, or the ability plus rage.
        public int ChanceOf(Unit actor, SkillDef skill)
        {
            if (skill.FixedChance > 0) return skill.FixedChance;
            int chance = skill.Stat.HasValue ? actor.Hero.Stat(skill.Stat.Value) : 50;
            return Dice.ClampChance(chance + Rage(actor));
        }

        // Berserker: a third of the health lost, in percent, up to +25.
        public static int Rage(Unit actor)
        {
            if (!actor.IsHero || actor.Hero.Class.Id != "berserker" || actor.MaxHp <= 0) return 0;
            int lost = (actor.MaxHp - actor.Hp) * 100 / actor.MaxHp;
            return Math.Min(RageCap, lost / 3);
        }

        // Elementalist: fire → frost → lightning → fire. The next element after the last one used resonates.
        public static string NextElement(string element) => element switch
        {
            "fire" => "frost",
            "frost" => "lightning",
            "lightning" => "fire",
            _ => "",
        };

        public static bool Resonates(Unit actor, SkillDef skill) =>
            skill.Element.Length > 0 && actor.LastElement.Length > 0 && NextElement(actor.LastElement) == skill.Element;

        void Support(Unit actor, SkillDef skill, Unit target, CheckResult check)
        {
            switch (skill.Effect)
            {
                case "guard":
                    if (actor.GuardTarget >= 0) _units[actor.GuardTarget].GuardedBy = -1;
                    target.GuardedBy = actor.Id;
                    actor.GuardTarget = target.Id;
                    Emit(new BattleEvent { Kind = BattleEventKind.Guard, Actor = actor.Id, Target = target.Id, Text = $"{Korean.Subject(actor.Name)} {Korean.Object(target.Name)} 지킨다 (방어도 +{GuardArmor})" });
                    break;
                case "taunt":
                    int armor = (check?.Successes ?? 0) + (check != null && check.Perfect && skill.Perfect == "armor1" ? 1 : 0);
                    actor.TauntArmor = armor;
                    actor.Statuses[StatusKind.Taunt] = 1;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Status = StatusKind.Taunt, Amount = armor, Text = $"{actor.Name}: 도발! 모든 적이 노린다 (방어도 +{armor})" });
                    foreach (var monster in Monsters.Where(m => !m.Down && m.Intent != null && m.Intent.Skill.Target == MonsterTarget.One))
                    {
                        if (monster.Intent.Target == actor.Id) continue;
                        monster.Intent.Target = actor.Id;
                        EmitIntent(monster);
                    }
                    break;
                case "mark":
                    AddStatus(target, StatusKind.Mark, MarkTurns);
                    break;
                case "bless":
                    target.Hero.Blessing++;
                    foreach (var kind in new[] { StatusKind.Burn, StatusKind.Weak })
                        if (target.Statuses.Remove(kind))
                            Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = target.Id, Status = kind, Text = $"{target.Name}: {Korean.Subject(StatusNames.Name(kind))} 걷혔다" });
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Amount = 1, Text = $"{target.Name}: 축복 (다음 판정 확정 +1)" });
                    break;
                case "lucky":
                    target.Hero.Luck += 2;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Amount = 2, Text = $"{target.Name}: 행운의 동전 (다음 판정 확정 +2)" });
                    break;
            }
        }

        void AfterAttack(Unit actor, SkillDef skill, CheckResult check, int dealt)
        {
            if (skill.Effect == "lifesteal" && dealt > 1)
            {
                int before = actor.Hp;
                actor.Hero.Heal(dealt / 2);
                if (actor.Hp > before)
                    Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = actor.Id, Amount = actor.Hp - before, Text = $"{actor.Name}: 피의 갈증 +{actor.Hp - before}" });
            }
            if (skill.Effect == "backfire" && check.Successes <= BackfireAtMost)
                Hurt(actor, BackfireDamage, actor.Id, false, "판돈을 잃어", leaveOne: true);
        }
    }
}
