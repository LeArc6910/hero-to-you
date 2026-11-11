using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // Battles on the map (Docs/Map.md 전투 합류): who joins, avoiding or scaring a group off, the boss as the run made
    // it, the battle itself, what it gives, and the level-ups it brings.
    public sealed partial class Adventure
    {
        public const int JoinRange = 2;
        public const int AvoidDice = 3;
        public const int AvoidNeed = 2;
        public const int MidbossRenown = 2;
        public const int WeakenPercent = 20;
        public const int WeakenArmor = 1;
        public const int GrownRift = 6;
        public const int GrownRiftPercent = 20;
        public const int BossRenown = 3;
        public const int BardSongPercent = 15;

        // Where the battle about to start came from.
        public enum JoinKind
        {
            Field,
            Ambush,
            Camp,
            Boss,
            Dungeon,
        }

        readonly Queue<(int hero, int level)> _levels = new Queue<(int, int)>();

        // Level-ups still waiting behind the one on screen (a late start brings many, Docs/Map.md 짧은 판).
        public int LevelUpsWaiting => _levels.Count;
        List<Hero> _fighters = new List<Hero>();
        bool _weakNext;

        // The next battle's enemies start weakened (an event's "foes_weak": a party that came prepared).
        bool _foesWeakNext;
        bool _joinTried;

        public EncounterDef Encounter { get; private set; }
        public Battle Battle { get; private set; }
        public JoinKind Kind { get; private set; }
        public bool BossBattle => Kind == JoinKind.Boss;
        public IReadOnlyList<Hero> Fighters => _fighters;

        // Who may join the battle about to start (Join): living heroes within JoinRange, the mover first.
        public List<int> JoinCandidates { get; } = new List<int>();
        public MapTile JoinTile { get; private set; }

        // The last avoid or scare check, for the join screen to show.
        public CheckResult JoinCheck { get; private set; }

        // The level-up whose upgrade is to be picked; nothing else can be done until it is.
        public LevelUpChoice LevelUp { get; private set; }

        void PrepareJoin(MapTile tile, string encounterId, JoinKind kind)
        {
            JoinTile = tile;
            Encounter = Data.Encounters[encounterId];
            Kind = kind;
            _joinTried = false;
            JoinCheck = null;
            JoinCandidates.Clear();
            JoinCandidates.Add(MapHero);
            for (int i = 0; i < Heroes.Count; i++)
                if (i != MapHero && !Heroes[i].Down && Positions[i].Distance(tile.Hex) <= JoinReach(Heroes[i])) JoinCandidates.Add(i);
            Screen = AdventureScreen.Join;
        }

        // How far a hero comes from to fight beside the others: the knight runs to a friend's fight from one hex
        // farther (Docs/Classes.md 기사 지도 특기).
        public const int KnightReach = 1;

        public static int JoinReach(Hero hero) => JoinRange + (hero.Class.Id == "knight" ? KnightReach : 0);

        // A group in the open can be slipped past once (agility, the rogue with a sure die) or, by the berserker,
        // scared off (strength). Not an ambush, a camp or the boss.
        public bool CanAvoid => Screen == AdventureScreen.Join && Kind == JoinKind.Field && !_joinTried;

        public bool CanScare => CanAvoid && Current.Class.Id == "berserker";

        public CheckOdds AvoidOdds(bool scare)
        {
            var hero = Current;
            int chance = Dice.ClampChance(scare ? hero.Stat(Stat.Strength) : hero.Stat(Stat.Agility));
            int sure = Math.Min(AvoidDice, hero.Blessing + hero.Luck + (!scare && hero.Class.Id == "rogue" ? 1 : 0));
            return new CheckOdds(AvoidDice, chance, sure);
        }

        public string Avoid() => TryAvoid(false);

        public string Scare() => TryAvoid(true);

        string TryAvoid(bool scare)
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (scare ? !CanScare : !CanAvoid) return scare ? Data.Line("adventure.error.cannot_scare") : Data.Line("adventure.error.cannot_avoid");
            var hero = Current;
            var odds = AvoidOdds(scare);
            hero.Blessing = 0;
            hero.Luck = 0;
            JoinCheck = _avoidFate.Roll(AvoidDice, odds.Chance, odds.Guaranteed);
            hero.AddGrit(JoinCheck.RolledFailures);
            _joinTried = true;
            Record(scare ? "scare" : "avoid");
            Report.Clear();
            if (JoinCheck.Successes < AvoidNeed)
            {
                Report.Add(scare ? Data.Line("adventure.scare.failed") : Data.Line("adventure.avoid.failed"));
                return null;
            }
            if (scare)
            {
                int xp = Encounter.Monsters.Sum(m => m.Xp) / 2;
                JoinTile.Content = Content.None;
                Report.Add(Data.Line("adventure.scare.done", hero.Name, xp));
                GroupCleared(JoinTile);
                GainXp(hero, xp);
            }
            else
            {
                Positions[MapHero] = _cameFrom;
                Report.Add(Data.Line("adventure.avoid.done", hero.Name));
            }
            EndTurn();
            return null;
        }

        // Starts the battle with the chosen heroes; the hero whose move started it always fights.
        public string Join(IReadOnlyCollection<int> heroes)
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.Join) return Data.Line("adventure.error.no_battle_to_join");
            if (heroes == null || !heroes.Contains(MapHero)) return Data.Line("adventure.error.mover_must_fight", Current.Name);
            if (heroes.Any(h => !JoinCandidates.Contains(h))) return Data.Line("adventure.error.too_far_to_join");
            var chosen = JoinCandidates.Where(heroes.Contains).ToList();
            Record("join " + string.Join(",", chosen));
            _fighters = chosen.Select(i => Heroes[i]).ToList();
            Report.Clear();
            var monsters = Encounter.Monsters.Select(Empower).ToList();
            Battle = new Battle(_fighters, monsters, NextSeed(), _weakNext, Data, _foesWeakNext);
            _weakNext = false;
            _foesWeakNext = false;
            Screen = AdventureScreen.Battle;
            // Monsters quick enough can finish a lone, hurt hero before its first turn.
            if (Battle.Over) FinishBattle();
            return null;
        }

        public const int NormalHpPercent = 122;

        // A monster as the run made it (Docs/Map.md): the boss weaker for the quest site's words and without helpers
        // for the clue, stronger for a grown rift; every monster stronger on the hard road.
        MonsterDef Empower(MonsterDef monster)
        {
            var copy = monster.Clone();
            // Normal difficulty is set by the human-like bot (Docs/Design.md 7.1): monsters a little tougher than the sheet.
            if (Difficulty == Difficulty.Normal) copy.Hp = copy.Hp * NormalHpPercent / 100;
            if (Hard) copy.Hp = copy.Hp * 125 / 100;
            if (Difficulty == Difficulty.Easy) copy.Hp = Math.Max(1, copy.Hp * 75 / 100);
            if (!monster.Boss) return copy;
            if (HasFlag(Chapter.WeakenFlag))
            {
                copy.Hp = copy.Hp * (100 - WeakenPercent) / 100;
                copy.Armor = Math.Max(0, copy.Armor - WeakenArmor);
                Report.Add(Data.Line("adventure.boss.weakened", Chapter.WeakenText, WeakenPercent, WeakenArmor));
            }
            if (HasFlag(Chapter.NoSummonFlag) && copy.Phase2Summon.Count > 0)
            {
                copy.Phase2Summon = new List<MonsterDef>();
                Report.Add(Data.Line("adventure.boss.no_summon", Chapter.NoSummonText));
            }
            if (Doom >= GrownRift)
            {
                copy.Hp = copy.Hp * (100 + GrownRiftPercent) / 100;
                Report.Add(Data.Line("adventure.boss.grown_rift", Doom, copy.Name, GrownRiftPercent));
            }
            return copy;
        }

        // Each battle's seed comes from the adventure's seed and how many battles came before, nothing else.
        ulong NextSeed()
        {
            var random = new GameRandom(Seed, 1000 + (ulong)_battlesStarted++);
            return ((ulong)random.NextUInt() << 32) | random.NextUInt();
        }

        // The current hero's battle command. When it ends the battle, the rewards are given and the screen moves on;
        // the battle stays readable for the screen to finish showing it.
        public string Fight(BattleCommand command)
        {
            if (Screen != AdventureScreen.Battle || Battle == null || Battle.Over) return Data.Line("adventure.error.not_in_battle");
            if (command == null) return Data.Line("adventure.error.no_command");
            string error = Battle.Do(command);
            if (error != null) return error;
            Record($"fight {command.Actor} {command.Skill} {command.Target} {command.Resolve}");
            if (Battle.Over) FinishBattle();
            return null;
        }

        void FinishBattle()
        {
            Report.Clear();
            foreach (var monster in Battle.Monsters) if (monster.Monster != null) _codexMonsters.Add(monster.Monster.Id);
            if (Battle.State == BattleState.Fled)
            {
                Positions[MapHero] = _cameFrom;
                Report.Add(Data.Line("adventure.battle.fled"));
                Screen = AdventureScreen.BattleResult;
                return;
            }
            // Every hero who fell keeps a wound (Docs/Combat.md 상처); in the hardcore mode one who fell with no hope
            // left is gone for good.
            foreach (var hero in _fighters.Where(h => h.Down))
            {
                Falls++;
                if (Hardcore && Hope <= 0)
                {
                    hero.Dead = true;
                    Report.Add(Data.Line("adventure.battle.gone", hero.Name));
                    continue;
                }
                hero.AddWound();
                Report.Add(Data.Line("adventure.battle.wound", hero.Name, hero.Wounds, Hero.MaxWounds, Hero.WoundPercent * hero.Wounds));
            }
            if (Battle.State == BattleState.Lost)
            {
                Report.Add(Data.Line("adventure.battle.all_down", string.Join(", ", _fighters.Select(h => h.Name))));
                if (Heroes.All(h => h.Down))
                {
                    Screen = AdventureScreen.Defeat;
                    return;
                }
                Report.Add(Hope > 0 ? Data.Line("adventure.battle.down_hope") : Data.Line("adventure.battle.down_no_hope"));
                Screen = AdventureScreen.BattleResult;
                return;
            }
            BattlesWon++;
            int xp = Battle.XpReward;
            int gold = Battle.GoldReward;
            Gold += gold;
            Report.Add(Data.Line("adventure.battle.reward", xp, gold));
            foreach (var hero in _fighters.Where(h => h.Down && !h.Dead))
            {
                if (Hope <= 0)
                {
                    Report.Add(Data.Line("adventure.battle.stays_down", hero.Name));
                    continue;
                }
                Hope--;
                hero.Hp = Math.Max(1, hero.MaxHp * HopeRevivePercent / 100);
                Report.Add(Data.Line("adventure.battle.hope_revive", hero.Name, hero.Hp, Hope));
            }
            // The bard's closing song (Docs/Classes.md 음유시인): a bard who fought and still stands eases every fighter.
            if (_fighters.Any(h => h.Class.Id == "bard" && !h.Down))
                foreach (var hero in _fighters.Where(h => !h.Down && h.Hp < h.MaxHp))
                    Report.Add(Data.Line("adventure.battle.bard_song", HealLine(hero, Math.Max(1, hero.MaxHp * BardSongPercent / 100))));
            // The alchemist gathers from what fell: a reagent for every two foes, at least one (Docs/Classes.md 연금술사).
            int foes = Battle.Monsters.Count();
            foreach (var hero in _fighters)
            {
                if (!hero.Down && hero.Resolve < hero.MaxResolve) hero.GainResolve(1);
                if (hero.Class.Id == "alchemist")
                {
                    hero.GainReagents(Math.Max(1, (foes + 1) / 2) + hero.Special("reagent_gain"));
                    Report.Add(Data.Line("adventure.reagents", hero.Name, hero.Reagents, hero.ReagentMax));
                }
                GainXp(hero, xp);
            }
            switch (Kind)
            {
                case JoinKind.Field:
                    if (JoinTile != null && JoinTile.Content == Content.Enemy) JoinTile.Content = Content.None;
                    GroupCleared(JoinTile);
                    break;
                case JoinKind.Dungeon:
                    DungeonRoomCleared();
                    return;
                case JoinKind.Camp:
                    JoinTile.Used = true;
                    MidbossesBeaten++;
                    Hope++;
                    Doom = Math.Max(0, Doom - 1);
                    var loot = Loot(Chapter.Index + 2);
                    if (loot != null) AddToBag(loot);
                    Report.Add(loot != null ? Data.Line("adventure.camp_won.loot", Chapter.MidbossName, loot.Name) : Data.Line("adventure.camp_won", Chapter.MidbossName));
                    GainRenown(MidbossRenown, Report);
                    break;
                case JoinKind.Boss:
                    JoinTile.Used = true;
                    GainRenown(BossRenown, Report);
                    EndChapter();
                    return;
            }
            Screen = AdventureScreen.BattleResult;
        }

        void GainXp(Hero hero, int xp)
        {
            var levels = hero.GainXp(xp);
            if (levels.Count == 0) return;
            var main = hero.Class.MainStats;
            Report.Add(Data.Line("adventure.level_up", hero.Name, levels[levels.Count - 1], Hero.LevelHp * levels.Count, Stats.Name(main[0], Data), Stats.Name(main[1], Data), Hero.LevelStat * levels.Count));
            foreach (int level in levels) _levels.Enqueue((Heroes.IndexOf(hero), level));
            NextLevelUp();
        }

        void NextLevelUp()
        {
            while (LevelUp == null && _levels.Count > 0)
            {
                var (index, level) = _levels.Dequeue();
                var hero = Heroes[index];
                var left = hero.Class.Upgrades.Where(u => !hero.Upgrades.Contains(u)).ToList();
                if (left.Count == 0) continue;
                var options = new List<UpgradeDef>();
                while (options.Count < 2 && left.Count > 0)
                {
                    int pick = _levelRandom.Next(left.Count);
                    options.Add(left[pick]);
                    left.RemoveAt(pick);
                }
                LevelUp = new LevelUpChoice(index, level, options);
            }
        }

        // Takes one of the level-up's upgrades.
        public string ChooseUpgrade(int option)
        {
            if (LevelUp == null) return Data.Line("adventure.error.no_level_up");
            if (option < 0 || option >= LevelUp.Options.Count) return Data.Line("adventure.error.no_such_upgrade");
            var hero = Heroes[LevelUp.Hero];
            var upgrade = LevelUp.Options[option];
            hero.ApplyUpgrade(upgrade);
            Record($"upgrade {option}");
            Report.Add($"{hero.Name}: {upgrade.Name} ({upgrade.Text})");
            LevelUp = null;
            NextLevelUp();
            return null;
        }
    }
}
