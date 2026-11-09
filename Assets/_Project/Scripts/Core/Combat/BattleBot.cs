using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // A player stand-in for the balance sandbox, the tests and the playtest's "auto" command: picks a command for the
    // current hero. Smart plays the way a careful person would (revive, heal the hurt, protect, set up each class's
    // mechanic, then the best expected damage, resolve on big checks); otherwise it picks any legal command.
    public static class BattleBot
    {
        public static BattleCommand Choose(Battle battle, GameRandom random, bool smart = true)
        {
            var actor = battle.Current;
            if (actor == null) return null;
            var options = Options(battle, actor);
            if (options.Count == 0) return new BattleCommand(actor.Id, Hero.AttackId, battle.Monsters.First(m => !m.Down).Id);
            if (!smart)
            {
                var pick = options[random.Next(options.Count)];
                var preview = battle.Preview(pick);
                int resolve = preview.ResolveRoom > 0 && random.Next(3) == 0 ? random.Next(preview.ResolveRoom + 1) : 0;
                return new BattleCommand(pick.Actor, pick.Skill, pick.Target, resolve);
            }
            return Smart(battle, actor, options);
        }

        // Every command the hero could give now, with no resolve.
        static List<BattleCommand> Options(Battle battle, Unit actor)
        {
            var options = new List<BattleCommand>();
            foreach (var skill in actor.Hero.Skills)
            {
                if (battle.Cooldown(actor, skill) > 0) continue;
                foreach (var target in battle.Targets(actor, skill))
                {
                    var command = new BattleCommand(actor.Id, skill.Id, target.Id);
                    if (battle.Preview(command).Ok) options.Add(command);
                    if (skill.Target == SkillTarget.AllEnemies || skill.Target == SkillTarget.Self) break;
                }
            }
            if (actor.Hero.Potions > 0 && actor.Hp < actor.MaxHp) options.Add(new BattleCommand(actor.Id, BattleCommand.Potion, actor.Id));
            return options;
        }

        static BattleCommand Smart(Battle battle, Unit actor, List<BattleCommand> options)
        {
            var hero = actor.Hero;
            var living = battle.Heroes.Where(h => !h.Down).ToList();
            var enemies = battle.Monsters.Where(m => !m.Down).ToList();
            SkillDef SkillOf(BattleCommand o) => hero.Skill(o.Skill);
            BattleCommand Find(string effectOrKind, int target = -1) => options.FirstOrDefault(o =>
            {
                var skill = SkillOf(o);
                if (skill == null) return false;
                bool matches = skill.Effect == effectOrKind || skill.Kind.ToString() == effectOrKind;
                return matches && (target < 0 || o.Target == target);
            });

            // Revive the fallen first.
            var revive = Find(nameof(SkillKind.Revive));
            if (revive != null) return revive;

            // Heal whoever is lowest when someone is under half.
            var hurt = living.OrderBy(h => h.Hp * 100 / h.MaxHp).First();
            if (hurt.Hp * 2 < hurt.MaxHp)
            {
                var heal = Find(nameof(SkillKind.Heal), hurt.Id);
                if (heal != null) return WithResolve(battle, heal, hurt.Hp * 4 < hurt.MaxHp ? 1 : 0);
            }

            // Someone aiming at a hurt ally: guard it (knight or wolf), or the knight taunts when several aim at others.
            var threatened = enemies.Select(m => battle.IntentTarget(m)).Where(t => t != null && t.IsHero && t.Id != actor.Id).ToList();
            var guardee = threatened.Where(t => t.Hp * 2 < t.MaxHp).OrderBy(t => t.Hp).FirstOrDefault();
            if (guardee != null)
            {
                var guard = Find("guard", guardee.Id) ?? Find("wolfguard", guardee.Id);
                if (guard != null) return guard;
            }
            if (threatened.Count >= 2)
            {
                var taunt = Find("taunt");
                if (taunt != null && actor.Hp * 2 > actor.MaxHp) return taunt;
            }
            if (actor.Hp * 2 < actor.MaxHp && enemies.Any(m => battle.IntentTarget(m) == actor))
            {
                var hide = Find("hide");
                if (hide != null) return hide;
            }

            // Each class's set-up, when it is worth it.
            if (battle.Song == Song.None)
            {
                bool hurtParty = living.Count(h => h.Hp * 3 < h.MaxHp * 2) >= 2;
                var song = Find(hurtParty ? "song_rest" : "song_courage") ?? Find("song_courage");
                if (song != null) return song;
            }
            var raise = Find("raise");
            if (raise != null) return raise;
            if (enemies.Count >= 2 && !enemies.Any(e => e.Has(StatusKind.Foretold)))
            {
                var foretell = Find("foretell");
                if (foretell != null) return foretell;
            }
            var foresight = battle.Foresee();
            if (foresight.Count > 0 && foresight[0] >= 80)
            {
                var twist = Find("twist");
                if (twist != null) return twist;
            }
            var wolf = battle.Summons.FirstOrDefault(s => s.Owner == actor.Id);
            if (wolf != null && (wolf.Down || wolf.Hp * 2 < wolf.MaxHp))
            {
                var call = Find("call");
                if (call != null) return call;
            }
            if (!enemies.Any(e => e.Has(StatusKind.Mark)))
            {
                var tough = enemies.OrderByDescending(e => e.Hp).First();
                if (tough.Hp >= 15)
                {
                    var mark = Find("mark", tough.Id) ?? Find("deathmark", tough.Id);
                    if (mark != null) return mark;
                }
            }

            // A potion when low and nobody can heal.
            if (actor.Hp * 10 < actor.MaxHp * 3 && hero.Potions > 0 && !living.Any(h => h.Hero.Skills.Any(s => s.Kind == SkillKind.Heal)))
                return new BattleCommand(actor.Id, BattleCommand.Potion, actor.Id);

            // Otherwise the attack with the best expected damage, finishing a monster off counting double.
            BattleCommand best = null;
            double bestScore = -1;
            foreach (var option in options)
            {
                var preview = battle.Preview(option);
                if (preview.Skill == null || !preview.Skill.Damages) continue;
                double score = preview.Expected;
                var target = battle.Unit(option.Target);
                if (preview.Skill.Target == SkillTarget.Enemy && target != null && preview.Expected >= target.Hp) score *= 2;
                if (preview.Skill.Effect == "backfire" && actor.Hp <= Battle.BackfireDamage + 2) score *= 0.3;
                // A finisher waits for a few combos.
                if (preview.Skill.Effect == "finisher" && actor.Combo < 3 && !(target != null && preview.Expected >= target.Hp)) score *= 0.5;
                if (preview.Skill.CostKind == "reagent" && hero.Reagents <= 1) score *= 0.8;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = option;
                }
            }
            if (best == null)
            {
                var support = options.FirstOrDefault(o => o.Skill != BattleCommand.Potion);
                return support ?? options[0];
            }
            var bestSkill = battle.Preview(best).Skill;
            int dice = battle.DiceOf(actor, bestSkill);
            int resolve = hero.Resolve >= 2 && dice >= 3 ? 1 : 0;
            if (dice >= 4 && hero.Resolve >= 3) resolve = 2;
            return WithResolve(battle, best, resolve);
        }

        static BattleCommand WithResolve(Battle battle, BattleCommand command, int resolve)
        {
            int room = battle.Preview(command).ResolveRoom;
            return new BattleCommand(command.Actor, command.Skill, command.Target, System.Math.Min(room, resolve));
        }
    }
}
