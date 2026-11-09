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
                    return "ok";
                case "buttons":
                    return Buttons();
                case "texts":
                    return Texts();
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
                    _adventure.Continue();
                    return "ok";
                case "jump":
                    _adventure.JumpTo(Number(1));
                    return "ok";
                case "choose":
                    return _adventure.Choose(Number(1), Number(2), Number(3)) ?? "ok";
                case "autochoose":
                    // The first choice the party can take, tried by the hero likeliest to pass.
                    var choice = _adventure.Choices().First(_adventure.Affordable);
                    var by = _adventure.CanAttempt(choice).OrderByDescending(h => choice.HasCheck ? Adventure.Odds(choice, h, 0).AtLeast(choice.Need) : 0).First();
                    return _adventure.Choose(choice.Index, _adventure.Heroes.IndexOf(by)) ?? "ok " + choice.Label;
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
                string error = _adventure.Fight(BattleBot.Choose(battle, BotRandom));
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
                .Add("height", Ui.Height);
            var adventure = _adventure;
            if (adventure != null)
            {
                json.Add("adventure", adventure.Screen.ToString()).Add("step", adventure.Step).Add("gold", adventure.Gold).Add("hope", adventure.Hope)
                    .Add("playing", BattlePlaying).Add("awaiting", AwaitingHero);
                json.List("heroes");
                foreach (var hero in adventure.Heroes)
                    json.Open().Add("class", hero.Class.Id).Add("level", hero.Level).Add("hp", hero.Hp).Add("max", hero.MaxHp)
                        .Add("resolve", hero.Resolve).Add("grit", hero.Grit).Add("potions", hero.Potions).Close();
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
