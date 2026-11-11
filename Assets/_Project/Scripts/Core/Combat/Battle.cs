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

        // `weakStart`: the heroes start weakened; `foesWeak`: the monsters do (Docs/Narrative.md weak_next, foes_weak).
        public Battle(IReadOnlyList<Hero> heroes, IReadOnlyList<MonsterDef> monsters, ulong seed, bool weakStart = false, GameData data = null, bool foesWeak = false)
        {
            if (heroes == null || heroes.Count == 0) throw new ArgumentException("A battle needs heroes.", nameof(heroes));
            if (monsters == null || monsters.Count == 0) throw new ArgumentException("A battle needs monsters.", nameof(monsters));
            _data = data;
            Words = new Sentences(data);
            Fate = new Fate(seed, 1);
            _ai = new GameRandom(seed, 2);

            foreach (var hero in heroes) _units.Add(new Unit(_units.Count, hero));
            foreach (var (monster, name) in Names(monsters)) _units.Add(new Unit(_units.Count, monster, name));
            foreach (var unit in _units.ToList()) StartWith(unit, weakStart);
            if (foesWeak)
                foreach (var unit in _units.Where(u => !u.IsHero && u.Side == Side.Foe)) unit.Statuses[StatusKind.Weak] = WeakTurns;
            foreach (var unit in _units)
            {
                long ready = Wait(unit.Speed, 100) * (40 + _ai.Next(61)) / 100;
                // The rogue strikes first (Docs/Classes.md).
                unit.Ready = Ambusher(unit) ? ready / 2 : ready;
            }
            foreach (var unit in _units.Where(u => !u.IsHero && !u.Down)) ChooseIntent(unit);
            StarWarning();
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

        // The battle log's and the refusals' sentences (Data/Text/battle.csv) in the language the sheets were loaded in.
        Sentences Words { get; }

        sealed class Sentences
        {
            readonly GameData _data;

            public Sentences(GameData data) => _data = data;

            // A battle made without the sheets writes each sentence as its id.
            GameData Data => _data;

            public string Line(string id, params object[] args) => Data?.Line(id, args) ?? id;

            public string Status(StatusKind kind) => StatusNames.Name(kind, Data);
        }

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

        // Whether the party's side (a hero or a summon) takes the turn after `actor` acts now with `delay`: whose roll
        // the next die of fate goes to, for the seer's choice (Docs/Classes.md 별의 눈).
        public bool PartyActsNext(Unit actor, int delay)
        {
            long mine = actor.Ready + Wait(actor.Speed, delay);
            var next = _units.Where(u => !u.Down && u.Id != actor.Id).OrderBy(u => u.Ready).ThenBy(Order).ThenBy(u => u.Id).FirstOrDefault();
            return next == null || mine <= next.Ready || next.Side == Side.Party;
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
                if (State == BattleState.Fled) return;
                if (Monsters.All(m => m.Down))
                {
                    State = BattleState.Won;
                    Emit(new BattleEvent { Kind = BattleEventKind.Victory, Amount = XpReward, Text = Words.Line("battle.victory", XpReward, GoldReward) });
                    return;
                }
                if (Heroes.All(h => h.Down))
                {
                    State = BattleState.Lost;
                    Emit(new BattleEvent { Kind = BattleEventKind.Defeat, Text = Words.Line("battle.defeat") });
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

        // The bosses' own rules on a hero they hit (Docs/Combat.md 보스): a sealed skill, every skill erased, frozen.
        public const int SealTurns = 3;
        public const int EraseTurns = 2;

        void BossRule(Unit actor, MonsterSkillDef skill, Unit target)
        {
            var own = target.Hero.Skills.Where(s => s.Id != Hero.AttackId).ToList();
            switch (skill.Effect)
            {
                case "seal":
                    var best = own.Where(s => Cooldown(target, s) == 0).OrderByDescending(s => s.Power + s.Dice * 3).FirstOrDefault();
                    if (best == null) break;
                    target.AvailableAt[best.Id] = target.TurnsTaken + SealTurns + 1;
                    target.Blocked[best.Id] = "seal";
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Text = Words.Line("battle.boss.seal", actor.Name, target.Name, best.Name, SealTurns) });
                    break;
                case "erase":
                    foreach (var s in own)
                    {
                        target.AvailableAt[s.Id] = Math.Max(target.AvailableAt.TryGetValue(s.Id, out int at) ? at : 0, target.TurnsTaken + EraseTurns + 1);
                        if (!target.Blocked.ContainsKey(s.Id)) target.Blocked[s.Id] = "erase";
                    }
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Text = Words.Line("battle.boss.erase", actor.Name, target.Name, EraseTurns) });
                    break;
                case "freeze":
                    AddStatus(target, StatusKind.Stun, 1);
                    break;
                case "scorch":
                    // The warden's brand: a resolve burnt away, and the burn.
                    if (target.Hero.Resolve > 0)
                    {
                        target.Hero.Resolve--;
                        Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = target.Id, Amount = -1, Text = Words.Line("battle.boss.brand", target.Name, target.Hero.Resolve, target.Hero.MaxResolve) });
                    }
                    AddStatus(target, StatusKind.Burn, BurnTurns);
                    break;
                case "daze":
                    AddStatus(target, StatusKind.Daze, 1);
                    break;
            }
        }

        // Armor with thorns hurts back whoever struck the hero.
        void Thorns(Unit target, Unit attacker)
        {
            if (!target.IsHero || attacker.Down) return;
            int thorns = target.Hero.Equipment.Where(i => i.Effect == "thorns").Sum(i => i.EffectAmount);
            if (thorns > 0) Hurt(attacker, thorns, target.Id, false, Words.Line("battle.cause.thorns"));
        }

        // The start of a unit's turn: what was set up until its turn ends, songs, burns, stuns. False when it cannot act.
        bool StartTurn(Unit unit)
        {
            unit.TurnsTaken++;
            Emit(new BattleEvent { Kind = BattleEventKind.Turn, Actor = unit.Id, Text = Words.Line("battle.turn", unit.Name) });
            int regen = unit.IsHero && !unit.Down ? unit.Hero.Equipment.Where(i => i.Effect == "regen").Sum(i => i.EffectAmount) : 0;
            if (regen > 0 && unit.Hp < unit.MaxHp)
            {
                int was = unit.Hp;
                unit.Hero.Heal(regen);
                Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = unit.Id, Target = unit.Id, Amount = unit.Hp - was, Text = Words.Line("battle.regen", unit.Name, unit.Hp - was) });
            }

            foreach (var guarded in _units.Where(u => u.GuardUntil == unit.Id).ToList()) EndGuard(guarded);
            if (unit.Statuses.Remove(StatusKind.Taunt))
            {
                unit.TauntArmor = 0;
                Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = unit.Id, Status = StatusKind.Taunt, Text = Words.Line("battle.taunt.end", unit.Name) });
            }
            if (unit.IsHero && Song == Song.Rest)
            {
                int before = unit.Hp;
                unit.Hero.Heal(RestHeal);
                if (unit.Hp > before) Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = SongBy, Target = unit.Id, Amount = unit.Hp - before, Text = Words.Line("battle.song.rest.heal", unit.Name, unit.Hp - before) });
            }
            if (unit.Has(StatusKind.Burn))
            {
                Hurt(unit, BurnDamage, -1, false, Words.Status(StatusKind.Burn));
                if (unit.Down) return false;
            }
            if (unit.Statuses.Remove(StatusKind.Stun))
            {
                Emit(new BattleEvent { Kind = BattleEventKind.Skip, Actor = unit.Id, Text = Words.Line("battle.stun.skip", unit.Name) });
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
                Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = unit.Id, Status = StatusKind.Chill, Text = Words.Line("battle.chill.slow", unit.Name) });
            }
            unit.Ready += wait;
            foreach (var kind in new[] { StatusKind.Burn, StatusKind.Mark, StatusKind.Weak, StatusKind.Corrode, StatusKind.DeathMark, StatusKind.Daze })
            {
                if (!unit.Statuses.TryGetValue(kind, out int left)) continue;
                if (left > 1)
                {
                    unit.Statuses[kind] = left - 1;
                    continue;
                }
                unit.Statuses.Remove(kind);
                if (!unit.Down)
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOff, Target = unit.Id, Status = kind, Text = Words.Line("battle.status.off", unit.Name, Words.Status(kind)) });
            }
        }

        void Emit(BattleEvent e) => _events.Add(e);

        void AddStatus(Unit unit, StatusKind kind, int turns)
        {
            if (unit.Down) return;
            unit.Statuses.TryGetValue(kind, out int had);
            unit.Statuses[kind] = Math.Max(had, turns);
            Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Target = unit.Id, Status = kind, Amount = turns, Text = $"{unit.Name}: {Words.Status(kind)}" });
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
                Text = perfect ? Words.Line("battle.damage.perfect", target.Name, what, dealt) : Words.Line("battle.damage", target.Name, what, dealt),
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
            Emit(new BattleEvent { Kind = BattleEventKind.Down, Target = unit.Id, Text = Words.Line("battle.down", unit.Name) });
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
            // Its second phase can call others in (the warden's ash wisps).
            var called = def.Phase2Summon.Select((d, i) => (d, i)).ToList();
            foreach (var (summonDef, index) in called)
            {
                var unit = new Unit(_units.Count, summonDef, called.Count > 1 ? $"{summonDef.Name} {(char)('A' + index)}" : summonDef.Name);
                unit.Ready = monster.Ready + Wait(unit.Speed, 50) + index;
                _units.Add(unit);
                Emit(new BattleEvent { Kind = BattleEventKind.Revive, Actor = monster.Id, Target = unit.Id, Amount = unit.Hp, Text = Words.Line("battle.appears", unit.Name) });
                ChooseIntent(unit);
            }
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
                MonsterTarget.All => Words.Line("battle.intent.all"),
                MonsterTarget.Self => Words.Line("battle.intent.self"),
                _ => intent.Target >= 0 ? _units[intent.Target].Name : "?",
            };
            Emit(new BattleEvent { Kind = BattleEventKind.Intent, Actor = actor.Id, Target = intent.Target, Skill = intent.Skill.Id, SkillName = intent.Skill.Name, Text = Words.Line("battle.intent", actor.Name, intent.Skill.Name, whom) });
        }

        // A living opponent to aim at: for monsters, the taunting knight if there is one; nobody hidden while anyone
        // is not; then by the aim.
        // Rows (Docs/Combat.md 줄): a monster's random blow lands on the front row while anyone stands there; aimed
        // attacks (the weakest, the strongest) reach the back too.
        public const int BackRowPercent = 75;

        public static bool InFront(Unit unit) => !unit.IsHero || !unit.Hero.Back;

        // A hero striking up close (a physical blow by strength or agility) from the back row hits less hard.
        public static bool CloseFromBack(Unit actor, SkillDef skill) =>
            actor.IsHero && actor.Hero.Back && skill.Damages && skill.Kind == SkillKind.Physical && skill.Stat.HasValue
            && (skill.Stat.Value == Stat.Strength || skill.Stat.Value == Stat.Agility);

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
            if (actor.IsFoe && aim == MonsterAim.Random)
            {
                var front = living.Where(InFront).ToList();
                if (front.Count > 0) living = front;
            }
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
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Status = StatusKind.Howl, Text = Words.Line("battle.howl", actor.Name, skill.Name) });
                }
                else
                {
                    // A wind-up whose blow the next intent shows (the warden's 잿불 모으기).
                    Emit(new BattleEvent { Kind = BattleEventKind.StatusOn, Actor = actor.Id, Target = actor.Id, Text = $"{actor.Name}: {skill.Name}! {skill.Text}" });
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
            // The gambler makes a duel a coin toss both ways: a blow at it alone hits at 50% at best (Docs/Classes.md 도박사).
            if (skill.Target != MonsterTarget.All && Is(targets[0], "gambler")) chance = Math.Min(chance, GamblerChance);
            if (actor.Statuses.TryGetValue(StatusKind.Foretold, out int foretold))
            {
                chance -= foretold;
                actor.Statuses.Remove(StatusKind.Foretold);
            }
            chance = Dice.ClampChance(chance);
            var check = Fate.Roll(skill.Dice, chance, sure);
            Emit(new BattleEvent { Kind = BattleEventKind.Roll, Actor = actor.Id, Target = skill.Target == MonsterTarget.All ? -1 : targets[0].Id, Skill = skill.Id, SkillName = skill.Name, Check = check, Perfect = check.Perfect, Text = RollLine(actor.Name, skill.Name, chance, check.Dice, sure, check.Successes, false) });

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
                    Emit(new BattleEvent { Kind = BattleEventKind.Miss, Actor = actor.Id, Target = target.Id, Text = Words.Line("battle.miss", target.Name) });
                    continue;
                }
                Hurt(target, damage, actor.Id, false, skill.Name);
                if (check.Successes > 0 && target.IsHero && !target.Down) BossRule(actor, skill, target);
                Thorns(target, actor);
                Answer(target, actor);
                if (skill.Effect == "burn" && check.Successes > 0) AddStatus(target, StatusKind.Burn, BurnTurns);
                if (skill.Effect == "chill" && check.Successes > 0) AddStatus(target, StatusKind.Chill, 1);
                if (skill.Effect == "weaken" && check.Successes > 0) AddStatus(target, StatusKind.Weak, WeakTurns);
            }
        }

        // The log line of a roll: "기사의 베기: 65% ×3 (확정 1) → 성공 2 완벽!"
        string RollLine(string who, string what, int chance, int dice, int sure, int successes, bool perfect)
        {
            string line = sure > 0
                ? Words.Line("battle.roll.sure", who, what, chance, dice, successes, sure)
                : Words.Line("battle.roll", who, what, chance, dice, successes);
            return perfect ? Words.Line("battle.roll.perfect", line) : line;
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
                SkillTarget.AllAllies => Heroes.Where(h => !h.Down).ToList(),
                _ => new List<Unit> { actor },
            };
        }

        // The target a skill takes when there is nothing to choose (all enemies, self), else -1.
        public static int FixedTarget(Unit actor, SkillDef skill) =>
            skill != null && (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.AllEnemies || skill.Target == SkillTarget.AllAllies) ? actor.Id : -1;

        // What the command would do, without doing it. Error says why it cannot be given.
        public SkillPreview Preview(BattleCommand command)
        {
            var preview = new SkillPreview();
            var actor = Unit(command.Actor);
            if (State != BattleState.HeroTurn || actor == null || actor.Id != CurrentId) return Fail(preview, Words.Line("battle.error.not_your_turn"));
            var hero = actor.Hero;

            if (BattleCommand.IsUse(command.Skill)) return PreviewUse(preview, actor, command);
            if (command.Skill == BattleCommand.Flee) return PreviewFlee(preview, actor, command);

            var skill = SkillOf(actor, command.Skill);
            preview.Skill = skill;
            if (skill == null) return Fail(preview, Words.Line("battle.error.not_a_skill", hero.Name, command.Skill));
            int wait = Cooldown(actor, skill);
            if (wait > 0) return Fail(preview, Words.Line("battle.error.cooldown", skill.Name, wait));
            string cannot = CannotUse(actor, skill);
            if (cannot != null) return Fail(preview, cannot);
            var target = TargetOf(actor, skill, command.Target);
            if (target == null) return Fail(preview, Words.Line("battle.error.no_target", skill.Name));

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
            // `reason` names the sure successes with their number: "표식 1".
            void Add(int amount, string reason)
            {
                if (amount <= 0) return;
                free += amount;
                preview.Reasons.Add(reason);
            }
            Add(hero.Blessing, Words.Line("battle.reason.blessing", hero.Blessing));
            Add(hero.Luck, Words.Line("battle.reason.luck", hero.Luck));
            if (skill.Damages && skill.Target == SkillTarget.Enemy)
            {
                if (target.Has(StatusKind.Mark)) Add(1, Words.Line("battle.reason.mark", 1));
                if (_lastAttacker >= 0 && _lastAttacker != actor.Id && _lastAttacked == target.Id) Add(1, Words.Line("battle.reason.follow_up", 1));
            }
            if (skill.Damages && actor.Has(StatusKind.Hidden)) Add(1, Words.Line("battle.reason.ambush", 1));
            if (Resonates(actor, skill)) Add(1, Words.Line("battle.reason.resonance", 1));
            if (CashesStake(actor, skill)) Add(actor.Stake, Words.Line("battle.reason.stake", actor.Stake));
            preview.Free = Math.Min(free, dice);
            preview.ResolveRoom = Math.Min(hero.Resolve, dice - preview.Free);
            if (command.Resolve < 0 || command.Resolve > preview.ResolveRoom)
                return Fail(preview, Words.Line("battle.error.resolve", preview.ResolveRoom));
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
            if (skill.Target == SkillTarget.Self || skill.Target == SkillTarget.AllEnemies || skill.Target == SkillTarget.AllAllies) return actor;
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
                    return targets.Sum(t => Reduce(skill, value, t));
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
            if (CloseFromBack(actor, skill)) power = Dice.Percent(power, BackRowPercent);
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

            if (BattleCommand.IsUse(command.Skill))
            {
                DoUse(actor, preview);
                return null;
            }
            if (command.Skill == BattleCommand.Flee)
            {
                DoFlee(actor, preview);
                return null;
            }

            var skill = preview.Skill;
            var target = TargetOf(actor, skill, command.Target);
            if (skill.Cooldown > 0) actor.AvailableAt[skill.Id] = actor.TurnsTaken + skill.Cooldown + 1;
            actor.Blocked.Remove(skill.Id);
            PayCost(actor, skill);

            CheckResult check = null;
            if (skill.Rolls)
            {
                hero.Resolve -= preview.Resolve;
                hero.Blessing = 0;
                hero.Luck = 0;
                check = Fate.Roll(preview.Dice, preview.Chance, preview.Guaranteed);
                Emit(new BattleEvent { Kind = BattleEventKind.Roll, Actor = actor.Id, Target = target.Id, Skill = skill.Id, SkillName = skill.Name, Check = check, Perfect = check.Perfect, Text = RollLine(actor.Name, skill.Name, preview.Chance, check.Dice, check.Guaranteed, check.Successes, check.Perfect) });
                int gained = hero.AddGrit(check.RolledFailures);
                if (gained > 0) Emit(new BattleEvent { Kind = BattleEventKind.ResolveGained, Actor = actor.Id, Amount = gained, Text = Words.Line("battle.grit", actor.Name, gained) });
                TakeStake(actor, skill, check);
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
                    // A heal on everyone (the alchemist's mist) touches every hero standing; the rest go on as one.
                    if (skill.Target == SkillTarget.AllAllies)
                        foreach (var other in Heroes.Where(h => !h.Down && h.Id != target.Id).ToList())
                        {
                            int was = other.Hp;
                            other.Hero.Heal(heal);
                            Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = other.Id, Amount = other.Hp - was, Perfect = perfect, Text = Words.Line("battle.heal", other.Name, other.Hp - was) });
                        }
                    int before = target.Hp;
                    target.Hero.Heal(heal);
                    Emit(new BattleEvent { Kind = BattleEventKind.Heal, Actor = actor.Id, Target = target.Id, Amount = target.Hp - before, Perfect = perfect, Text = Words.Line("battle.heal", target.Name, target.Hp - before) });
                    break;
                case SkillKind.Revive:
                    int hp = EffectOn(actor, skill, target, successes, check?.Dice ?? 0);
                    target.Hp = Math.Min(target.MaxHp, hp);
                    Emit(new BattleEvent { Kind = BattleEventKind.Revive, Actor = actor.Id, Target = target.Id, Amount = target.Hp, Text = Words.Line("battle.revive", target.Name, target.Hp) });
                    break;
                case SkillKind.Support:
                    // A support on everyone (the seer's guiding star) touches every hero standing.
                    if (skill.Target == SkillTarget.AllAllies)
                        foreach (var each in Heroes.Where(h => !h.Down).ToList()) Support(actor, skill, each, check);
                    else Support(actor, skill, target, check);
                    break;
            }
            if (skill.Element.Length > 0) actor.LastElement = skill.Element;
            if (!Is(actor, "mnemonist") && skill.Id != EchoId) _lastAllySkill = skill;
            AfterAction(actor, skill);
            EndTurn(actor, skill.Delay);
            Advance();
            return null;
        }

        // Armor (or ward) taken off a blow; the monk's piercing palm goes straight through.
        int Reduce(SkillDef skill, int value, Unit target) => skill.Effect == "pierce" ? value : Mitigate(value, target, skill.Kind == SkillKind.Physical);

        void Attack(Unit actor, SkillDef skill, Unit target, CheckResult check)
        {
            int value = RawValue(actor, skill, check.Successes, check.Dice);
            // A resonant frost freezes: the foe loses its next turn instead of only waiting longer (Docs/Classes.md 원소술사).
            var cold = skill.Element == "frost" && Resonates(actor, skill) ? StatusKind.Stun : StatusKind.Chill;
            var targets = skill.Target == SkillTarget.AllEnemies ? Monsters.Where(m => !m.Down).ToList() : new List<Unit> { target };
            int total = 0;
            foreach (var t in targets)
            {
                int damage = Reduce(skill, value, t);
                if (damage <= 0)
                {
                    Emit(new BattleEvent { Kind = BattleEventKind.Miss, Actor = actor.Id, Target = t.Id, Text = Words.Line("battle.miss", t.Name) });
                    continue;
                }
                int before = t.Hp;
                Hurt(t, damage, actor.Id, check.Perfect, skill.Name);
                total += before - t.Hp;
                if (Is(actor, "monk") && before > t.Hp && actor.Ki < KiCap + actor.Hero.Special("ki_cap")) actor.Ki++;
                if (check.Successes > 0)
                {
                    if (skill.Effect == "burn") AddStatus(t, StatusKind.Burn, BurnTurns);
                    if (skill.Effect == "chill") AddStatus(t, cold, 1);
                    if (skill.Effect == "weaken") AddStatus(t, StatusKind.Weak, WeakTurns);
                    if (skill.Effect == "corrode") AddStatus(t, StatusKind.Corrode, CorrodeTurns);
                    if (skill.Effect == "snare" && check.Successes >= 2) AddStatus(t, StatusKind.Stun, 1);
                }
                if (check.Perfect && skill.Perfect == "stun") AddStatus(t, StatusKind.Stun, 1);
            }
            AfterAttack(actor, skill, check, total, target);
        }
    }
}
