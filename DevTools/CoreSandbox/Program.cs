using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;

// Balance sandbox (CLAUDE.md 명령, Docs/Dice.md 판정 크기의 기준). Plays the Core with BattleBot and prints numbers.
//   --dice                                  odds of the skills at level 1
//   --battle N --party a,b,c --encounter id [--level L] [--random]
//   --classes N [--encounter id] [--level L] every party of three playable classes
//   --replay report.txt                         a bug report from the game's settings, replayed
//   --adventure N --party a,b,c [--random | --human] [--easy] [--retries R]  whole campaigns (R: chapter retries) (--human: the smart bot with a
//                                                              quarter of its choices random, like a person)
static class Program
{
    static GameData _data;

    static int Main(string[] args)
    {
        _data = GameData.Load(path => File.ReadAllText(Path.Combine(DataFolder(), path)));
        string Option(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        int Count(string name, int fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) ? n : fallback;
        }
        bool smart = !args.Contains("--random");
        string[] party = Option("--party", "knight,hunter,cleric").Split(',');
        int level = Count("--level", 1);

        if (args.Contains("--dice")) DiceTable();
        else if (args.Contains("--battle")) Battles(Count("--battle", 1000), party, Option("--encounter", "goblins"), level, smart, true);
        else if (args.Contains("--classes")) Classes(Count("--classes", 500), Option("--encounter", ""), level, smart);
        else if (args.Contains("--replay")) Replay(Option("--replay", "report.txt"));
        else if (args.Contains("--adventure")) Adventures(Count("--adventure", 500), party, smart, args.Contains("--human") ? AdventureBot.HumanMistakes : 0, args.Contains("--easy") ? Difficulty.Easy : Difficulty.Normal, Count("--retries", 0), Option("--chapters", ""));
        else
        {
            Console.WriteLine("--dice | --battle N --party a,b,c --encounter id [--level L] | --classes N | --adventure N --party a,b,c  ([--random])");
            return 1;
        }
        return 0;
    }

    static string DataFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "_Project", "Resources", "Data");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Assets/_Project/Resources/Data");
    }

    static void DiceTable()
    {
        Console.WriteLine("기술별 1레벨 판정 (방어도 0 대상)");
        Console.WriteLine($"{"직업",-6} {"기술",-8} {"주사위",4} {"확률",4} {"빗나감",6} {"완벽",6} {"평균",6} {"결의1 평균",8}");
        foreach (var cls in _data.PlayableClasses)
        {
            var hero = new Hero(cls);
            foreach (var skill in hero.Skills.Where(s => s.Rolls))
            {
                int chance = skill.FixedChance > 0 ? skill.FixedChance : Dice.ClampChance(hero.Stat(skill.Stat.Value));
                double Mean(int sure)
                {
                    var odds = new CheckOdds(skill.Dice, chance, sure);
                    double mean = 0;
                    for (int s = 0; s <= skill.Dice; s++)
                    {
                        int v = Dice.Value(skill.Power, skill.Floor, s, skill.Dice);
                        if (s == skill.Dice) v = Dice.Percent(v, skill.PerfectPercent);
                        mean += odds.BySuccesses[s] * v;
                    }
                    return mean;
                }
                var plain = new CheckOdds(skill.Dice, chance, 0);
                Console.WriteLine($"{cls.Name,-6} {skill.Name,-8} {skill.Dice,4} {chance,3}% {plain.None,6:P1} {plain.Perfect,6:P1} {Mean(0),6:0.0} {Mean(1),8:0.0}");
            }
        }
        Console.WriteLine();
        Console.WriteLine("같은 기본 공격(3개, 70%)이 세 번 연속 빗나갈 확률: " + Math.Pow(new CheckOdds(3, 70, 0).None, 3).ToString("P4"));
    }

    sealed class Tally
    {
        public int Runs, Wins, Turns, HeroDowns, Survivors, HpLeft, HpMax;
        public double WinRate => Runs == 0 ? 0 : (double)Wins / Runs;
    }

    static Tally Battles(int runs, string[] party, string encounterId, int level, bool smart, bool print)
    {
        var encounter = _data.Encounters[encounterId];
        var tally = new Tally();
        for (int run = 0; run < runs; run++)
        {
            var heroes = party.Select(id => new Hero(_data.Classes[id])).ToList();
            foreach (var hero in heroes) hero.GainXp(level > 1 ? Hero.LevelXp[level - 1] : 0);
            var battle = new Battle(heroes, encounter.Monsters, (ulong)(run * 7919 + 13), false, _data);
            var random = new GameRandom((ulong)run, 3);
            int turns = 0;
            while (!battle.Over && turns < 500)
            {
                string error = battle.Do(BattleBot.Choose(battle, random, smart));
                if (error != null) throw new InvalidOperationException(error);
                turns++;
            }
            tally.Runs++;
            if (battle.State == BattleState.Won) tally.Wins++;
            tally.Turns += turns;
            tally.HeroDowns += battle.Events.Count(e => e.Kind == BattleEventKind.Down && battle.Unit(e.Target).IsHero);
            tally.Survivors += heroes.Count(h => !h.Down);
            tally.HpLeft += heroes.Sum(h => h.Hp);
            tally.HpMax += heroes.Sum(h => h.MaxHp);
        }
        if (print)
        {
            Console.WriteLine($"{string.Join("+", party.Select(p => _data.Classes[p].Name))} 대 {encounterId} (레벨 {level}, {(smart ? "똑똑한" : "아무렇게나")} 봇, {runs}판)");
            Console.WriteLine($"  승률 {tally.WinRate:P1}, 영웅 차례 평균 {(double)tally.Turns / runs:0.0}, 쓰러짐 평균 {(double)tally.HeroDowns / runs:0.00}, 끝까지 선 영웅 {(double)tally.Survivors / runs:0.00}/3, 남은 생명력 {(double)tally.HpLeft / tally.HpMax:P0}");
        }
        return tally;
    }

    static void Classes(int runs, string encounterId, int level, bool smart)
    {
        var ids = _data.PlayableClasses.Select(c => c.Id).ToList();
        var parties = new List<string[]>();
        for (int a = 0; a < ids.Count; a++)
            for (int b = a + 1; b < ids.Count; b++)
                for (int c = b + 1; c < ids.Count; c++)
                    parties.Add(new[] { ids[a], ids[b], ids[c] });
        var encounters = encounterId.Length > 0 ? new[] { encounterId } : new[] { "goblins", "wolves", "bandits", "warden" };
        Console.WriteLine($"모든 셋 조합 ({parties.Count}개), 레벨 {level}(보스는 3), {(smart ? "똑똑한" : "아무렇게나")} 봇, 조합·전투마다 {runs}판");
        Console.WriteLine($"{"파티",-24} " + string.Join(" ", encounters.Select(e => $"{e,8}")));
        var byClass = ids.ToDictionary(id => id, id => new List<double>());
        var partyRates = new List<(string[] party, double win, double hp)>();
        foreach (var party in parties)
        {
            var rates = encounters.Select(e => Battles(runs, party, e, e == "warden" ? Math.Max(level, 3) : level, smart, false)).ToList();
            if (parties.Count <= 40) Console.WriteLine($"{string.Join("+", party.Select(p => _data.Classes[p].Name)),-24} " + string.Join(" ", rates.Select(t => $"{t.WinRate,8:P0}")));
            partyRates.Add((party, rates.Average(t => t.WinRate), rates.Average(t => (double)t.HpLeft / Math.Max(1, t.HpMax))));
            foreach (string id in party) byClass[id].Add(rates.Average(t => t.WinRate));
        }
        Console.WriteLine();
        Console.WriteLine("직업이 들어간 파티의 평균 승률");
        foreach (var pair in byClass.OrderByDescending(p => p.Value.Average())) Console.WriteLine($"  {_data.Classes[pair.Key].Name,-6} {pair.Value.Average():P1}");
        Console.WriteLine();
        Console.WriteLine("가장 약한 파티 다섯과 가장 센 파티 다섯 (전투 평균 승률, 남은 생명력)");
        var sorted = partyRates.OrderBy(p => p.win).ThenBy(p => p.hp).ToList();
        foreach (var p in sorted.Take(5).Concat(sorted.Skip(Math.Max(5, sorted.Count - 5))))
            Console.WriteLine($"  {string.Join("+", p.party.Select(id => _data.Classes[id].Name)),-24} {p.win:P0} {p.hp:P0}");
    }

    static void Adventures(int runs, string[] party, bool smart, int mistakes, Difficulty difficulty, int retries, string chapters)
    {
        // A short run (Docs/Map.md 짧은 판): "--chapters 3-4" plays chapters 3 to 4, 1-based.
        int first = 0, last = -1;
        if (chapters.Length > 0)
        {
            string[] range = chapters.Split('-');
            first = int.Parse(range[0]) - 1;
            last = int.Parse(range[range.Length - 1]) - 1;
        }
        int wins = 0;
        var endings = new Dictionary<string, int>();
        double levels = 0, battles = 0, gold = 0, renown = 0, pages = 0;
        var reached = new int[_data.Chapters.Count + 1];
        int quest = 0, clue = 0, midboss = 0;
        double firstLevel = 0, firstDay = 0, firstBattles = 0;
        int usedRetries = 0;
        for (int run = 0; run < runs; run++)
        {
            var adventure = new Adventure(_data, party, (ulong)(run * 104729 + 7), null, difficulty, false, first, last);
            var bot = new GameRandom((ulong)run, 9);
            int cleared = 0;
            int tries = retries;
            for (int step = 0; step < 60000 && !adventure.Over; step++)
            {
                string error = AdventureBot.Step(adventure, bot, smart, mistakes);
                if (error != null) throw new InvalidOperationException($"bot step {step} on {adventure.Screen}: {error}");
                // A lost chapter tried again from its start, as a player would (the end screen's 이 장 처음부터).
                if (adventure.Screen == AdventureScreen.Defeat && tries > 0 && adventure.ChapterStartSave() is string again)
                {
                    tries--;
                    usedRetries++;
                    adventure = Adventure.Load(_data, again, out _, out _);
                }
                if (adventure.ChaptersCleared > cleared)
                {
                    cleared = adventure.ChaptersCleared;
                    if (cleared == 1)
                    {
                        firstLevel += adventure.Heroes.Average(h => h.Level);
                        firstDay += adventure.Day;
                        firstBattles += adventure.BattlesWon;
                    }
                }
            }
            string chapter = $"{adventure.Chapter.Index + 1}장 ";
            string end;
            if (adventure.Screen == AdventureScreen.Victory)
            {
                wins++;
                end = "승리" + (adventure.Story?.Id == adventure.Chapter.EpilogueKind ? "(다정한 결말)" : adventure.Story?.Id == adventure.Chapter.EpilogueGrim ? "(쓸쓸한 결말)" : "");
            }
            else if (adventure.Story?.Id == adventure.Chapter.Rift) end = chapter + "균열이 열림";
            else end = chapter + (adventure.Battle != null && adventure.Battle.Monsters.Any(m => m.Monster.Boss) ? "보스에게 짐" : adventure.Battle != null && adventure.Kind == Adventure.JoinKind.Camp ? "중간 보스에게 짐" : "길에서 전멸");
            endings.TryGetValue(end, out int n);
            endings[end] = n + 1;
            reached[adventure.ChaptersCleared]++;
            levels += adventure.Heroes.Average(h => h.Level);
            battles += adventure.BattlesWon;
            gold += adventure.Gold;
            renown += adventure.Renown;
            pages += adventure.PagesEarned;
            midboss += adventure.MidbossesBeaten;
            if (adventure.HasFlag(_data.Chapters[0].WeakenFlag)) quest++;
            if (adventure.HasFlag(_data.Chapters[0].NoSummonFlag)) clue++;
        }
        Console.WriteLine($"{string.Join("+", party.Select(p => _data.Classes[p].Name))} ({(smart ? mistakes > 0 ? "사람 같은" : "똑똑한" : "아무렇게나")} 봇, {(difficulty == Difficulty.Easy ? "쉬움, " : "")}{runs}판): 캠페인 승리 {(double)wins / runs:P1}, 1장 넘김 {(double)(runs - reached[0]) / runs:P0}, 레벨 {levels / runs:0.0}, 이긴 전투 {battles / runs:0.0}, 남은 금화 {gold / runs:0}, 명성 {renown / runs:0.0}, 일지 페이지 {pages / runs:0.0}");
        int past = Math.Max(1, runs - reached[0]);
        if (retries > 0) Console.WriteLine($"  장 다시 하기: 판마다 {retries}번까지, 모두 {usedRetries}번 썼다");
        Console.WriteLine($"  1장 비석 {(double)quest / runs:P0}, 1장 단서 {(double)clue / runs:P0}, 중간 보스 {midboss / (double)runs:0.0} · 1장을 넘긴 판: {firstDay / past:0.0}일, 레벨 {firstLevel / past:0.0}, 이긴 전투 {firstBattles / past:0.0}");
        foreach (var pair in endings.OrderByDescending(p => p.Value)) Console.WriteLine($"  {pair.Key}: {pair.Value}");
    }

    // A bug report (the settings' 버그 제보, Docs/Playtest.md): the save is replayed and the state it reaches is
    // printed, with how far the commands fitted.
    static void Replay(string file)
    {
        string text = System.IO.File.ReadAllText(file);
        string save = string.Join("\n", text.Replace("\r", "").Split('\n').Where(l => !l.StartsWith("#")));
        var adventure = Adventure.Load(_data, save, out string error, out string note);
        foreach (string line in text.Split('\n').Where(l => l.StartsWith("#"))) Console.WriteLine(line);
        if (adventure == null)
        {
            Console.WriteLine($"불러오지 못했다: {error}");
            return;
        }
        if (note != null) Console.WriteLine(note);
        Console.WriteLine($"{adventure.Chapter.Index + 1}장 {adventure.Chapter.Title}, {adventure.Day}일째 {adventure.TimeName}, 화면 {adventure.Screen}, 명령 {adventure.Log.Count}개, 균열 {adventure.Doom}, 금화 {adventure.Gold}, 희망 {adventure.Hope}, 명성 {adventure.Renown}");
        foreach (var hero in adventure.Heroes)
            Console.WriteLine($"  {hero.Name} Lv{hero.Level} {hero.Hp}/{hero.MaxHp} 결의 {hero.Resolve} 상처 {hero.Wounds}{(hero.Dead ? " (떠남)" : "")} @{adventure.Positions[adventure.Heroes.IndexOf(hero)]} 무기 {hero.Weapon.Name}");
        if (adventure.Battle != null && adventure.Screen == AdventureScreen.Battle)
            foreach (var e in adventure.Battle.Events.Skip(Math.Max(0, adventure.Battle.Events.Count - 12))) Console.WriteLine("  · " + e.Text);
        foreach (string line in adventure.Report) Console.WriteLine("  > " + line);
    }
}
