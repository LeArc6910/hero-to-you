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
        public const int ComboPower = 5;
        public const int SoulCap = 6;
        public const int CourageBonus = 10;
        public const int RestHealBase = 3;
        public const int ForetellCut = 15;
        public const int Foresight = 3;
        public const int WolfCallHeal = 8;
        public const int SkeletonHp = 14;
        public const int WolfHp = 18;

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
            if (Is(unit, "necromancer")) unit.Souls = 1 + unit.Hero.Special("soul_start");
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
        public int DiceOf(Unit actor, SkillDef skill) => skill.Effect == "finisher" ? Math.Min(9, skill.Dice + actor.Combo) : skill.Dice;

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
            if (skill.CostKind == "reagent" && actor.Hero.Reagents < skill.CostAmount) return $"{skill.Name}: 재료가 모자랍니다 ({actor.Hero.Reagents}/{skill.CostAmount}).";
            if (skill.CostKind == "soul" && actor.Souls < skill.CostAmount) return $"{skill.Name}: 영혼이 모자랍니다 ({actor.Souls}/{skill.CostAmount}).";
            if (skill.Effect == "raise" && SummonOf(actor, GameData.SkeletonId) is Unit skeleton && !skeleton.Down) return "해골은 한 번에 하나만 일으킵니다.";
            if (skill.Effect == "finisher" && actor.Combo == 0) return $"{skill.Name}: 연계가 없습니다(먼저 다른 행동을 하세요).";
            if ((skill.Effect == "wolfguard" || skill.Effect == "command") && !(SummonOf(actor, GameData.CompanionWolfId) is Unit wolf && !wolf.Down))
                return "늑대가 쓰러져 있습니다(야성의 부름으로 일으킵니다).";
            return null;
        }

        void PayCost(Unit actor, SkillDef skill)
        {
            if (skill.CostKind == "reagent") actor.Hero.Reagents -= skill.CostAmount;
            if (skill.CostKind == "soul") actor.Souls -= skill.CostAmount;
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
                Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = necro.Id, Target = necro.Id, Amount = gain, Text = $"{necro.Name}: 영혼 +{gain} ({necro.Souls})" });
            }
        }

        void EndSong()
        {
            if (Song == Song.None) return;
            Song = Song.None;
            SongBy = -1;
            Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Text = "노래가 그쳤다" });
        }

        void Guard(Unit guard, Unit target, Unit until)
        {
            if (guard.GuardTarget >= 0) EndGuard(_units[guard.GuardTarget]);
            if (target.GuardedBy >= 0) EndGuard(target);
            target.GuardedBy = guard.Id;
            target.GuardUntil = until.Id;
            guard.GuardTarget = target.Id;
            Emit(new BattleEvent { Kind = BattleEventKind.Guard, Actor = guard.Id, Target = target.Id, Text = $"{Korean.Subject(guard.Name)} {Korean.Object(target.Name)} 지킨다 (방어도 +{GuardArmor})" });
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
                case "hide":
                    actor.Statuses[StatusKind.Hidden] = 1;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Status = StatusKind.Hidden, Text = $"{actor.Name}: 그림자 속으로 숨었다" });
                    Retarget(actor);
                    break;
                case "song_courage":
                case "song_rest":
                    Song = skill.Effect == "song_courage" ? Song.Courage : Song.Rest;
                    SongBy = actor.Id;
                    string what = Song == Song.Courage ? $"용기의 노래: 모든 아군 확률 +{CourageBonus + SongPower}" : $"안식의 노래: 아군이 차례마다 생명력 +{RestHeal}";
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Text = what });
                    break;
                case "encore":
                    target.Ready = _units.Where(u => !u.Down && u.Id != target.Id).Select(u => u.Ready).DefaultIfEmpty(0).Min() - 1;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Text = $"{target.Name}: 앙코르! 바로 다음 차례" });
                    break;
                case "raise":
                    var skeleton = SummonOf(actor, GameData.SkeletonId);
                    int hp = SkeletonHp + actor.Hero.Special("skeleton_hp");
                    if (skeleton != null)
                    {
                        skeleton.Hp = hp;
                        skeleton.Statuses.Clear();
                    }
                    else skeleton = Summon(actor, GameData.SkeletonId, hp, 0);
                    skeleton.Ready = actor.Ready + Wait(skeleton.Speed, 50);
                    Emit(new BattleEvent { Kind = BattleEventKind.Revive, Actor = actor.Id, Target = skeleton.Id, Amount = skeleton.Hp, Text = $"{actor.Name}: 해골이 일어났다 (생명력 {skeleton.Hp})" });
                    ChooseIntent(skeleton);
                    break;
                case "deathmark":
                    AddStatus(target, StatusKind.DeathMark, MarkTurns);
                    break;
                case "foretell":
                    int cut = ForetellCut + actor.Hero.Special("foretell");
                    foreach (var monster in Monsters.Where(m => !m.Down)) monster.Statuses[StatusKind.Foretold] = cut;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Status = StatusKind.Foretold, Text = $"{actor.Name}: 예언! 모든 적의 다음 공격 확률 −{cut}" });
                    break;
                case "twist":
                    int dropped = Fate.Next();
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Amount = dropped, Text = $"{actor.Name}: 운명 바꾸기, 다음 수 하나({dropped})를 버렸다" });
                    break;
                case "wolfguard":
                    var guardWolf = SummonOf(actor, GameData.CompanionWolfId);
                    Guard(guardWolf, target, actor);
                    break;
                case "call":
                    var wolf = SummonOf(actor, GameData.CompanionWolfId);
                    if (wolf == null) break;
                    if (wolf.Down)
                    {
                        wolf.Hp = Math.Max(1, wolf.MaxHp / 2);
                        wolf.Ready = actor.Ready + Wait(wolf.Speed, 50);
                        Emit(new BattleEvent { Kind = BattleEventKind.Revive, Actor = actor.Id, Target = wolf.Id, Amount = wolf.Hp, Text = $"{actor.Name}: 늑대가 다시 일어났다 (생명력 {wolf.Hp})" });
                        ChooseIntent(wolf);
                    }
                    else
                    {
                        int before = wolf.Hp;
                        wolf.Hp = Math.Min(wolf.MaxHp, wolf.Hp + WolfCallHeal);
                        if (wolf.Hp > before) Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = wolf.Id, Amount = wolf.Hp - before, Text = $"늑대: +{wolf.Hp - before} 회복" });
                    }
                    wolf.Statuses[StatusKind.Howl] = 3;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = wolf.Id, Target = wolf.Id, Status = StatusKind.Howl, Text = "늑대: 울부짖음! 다음 공격 위력 +3" });
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
                Hurt(actor, BackfireDamage, actor.Id, false, "판돈을 잃어", leaveOne: true);
            if (skill.Effect == "finisher") actor.Combo = 0;
            if (actor.Statuses.Remove(StatusKind.Hidden))
                Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = actor.Id, Status = StatusKind.Hidden, Text = $"{actor.Name}: 그림자에서 나왔다" });
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
