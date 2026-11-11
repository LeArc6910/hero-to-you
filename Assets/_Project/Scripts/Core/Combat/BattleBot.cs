using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // A player stand-in for the balance sandbox, the tests and the playtest's "auto" command: picks a command for the
    // current hero. Smart plays the way a careful person would (revive, heal the hurt, protect, set up each class's
    // mechanic, lift an ally's next big roll or bring its turn forward, then the best expected damage with what a
    // stun, a chill or a weakening is worth, resolve on big checks); otherwise it picks any legal command.
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
            foreach (var own in actor.Hero.Skills)
            {
                var skill = battle.SkillOf(actor, own.Id);
                if (battle.Cooldown(actor, skill) > 0) continue;
                if (skill.Target == SkillTarget.AllAllies)
                {
                    var all = new BattleCommand(actor.Id, skill.Id, actor.Id);
                    if (battle.Preview(all).Ok) options.Add(all);
                    continue;
                }
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

        // How much a support that costs this much of a turn has to give, against the hero's own best blow now.
        const double SupportShare = 0.6;

        static BattleCommand Smart(Battle battle, Unit actor, List<BattleCommand> options)
        {
            var hero = actor.Hero;
            var living = battle.Heroes.Where(h => !h.Down).ToList();
            var enemies = battle.Monsters.Where(m => !m.Down).ToList();
            SkillDef SkillOf(BattleCommand o) => battle.SkillOf(actor, o.Skill);
            BattleCommand Find(string effectOrKind, int target = -1) => options.FirstOrDefault(o =>
            {
                var skill = SkillOf(o);
                if (skill == null) return false;
                bool matches = skill.Effect == effectOrKind || skill.Kind.ToString() == effectOrKind;
                return matches && (target < 0 || o.Target == target);
            });
            // The blows aimed at a hero now (raw power, the guard taking them for whoever it guards).
            int Incoming(Unit unit) => enemies.Where(m => battle.IntentTarget(m) == unit).Sum(m => Blow(m));

            // Revive the fallen first.
            var revive = Find(nameof(SkillKind.Revive));
            if (revive != null) return revive;

            // Heal everyone when several are hurt.
            if (living.Count(h => h.Hp * 5 < h.MaxHp * 3) >= 2)
            {
                var mist = options.FirstOrDefault(o => SkillOf(o) is SkillDef s && s.Kind == SkillKind.Heal && s.Target == SkillTarget.AllAllies);
                if (mist != null) return mist;
            }

            // Heal whoever is lowest when someone is under half.
            var hurt = living.OrderBy(h => h.Hp * 100 / h.MaxHp).First();
            if (hurt.Hp * 2 < hurt.MaxHp)
            {
                var heal = Find(nameof(SkillKind.Heal), hurt.Id);
                if (heal != null) return WithResolve(battle, heal, hurt.Hp * 4 < hurt.MaxHp ? 1 : 0);
            }

            // Protect (Docs/Classes.md 기사): guard an ally a big blow is coming at (the knight, the wolf, an echo of
            // the guard), or the knight in front taunts when several aim at others. The knight answers blows taken
            // while guarding or taunting, so a healthy knight guards whoever is aimed at even when it is not in danger.
            var threatened = enemies.Select(m => battle.IntentTarget(m)).Where(t => t != null && t.IsHero && t.Id != actor.Id).Distinct().ToList();
            bool sturdy = actor.Hp * 2 > actor.MaxHp;
            int aimingAtOthers = enemies.Count(m => battle.IntentTarget(m) is Unit t && t.IsHero && t.Id != actor.Id);
            if (aimingAtOthers >= 2 && sturdy && Battle.InFront(actor))
            {
                var taunt = Find("taunt");
                if (taunt != null) return taunt;
            }
            var guardee = threatened.Where(t => t.Hp * 2 < t.MaxHp || Incoming(t) * 10 >= t.Hp * 4).OrderByDescending(t => Incoming(t) * 100 / t.Hp).FirstOrDefault();
            if (guardee != null)
            {
                var guard = (actor.Hp > guardee.Hp && actor.Hp * 10 > actor.MaxHp * 4 ? Find("guard", guardee.Id) : null) ?? Find("wolfguard", guardee.Id);
                if (guard != null) return guard;
            }
            if (Battle.Riposte(actor) > 0 && sturdy && threatened.Count > 0)
            {
                var aimed = threatened.OrderByDescending(t => Incoming(t)).First();
                var guard = Find("guard", aimed.Id);
                if (guard != null) return guard;
            }
            if (actor.Hp * 2 < actor.MaxHp && enemies.Any(m => battle.IntentTarget(m) == actor))
            {
                var hide = Find("hide");
                if (hide != null) return hide;
            }

            // Each class's set-up, when it is worth it.
            bool hurtParty = living.Count(h => h.Hp * 3 < h.MaxHp * 2) >= 2;
            if (battle.Song == Song.None)
            {
                var song = Find(hurtParty ? "song_rest" : "song_courage") ?? Find("song_courage");
                if (song != null) return song;
            }
            // Courage gives way to rest once the party is worn down and nobody has a heal ready.
            else if (battle.Song == Song.Courage && living.Count(h => h.Hp * 2 < h.MaxHp) >= 2)
            {
                var rest = Find("song_rest");
                if (rest != null) return rest;
            }
            var raise = Find("raise");
            if (raise != null) return raise;
            // The engineer's turret stands in front and shoots: build it, or build it again once it is broken.
            if (!battle.Summons.Any(u => u.Owner == actor.Id && !u.Down))
            {
                var turret = Find("turret");
                if (turret != null) return turret;
            }
            // The skeleton draws the blows when big ones are coming at the living.
            if (aimingAtOthers >= 2 || enemies.Any(m => m.Monster.Boss && m.Intent != null && m.Intent.Skill.Power >= 14))
            {
                var wall = Find("bonewall");
                if (wall != null) return wall;
            }
            // Foretell a crowd, or a boss (its wind-ups and storms are what a careful player answers, Docs/Combat.md 보스).
            if ((enemies.Count >= 2 || enemies.Any(e => e.Monster.Boss)) && !enemies.Any(e => e.Has(StatusKind.Foretold)))
            {
                var foretell = Find("foretell");
                if (foretell != null) return foretell;
            }
            var foresight = battle.Foresee();
            // A bad number is thrown away only when the party's side would roll it; a foe's bad number is a miss.
            if (foresight.Count > 0 && foresight[0] >= 80 && battle.PartyActsNext(actor, 40))
            {
                var twist = Find("twist");
                if (twist != null) return twist;
            }
            // The seer guides whoever will strike hardest next.
            if (!living.Any(h => h.Hero.Blessing > 0))
            {
                var striker = living.Where(h => h.Id != actor.Id).OrderByDescending(h => h.Hero.Level * 10 + h.Hp).FirstOrDefault();
                var guide = striker == null ? null : options.FirstOrDefault(o => SkillOf(o)?.Id == "seer_guide");
                if (guide != null) return guide;
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

            // The monk breathes when its ki cannot pay for a palm, or to mend when nobody else can.
            var meditate = Find("meditate");
            if (meditate != null && (actor.Ki < 2 && actor.Hp + Battle.MeditateHeal <= actor.MaxHp || actor.Ki == 0 || actor.Hp * 2 < actor.MaxHp))
                return meditate;

            // What the hero's own best blow is worth now, to weigh lifting someone else against it.
            double own = options.Select(o => battle.Preview(o)).Where(p => p.Ok && p.Skill != null && p.Skill.Damages).Select(p => p.Expected).DefaultIfEmpty(0).Max();

            // A blessing or a lucky coin (or an echo of one) on whoever's next roll it lifts most, clearing a burn or a
            // weakening on the way (Docs/Classes.md 성직자, 도박사).
            BattleCommand lift = null;
            double lifted = 0;
            foreach (var option in options)
            {
                var skill = SkillOf(option);
                if (skill == null || (skill.Effect != "bless" && skill.Effect != "lucky") || skill.Target == SkillTarget.AllAllies) continue;
                var target = battle.Unit(option.Target);
                if (target == null || !target.IsHero || target.Hero.Blessing + target.Hero.Luck > 0) continue;
                int sure = skill.Effect == "lucky" ? 2 : 1;
                double gain = StrikeValue(battle, target, sure, enemies) - StrikeValue(battle, target, 0, enemies);
                if (skill.Effect == "bless")
                {
                    if (target.Has(StatusKind.Burn)) gain += Battle.BurnDamage * target.Status[StatusKind.Burn];
                    if (target.Has(StatusKind.Weak)) gain += StrikeValue(battle, target, 0, enemies) * (100 - Battle.WeakPercent) / 100;
                }
                // A blessing kept for oneself waits a turn; one given lands on someone already on the way.
                if (target.Id == actor.Id) gain *= 0.7;
                if (gain > lifted)
                {
                    lifted = gain;
                    lift = option;
                }
            }
            if (lift != null && lifted >= own * SupportShare) return lift;

            // An encore brings forward the turn of whoever will do most with it: a healer when someone is low, else
            // the hardest striker, when its turn is still a good way off (Docs/Classes.md 음유시인).
            BattleCommand encore = null;
            double sooner = 0;
            foreach (var option in options)
            {
                var skill = SkillOf(option);
                if (skill == null || skill.Effect != "encore") continue;
                var target = battle.Unit(option.Target);
                if (target == null || target.Down || target.Id == actor.Id) continue;
                long turn = Battle.Clock / System.Math.Max(1, target.Speed);
                double share = System.Math.Min(1.0, (double)(target.Ready - actor.Ready) / turn);
                if (share < 0.4) continue;
                double value = StrikeValue(battle, target, target.Hero.Blessing + target.Hero.Luck, enemies);
                bool needed = living.Any(h => h.Hp * 5 < h.MaxHp * 2) || living.Count < battle.Heroes.Count();
                if (needed && target.Hero.Skills.Any(s => s.Kind == SkillKind.Heal || s.Kind == SkillKind.Revive)) value = System.Math.Max(value, 20);
                value *= share;
                if (value > sooner)
                {
                    sooner = value;
                    encore = option;
                }
            }
            if (encore != null && sooner >= own * SupportShare) return encore;

            // A potion when low and nobody can heal.
            if (actor.Hp * 10 < actor.MaxHp * 3 && hero.Potions > 0 && !living.Any(h => h.Hero.Skills.Any(s => s.Kind == SkillKind.Heal)))
                return new BattleCommand(actor.Id, BattleCommand.Potion, actor.Id);
            // Consumables (Docs/Combat.md 소모품): run from a lost fight, burn a crowd, steady a big turn.
            var monstersLeft = enemies.Sum(e => e.Hp);
            var partyLeft = living.Sum(h => h.Hp);
            if (battle.CanFlee && living.Count == 1 && actor.Hp * 10 < actor.MaxHp * 3 && monstersLeft > partyLeft * 2)
            {
                if (hero.Count("smoke") > 0) return new BattleCommand(actor.Id, BattleCommand.Use("smoke"), actor.Id);
            }
            if (hero.Count("firebomb") > 0 && enemies.Count >= 3) return new BattleCommand(actor.Id, BattleCommand.Use("firebomb"), actor.Id);
            if (hero.Count("antidote") > 0 && actor.Hp * 2 < actor.MaxHp && (actor.Has(StatusKind.Burn) || actor.Has(StatusKind.Corrode)))
                return new BattleCommand(actor.Id, BattleCommand.Use("antidote"), actor.Id);
            if (hero.Count("tonic") > 0 && hero.Resolve == 0 && enemies.Any(e => e.Monster.Boss)) return new BattleCommand(actor.Id, BattleCommand.Use("tonic"), actor.Id);

            // Otherwise the attack with the best expected damage, finishing a monster off counting double, and what
            // a stun, a chill, a weakening or a burn takes off the foes' coming blows counted too.
            BattleCommand best = null;
            double bestScore = -1;
            foreach (var option in options)
            {
                var preview = battle.Preview(option);
                if (preview.Skill == null || !preview.Skill.Damages) continue;
                double score = preview.Expected;
                var target = battle.Unit(option.Target);
                bool kills = preview.Skill.Target == SkillTarget.Enemy && target != null && preview.Expected >= target.Hp;
                if (kills) score *= 2;
                else score += Control(battle, actor, preview, target, enemies, living.Count);
                if (preview.Skill.Effect == "backfire" && actor.Hp <= Battle.BackfireDamage + 2) score *= 0.3;
                // A finisher waits for a few combos.
                if (preview.Skill.Effect == "finisher" && actor.Combo < 3 && !kills) score *= 0.5;
                // All In waits for a stake (Docs/Classes.md 도박사).
                if (preview.Skill.Effect == "backfire" && actor.Stake < 2 && !kills) score *= 0.5;
                if (preview.Skill.CostKind == "reagent" && hero.Reagents <= 1) score *= 0.8;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = option;
                }
            }
            if (best == null)
            {
                var support = options.FirstOrDefault(o => !BattleCommand.IsUse(o.Skill));
                return support ?? options[0];
            }
            var bestSkill = battle.Preview(best).Skill;
            int dice = battle.DiceOf(actor, bestSkill);
            int resolve = hero.Resolve >= 2 && dice >= 3 ? 1 : 0;
            if (dice >= 4 && hero.Resolve >= 3) resolve = 2;
            return WithResolve(battle, best, resolve);
        }

        // A monster's next blow at full strength: its intent's power, or for a wind-up the hardest blow it has.
        static int Blow(Unit monster)
        {
            var intent = monster.Intent?.Skill;
            if (intent != null && intent.Damages) return intent.Power + monster.PowerBonus;
            var pattern = monster.Monster?.Pattern;
            return pattern == null || pattern.Count == 0 ? 0 : pattern.Max(s => s.Damages ? s.Power : 0) + monster.PowerBonus;
        }

        // What the foe's next action is likely to take off the party: about six in ten of its blow, on everyone for
        // an attack on all.
        static double Threat(Unit monster, int heroes)
        {
            var intent = monster.Intent?.Skill;
            int targets = intent != null && intent.Damages && intent.Target == MonsterTarget.All ? heroes : 1;
            return Blow(monster) * targets * 0.6;
        }

        // The part of the foes' coming blows an attack's rider takes away, by its odds: a stun skips a turn, a chill
        // or a resonant frost puts one off, a weakening softens two, a burn and a corrosion add damage, the wolf bites
        // after a pack attack.
        static double Control(Battle battle, Unit actor, SkillPreview preview, Unit target, List<Unit> enemies, int heroes)
        {
            var skill = preview.Skill;
            var odds = preview.Odds;
            if (odds == null || odds.Dice == 0) return 0;
            var targets = skill.Target == SkillTarget.AllEnemies ? enemies : target != null && !target.Down ? new List<Unit> { target } : new List<Unit>();
            double hit = odds.AtLeast(1);
            double value = 0;
            foreach (var t in targets)
            {
                if (t.Has(StatusKind.Stun)) continue;
                double threat = Threat(t, heroes);
                switch (skill.Effect)
                {
                    case "snare":
                        value += odds.AtLeast(2) * threat;
                        break;
                    case "chill":
                        if (skill.Element == "frost" && Battle.Resonates(actor, skill)) value += hit * threat;
                        else if (!t.Has(StatusKind.Chill)) value += hit * threat * 0.5;
                        break;
                    case "weaken":
                        if (!t.Has(StatusKind.Weak)) value += hit * threat * (100 - Battle.WeakPercent) / 100 * 1.5;
                        break;
                    case "burn":
                        if (!t.Has(StatusKind.Burn)) value += hit * Battle.BurnDamage * Battle.BurnTurns;
                        break;
                    case "corrode":
                        if (!t.Has(StatusKind.Corrode)) value += hit * 4;
                        break;
                    case "command":
                        var wolf = battle.Summons.FirstOrDefault(s => s.Owner == actor.Id && !s.Down);
                        if (wolf != null) value += 0.6 * (wolf.Monster.Pattern[0].Power + wolf.PowerBonus);
                        break;
                }
                if (skill.Perfect == "stun") value += odds.Perfect * threat;
            }
            return value;
        }

        // What a hero's best blow on its next turn is worth (after the toughest foe's armor), with `sure` more sure
        // successes: for weighing a blessing, a lucky coin or an encore on it. Skills still waiting or unpaid are left out.
        static double StrikeValue(Battle battle, Unit unit, int sure, List<Unit> enemies)
        {
            if (unit == null || !unit.IsHero || unit.Down || enemies.Count == 0) return 0;
            var foe = enemies.OrderByDescending(e => e.Hp).First();
            double best = 0;
            foreach (var own in unit.Hero.Skills)
            {
                var skill = battle.SkillOf(unit, own.Id);
                if (skill == null || !skill.Damages || !skill.Rolls || battle.Cooldown(unit, skill) > 0) continue;
                if (skill.CostKind == "reagent" && unit.Hero.Reagents < skill.CostAmount) continue;
                if (skill.CostKind == "ki" && unit.Ki < skill.CostAmount) continue;
                if (skill.CostKind == "soul" && unit.Souls < skill.CostAmount) continue;
                if (skill.Effect == "finisher" && unit.Combo == 0) continue;
                int dice = battle.DiceOf(unit, skill);
                int free = sure + unit.Hero.Blessing + unit.Hero.Luck + (skill.Effect == "backfire" ? unit.Stake : 0);
                var odds = new CheckOdds(dice, battle.ChanceOf(unit, skill), System.Math.Min(dice, free));
                int power = battle.PowerOf(unit, skill);
                if (unit.Has(StatusKind.Weak)) power = Dice.Percent(power, Battle.WeakPercent);
                int armor = skill.Effect == "pierce" ? 0 : skill.Kind == SkillKind.Physical ? foe.Armor : foe.Ward;
                int count = skill.Target == SkillTarget.AllEnemies ? enemies.Count : 1;
                double expected = 0;
                for (int s = 0; s <= dice; s++)
                {
                    int value = Dice.Value(power, skill.Floor, s, dice);
                    if (s == dice && dice > 0) value = Dice.Percent(value, skill.PerfectPercent);
                    expected += odds.BySuccesses[s] * (value <= 0 ? 0 : System.Math.Max(1, value - armor)) * count;
                }
                if (expected > best) best = expected;
            }
            return best;
        }

        static BattleCommand WithResolve(Battle battle, BattleCommand command, int resolve)
        {
            int room = battle.Preview(command).ResolveRoom;
            return new BattleCommand(command.Actor, command.Skill, command.Target, System.Math.Min(room, resolve));
        }
    }
}
