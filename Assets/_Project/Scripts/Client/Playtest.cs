using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    // The cloud's playtests drive the game through this (Docs/Playtest.md): the web page calls
    // unityInstance.SendMessage("Game", "Playtest", "command; command"). Each command answers one console line,
    // "[playtest] <command> <answer>"; a long answer goes on in "[playtest]+" lines. Buttons are pressed by the
    // playtest itself with real clicks and taps where "buttons" says they are; these commands only read the game,
    // set up a case, or skip ahead.
    public sealed partial class GameRoot
    {
        const int LineLength = 900;
        static readonly GameRandom BotRandom = new GameRandom(20261109, 5);

        // Unity calls this by name from the page.
        public void Playtest(string commands)
        {
            foreach (string raw in commands.Split(';'))
            {
                string command = raw.Trim();
                if (command.Length == 0) continue;
                string answer;
                try
                {
                    answer = Run(command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                }
                catch (Exception e)
                {
                    answer = "error " + e.Message;
                }
                Answer(command.Split(' ')[0], answer);
            }
        }

        static void Answer(string word, string answer)
        {
            string line = $"[playtest] {word} {answer}";
            Debug.Log(line.Length <= LineLength ? line : line.Substring(0, LineLength));
            for (int at = LineLength; at < line.Length; at += LineLength)
                Debug.Log("[playtest]+" + line.Substring(at, Math.Min(LineLength, line.Length - at)));
        }

        string Run(string[] words)
        {
            string Arg(int i, string fallback = "") => i < words.Length ? words[i] : fallback;
            int Number(int i, int fallback = 0) => i < words.Length && int.TryParse(words[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;

            switch (words[0])
            {
                case "probe":
                    Ui.Probing = Arg(1, "on") != "off";
                    // The first-time tip cards would cover what the playtest presses; "tips reset" brings them back.
                    if (Ui.Probing)
                    {
                        Tips.MarkAllSeen();
                        _tipOpen = null;
                    }
                    return "ok";
                case "buttons":
                    return Buttons();
                case "texts":
                    return Texts();
                case "spills":
                    // Lines drawn wider than their place on the last frame (Ui.Text).
                    {
                        var json = new Json().Open().List("spills");
                        foreach (string text in Ui.LastSpilled.Distinct()) json.Add(null, text);
                        return json.End().Close().ToString();
                    }
                case "report":
                    return Report();
                case "speed":
                    _speed = float.Parse(Arg(1, "1"), CultureInfo.InvariantCulture);
                    return "ok";
                case "touch":
                    WebBridge.ForceTouch = Arg(1) == "on" ? true : Arg(1) == "off" ? (bool?)false : null;
                    return "ok";
                case "seed":
                    _nextSeed = ulong.Parse(Arg(1, "1"), CultureInfo.InvariantCulture);
                    return "ok";
                case "start":
                    string[] party = Arg(1, "knight,hunter,cleric").Split(',');
                    StartAdventure(party, words.Length > 2 ? ulong.Parse(Arg(2), CultureInfo.InvariantCulture) : (ulong?)null);
                    return "ok";
                case "title":
                    BackToTitle();
                    return "ok";
                case "continue":
                    return Act(_adventure.Continue) ?? "ok";
                case "choose":
                    return Act(() => _adventure.Choose(Number(1), Number(2), Number(3))) ?? "ok";
                case "autochoose":
                    // The first choice the party can take, tried by the hero likeliest to pass.
                    var choice = _adventure.Choices().First(c => _adventure.Affordable(c) && _adventure.CanAttempt(c).Count > 0);
                    var by = _adventure.CanAttempt(choice).OrderByDescending(h => choice.HasCheck ? Adventure.Odds(choice, h, 0).AtLeast(choice.Need) : 0).First();
                    return Act(() => _adventure.Choose(choice.Index, _adventure.Heroes.IndexOf(by))) ?? "ok " + choice.Label;
                case "roll":
                    return Act(_adventure.RollMove) ?? "ok " + _adventure.MovePoints;
                case "move":
                    var to = new Hex(Number(1), Number(2));
                    return Act(() => _adventure.Move(to)) ?? "ok";
                case "stay":
                    return Act(_adventure.Stay) ?? "ok";
                case "camp":
                    return Act(_adventure.Camp) ?? "ok";
                case "join":
                    var all = _adventure.JoinCandidates.ToList();
                    return Act(() => _adventure.Join(all)) ?? "ok";
                case "upgrade":
                    return Act(() => _adventure.ChooseUpgrade(Number(1))) ?? "ok";
                case "goto":
                    // Puts the current hero next to a place: town, quest, boss, shrine, or col row.
                    var where = Arg(1) switch
                    {
                        "town" => _adventure.Map.Tile(Site.Town).Hex,
                        "quest" => _adventure.Map.Tile(Site.Quest).Hex,
                        "boss" => _adventure.Map.Tile(Site.Boss).Hex,
                        "shrine" => _adventure.Map.Tile(Site.Shrine).Hex,
                        _ => new Hex(Number(1), Number(2)),
                    };
                    var near = where.Neighbors().FirstOrDefault(h => _adventure.Map.Def.Passable(h) && !_adventure.Map.Stops(_adventure.Map.Tile(h)));
                    _adventure.Teleport(_adventure.MapHero, near);
                    // The place itself last: the chapter's maps differ by seed, so the test asks where it is.
                    return $"ok {near} {where}";
                case "autoplay":
                    // The adventure bot plays this many steps (whole battles count one step each command).
                    // "autoplay 20 camp" stops when the camp between two chapters opens (Docs/Narrative.md 막간).
                    int steps = Number(1, 50);
                    bool stopAtCamp = Arg(2) == "camp";
                    for (int i = 0; i < steps && _adventure.Screen != AdventureScreen.Victory && _adventure.Screen != AdventureScreen.Defeat; i++)
                    {
                        string failed = Act(() => AdventureBot.Step(_adventure, BotRandom));
                        if (failed != null) return "error " + failed;
                        if (stopAtCamp && _adventure.Screen == AdventureScreen.Event && _adventure.Event.Pool == _adventure.Chapter.InterludePool) break;
                    }
                    if (_battleShown != null)
                    {
                        while (_played < _battleShown.Events.Count) Play(_battleShown.Events[_played++]);
                        _eventTimer = 0;
                    }
                    // The bot heard the campfire talks; their cards would only cover the buttons ("talk" shows one).
                    _talksShown = _adventure.Talks.Count;
                    return "ok " + _adventure.Screen;
                case "gear":
                    _gearOpen = Arg(1, "on") != "off";
                    return "ok";
                case "sound":
                    Sounds.Muted = Arg(1) == "off";
                    return "ok";
                case "auto":
                    return Auto(false);
                case "autobattle":
                    return Auto(true);
                case "skip":
                    // Shows the rest of the battle's playback at once.
                    if (_battleShown != null)
                    {
                        while (_played < _battleShown.Events.Count) Play(_battleShown.Events[_played++]);
                        _eventTimer = 0;
                    }
                    return "ok";
                case "tips":
                    // "tips off" marks every first-time tip seen (the default for playtests), "tips reset" brings them back.
                    if (Arg(1, "off") == "reset") Tips.Reset();
                    else Tips.MarkAllSeen();
                    _tipOpen = null;
                    return "ok";
                case "tip":
                    ShowTip(Arg(1, "map"));
                    return _tipOpen != null ? "ok" : "error no tip " + Arg(1);
                case "avoid":
                    return Act(_adventure.Avoid) ?? "ok " + _adventure.Screen;
                case "scare":
                    return Act(_adventure.Scare) ?? "ok " + _adventure.Screen;
                case "reroll":
                    return Act(_adventure.Reroll) ?? "ok " + _adventure.MovePoints;
                case "rumor":
                    return Act(_adventure.Rumor) ?? "ok";
                case "donate":
                    return Act(_adventure.Donate) ?? "ok " + _adventure.Renown;
                case "use":
                    return Act(() => _adventure.UseItem(Number(1), Arg(2, Hero.PotionId))) ?? "ok";
                case "give":
                    // Puts consumables in a hero's pack: give hero item count.
                    _adventure.Heroes[Number(1)].AddConsumable(Arg(2, Hero.PotionId), Number(3, 1));
                    return "ok";
                case "forge":
                    return Act(() => _adventure.Forge(Number(1), (ItemSlot)System.Enum.Parse(typeof(ItemSlot), Arg(2, "weapon"), true))) ?? "ok";
                case "recruit":
                    return Act(() => _adventure.Recruit(Arg(1))) ?? "ok";
                case "hardcore":
                    _hardcore = Arg(1, "on") == "on";
                    return "ok";
                case "autobutton":
                    _autoBattle = Arg(1, "on") == "on";
                    return "ok";
                case "history":
                    _historyOpen = Arg(1, "on") == "on";
                    return "ok " + _history.Count;
                case "bugreport":
                    // Opens the bug report window; "bugreport text" answers the report itself.
                    if (Arg(1) == "text") return (BugReport(SaveStore.Read() ?? _adventure?.Save() ?? "")).Replace("\n", " | ");
                    _reportOpen = Arg(1, "on") == "on";
                    return "ok";
                case "retry":
                    return RetryChapter() ?? "ok " + _adventure.Chapter.Id;
                case "gold":
                    _adventure.Gold = Number(1, 500);
                    return "ok";
                case "talk":
                    // Shows the last campfire talk again as its card.
                    if (_adventure.Talks.Count == 0) return "error no talk yet";
                    _talksShown = _adventure.Talks.Count - 1;
                    return "ok";
                case "row":
                    return Act(() => _adventure.SetRow(Number(1), Arg(2) == "back")) ?? "ok";
                case "delve":
                    return Act(_adventure.Delve) ?? "ok";
                case "leavedungeon":
                    return Act(_adventure.LeaveDungeon) ?? "ok";
                case "contract":
                    return Act(_adventure.TakeContract) ?? "ok";
                case "difficulty":
                    _difficulty = Arg(1) == "easy" ? Difficulty.Easy : Difficulty.Normal;
                    return "ok";
                case "language":
                    Settings.Language = Arg(1, "ko");
                    ReloadData();
                    return "ok";
                case "art":
                    // What the drawn pictures did: the title picture, the battle's backdrop, how many hit effects showed.
                    return ArtReport();
                case "perf":
                    // Frame times and memory since the last "perf reset" (GameRoot.Perf.cs).
                    if (Arg(1) == "reset")
                    {
                        ResetPerf();
                        return "ok";
                    }
                    return PerfReport();
                case "slot":
                    // The save slot new and continued adventures use (0 is the practice run's).
                    SaveStore.Slot = Number(1, 1);
                    return "ok " + SaveStore.Slot;
                case "practice":
                    StartPractice();
                    return "ok";
                case "reached":
                    // The furthest chapter the journal remembers (to try the party screen's chapter choice).
                    JournalNow.ReachForTest(Number(1, 2));
                    SaveStore.WriteJournal(JournalNow);
                    return "ok " + JournalNow.Reached;
                case "range":
                    // The party screen's start chapter (1-based) and "one" for a one-chapter run.
                    _startChapter = Number(1, 1) - 1;
                    _oneChapter = Arg(2) == "one";
                    return "ok";
                case "share":
                    // What the end screen's [기록 복사] copies, its lines joined by " | ".
                    if (_adventure == null) return "error no adventure";
                    return RunSummary(_adventure).Replace("\n", " | ");
                case "textsize":
                    Settings.TextSize = Number(1, 100);
                    return "ok";
                case "wound":
                    _adventure.Heroes[Number(1)].AddWound();
                    return "ok";
                case "chapter":
                    // Skips to the start of chapter n (1-based) by beating the bosses before it.
                    int target = Number(1, 2) - 1;
                    for (int guard = 0; guard < 20 && _adventure.Chapter.Index < target && !_adventure.Over; guard++)
                    {
                        if (_adventure.Screen == AdventureScreen.Story) _adventure.Continue();
                        else _adventure.EndChapterForTest();
                    }
                    SaveStore.Write(_adventure.Save());
                    return "ok " + _adventure.Chapter.Id;
                case "endchapter":
                    // The chapter's boss beaten without the battle: its ending page comes up.
                    _adventure.EndChapterForTest();
                    SaveStore.Write(_adventure.Save());
                    return "ok " + _adventure.Story?.Id;
                case "pages":
                    // Gives the journal pages (to try the journal screen).
                    JournalNow.AddPages(Number(1, 10));
                    SaveStore.WriteJournal(JournalNow);
                    return "ok " + JournalNow.Pages;
                case "screen":
                    _screen = Arg(1) switch
                    {
                        "journal" => ClientScreen.Journal,
                        "settings" => ClientScreen.Settings,
                        "party" => ClientScreen.Party,
                        "slots" => ClientScreen.Slots,
                        _ => ClientScreen.Title,
                    };
                    return "ok";
                case "hurt":
                    var hero = _adventure.Heroes[Number(1)];
                    hero.Hp = Math.Max(0, hero.Hp - Number(2));
                    return "ok";
                case "resolve":
                    _adventure.Heroes[Number(1)].Resolve = Number(2);
                    return "ok";
                default:
                    return "error unknown command " + words[0];
            }
        }

        // The battle bot's command for the current hero (auto), or until the battle ends (autobattle).
        string Auto(bool all)
        {
            var battle = _adventure?.Battle;
            if (battle == null || _adventure.Screen != AdventureScreen.Battle) return "error no battle";
            int done = 0;
            do
            {
                if (battle.State != BattleState.HeroTurn) break;
                string error = Act(() => _adventure.Fight(BattleBot.Choose(battle, BotRandom)));
                if (error != null) return "error " + error;
                done++;
            } while (all && done < 500);
            _selectedFor = -1;
            return "ok " + done;
        }

        string Buttons()
        {
            var json = new Json().Open().Add("width", Ui.Width).Add("height", Ui.Height).Add("scale", Ui.Scale).List("buttons");
            foreach (var b in Ui.LastDrawn)
            {
                json.Open().Add("label", b.Label).Add("row", b.Row ?? "").Add("enabled", b.Enabled).Add("shown", b.Shown)
                    .List("rect").Add(null, b.Rect.x).Add(null, b.Rect.y).Add(null, b.Rect.width).Add(null, b.Rect.height).End().Close();
            }
            return json.End().Close().ToString();
        }

        string Texts()
        {
            var json = new Json().Open().List("texts");
            foreach (string text in Ui.LastWritten.Distinct()) json.Add(null, text);
            return json.End().Close().ToString();
        }

        // The state a playtest checks, in one line.
        string Report()
        {
            var json = new Json().Open()
                .Add("screen", _screen.ToString())
                .Add("touch", Ui.Touch)
                .Add("width", Ui.Width)
                .Add("height", Ui.Height)
                .Add("pages", JournalNow.Pages).Add("perks", string.Join(",", ChosenPerks()))
                .Add("language", Settings.Language).Add("textSize", Settings.TextSize).Add("vibration", Settings.Vibration)
                .Add("achievements", JournalNow.Achieved.Count).Add("colorMarks", Settings.ColorMarks).Add("lessMotion", Settings.LessMotion)
                .Add("slot", SaveStore.Slot).Add("reached", JournalNow.Reached).Add("startChapter", _startChapter).Add("oneChapter", _oneChapter);
            var adventure = _adventure;
            if (adventure != null)
            {
                json.Add("adventure", adventure.Screen.ToString()).Add("day", adventure.Day).Add("time", adventure.TimeOfDay).Add("doom", adventure.Doom)
                    .Add("gold", adventure.Gold).Add("hope", adventure.Hope).Add("mapHero", adventure.MapHero).Add("movePoints", adventure.MovePoints)
                    .Add("reachable", adventure.Reachable().Count).Add("levelUp", adventure.LevelUp != null).Add("gearOpen", _gearOpen)
                    .Add("bag", adventure.Bag.Count).Add("commands", adventure.Log.Count).Add("saved", SaveStore.Exists)
                    .Add("playing", BattlePlaying).Add("awaiting", AwaitingHero)
                    .Add("chapter", adventure.Chapter.Id).Add("renown", adventure.Renown).Add("rank", adventure.Rank.Title)
                    .Add("unknown", adventure.Map.Tiles.Count(t => !adventure.Knows(t))).Add("explored", adventure.Map.Tiles.Count(t => adventure.Explored(t.Hex)))
                    .Add("canAvoid", adventure.CanAvoid).Add("canScare", adventure.CanScare).Add("footsteps", adventure.Footsteps.Count)
                    .Add("tip", _tipOpen ?? "").Add("tooltip", Ui.ShownTip ?? "").Add("music", Music.Volume)
                    .Add("difficulty", adventure.Difficulty.ToString()).Add("dungeonRoom", adventure.DungeonRoom).Add("dungeonEvent", adventure.DungeonEvent).Add("contract", adventure.Contract != null)
                    .Add("talks", adventure.Talks.Count).Add("talkWaiting", TalkWaiting).Add("story", adventure.Story?.Id ?? "").Add("storyAll", _storyAll)
                    .Add("hardcore", adventure.Hardcore).Add("auto", _autoBattle).Add("history", _history.Count).Add("canRetry", _retrySave != null)
                    .Add("reportOpen", _reportOpen).Add("historyOpen", _historyOpen)
                    .Add("firstChapter", adventure.FirstChapter).Add("lastChapter", adventure.LastChapter)
                    .Add("combos", string.Join(",", adventure.Combos.Select(c => c.Id))).Add("tutorial", TutorialStep);
                json.List("heroes");
                foreach (var hero in adventure.Heroes)
                    json.Open().Add("class", hero.Class.Id).Add("level", hero.Level).Add("hp", hero.Hp).Add("max", hero.MaxHp)
                        .Add("resolve", hero.Resolve).Add("grit", hero.Grit).Add("potions", hero.Potions).Add("weapon", hero.Weapon.Id)
                        .Add("items", string.Join(",", hero.Consumables.Where(c => c.Value > 0).Select(c => c.Key + ":" + c.Value)))
                        .Add("wounds", hero.Wounds).Add("back", hero.Back).Add("dead", hero.Dead).Add("plus", hero.Weapon.Plus)
                        .Add("at", adventure.Positions[adventure.Heroes.IndexOf(hero)].ToString()).Close();
                json.End();
                if (adventure.Event != null && adventure.Screen == AdventureScreen.Event)
                {
                    json.Add("event", adventure.Event.Id).List("choices");
                    foreach (var choice in adventure.Choices()) json.Add(null, choice.Label);
                    json.End();
                }
                if (adventure.Result != null && adventure.Screen == AdventureScreen.EventResult) json.Add("success", adventure.Result.Success);
                var battle = adventure.Battle;
                if (battle != null && (adventure.Screen == AdventureScreen.Battle || BattlePlaying))
                {
                    json.Open("battle").Add("state", battle.State.ToString()).Add("current", battle.Current?.Name ?? "")
                        .Add("encounter", adventure.Encounter?.Id ?? "").Add("events", battle.Events.Count).Add("shown", _played)
                        .Add("skill", _skill ?? "").Add("target", _target).Add("resolve", _resolve);
                    json.List("monsters");
                    foreach (var monster in battle.Monsters)
                        json.Open().Add("name", monster.Name).Add("hp", monster.Hp).Add("max", monster.MaxHp)
                            .Add("intent", monster.Intent?.Skill.Name ?? "").Add("aim", battle.IntentTarget(monster)?.Name ?? "").Close();
                    json.End().Close();
                }
            }
            return json.Close().ToString();
        }
    }
}
