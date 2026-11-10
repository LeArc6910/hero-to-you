using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    public enum Song
    {
        None,
        Courage,
        Rest,
    }

    // The classes' own rules (Docs/Classes.md): rage, the gambler's fixed chance, the element cycle, combos and
    // hiding, songs, reagents, souls and the skeleton, the seer's foresight, the wolf, and the effects of support
    // skills and attacks with something extra.
    public sealed partial class Battle
    {
        public const int RageCap = 25;
        public const int GuardArmor = 2;
        public const int CorrodeTurns = 3;
        public const int ComboCap = 5;
        public const int ComboPower = 4;
        public const int SoulCap = 6;
        public const int CourageBonus = 10;
        public const int RestHealBase = 3;
        public const int ForetellCut = 30;
        public const int Foresight = 3;
        public const int WolfCallHeal = 8;
        public const int SkeletonHp = 20;
        public const int BoneWallArmor = 3;

        // The skeleton grows with its master: +2 life a level, like the engineer's turret.
        static int SkeletonLife(Unit owner) => SkeletonHp + owner.Hero.Special("skeleton_hp") + 2 * (owner.Hero.Level - 1);
        public const int WolfHp = 18;
        public const int KiCap = 5;
        public const int MeditateKi = 2;
        public const int MeditateHeal = 6;
        public const int TurretHp = 24;
        public const string EchoId = "mnem_echo";

        // Effects the mnemonist can echo: those that need nothing of the class that first used them.
        static readonly string[] Echoable = { "", "burn", "chill", "lifesteal", "mark", "bless", "lucky", "weaken", "corrode", "foretell", "guard", "taunt", "encore", "song_courage", "song_rest", "snare", "pierce" };

        // The last skill a hero other than the mnemonist used, for its echo.
        SkillDef _lastAllySkill;

        // The skill a command names for this unit: its own, or for the mnemonist's echo, the last ally skill made
        // its own (no cost, its own cooldown, rolled with the mnemonist's abilities).
        public SkillDef SkillOf(Unit actor, string id)
        {
            var own = actor.Hero?.Skill(id);
            if (own == null || own.Effect != "echo") return own;
            if (_lastAllySkill == null) return own;
            var echo = _lastAllySkill.Clone();
            echo.Id = own.Id;
            echo.Name = $"{own.Name}: {_lastAllySkill.Name}";
            echo.Cooldown = own.Cooldown;
            echo.CostKind = "";
            echo.CostAmount = 0;
            echo.Text = Words.Line("battle.echo.text", _lastAllySkill.Name, _lastAllySkill.Text);
            return echo;
        }

        // The song the bard keeps up, and which unit sings it (-1 for none).
        public Song Song { get; private set; }
        public int SongBy { get; private set; } = -1;

        int SongPower => SongBy >= 0 ? _units[SongBy].Hero?.Special("song") ?? 0 : 0;
        int RestHeal => RestHealBase + SongPower * 2 / 5;

        static bool Is(Unit unit, string classId) => unit.IsHero && unit.Hero.Class.Id == classId;

        static bool Ambusher(Unit unit) => Is(unit, "rogue");

        // Set up at the start: a weakened start, the beastmaster's wolf, the necromancer's first souls.
        void StartWith(Unit unit, bool weakStart)
        {
            if (!unit.IsHero || unit.Down) return;
            if (weakStart) unit.Statuses[StatusKind.Weak] = WeakTurns;
            if (Is(unit, "necromancer"))
            {
                unit.Souls = 2 + unit.Hero.Special("soul_start");
                // The necromancer walks in with a skeleton already up: it stands in front (Docs/Classes.md).
                Summon(unit, GameData.SkeletonId, SkeletonLife(unit), 0);
            }
            if (Is(unit, "monk")) unit.Ki = 1 + unit.Hero.Special("ki_start");
            if (Is(unit, "beastmaster")) Summon(unit, GameData.CompanionWolfId, WolfHp + 2 * (unit.Hero.Level - 1) + unit.Hero.Special("wolf_hp"), unit.Hero.Special("wolf_power"));
        }

        MonsterDef SummonDef(string id)
        {
            if (_data != null && _data.Monsters.TryGetValue(id, out var def)) return def;
            // Without the sheets (some tests): a stand-in with the same shape.
            return new MonsterDef
            {
                Id = id,
                Name = id == GameData.SkeletonId ? "해골" : "늑대",
                Hp = id == GameData.SkeletonId ? SkeletonHp : WolfHp,
                Speed = id == GameData.SkeletonId ? 10 : 14,
                Accuracy = 70,
                Armor = id == GameData.SkeletonId ? 1 : 0,
                Pattern = new List<MonsterSkillDef>
                {
                    new MonsterSkillDef { Id = "summon_attack", Name = "공격", Dice = id == GameData.SkeletonId ? 2 : 3, Power = 6, Kind = "physical", Target = MonsterTarget.One, Aim = MonsterAim.Random, Delay = 100, Effect = "", Text = "" },
                },
                Color = "#CCCCCC",
                Size = 1,
            };
        }

        Unit Summon(Unit owner, string id, int hp, int powerBonus)
        {
            var def = SummonDef(id);
            var unit = new Unit(_units.Count, def, def.Name, Side.Party, owner.Id, hp) { PowerBonus = powerBonus };
            _units.Add(unit);
            return unit;
        }

        Unit SummonOf(Unit owner, string id) => _units.FirstOrDefault(u => u.IsSummon && u.Owner == owner.Id && u.Monster.Id == id);

        // A hero's chance for one die of the skill: a fixed chance (the gambler), or the ability plus rage and the
        // song of courage.
        public int ChanceOf(Unit actor, SkillDef skill)
        {
            if (skill.FixedChance > 0) return skill.FixedChance;
            int chance = skill.Stat.HasValue ? actor.Hero.Stat(skill.Stat.Value) : 50;
            if (Song == Song.Courage && actor.Side == Side.Party) chance += CourageBonus + SongPower;
            return Dice.ClampChance(chance + Rage(actor));
        }

        // Berserker: a third of the health lost, in percent, up to +25.
        public static int Rage(Unit actor)
        {
            if (!Is(actor, "berserker") || actor.MaxHp <= 0) return 0;
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

        // The rogue's finisher rolls a die more for each combo.
        public int DiceOf(Unit actor, SkillDef skill)
        {
            int dice = skill.Effect == "finisher" ? Math.Min(9, skill.Dice + actor.Combo) : skill.Dice;
            // The bell's daze: one die fewer, never none.
            if (actor.Has(StatusKind.Daze) && dice > 1) dice--;
            return dice;
        }

        // Why a skill cannot be used now when a boss is the reason: "seal" or "erase" (Docs/Combat.md 보스의 고유
        // 규칙). Null when it is free or only waiting its own cooldown.
        public string BlockedBy(Unit unit, SkillDef skill) =>
            skill != null && Cooldown(unit, skill) > 0 && unit.Blocked.TryGetValue(skill.Id, out string why) ? why : null;

        public int PowerOf(Unit actor, SkillDef skill) => skill.Effect == "finisher" ? skill.Power + ComboPower * actor.Combo : skill.Power;

        public int ComboMax(Unit actor) => ComboCap + (actor.IsHero ? actor.Hero.Special("combo_cap") : 0);

        int PotionOf(Unit actor) => Is(actor, "alchemist") ? PotionHeal * 3 / 2 : PotionHeal;

        // The values the next dice will get, for a party with a seer standing (Docs/Classes.md 별의 눈). Empty without.
        public List<int> Foresee()
        {
            var seer = Heroes.FirstOrDefault(h => !h.Down && Is(h, "seer"));
            var values = new List<int>();
            if (seer == null) return values;
            int count = Foresight + seer.Hero.Special("foresight");
            for (int i = 0; i < count; i++) values.Add(Fate.Peek(i));
            return values;
        }

        // Why the skill cannot be used now beyond its cooldown and target, or null.
        string CannotUse(Unit actor, SkillDef skill)
        {
            if (skill.CostKind == "reagent" && actor.Hero.Reagents < skill.CostAmount) return Words.Line("battle.error.reagents", skill.Name, actor.Hero.Reagents, skill.CostAmount);
            if (skill.CostKind == "soul" && actor.Souls < skill.CostAmount) return Words.Line("battle.error.souls", skill.Name, actor.Souls, skill.CostAmount);
            if (skill.CostKind == "ki" && actor.Ki < skill.CostAmount) return Words.Line("battle.error.ki", skill.Name, actor.Ki, skill.CostAmount);
            if (skill.Effect == "echo") return Words.Line("battle.error.echo_nothing");
            if (skill.Id == EchoId && Array.IndexOf(Echoable, skill.Effect) < 0) return Words.Line("battle.error.echo_cannot", skill.Name);
            if (skill.Effect == "raise" && SummonOf(actor, GameData.SkeletonId) is Unit skeleton && !skeleton.Down) return Words.Line("battle.error.one_skeleton");
            if (skill.Effect == "bonewall" && !(SummonOf(actor, GameData.SkeletonId) is Unit bones && !bones.Down)) return Words.Line("battle.error.no_skeleton");
            if (skill.Effect == "finisher" && actor.Combo == 0) return Words.Line("battle.error.no_combo", skill.Name);
            if ((skill.Effect == "wolfguard" || skill.Effect == "command") && !(SummonOf(actor, GameData.CompanionWolfId) is Unit wolf && !wolf.Down))
                return Words.Line("battle.error.wolf_down");
            return null;
        }

        void PayCost(Unit actor, SkillDef skill)
        {
            if (skill.CostKind == "reagent") actor.Hero.Reagents -= skill.CostAmount;
            if (skill.CostKind == "soul") actor.Souls -= skill.CostAmount;
            if (skill.CostKind == "ki") actor.Ki -= skill.CostAmount;
        }

        // After any action: the rogue's combo grows (the finisher spent it before).
        void AfterAction(Unit actor, SkillDef skill)
        {
            if (Is(actor, "rogue") && skill?.Effect != "finisher") actor.Combo = Math.Min(ComboMax(actor), actor.Combo + 1);
        }

        void GainSouls(bool marked)
        {
            foreach (var necro in Heroes.Where(h => !h.Down && Is(h, "necromancer")))
            {
                int gain = marked ? 3 : 1;
                necro.Souls = Math.Min(SoulCap, necro.Souls + gain);
                Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = necro.Id, Target = necro.Id, Amount = gain, Text = Words.Line("battle.souls", necro.Name, gain, necro.Souls) });
            }
        }

        void EndSong()
        {
            if (Song == Song.None) return;
            Song = Song.None;
            SongBy = -1;
            Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Text = Words.Line("battle.song.end") });
        }

        void Guard(Unit guard, Unit target, Unit until)
        {
            if (guard.GuardTarget >= 0) EndGuard(_units[guard.GuardTarget]);
            if (target.GuardedBy >= 0) EndGuard(target);
            target.GuardedBy = guard.Id;
            target.GuardUntil = until.Id;
            guard.GuardTarget = target.Id;
            Emit(new BattleEvent { Kind = BattleEventKind.Guard, Actor = guard.Id, Target = target.Id, Text = Words.Line("battle.guard", guard.Name, target.Name, GuardArmor) });
        }

        void Support(Unit actor, SkillDef skill, Unit target, CheckResult check)
        {
            switch (skill.Effect)
            {
                case "guard":
                    Guard(actor, target, actor);
                    break;
                case "taunt":
                    int armor = (check?.Successes ?? 0) + (check != null && check.Perfect && skill.Perfect == "armor1" ? 1 : 0);
                    actor.TauntArmor = armor;
                    actor.Statuses[StatusKind.Taunt] = 1;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Status = StatusKind.Taunt, Amount = armor, Text = Words.Line("battle.taunt", actor.Name, armor) });
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
                            Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = target.Id, Status = kind, Text = Words.Line("battle.status.cleared", target.Name, Words.Status(kind)) });
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Amount = 1, Text = Words.Line("battle.bless", target.Name) });
                    break;
                case "lucky":
                    target.Hero.Luck += 2;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Amount = 2, Text = Words.Line("battle.lucky_coin", target.Name) });
                    break;
                case "hide":
                    actor.Statuses[StatusKind.Hidden] = 1;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Status = StatusKind.Hidden, Text = Words.Line("battle.hide", actor.Name) });
                    Retarget(actor);
                    break;
                case "song_courage":
                case "song_rest":
                    Song = skill.Effect == "song_courage" ? Song.Courage : Song.Rest;
                    SongBy = actor.Id;
                    string what = Song == Song.Courage ? Words.Line("battle.song.courage", CourageBonus + SongPower) : Words.Line("battle.song.rest", RestHeal);
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Text = what });
                    break;
                case "encore":
                    target.Ready = _units.Where(u => !u.Down && u.Id != target.Id).Select(u => u.Ready).DefaultIfEmpty(0).Min() - 1;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Text = Words.Line("battle.encore", target.Name) });
                    break;
                case "raise":
                    var skeleton = SummonOf(actor, GameData.SkeletonId);
                    int hp = SkeletonLife(actor);
                    if (skeleton != null)
                    {
                        skeleton.Hp = hp;
                        skeleton.Statuses.Clear();
                    }
                    else skeleton = Summon(actor, GameData.SkeletonId, hp, 0);
                    skeleton.Ready = actor.Ready + Wait(skeleton.Speed, 50);
                    Emit(new BattleEvent { Kind = BattleEventKind.Revive, Actor = actor.Id, Target = skeleton.Id, Amount = skeleton.Hp, Text = Words.Line("battle.raise", actor.Name, skeleton.Hp) });
                    ChooseIntent(skeleton);
                    break;
                case "deathmark":
                    AddStatus(target, StatusKind.DeathMark, MarkTurns);
                    break;
                case "bonewall":
                    var wall = SummonOf(actor, GameData.SkeletonId);
                    wall.TauntArmor = BoneWallArmor;
                    wall.Statuses[StatusKind.Taunt] = 1;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = wall.Id, Status = StatusKind.Taunt, Amount = BoneWallArmor, Text = Words.Line("battle.bone_wall", actor.Name, BoneWallArmor) });
                    foreach (var monster in Monsters.Where(m => !m.Down && m.Intent != null && m.Intent.Skill.Target == MonsterTarget.One && m.Intent.Target != wall.Id))
                    {
                        monster.Intent.Target = wall.Id;
                        EmitIntent(monster);
                    }
                    break;
                case "foretell":
                    int cut = ForetellCut + actor.Hero.Special("foretell");
                    foreach (var monster in Monsters.Where(m => !m.Down)) monster.Statuses[StatusKind.Foretold] = cut;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Status = StatusKind.Foretold, Text = Words.Line("battle.foretell", actor.Name, cut) });
                    break;
                case "twist":
                    int dropped = Fate.Next();
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Amount = dropped, Text = Words.Line("battle.twist", actor.Name, dropped) });
                    break;
                case "wolfguard":
                    var guardWolf = SummonOf(actor, GameData.CompanionWolfId);
                    Guard(guardWolf, target, actor);
                    break;
                case "meditate":
                    actor.Ki = Math.Min(KiCap + actor.Hero.Special("ki_cap"), actor.Ki + MeditateKi);
                    int calm = actor.Hp;
                    actor.Hero.Heal(MeditateHeal + actor.Hero.Special("meditate"));
                    Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = actor.Id, Amount = actor.Hp - calm, Text = Words.Line("battle.meditate", actor.Name, actor.Ki, actor.Hp - calm) });
                    break;
                case "turret":
                    var turret = SummonOf(actor, GameData.TurretId);
                    int turretHp = TurretHp + actor.Hero.Special("turret_hp") + 2 * (actor.Hero.Level - 1);
                    if (turret != null)
                    {
                        turret.Hp = turretHp;
                        turret.Statuses.Clear();
                    }
                    else turret = Summon(actor, GameData.TurretId, turretHp, actor.Hero.Special("turret_power") + actor.Hero.Level / 3);
                    turret.Ready = actor.Ready + Wait(turret.Speed, 50);
                    Emit(new BattleEvent { Kind = BattleEventKind.Revive, Actor = actor.Id, Target = turret.Id, Amount = turret.Hp, Text = Words.Line("battle.turret", actor.Name, turret.Hp) });
                    ChooseIntent(turret);
                    break;
                case "call":
                    var wolf = SummonOf(actor, GameData.CompanionWolfId);
                    if (wolf == null) break;
                    if (wolf.Down)
                    {
                        wolf.Hp = Math.Max(1, wolf.MaxHp / 2);
                        wolf.Ready = actor.Ready + Wait(wolf.Speed, 50);
                        Emit(new BattleEvent { Kind = BattleEventKind.Revive, Actor = actor.Id, Target = wolf.Id, Amount = wolf.Hp, Text = Words.Line("battle.wolf.up", actor.Name, wolf.Hp) });
                        ChooseIntent(wolf);
                    }
                    else
                    {
                        int before = wolf.Hp;
                        wolf.Hp = Math.Min(wolf.MaxHp, wolf.Hp + WolfCallHeal);
                        if (wolf.Hp > before) Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = wolf.Id, Amount = wolf.Hp - before, Text = Words.Line("battle.heal", wolf.Name, wolf.Hp - before) });
                    }
                    wolf.Statuses[StatusKind.Howl] = 3;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = wolf.Id, Target = wolf.Id, Status = StatusKind.Howl, Text = Words.Line("battle.howl", wolf.Name, Words.Status(StatusKind.Howl)) });
                    break;
            }
        }

        void AfterAttack(Unit actor, SkillDef skill, CheckResult check, int dealt, Unit target)
        {
            if (skill.Effect == "lifesteal" && dealt > 1)
            {
                int before = actor.Hp;
                actor.Hero.Heal(dealt / 2);
                if (actor.Hp > before)
                    Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = actor.Id, Amount = actor.Hp - before, Text = $"{actor.Name}: {skill.Name} +{actor.Hp - before}" });
            }
            if (skill.Effect == "backfire" && check.Successes <= BackfireAtMost)
                Hurt(actor, BackfireDamage, actor.Id, false, Words.Line("battle.cause.backfire"), leaveOne: true);
            if (skill.Effect == "finisher") actor.Combo = 0;
            if (actor.Statuses.Remove(StatusKind.Hidden))
                Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = actor.Id, Status = StatusKind.Hidden, Text = Words.Line("battle.unhide", actor.Name) });
            // The wolf bites the same enemy right after: a combo attack.
            if (skill.Effect == "command" && !target.Down)
            {
                var wolf = SummonOf(actor, GameData.CompanionWolfId);
                if (wolf != null && !wolf.Down)
                {
                    var bite = wolf.Monster.Pattern[0];
                    Strike(wolf, bite, new List<Unit> { target }, 1);
                }
            }
        }
    }
}
