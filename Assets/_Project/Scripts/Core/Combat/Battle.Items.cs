using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // Consumables and running away (Docs/Combat.md 소모품, 도망).
    public sealed partial class Battle
    {
        public const int FleeDice = 3;
        public const int FleeNeed = 2;

        // Not from a boss.
        public bool CanFlee => !Monsters.Any(m => m.Monster.Boss);

        ItemDef ItemOf(string id)
        {
            if (_data != null && _data.Items.TryGetValue(id, out var item)) return item;
            // Without the sheets (some tests): the plain potion.
            return id == Hero.PotionId
                ? new ItemDef { Id = id, Name = "회복 물약", Slot = ItemSlot.Consumable, Use = new List<UpgradeEffect> { new UpgradeEffect { Verb = "heal", Target = "", Amount = PotionHeal } } }
                : null;
        }

        // Healing a potion gives this hero (the alchemist's are half again as strong).
        int HealOf(Unit actor, int amount) => Is(actor, "alchemist") ? amount * 3 / 2 : amount;

        SkillPreview PreviewUse(SkillPreview preview, Unit actor, BattleCommand command)
        {
            string id = command.Skill.Substring(BattleCommand.UsePrefix.Length);
            var item = ItemOf(id);
            preview.Item = item;
            if (item == null || !item.Consumable) return Fail(preview, Words.Line("battle.error.no_such_item", id));
            if (actor.Hero.Count(id) <= 0) return Fail(preview, Words.Line("battle.error.none_left", item.Name));
            var user = Unit(command.Target);
            if (user != null && user.Id != actor.Id && !item.Use.Any(u => u.Verb == "bomb")) return Fail(preview, Words.Line("battle.error.self_only", item.Name));
            if (item.Use.Any(u => u.Verb == "flee") && !CanFlee) return Fail(preview, Words.Line("battle.error.boss_flee"));
            preview.Odds = new CheckOdds(0, 100, 0);
            int value = 0;
            foreach (var use in item.Use)
            {
                if (use.Verb == "heal") value += Math.Min(HealOf(actor, use.Amount), actor.MaxHp - actor.Hp);
                if (use.Verb == "bomb") value += Monsters.Where(m => !m.Down).Sum(m => Mitigate(use.Amount, m, false));
            }
            preview.ValueBySuccesses = new[] { value };
            preview.Expected = value;
            return preview;
        }

        void DoUse(Unit actor, SkillPreview preview)
        {
            var item = preview.Item;
            var hero = actor.Hero;
            hero.SetCount(item.Id, hero.Count(item.Id) - 1);
            _lastAttacker = _lastAttacked = -1;
            Emit(new BattleEvent { Kind = BattleEventKind.Potion, Actor = actor.Id, Target = actor.Id, Amount = 0, Skill = BattleCommand.Use(item.Id), SkillName = item.Name, Text = $"{actor.Name}: {item.Name}" });
            foreach (var use in item.Use)
            {
                switch (use.Verb)
                {
                    case "heal":
                        int before = actor.Hp;
                        hero.Heal(HealOf(actor, use.Amount));
                        if (actor.Hp > before) Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = actor.Id, Amount = actor.Hp - before, Text = Words.Line("battle.heal", actor.Name, actor.Hp - before) });
                        break;
                    case "resolve":
                        hero.GainResolve(use.Amount);
                        Emit(new BattleEvent { Kind = BattleEventKind.ResolveGained, Actor = actor.Id, Amount = use.Amount, Text = Words.Line("battle.resolve", actor.Name, use.Amount) });
                        break;
                    case "cleanse":
                        foreach (var kind in new[] { StatusKind.Burn, StatusKind.Weak, StatusKind.Corrode })
                            if (actor.Statuses.Remove(kind))
                                Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = actor.Id, Status = kind, Text = Words.Line("battle.status.cleared", actor.Name, Words.Status(kind)) });
                        break;
                    case "luck":
                        hero.Luck += use.Amount;
                        Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Amount = use.Amount, Text = Words.Line("battle.luck", actor.Name, use.Amount) });
                        break;
                    case "bomb":
                        foreach (var monster in Monsters.Where(m => !m.Down).ToList())
                        {
                            Hurt(monster, Mitigate(use.Amount, monster, false), actor.Id, false, item.Name);
                            AddStatus(monster, StatusKind.Burn, BurnTurns);
                        }
                        break;
                    case "flee":
                        Escape(actor, Words.Line("battle.flee.smoke", actor.Name, item.Name));
                        return;
                }
            }
            AfterAction(actor, null);
            EndTurn(actor, PotionDelay);
            Advance();
        }

        // Running away: the current hero's agility, three dice, two successes; resolve can buy sure ones.
        SkillPreview PreviewFlee(SkillPreview preview, Unit actor, BattleCommand command)
        {
            if (!CanFlee) return Fail(preview, Words.Line("battle.error.boss_flee"));
            var hero = actor.Hero;
            preview.Dice = FleeDice;
            preview.Need = FleeNeed;
            preview.Chance = Dice.ClampChance(hero.Stat(Stat.Agility));
            int free = Math.Min(FleeDice, hero.Blessing + hero.Luck);
            if (free > 0) preview.Reasons.Add(Words.Line("battle.reason.blessing_luck", free));
            preview.Free = free;
            preview.ResolveRoom = Math.Min(hero.Resolve, FleeDice - free);
            if (command.Resolve < 0 || command.Resolve > preview.ResolveRoom) return Fail(preview, Words.Line("battle.error.resolve", preview.ResolveRoom));
            preview.Resolve = command.Resolve;
            preview.Odds = new CheckOdds(FleeDice, preview.Chance, free + command.Resolve);
            preview.ValueBySuccesses = new int[FleeDice + 1];
            preview.Expected = preview.Odds.AtLeast(FleeNeed);
            return preview;
        }

        void DoFlee(Unit actor, SkillPreview preview)
        {
            var hero = actor.Hero;
            hero.Resolve -= preview.Resolve;
            hero.Blessing = 0;
            hero.Luck = 0;
            var check = Fate.Roll(FleeDice, preview.Chance, preview.Guaranteed);
            Emit(new BattleEvent { Kind = BattleEventKind.Roll, Actor = actor.Id, Target = actor.Id, Skill = BattleCommand.Flee, SkillName = Words.Line("battle.flee"), Check = check, Perfect = check.Perfect, Text = Words.Line("battle.flee.roll", actor.Name, preview.Chance, FleeDice, check.Successes, FleeNeed) });
            hero.AddGrit(check.RolledFailures);
            _lastAttacker = _lastAttacked = -1;
            if (check.Successes >= FleeNeed)
            {
                Escape(actor, Words.Line("battle.flee.done", actor.Name));
                return;
            }
            Emit(new BattleEvent { Kind = BattleEventKind.Miss, Actor = actor.Id, Target = actor.Id, Text = Words.Line("battle.flee.failed", actor.Name) });
            AfterAction(actor, null);
            EndTurn(actor, 100);
            Advance();
        }

        void Escape(Unit actor, string text)
        {
            State = BattleState.Fled;
            CurrentId = -1;
            Emit(new BattleEvent { Kind = BattleEventKind.Fled, Actor = actor.Id, Text = text });
        }
    }
}
