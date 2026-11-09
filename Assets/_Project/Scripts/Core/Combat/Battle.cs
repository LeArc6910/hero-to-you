using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // One turn-based battle (Docs/Combat.md): up to three heroes (with their summons) against one to five monsters,
    // acting one at a time by the clock of the turn order. Heroes act through Do(BattleCommand); monsters and summons
    // act on their own between hero turns, doing what their intent showed. Everything that happens is added to
    // Events in order. Every die comes from the battle's Fate, every other random choice (first turns, a target) from
    // its own stream, so the same seed and commands give the same battle (Docs/Design.md 7.2).
    public sealed partial class Battle
    {
        // The clock: an action with 100% delay at speed 12 waits 1000.
        public const int Clock = 12000;
        public const int BurnDamage = 3;
        public const int BurnTurns = 2;
        public const int MarkTurns = 3;
        public const int WeakTurns = 2;
        public const int WeakPercent = 70;
        public const int ChillPercent = 150;
        public const int PotionHeal = 15;
        public const int PotionDelay = 50;
        public const int BackfireDamage = 4;
        public const int BackfireAtMost = 2;

        readonly List<Unit> _units = new List<Unit>();
        readonly List<BattleEvent> _events = new List<BattleEvent>();
        readonly GameRandom _ai;
        readonly GameData _data;

        // The hero who attacked last and whom, when that was the turn just before (Docs/Combat.md 협공).
        int _lastAttacker = -1;
        int _lastAttacked = -1;

        public Battle(IReadOnlyList<Hero> heroes, IReadOnlyList<MonsterDef> monsters, ulong seed, bool weakStart = false, GameData data = null)
        {
            if (heroes == null || heroes.Count == 0) throw new ArgumentException("A battle needs heroes.", nameof(heroes));
            if (monsters == null || monsters.Count == 0) throw new ArgumentException("A battle needs monsters.", nameof(monsters));
            _data = data;
            Fate = new Fate(seed, 1);
            _ai = new GameRandom(seed, 2);

            foreach (var hero in heroes) _units.Add(new Unit(_units.Count, hero));
            foreach (var (monster, name) in Names(monsters)) _units.Add(new Unit(_units.Count, monster, name));
            foreach (var unit in _units.ToList()) StartWith(unit, weakStart);
            foreach (var unit in _units)
            {
                long ready = Wait(unit.Speed, 100) * (40 + _ai.Next(61)) / 100;
                // The rogue strikes first (Docs/Classes.md).
                unit.Ready = Ambusher(unit) ? ready / 2 : ready;
            }
            foreach (var unit in _units.Where(u => !u.IsHero && !u.Down)) ChooseIntent(unit);
            Advance();
        }

        public Fate Fate { get; }
        public IReadOnlyList<Unit> Units => _units;
        public IEnumerable<Unit> Heroes => _units.Where(u => u.IsHero);

        // The other side: monsters (foes). Summons are on the party's side and in Summons.
        public IEnumerable<Unit> Monsters => _units.Where(u => u.IsFoe);
        public IEnumerable<Unit> Summons => _units.Where(u => u.IsSummon);
        public IEnumerable<Unit> Party => _units.Where(u => u.Side == Side.Party);
        public IReadOnlyList<BattleEvent> Events => _events;
        public BattleState State { get; private set; } = BattleState.HeroTurn;

        // The hero whose command is awaited, or -1 when the battle is over.
        public int CurrentId { get; private set; } = -1;
        public Unit Current => CurrentId >= 0 ? _units[CurrentId] : null;
        public bool Over => State != BattleState.HeroTurn;

        public int XpReward => Monsters.Sum(m => m.Monster.Xp);
        public int GoldReward => Monsters.Sum(m => m.Monster.Gold);

        public Unit Unit(int id) => id >= 0 && id < _units.Count ? _units[id] : null;

        // Two goblins are 고블린 A and 고블린 B; a lone one is just 고블린.
        static IEnumerable<(MonsterDef, string)> Names(IReadOnlyList<MonsterDef> monsters)
        {
            var seen = new Dictionary<string, int>();
            foreach (var monster in monsters)
            {
                int count = monsters.Count(m => m.Id == monster.Id);
                seen.TryGetValue(monster.Id, out int index);
                seen[monster.Id] = index + 1;
                yield return (monster, count > 1 ? $"{monster.Name} {(char)('A' + index)}" : monster.Name);
            }
        }

        static long Wait(int speed, int delay) => (long)Clock * delay / (100L * Math.Max(1, speed));

        static Side Opposite(Side side) => side == Side.Party ? Side.Foe : Side.Party;

        IEnumerable<Unit> Living(Side side) => _units.Where(u => u.Side == side && !u.Down);

        // The next `count` turns, the current one first, as if everyone used 100% delay.
        public List<Unit> Timeline(int count)
        {
            var clocks = _units.Where(u => !u.Down).ToDictionary(u => u.Id, u => u.Ready);
            var order = new List<Unit>();
            while (order.Count < count && clocks.Count > 0)
            {
                var next = clocks.OrderBy(c => c.Value).ThenBy(c => Order(_units[c.Key])).ThenBy(c => c.Key).First();
                order.Add(_units[next.Key]);
                clocks[next.Key] = next.Value + Wait(_units[next.Key].Speed, 100);
            }
            return order;
        }

        // Ties go to heroes, then summons, then monsters.
        static int Order(Unit unit) => unit.IsHero ? 0 : unit.IsSummon ? 1 : 2;

        // Turns of `unit`'s own the skill still has to wait (0 = usable on its current or next turn).
        public int Cooldown(Unit unit, SkillDef skill) =>
            unit.CooldownFrom(skill.Id, unit.Id == CurrentId ? unit.TurnsTaken : unit.TurnsTaken + 1);

        // The unit an intent would really hit: a guarded one's guard takes it instead.
        public Unit IntentTarget(Unit actor)
        {
            var intent = actor.Intent;
            if (intent == null || intent.Skill.Target != MonsterTarget.One || intent.Target < 0) return null;
            return Redirect(_units[intent.Target]);
        }

        Unit Redirect(Unit target)
        {
            if (target.GuardedBy < 0) return target;
            var guard = _units[target.GuardedBy];
            return guard.Down ? target : guard;
        }

        // Runs turns until a hero's command is needed or the battle ends.
        void Advance()
        {
            CurrentId = -1;
            for (int guard = 0; guard < 10000; guard++)
            {
                if (Monsters.All(m => m.Down))
                {
                    State = BattleState.Won;
                    Emit(new BattleEvent { Kind = BattleEventKind.Victory, Amount = XpReward, Text = $"승리! 경험치 {XpReward}, 금화 {GoldReward}" });
                    return;
                }
                if (Heroes.All(h => h.Down))
                {
                    State = BattleState.Lost;
                    Emit(new BattleEvent { Kind = BattleEventKind.Defeat, Text = "모두 쓰러졌다…" });
                    return;
                }

                var next = _units.Where(u => !u.Down).OrderBy(u => u.Ready).ThenBy(Order).ThenBy(u => u.Id).First();
                if (!StartTurn(next)) continue;
                if (next.IsHero)
                {
                    CurrentId = next.Id;
                    return;
                }
                UnitAct(next);
            }
            throw new InvalidOperationException("The battle did not reach a hero's turn.");
        }

        // The start of a unit's turn: what was set up until its turn ends, songs, burns, stuns. False when it cannot act.
        bool StartTurn(Unit unit)
        {
            unit.TurnsTaken++;
            Emit(new BattleEvent { Kind = BattleEventKind.Turn, Actor = unit.Id, Text = $"{unit.Name}의 차례" });

            foreach (var guarded in _units.Where(u => u.GuardUntil == unit.Id).ToList()) EndGuard(guarded);
            if (unit.Statuses.Remove(StatusKind.Taunt))
            {
                unit.TauntArmor = 0;
                Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = unit.Id, Status = StatusKind.Taunt, Text = $"{unit.Name}: 도발이 끝났다" });
            }
            if (unit.IsHero && Song == Song.Rest)
            {
                int before = unit.Hp;
                unit.Hero.Heal(RestHeal);
                if (unit.Hp > before) Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = SongBy, Target = unit.Id, Amount = unit.Hp - before, Text = $"{unit.Name}: 안식의 노래 +{unit.Hp - before}" });
            }
            if (unit.Has(StatusKind.Burn))
            {
                Hurt(unit, BurnDamage, -1, false, "화상");
                if (unit.Down) return false;
            }
            if (unit.Statuses.Remove(StatusKind.Stun))
            {
                Emit(new BattleEvent { Kind = BattleEventKind.Skip, Actor = unit.Id, Text = $"{unit.Name}: 기절해서 차례를 넘긴다" });
                EndTurn(unit, 100);
                return false;
            }
            return true;
        }

        void EndGuard(Unit guarded)
        {
            if (guarded.GuardedBy >= 0) _units[guarded.GuardedBy].GuardTarget = -1;
            guarded.GuardedBy = -1;
            guarded.GuardUntil = -1;
        }

        // The end of a unit's turn: its next turn's time, and its statuses one turn shorter.
        void EndTurn(Unit unit, int delay)
        {
            long wait = Wait(unit.Speed, delay);
            if (unit.Statuses.Remove(StatusKind.Chill))
            {
                wait = wait * ChillPercent / 100;
                Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = unit.Id, Status = StatusKind.Chill, Text = $"{unit.Name}: 냉기로 다음 차례가 늦어진다" });
            }
            unit.Ready += wait;
            foreach (var kind in new[] { StatusKind.Burn, StatusKind.Mark, StatusKind.Weak, StatusKind.Corrode, StatusKind.DeathMark })
            {
                if (!unit.Statuses.TryGetValue(kind, out int left)) continue;
                if (left > 1)
                {
                    unit.Statuses[kind] = left - 1;
                    continue;
                }
                unit.Statuses.Remove(kind);
                if (!unit.Down)
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = unit.Id, Status = kind, Text = $"{unit.Name}: {Korean.Subject(StatusNames.Name(kind))} 풀렸다" });
            }
        }

        void Emit(BattleEvent e) => _events.Add(e);

        void AddStatus(Unit unit, StatusKind kind, int turns)
        {
            if (unit.Down) return;
            unit.Statuses.TryGetValue(kind, out int had);
            unit.Statuses[kind] = Math.Max(had, turns);
            Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Target = unit.Id, Status = kind, Amount = turns, Text = $"{unit.Name}: {StatusNames.Name(kind)}" });
        }

        // Damage after armor (physical) or ward (magic). Anything that landed does at least 1.
        static int Mitigate(int value, Unit target, bool physical)
        {
            if (value <= 0) return 0;
            int damage = value - (physical ? target.Armor : target.Ward);
            return Math.Max(1, damage);
        }

        // Takes health; at zero the unit is down. `leaveOne` keeps it standing at 1 (the gambler's backfire).
        void Hurt(Unit target, int amount, int source, bool perfect, string what, bool leaveOne = false)
        {
            if (target.Down || amount <= 0) return;
            int hp = target.Hp - amount;
            if (leaveOne) hp = Math.Max(1, hp);
            int dealt = target.Hp - Math.Max(0, hp);
            target.Hp = Math.Max(0, hp);
            Emit(new BattleEvent
            {
                Kind = BattleEventKind.Damage,
                Actor = source,
                Target = target.Id,
                Amount = dealt,
                Perfect = perfect,
                Text = $"{target.Name}: {what} {dealt} 피해" + (perfect ? " (완벽!)" : ""),
            });
            if (target.Down) KnockDown(target);
            else if (target.IsFoe) CheckPhase(target);
        }

        void KnockDown(Unit unit)
        {
            bool marked = unit.Has(StatusKind.DeathMark);
            unit.Hp = 0;
            unit.Statuses.Clear();
            unit.TauntArmor = 0;
            if (unit.GuardTarget >= 0) EndGuard(_units[unit.GuardTarget]);
            EndGuard(unit);
            Emit(new BattleEvent { Kind = BattleEventKind.Down, Target = unit.Id, Text = $"{unit.Name} 쓰러짐" });
            if (unit.IsFoe) GainSouls(marked);
            if (unit.Id == SongBy) EndSong();
            // Whoever aimed at the fallen picks someone else, and shows it.
            foreach (var actor in _units.Where(u => !u.IsHero && !u.Down && u.Intent != null && u.Intent.Skill.Target == MonsterTarget.One && u.Intent.Target == unit.Id))
            {
                int target = PickTarget(actor, actor.Intent.Skill.Aim);
                if (target < 0) continue;
                actor.Intent.Target = target;
                EmitIntent(actor);
            }
        }

        void CheckPhase(Unit monster)
        {
            var def = monster.Monster;
            if (def.Phase2 <= 0 || monster.Phase != 0 || monster.Hp * 100 > monster.MaxHp * def.Phase2) return;
            monster.Phase = 1;
            monster.PatternIndex = 0;
            Emit(new BattleEvent { Kind = BattleEventKind.Phase, Actor = monster.Id, Text = $"{monster.Name}: {def.Phase2Line}" });
            ChooseIntent(monster);
        }

        // ---- Monsters and summons ----

        void ChooseIntent(Unit actor)
        {
            var def = actor.Monster;
            var pattern = actor.Phase == 1 && def.Pattern2.Count > 0 ? def.Pattern2 : def.Pattern;
            var skill = pattern[actor.PatternIndex % pattern.Count];
            actor.PatternIndex++;
            int target = skill.Target switch
            {
                MonsterTarget.All => -1,
                MonsterTarget.Self => actor.Id,
                _ => PickTarget(actor, skill.Aim),
            };
            actor.Intent = new Intent(skill, target);
            EmitIntent(actor);
        }

        void EmitIntent(Unit actor)
        {
            var intent = actor.Intent;
            string whom = intent.Skill.Target switch
            {
                MonsterTarget.All => "모두",
                MonsterTarget.Self => "자신",
                _ => intent.Target >= 0 ? _units[intent.Target].Name : "?",
            };
            Emit(new BattleEvent { Kind = BattleEventKind.Intent, Actor = actor.Id, Target = intent.Target, Skill = intent.Skill.Id, SkillName = intent.Skill.Name, Text = $"{actor.Name}의 다음 행동: {intent.Skill.Name} → {whom}" });
        }

        // A living opponent to aim at: for monsters, the taunting knight if there is one; nobody hidden while anyone
        // is not; then by the aim.
        int PickTarget(Unit actor, MonsterAim aim)
        {
            var living = Living(Opposite(actor.Side)).ToList();
            if (living.Count == 0) return -1;
            if (actor.IsFoe)
            {
                var taunting = living.FirstOrDefault(h => h.Has(StatusKind.Taunt));
                if (taunting != null) return taunting.Id;
            }
            var seen = living.Where(u => !u.Has(StatusKind.Hidden)).ToList();
            if (seen.Count > 0) living = seen;
            return aim switch
            {
                MonsterAim.Weakest => living.OrderBy(h => h.Hp).ThenBy(h => h.Id).First().Id,
                MonsterAim.Strongest => living.OrderByDescending(h => h.Hp).ThenBy(h => h.Id).First().Id,
                _ => living[_ai.Next(living.Count)].Id,
            };
        }

        // Turns every aimed intent that cannot or should not stay on `from` to a new pick (a hero hiding).
        void Retarget(Unit from)
        {
            foreach (var actor in _units.Where(u => !u.IsHero && !u.Down && u.Side != from.Side && u.Intent != null && u.Intent.Skill.Target == MonsterTarget.One && u.Intent.Target == from.Id))
            {
                int target = PickTarget(actor, actor.Intent.Skill.Aim);
                if (target < 0 || target == from.Id) continue;
                actor.Intent.Target = target;
                EmitIntent(actor);
            }
        }

        void UnitAct(Unit actor)
        {
            var intent = actor.Intent;
            var skill = intent.Skill;
            _lastAttacker = _lastAttacked = -1;

            if (!skill.Damages)
            {
                if (skill.Effect == "howl")
                {
                    actor.Statuses[StatusKind.Howl] = 3;
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Status = StatusKind.Howl, Text = $"{actor.Name}: {skill.Name}! 다음 공격 위력 +3" });
                }
                EndTurn(actor, skill.Delay);
                ChooseIntent(actor);
                return;
            }

            var targets = new List<Unit>();
            if (skill.Target == MonsterTarget.All) targets.AddRange(Living(Opposite(actor.Side)));
            else if (intent.Target >= 0)
            {
                var aimed = _units[intent.Target];
                if (aimed.Down || (aimed.Has(StatusKind.Hidden) && actor.IsFoe))
                {
                    int other = PickTarget(actor, skill.Aim);
                    if (other >= 0) aimed = _units[other];
                }
                if (!aimed.Down) targets.Add(Redirect(aimed));
            }
            if (targets.Count > 0) Strike(actor, skill, targets, 0);
            EndTurn(actor, skill.Delay);
            ChooseIntent(actor);
        }

        // A monster's or summon's attack: one roll for the whole attack. A single target's agility lowers the chance;
        // an attack on everyone ignores it (Docs/Combat.md).
        void Strike(Unit actor, MonsterSkillDef skill, List<Unit> targets, int sure)
        {
            int chance = skill.Target == MonsterTarget.All ? actor.Accuracy : actor.Accuracy - targets[0].Evasion;
            if (actor.Statuses.TryGetValue(StatusKind.Foretold, out int foretold))
            {
                chance -= foretold;
                actor.Statuses.Remove(StatusKind.Foretold);
            }
            chance = Dice.ClampChance(chance);
            var check = Fate.Roll(skill.Dice, chance, sure);
            string sureText = sure > 0 ? $" (확정 {sure})" : "";
            Emit(new BattleEvent { Kind = BattleEventKind.Roll, Actor = actor.Id, Target = skill.Target == MonsterTarget.All ? -1 : targets[0].Id, Skill = skill.Id, SkillName = skill.Name, Check = check, Perfect = check.Perfect, Text = $"{actor.Name}의 {skill.Name}: {chance}% ×{check.Dice}{sureText} → 성공 {check.Successes}" });

            int power = skill.Power + actor.PowerBonus;
            if (actor.Statuses.TryGetValue(StatusKind.Howl, out int howl))
            {
                power += howl;
                actor.Statuses.Remove(StatusKind.Howl);
            }
            if (actor.Has(StatusKind.Weak)) power = Dice.Percent(power, WeakPercent);
            int value = Dice.Value(power, skill.Floor, check.Successes, check.Dice);

            foreach (var target in targets)
            {
                int damage = Mitigate(value, target, skill.Kind == "physical");
                if (damage <= 0)
                {
                    Emit(new BattleEvent { Kind = BattleEventKind.Miss, Actor = actor.Id, Target = target.Id, Text = $"{target.Name}: 빗나감" });
                    continue;
                }
                Hurt(target, damage, actor.Id, false, skill.Name);
                if (skill.Effect == "burn" && check.Successes > 0) AddStatus(target, StatusKind.Burn, BurnTurns);
            }
        }

        // ---- Heroes ----

        public IEnumerable<SkillDef> SkillsOf(Unit hero) => hero.IsHero ? hero.Hero.Skills : Enumerable.Empty<SkillDef>();

        // The units the skill may be aimed at now (for the screen to offer; Do checks the same).
        public List<Unit> Targets(Unit actor, SkillDef skill)
        {
            if (skill == null) return new List<Unit> { actor };
            return skill.Target switch
            {
                SkillTarget.Enemy => Monsters.Where(m => !m.Down).ToList(),
                SkillTarget.AllEnemies => Monsters.Where(m => !m.Down).ToList(),
                SkillTarget.Ally => Heroes.Where(h => !h.Down).ToList(),
                SkillTarget.OtherAlly => Heroes.Where(h => !h.Down && h.Id != actor.Id).ToList(),
                SkillTarget.DownedAlly => Heroes.Where(h => h.Down).ToList(),
                _ => new List<Unit> { actor },
            };
        }

        // The target a skill takes when there is nothing to choose (all enemies, self), else -1.
        public static int FixedTarget(Unit actor, SkillDef skill) =>
            skill != null && (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.AllEnemies) ? actor.Id : -1;

        // What the command would do, without doing it. Error says why it cannot be given.
        public SkillPreview Preview(BattleCommand command)
        {
            var preview = new SkillPreview();
            var actor = Unit(command.Actor);
            if (State != BattleState.HeroTurn || actor == null || actor.Id != CurrentId) return Fail(preview, "지금은 이 영웅의 차례가 아닙니다.");
            var hero = actor.Hero;

            if (command.Skill == BattleCommand.Potion)
            {
                var drinker = Unit(command.Target);
                if (hero.Potions <= 0) return Fail(preview, "물약이 없습니다.");
                if (drinker != null && drinker.Id != actor.Id) return Fail(preview, "물약은 자신에게만 씁니다.");
                preview.Odds = new CheckOdds(0, 100, 0);
                preview.ValueBySuccesses = new[] { Math.Min(PotionOf(actor), actor.MaxHp - actor.Hp) };
                preview.Expected = preview.ValueBySuccesses[0];
                return preview;
            }

            var skill = hero.Skill(command.Skill);
            preview.Skill = skill;
            if (skill == null) return Fail(preview, $"{hero.Name}의 기술이 아닙니다: {command.Skill}");
            int wait = Cooldown(actor, skill);
            if (wait > 0) return Fail(preview, $"{skill.Name}: 차례 {wait}번 뒤에 쓸 수 있습니다.");
            string cannot = CannotUse(actor, skill);
            if (cannot != null) return Fail(preview, cannot);
            var target = TargetOf(actor, skill, command.Target);
            if (target == null) return Fail(preview, $"{skill.Name}: 대상을 고르세요.");

            if (!skill.Rolls)
            {
                preview.Odds = new CheckOdds(0, 100, 0);
                preview.ValueBySuccesses = new[] { 0 };
                return preview;
            }

            int dice = DiceOf(actor, skill);
            preview.Dice = dice;
            preview.Chance = ChanceOf(actor, skill);
            int free = 0;
            void Add(int amount, string why)
            {
                if (amount <= 0) return;
                free += amount;
                preview.Reasons.Add($"{why} {amount}");
            }
            Add(hero.Blessing, "축복");
            Add(hero.Luck, "행운");
            if (skill.Damages && skill.Target == SkillTarget.Enemy)
            {
                if (target.Has(StatusKind.Mark)) Add(1, "표식");
                if (_lastAttacker >= 0 && _lastAttacker != actor.Id && _lastAttacked == target.Id) Add(1, "협공");
            }
            if (skill.Damages && actor.Has(StatusKind.Hidden)) Add(1, "기습");
            if (Resonates(actor, skill)) Add(1, "공명");
            preview.Free = Math.Min(free, dice);
            preview.ResolveRoom = Math.Min(hero.Resolve, dice - preview.Free);
            if (command.Resolve < 0 || command.Resolve > preview.ResolveRoom)
                return Fail(preview, $"결의는 0~{preview.ResolveRoom}개 쓸 수 있습니다.");
            preview.Resolve = command.Resolve;
            preview.Odds = new CheckOdds(dice, preview.Chance, preview.Free + preview.Resolve);
            preview.ValueBySuccesses = new int[dice + 1];
            double expected = 0;
            for (int s = 0; s <= dice; s++)
            {
                preview.ValueBySuccesses[s] = EffectOn(actor, skill, target, s, dice);
                expected += preview.Odds.BySuccesses[s] * preview.ValueBySuccesses[s];
            }
            preview.Expected = expected;
            return preview;
        }

        static SkillPreview Fail(SkillPreview preview, string error)
        {
            preview.Error = error;
            return preview;
        }

        Unit TargetOf(Unit actor, SkillDef skill, int targetId)
        {
            if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.AllEnemies) return actor;
            var target = Unit(targetId);
            return target != null && Targets(actor, skill).Contains(target) ? target : null;
        }

        // The skill's effect for a number of successes: damage after armor (summed over every enemy for an attack on
        // all), healing, or the health a revived hero gets back.
        int EffectOn(Unit actor, SkillDef skill, Unit target, int successes, int dice)
        {
            int value = RawValue(actor, skill, successes, dice);
            switch (skill.Kind)
            {
                case SkillKind.Physical:
                case SkillKind.Magic:
                    var targets = skill.Target == SkillTarget.AllEnemies ? Monsters.Where(m => !m.Down) : new[] { target };
                    return targets.Sum(t => Mitigate(value, t, skill.Kind == SkillKind.Physical));
                case SkillKind.Heal:
                    return value;
                case SkillKind.Revive:
                    return Math.Max(1, target.MaxHp * value / 100);
                default:
                    return 0;
            }
        }

        // Before armor: the dice's share of the power, weakened, and the perfect multiplier.
        int RawValue(Unit actor, SkillDef skill, int successes, int dice)
        {
            int power = PowerOf(actor, skill);
            if (skill.Damages && actor.Has(StatusKind.Weak)) power = Dice.Percent(power, WeakPercent);
            int value = Dice.Value(power, skill.Floor, successes, dice);
            if (successes == dice && dice > 0) value = Dice.Percent(value, skill.PerfectPercent);
            return value;
        }

        // Gives the current hero's command. Returns null when done, or why it could not be done (nothing changes).
        public string Do(BattleCommand command)
        {
            var preview = Preview(command);
            if (!preview.Ok) return preview.Error;
            var actor = _units[command.Actor];
            var hero = actor.Hero;

            if (command.Skill == BattleCommand.Potion)
            {
                hero.Potions--;
                int healed = Math.Min(PotionOf(actor), actor.MaxHp - actor.Hp);
                hero.Heal(PotionOf(actor));
                Emit(new BattleEvent { Kind = BattleEventKind.Potion, Actor = actor.Id, Target = actor.Id, Amount = healed, Text = $"{actor.Name}: 물약을 마셨다 (+{healed})" });
                _lastAttacker = _lastAttacked = -1;
                AfterAction(actor, null);
                EndTurn(actor, PotionDelay);
                Advance();
                return null;
            }

            var skill = preview.Skill;
            var target = TargetOf(actor, skill, command.Target);
            if (skill.Cooldown > 0) actor.AvailableAt[skill.Id] = actor.TurnsTaken + skill.Cooldown + 1;
            PayCost(actor, skill);

            CheckResult check = null;
            if (skill.Rolls)
            {
                hero.Resolve -= preview.Resolve;
                hero.Blessing = 0;
                hero.Luck = 0;
                check = Fate.Roll(preview.Dice, preview.Chance, preview.Guaranteed);
                string sure = check.Guaranteed > 0 ? $" (확정 {check.Guaranteed})" : "";
                Emit(new BattleEvent { Kind = BattleEventKind.Roll, Actor = actor.Id, Target = target.Id, Skill = skill.Id, SkillName = skill.Name, Check = check, Perfect = check.Perfect, Text = $"{actor.Name}의 {skill.Name}: {preview.Chance}% ×{check.Dice}{sure} → 성공 {check.Successes}" + (check.Perfect ? " 완벽!" : "") });
                int gained = hero.AddGrit(check.RolledFailures);
                if (gained > 0) Emit(new BattleEvent { Kind = BattleEventKind.ResolveGained, Actor = actor.Id, Amount = gained, Text = $"{actor.Name}: 끈기가 차서 결의 +{gained}" });
            }
            int successes = check?.Successes ?? 0;
            bool perfect = check != null && check.Perfect;

            _lastAttacker = _lastAttacked = -1;
            switch (skill.Kind)
            {
                case SkillKind.Physical:
                case SkillKind.Magic:
                    Attack(actor, skill, target, check);
                    if (skill.Target == SkillTarget.Enemy)
                    {
                        _lastAttacker = actor.Id;
                        _lastAttacked = target.Id;
                    }
                    break;
                case SkillKind.Heal:
                    int heal = RawValue(actor, skill, successes, check?.Dice ?? 0);
                    int before = target.Hp;
                    target.Hero.Heal(heal);
                    Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = target.Id, Amount = target.Hp - before, Perfect = perfect, Text = $"{target.Name}: +{target.Hp - before} 회복" });
                    break;
                case SkillKind.Revive:
                    int hp = EffectOn(actor, skill, target, successes, check?.Dice ?? 0);
                    target.Hp = Math.Min(target.MaxHp, hp);
                    Emit(new BattleEvent { Kind = BattleEventKind.Revive, Actor = actor.Id, Target = target.Id, Amount = target.Hp, Text = $"{Korean.Subject(target.Name)} 일어났다 (생명력 {target.Hp})" });
                    break;
                case SkillKind.Support:
                    Support(actor, skill, target, check);
                    break;
            }
            if (skill.Element.Length > 0) actor.LastElement = skill.Element;
            AfterAction(actor, skill);
            EndTurn(actor, skill.Delay);
            Advance();
            return null;
        }

        void Attack(Unit actor, SkillDef skill, Unit target, CheckResult check)
        {
            int value = RawValue(actor, skill, check.Successes, check.Dice);
            var targets = skill.Target == SkillTarget.AllEnemies ? Monsters.Where(m => !m.Down).ToList() : new List<Unit> { target };
            int total = 0;
            foreach (var t in targets)
            {
                int damage = Mitigate(value, t, skill.Kind == SkillKind.Physical);
                if (damage <= 0)
                {
                    Emit(new BattleEvent { Kind = BattleEventKind.Miss, Actor = actor.Id, Target = t.Id, Text = $"{t.Name}: 빗나감" });
                    continue;
                }
                int before = t.Hp;
                Hurt(t, damage, actor.Id, check.Perfect, skill.Name);
                total += before - t.Hp;
                if (check.Successes > 0)
                {
                    if (skill.Effect == "burn") AddStatus(t, StatusKind.Burn, BurnTurns);
                    if (skill.Effect == "chill") AddStatus(t, StatusKind.Chill, 1);
                    if (skill.Effect == "weaken") AddStatus(t, StatusKind.Weak, WeakTurns);
                    if (skill.Effect == "corrode") AddStatus(t, StatusKind.Corrode, CorrodeTurns);
                }
                if (check.Perfect && skill.Perfect == "stun") AddStatus(t, StatusKind.Stun, 1);
            }
            AfterAttack(actor, skill, check, total, target);
        }
    }
}
