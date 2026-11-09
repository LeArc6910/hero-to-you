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

        // Where the battle about to start came from.
        public enum JoinKind
        {
            Field,
            Ambush,
            Camp,
            Boss,
        }

        readonly Queue<(int hero, int level)> _levels = new Queue<(int, int)>();
        List<Hero> _fighters = new List<Hero>();
        bool _weakNext;
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
                if (i != MapHero && !Heroes[i].Down && Positions[i].Distance(tile.Hex) <= JoinRange) JoinCandidates.Add(i);
            Screen = AdventureScreen.Join;
        }

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
            if (scare ? !CanScare : !CanAvoid) return scare ? "광전사가 들판의 무리를 만났을 때 한 번만 겁줄 수 있습니다." : "들판의 무리를 만났을 때 한 번만 피할 수 있습니다.";
            var hero = Current;
            var odds = AvoidOdds(scare);
            hero.Blessing = 0;
            hero.Luck = 0;
            JoinCheck = _fate.Roll(AvoidDice, odds.Chance, odds.Guaranteed);
            hero.AddGrit(JoinCheck.RolledFailures);
            _joinTried = true;
            Record(scare ? "scare" : "avoid");
            Report.Clear();
            if (JoinCheck.Successes < AvoidNeed)
            {
                Report.Add(scare ? "무리가 겁먹지 않았다. 싸울 수밖에 없다." : "피하지 못했다. 싸울 수밖에 없다.");
                return null;
            }
            if (scare)
            {
                int xp = Encounter.Monsters.Sum(m => m.Xp) / 2;
                JoinTile.Content = Content.None;
                Report.Add($"{Korean.Subject(hero.Name)} 포효하자 무리가 흩어졌다 (경험치 +{xp})");
                GainXp(hero, xp);
            }
            else
            {
                Positions[MapHero] = _cameFrom;
                Report.Add($"{Korean.Subject(hero.Name)} 무리를 피해 물러났다");
            }
            EndTurn();
            return null;
        }

        // Starts the battle with the chosen heroes; the hero whose move started it always fights.
        public string Join(IReadOnlyCollection<int> heroes)
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.Join) return "합류할 전투가 없습니다.";
            if (heroes == null || !heroes.Contains(MapHero)) return $"{Korean.Topic(Current.Name)} 빠질 수 없습니다.";
            if (heroes.Any(h => !JoinCandidates.Contains(h))) return "멀리 있는 영웅은 합류할 수 없습니다.";
            var chosen = JoinCandidates.Where(heroes.Contains).ToList();
            Record("join " + string.Join(",", chosen));
            _fighters = chosen.Select(i => Heroes[i]).ToList();
            Report.Clear();
            var monsters = Encounter.Monsters.Select(Empower).ToList();
            Battle = new Battle(_fighters, monsters, NextSeed(), _weakNext, Data);
            _weakNext = false;
            Screen = AdventureScreen.Battle;
            // Monsters quick enough can finish a lone, hurt hero before its first turn.
            if (Battle.Over) FinishBattle();
            return null;
        }

        // A monster as the run made it (Docs/Map.md): the boss weaker for the quest site's words and without helpers
        // for the clue, stronger for a grown rift; every monster stronger on the hard road.
        MonsterDef Empower(MonsterDef monster)
        {
            if (!monster.Boss && !Hard) return monster;
            var copy = monster.Clone();
            if (Hard) copy.Hp = copy.Hp * 125 / 100;
            if (!monster.Boss) return copy;
            if (HasFlag(Chapter.WeakenFlag))
            {
                copy.Hp = copy.Hp * (100 - WeakenPercent) / 100;
                copy.Armor = Math.Max(0, copy.Armor - WeakenArmor);
                Report.Add(Chapter.WeakenText + $" (생명력 −{WeakenPercent}%, 방어도 −{WeakenArmor})");
            }
            if (HasFlag(Chapter.NoSummonFlag) && copy.Phase2Summon.Count > 0)
            {
                copy.Phase2Summon = new List<MonsterDef>();
                Report.Add(Chapter.NoSummonText + " (부하를 부르지 못한다)");
            }
            if (Doom >= GrownRift)
            {
                copy.Hp = copy.Hp * (100 + GrownRiftPercent) / 100;
                Report.Add($"자란 균열(균열 {Doom})이 {copy.Name}에게 힘을 준다. 생명력 +{GrownRiftPercent}%");
            }
            return copy;
        }

        ulong NextSeed() => ((ulong)_random.NextUInt() << 32) | _random.NextUInt();

        // The current hero's battle command. When it ends the battle, the rewards are given and the screen moves on;
        // the battle stays readable for the screen to finish showing it.
        public string Fight(BattleCommand command)
        {
            if (Screen != AdventureScreen.Battle || Battle == null || Battle.Over) return "전투 중이 아닙니다.";
            if (command == null) return "명령이 없습니다.";
            string error = Battle.Do(command);
            if (error != null) return error;
            Record($"fight {command.Actor} {command.Skill} {command.Target} {command.Resolve}");
            if (Battle.Over) FinishBattle();
            return null;
        }

        void FinishBattle()
        {
            Report.Clear();
            if (Battle.State == BattleState.Fled)
            {
                Positions[MapHero] = _cameFrom;
                Report.Add("도망쳤다. 적 무리는 그 자리에 남아 있다.");
                Screen = AdventureScreen.BattleResult;
                return;
            }
            if (Battle.State == BattleState.Lost)
            {
                Report.Add($"{string.Join(", ", _fighters.Select(h => h.Name))}: 모두 쓰러졌다.");
                if (Heroes.All(h => h.Down))
                {
                    Screen = AdventureScreen.Defeat;
                    return;
                }
                Report.Add(Hope > 0 ? "쓰러진 영웅은 다음 아침에 희망으로 일어나거나, 동료가 그 칸에 가서 부축하면 일어난다." : "희망이 없다. 동료가 그 칸에 가서 부축해야 일어난다.");
                Screen = AdventureScreen.BattleResult;
                return;
            }
            BattlesWon++;
            int xp = Battle.XpReward;
            int gold = Battle.GoldReward;
            Gold += gold;
            Report.Add($"경험치 {xp}(싸운 영웅만), 금화 {gold}");
            foreach (var hero in _fighters.Where(h => h.Down))
            {
                if (Hope <= 0)
                {
                    Report.Add($"{Korean.Topic(hero.Name)} 쓰러진 채다. 희망이 없다 (동료가 부축하면 일어난다)");
                    continue;
                }
                Hope--;
                hero.Hp = Math.Max(1, hero.MaxHp * HopeRevivePercent / 100);
                Report.Add($"{Korean.Subject(hero.Name)} 희망으로 일어났다 (생명력 {hero.Hp}, 남은 희망 {Hope})");
            }
            foreach (var hero in _fighters)
            {
                if (!hero.Down && hero.Resolve < hero.MaxResolve) hero.GainResolve(1);
                if (hero.Class.Id == "alchemist")
                {
                    hero.GainReagents(1 + hero.Special("reagent_gain"));
                    Report.Add($"{hero.Name}: 재료 {hero.Reagents}/{hero.ReagentMax}");
                }
                GainXp(hero, xp);
            }
            switch (Kind)
            {
                case JoinKind.Field:
                    if (JoinTile != null && JoinTile.Content == Content.Enemy) JoinTile.Content = Content.None;
                    break;
                case JoinKind.Camp:
                    JoinTile.Used = true;
                    MidbossesBeaten++;
                    Hope++;
                    Doom = Math.Max(0, Doom - 1);
                    var loot = Loot(Chapter.Index + 2);
                    if (loot != null) Bag.Add(loot);
                    Report.Add($"{Korean.Object(Chapter.MidbossName)} 쳤다. 희망 +1, 균열 −1" + (loot != null ? $", {Korean.Object(loot.Name)} 얻었다 (가방)" : ""));
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
            Report.Add($"{hero.Name} 레벨 {levels[levels.Count - 1]}! 최대 생명력 +{Hero.LevelHp * levels.Count}, {Stats.Name(main[0])}·{Stats.Name(main[1])} +{Hero.LevelStat * levels.Count}");
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
                    int pick = _random.Next(left.Count);
                    options.Add(left[pick]);
                    left.RemoveAt(pick);
                }
                LevelUp = new LevelUpChoice(index, level, options);
            }
        }

        // Takes one of the level-up's upgrades.
        public string ChooseUpgrade(int option)
        {
            if (LevelUp == null) return "고를 레벨업이 없습니다.";
            if (option < 0 || option >= LevelUp.Options.Count) return "그런 강화는 없습니다.";
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
