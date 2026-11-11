using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    // The screens between battles (Docs/Narrative.md): the story pages, events with their choices and who tries them,
    // what a choice did, the battle's rewards and the rest, and the two ends.
    public sealed partial class GameRoot
    {
        int _choice = -1;
        int _chooser = -1;
        int _eventResolve;
        string _eventError;
        float _columnScroll;
        float _columnHeight;
        bool _columnDragging;
        float _columnLastY;
        string _columnFor;

        void ResetEventView()
        {
            _choice = -1;
            _chooser = -1;
            _eventResolve = 0;
            _eventError = null;
        }

        const float StripHeight = 46;

        // The scrolled column screens draw into, under the party strip, with room for the buttons at the bottom.
        Rect BeginColumn(string page, float footer)
        {
            if (page != _columnFor)
            {
                _columnFor = page;
                _columnScroll = 0;
            }
            DrawPartyStrip(new Rect(4, 4, Ui.Width - 8, StripHeight - 6));
            float w = Mathf.Min(680, Ui.Width - 24);
            var area = new Rect((Ui.Width - w) / 2, StripHeight + 10, w, Ui.Height - StripHeight - 24 - footer);
            // The page lies on a stone panel (UiKit) as tall as its words (last frame's), its words inside the frame.
            float shown = _columnHeight > 0 ? Mathf.Min(area.height, _columnHeight + 6) : area.height;
            Ui.Panel(new Rect(area.x - 8, area.y - 6, w + 16, Mathf.Max(80, shown) + 12));
            _columnScroll = Ui.Scroll(area, _columnScroll, _columnHeight - area.height, ref _columnDragging, ref _columnLastY);
            Ui.BeginArea(area, _columnScroll);
            return new Rect(0, 0, w, area.height);
        }

        void EndColumn(float contentHeight)
        {
            Ui.EndArea(_columnScroll);
            _columnHeight = contentHeight;
        }

        Rect Footer(float height) => new Rect((Ui.Width - Mathf.Min(680, Ui.Width - 24)) / 2, Ui.Height - height - 6, Mathf.Min(680, Ui.Width - 24), height);

        float Heading(Rect column, float y, string title, Color? color = null)
        {
            Ui.Title(new Rect(0, y, column.width, 36), title, 20, color);
            return y + 42;
        }

        float Paragraph(Rect column, float y, string text, float size = 14.5f, Color? color = null)
        {
            if (string.IsNullOrEmpty(text)) return y;
            float h = Ui.TextHeight(text, size, column.width);
            Ui.Text(new Rect(0, y, column.width, h), text, size, color ?? Palette.Ink, TextAnchor.UpperLeft, false, true);
            return y + h + 8;
        }

        // The story page (Docs/Narrative.md 연출): a picture of the place, then the text appearing a few letters at a
        // time; a tap shows it all, [계속] always goes on. An ending is a page of the journal being written.
        string _storyFor;
        float _storyStart;
        bool _storyAll;
        const float LettersPerSecond = 38;

        void DrawStory()
        {
            var story = _adventure.Story;
            if (_storyFor != story.Id + _adventure.Log.Count)
            {
                _storyFor = story.Id + _adventure.Log.Count;
                _storyStart = Time.unscaledTime;
                _storyAll = _speed <= 0;
            }
            // "journal" is a page of the journal; "journal+dawn" the same page under a picture.
            string art = story.Art ?? "";
            bool journal = art.StartsWith("journal");
            string scene = journal ? (art.Contains("+") ? art.Substring(art.IndexOf('+') + 1) : "") : art;
            var column = BeginColumn("story:" + story.Id, 56);
            float y = 4;
            if (scene.Length > 0)
            {
                float h = Mathf.Clamp(column.height * (journal ? 0.22f : 0.3f), 80, 170);
                SceneArt.Draw(new Rect(0, y, column.width, h), scene, PartyArt(), Time.unscaledTime);
                y += h + 8;
            }
            float size = 15;
            float textX = journal ? 18 : 0;
            float textW = column.width - textX * 2;
            string wrapped = Ui.Wrap(story.Text, size, textW);
            float textH = Ui.TextHeight(story.Text, size, textW);
            int shown = _storyAll ? wrapped.Length : Mathf.Min(wrapped.Length, (int)((Time.unscaledTime - _storyStart) * LettersPerSecond));
            if (shown >= wrapped.Length) _storyAll = true;
            if (journal)
            {
                // A page of the journal: paper, brown ink, the title as its heading.
                var page = new Rect(0, y, column.width, textH + 70);
                Ui.Fill(page, Palette.Hex("#E9DDC2"));
                Ui.Frame(page, Palette.Hex("#7A5A3A"), 2);
                Ui.Fill(new Rect(page.x + 8, page.y, 2, page.height), Palette.Hex("#C9B48E"));
                Ui.Text(new Rect(textX, y + 12, textW, 28), story.Title, 20, Palette.Hex("#5A3A22"), TextAnchor.MiddleLeft, true);
                Ui.Text(new Rect(textX, y + 48, textW, textH), wrapped.Substring(0, shown), size, Palette.Hex("#3A2A1A"), TextAnchor.UpperLeft);
                y += page.height + 8;
            }
            else
            {
                y = Heading(column, y, story.Title);
                Ui.Text(new Rect(0, y, textW, textH), wrapped.Substring(0, shown), size, Palette.Ink, TextAnchor.UpperLeft);
                y += textH + 8;
            }
            if (!_storyAll)
            {
                Ui.Text(new Rect(0, y, column.width, 18), Strings.Get("story.tap"), 11, Palette.Dim, TextAnchor.MiddleCenter);
                if (Ui.Hit(new Rect(0, 0, column.width, y + 20), "글", true, "이야기")) _storyAll = true;
                y += 20;
            }
            EndColumn(y);
            if (Ui.Button(Footer(48), Strings.Get("common.continue"), 17, true, Look.Primary, null, KeyCode.Space)) Act(_adventure.Continue);
        }

        // Between two rooms of the dungeon.
        void DrawDungeonRest()
        {
            var a = _adventure;
            var column = BeginColumn($"dungeon:{a.Log.Count}", 112);
            float y = Heading(column, 4, Strings.Format("dungeon.heading", a.Chapter.DungeonName, a.DungeonRoom, a.DungeonRooms));
            foreach (string line in a.Report) y = Paragraph(column, y, "· " + line, 13.5f, Palette.Journal);
            y = Paragraph(column, y, Strings.Format("dungeon.hint", Adventure.DelveHealPercent), 12, Palette.Dim);
            foreach (var hero in a.Fighters)
                y = Paragraph(column, y, $"{hero.Name}: {Strings.Format("hero.hp", hero.Hp, hero.MaxHp)}" + (hero.Wounds > 0 ? " · " + Strings.Format("hero.wounds", hero.Wounds) : "") + (hero.Down ? $" ({Strings.Get("hero.down")})" : ""), 13, hero.Down ? Palette.Damage : Palette.Ink);
            if (_actError != null) y = Paragraph(column, y, _actError, 12, Palette.Damage);
            EndColumn(y);
            var footer = Footer(104);
            if (Ui.Button(new Rect(footer.x, footer.y, footer.width, 48), Strings.Get("dungeon.delve"), 17, true, Look.Primary, null, KeyCode.Space)) Act(a.Delve);
            if (Ui.Button(new Rect(footer.x, footer.y + 56, footer.width, 48), Strings.Get("dungeon.leave"), 15, true, Look.Quiet, null, KeyCode.Escape)) Act(a.LeaveDungeon);
        }

        System.Collections.Generic.List<(string, string)> _partyArt;
        Adventure _partyArtFor;

        // The heroes for a picture, as (class, color); made again only for another adventure.
        System.Collections.Generic.List<(string, string)> PartyArt()
        {
            if (_partyArt == null || _partyArtFor != _adventure || _partyArt.Count != _adventure.Heroes.Count)
            {
                _partyArt = _adventure.Heroes.Select(hero => (hero.Class.Id, hero.Class.Color)).ToList();
                _partyArtFor = _adventure;
            }
            return _partyArt;
        }

        void DrawEvent()
        {
            var card = _adventure.Event;
            var choices = _adventure.Choices();
            var picking = choices.FirstOrDefault(c => c.Index == _choice);
            var column = BeginColumn($"event:{card.Id}:{_adventure.Log.Count}:{_choice}", picking != null ? 56 : 8);
            float y = 4;
            // The events that matter most (people coming back, shrines, the chapter's places) have a picture.
            if (!string.IsNullOrEmpty(card.Art) && picking == null)
            {
                float h = Mathf.Clamp(column.height * 0.2f, 64, 120);
                SceneArt.Draw(new Rect(0, y, column.width, h), card.Art, PartyArt(), Time.unscaledTime);
                y += h + 6;
            }
            // A room of the dungeon (Docs/Map.md 던전): which one, above the event.
            if (_adventure.DungeonEvent)
            {
                Ui.Text(new Rect(0, y, column.width, 18), Strings.Format("dungeon.heading", _adventure.Chapter.DungeonName, _adventure.DungeonRoom + 1, _adventure.DungeonRooms), 12, Palette.Dim);
                y += 20;
            }
            y = Heading(column, y, card.Title);
            y = Paragraph(column, y, card.Text);
            if (!string.IsNullOrEmpty(card.Journal)) y = Paragraph(column, y, Strings.Format("event.journal", card.Journal), 13, Palette.Journal);
            y += 4;

            if (picking == null)
            {
                for (int ci = 0; ci < choices.Count; ci++)
                {
                    var choice = choices[ci];
                    bool affordable = _adventure.Affordable(choice);
                    string label = Label(choice);
                    float h = Ui.Short ? 44 : 50;
                    var r = new Rect(0, y, column.width, h);
                    var look = choice.ClassId != null ? Look.Selected : Look.Normal;
                    Ui.Face(r, look, affordable);
                    float down = affordable && Ui.Pressed(r) ? 2 : 0;
                    Ui.Text(new Rect(14, y + 2 + down, column.width - 24, h * 0.55f), label, 14, Ui.LabelColor(look, affordable), TextAnchor.MiddleLeft, true);
                    Ui.TextFit(new Rect(14, y + h * 0.52f + down - 1, column.width - 24, h * 0.45f), Requirement(choice), 11, affordable ? Palette.Journal : Palette.Dim);
                    if (Ui.Hit(r, choice.Label, affordable, "선택지", Ui.Number(ci))) PickChoice(choice);
                    y += h + 6;
                }
                if (_eventError != null) y = Paragraph(column, y, _eventError, 12, Palette.Damage);
                EndColumn(y);
                return;
            }

            // Who tries it, and how much resolve.
            y = Paragraph(column, y, Strings.Format("event.who_for", picking.Label), 14, Palette.Gold);
            var heroes = _adventure.CanAttempt(picking);
            foreach (var hero in heroes)
            {
                int index = _adventure.Heroes.IndexOf(hero);
                int resolve = index == _chooser ? _eventResolve : 0;
                var odds = Adventure.Odds(picking, hero, resolve);
                string chance = picking.FixedChance > 0 ? $"{picking.FixedChance}%" : $"{Stats.Name(picking.Stat.Value, Sheets)} {Adventure.ChanceOf(picking, hero)}%";
                string label = Strings.Format("event.hero_odds", hero.Name, chance, picking.Dice, Percent(odds.AtLeast(picking.Need)));
                if (Ui.Button(new Rect(0, y, column.width, 44), label, 13.5f, true, index == _chooser ? Look.Selected : Look.Normal, hero.Name))
                {
                    _chooser = index;
                    _eventResolve = 0;
                }
                y += 50;
            }
            if (_chooser >= 0)
            {
                var hero = _adventure.Heroes[_chooser];
                int room = Adventure.ResolveRoom(picking, hero);
                Ui.Text(new Rect(0, y, 80, 36), Strings.Get("common.spend_resolve"), 13, Palette.Resolve, TextAnchor.MiddleLeft, true);
                if (Ui.Button(new Rect(84, y, 44, 36), "−", 18, _eventResolve > 0)) _eventResolve--;
                Ui.Text(new Rect(130, y, 60, 36), $"{_eventResolve}/{room}", 14, Palette.Ink, TextAnchor.MiddleCenter, true);
                if (Ui.Button(new Rect(192, y, 44, 36), "+", 18, _eventResolve < room)) _eventResolve++;
                y += 42;
                int free = Adventure.FreeOf(picking, hero);
                if (free > 0) y = Paragraph(column, y, Strings.Format("event.free", free), 12, Palette.Sure);
            }
            if (_eventError != null) y = Paragraph(column, y, _eventError, 12, Palette.Damage);
            EndColumn(y);

            var footer = Footer(48);
            float half = (footer.width - 8) / 2;
            if (Ui.Button(new Rect(footer.x, footer.y, half, footer.height), Strings.Get("event.other"), 15, true, Look.Quiet, null, KeyCode.Escape)) ResetEventView();
            if (Ui.Button(new Rect(footer.x + half + 8, footer.y, half, footer.height), Strings.Get("event.roll"), 17, _chooser >= 0, Look.Primary, null, KeyCode.Space))
                Choose(picking, _chooser, _eventResolve);
        }

        string Label(EventChoice choice) =>
            choice.ClassId != null ? $"[{(_data.Classes.TryGetValue(choice.ClassId, out var cls) ? cls.Name : choice.ClassId)}] {choice.Label}" : choice.Label;

        string Requirement(EventChoice choice)
        {
            int cost = Adventure.Cost(choice);
            string costText = cost > 0 ? Strings.Format("event.cost", cost) + (_adventure.Gold < cost ? " " + Strings.Get("event.short") : "") : "";
            if (!choice.HasCheck) return costText.Length > 0 ? costText : Strings.Get(choice.ClassId != null ? "event.no_check_class" : "event.no_check");
            string stat = choice.FixedChance > 0 ? $"{choice.FixedChance}%" : Stats.Name(choice.Stat.Value, Sheets);
            var best = _adventure.CanAttempt(choice).Select(h => Adventure.Odds(choice, h, 0).AtLeast(choice.Need)).DefaultIfEmpty(0).Max();
            return Strings.Format("event.check", stat, choice.Dice, choice.Need, Percent(best)) + " · " + FailCost(choice);
        }

        // What a check's failure takes (2026-11-11), next to its odds: "실패하면: 피해 4, 다음 전투 약화".
        static string FailCost(EventChoice choice)
        {
            var parts = new List<string>();
            foreach (var o in choice.Fail)
            {
                string part = o.Verb switch
                {
                    "hurt" => Strings.Format("event.lose.hurt", o.Amount),
                    "hurt_all" => Strings.Format("event.lose.hurt_all", o.Amount),
                    "gold" when o.Amount < 0 => Strings.Format("event.lose.gold", o.Amount),
                    "hope" when o.Amount < 0 => Strings.Format("event.lose.hope", o.Amount),
                    "renown" when o.Amount < 0 => Strings.Format("event.lose.renown", o.Amount),
                    "resolve" or "resolve_all" when o.Amount < 0 => Strings.Format("event.lose.resolve", o.Amount),
                    "weak_next" => Strings.Get("event.lose.weak_next"),
                    "battle" => Strings.Get("event.lose.battle"),
                    "doom" when o.Amount > 0 => Strings.Format("event.lose.doom", o.Amount),
                    _ => null,
                };
                if (part != null) parts.Add(part);
            }
            return Strings.Format("event.if_fail", parts.Count > 0 ? string.Join(", ", parts) : Strings.Get("event.lose.nothing"));
        }

        // Why a run was lost (2026-11-11): the blows that hurt the most in the last battle, the enemies' perfect rolls,
        // a boss rule that landed, potions never drunk; or the rift clock running out.
        List<string> DefeatCauses(Adventure a)
        {
            var lines = new List<string>();
            if (a.Story?.Id == a.Chapter.Rift)
            {
                lines.Add(Strings.Get("end.cause_rift"));
                return lines;
            }
            var battle = a.Battle;
            if (battle != null)
            {
                var hurt = new Dictionary<(string who, string what), int>();
                (string who, string what) blow = (null, null);
                string ruleHit = null, rule = null;
                int perfect = 0;
                foreach (var e in battle.Events)
                {
                    var actor = e.Actor >= 0 ? battle.Unit(e.Actor) : null;
                    if (e.Kind == BattleEventKind.Roll)
                    {
                        bool foe = actor != null && !actor.IsHero && actor.Side != Side.Party;
                        blow = foe ? (actor.Name, e.SkillName) : (null, null);
                        rule = foe && !string.IsNullOrEmpty(e.Skill) && _data.MonsterSkills.TryGetValue(e.Skill, out var skill) && RuleIcon(skill.Effect) != null ? skill.Effect : null;
                        if (foe && e.Perfect) perfect++;
                    }
                    else if (e.Kind == BattleEventKind.Damage && blow.who != null && battle.Unit(e.Target) is Unit target && target.IsHero)
                    {
                        hurt.TryGetValue(blow, out int had);
                        hurt[blow] = had + e.Amount;
                        if (rule != null) ruleHit = rule;
                    }
                }
                if (hurt.Count > 0)
                {
                    var worst = hurt.OrderByDescending(p => p.Value).First();
                    lines.Add(Strings.Format("end.cause_blow", worst.Key.who, worst.Key.what, worst.Value));
                }
                if (perfect > 0) lines.Add(Strings.Format("end.cause_perfect", perfect));
                if (ruleHit != null) lines.Add(Strings.Format("end.cause_rule", Strings.Get("rule." + ruleHit)));
            }
            int potions = a.Heroes.Sum(h => h.Count("potion"));
            if (potions > 0) lines.Add(Strings.Format("end.cause_potions", potions));
            return lines;
        }

        void PickChoice(EventChoice choice)
        {
            _eventError = null;
            var heroes = _adventure.CanAttempt(choice);
            if (heroes.Count == 0) return;
            if (!choice.HasCheck)
            {
                Choose(choice, _adventure.Heroes.IndexOf(heroes[0]), 0);
                return;
            }
            _choice = choice.Index;
            var best = heroes.OrderByDescending(h => Adventure.Odds(choice, h, 0).AtLeast(choice.Need)).First();
            _chooser = _adventure.Heroes.IndexOf(best);
            _eventResolve = 0;
        }

        void Choose(EventChoice choice, int hero, int resolve)
        {
            string error = Act(() => _adventure.Choose(choice.Index, hero, resolve));
            _eventError = error;
            if (error == null)
            {
                ResetEventView();
                var result = _adventure.Result;
                if (result?.Check != null) Sounds.Play(result.Check.Perfect ? Sound.Perfect : result.Success ? Sound.Success : Sound.Miss);
            }
        }

        void DrawEventResult()
        {
            var result = _adventure.Result;
            var column = BeginColumn($"result:{_adventure.Log.Count}", 56);
            float y = 4;
            string title = result.Check == null ? result.Choice.Label : Strings.Get(result.Success ? "event.success" : "event.failure");
            y = Heading(column, y, title, result.Check == null || result.Success ? Palette.Gold : Palette.Damage);
            if (result.Check != null)
            {
                Ui.Text(new Rect(0, y, column.width, 18), $"{result.Hero.Name}: {Strings.Format("dice.under", result.Check.Chance)} · {Strings.Format("dice.successes_of", result.Check.Successes, result.Check.Dice)} ({Strings.Format("dice.need", result.Choice.Need)})", 12, Palette.Journal);
                y += 22;
                float die = 34;
                for (int i = 0; i < result.Check.Dice; i++)
                {
                    var cell = new Rect(i * (die + 6), y, die, die);
                    bool sure = result.Check.Faces[i] < 0;
                    if (DieFace(cell, result.Check, i)) continue;
                    Ui.Fill(cell, sure ? Palette.Sure : result.Check.Hits[i] ? Settings.Success : Settings.Fail);
                    Ui.Text(cell, sure ? "★" : Settings.Face(result.Check.Faces[i], result.Check.Hits[i]), 14, sure || result.Check.Hits[i] ? Palette.Back : Palette.Ink, TextAnchor.MiddleCenter, true);
                }
                y += die + 10;
            }
            y = Paragraph(column, y, result.Text);
            foreach (string line in result.Changes) y = Paragraph(column, y, "· " + line, 13, Palette.Journal);
            EndColumn(y);
            if (Ui.Button(Footer(48), Strings.Get("common.continue"), 17, true, Look.Primary, null, KeyCode.Space)) Act(_adventure.Continue);
        }

        // After a battle: rewards, levels, hope; or who fell.
        // A place picture across the column's top (the party on it), when the picture exists; gives back the y below it.
        float Picture(Rect column, float y, string scene, float share)
        {
            if (PixelArt.Picture(scene) == null) return y;
            float h = Mathf.Clamp(column.height * share, 80, 170);
            SceneArt.Draw(new Rect(0, y, column.width, h), scene, PartyArt(), Time.unscaledTime);
            return y + h + 8;
        }

        void DrawReport()
        {
            var a = _adventure;
            bool won = a.Battle == null || a.Battle.State == BattleState.Won;
            bool fled = a.Battle != null && a.Battle.State == BattleState.Fled;
            var column = BeginColumn($"report:{a.Log.Count}", 56);
            float y = 4;
            // A boss or a chapter's lieutenant beaten: the place it fell (Art/scenes.txt boss_down).
            if (won && a.Battle != null && a.Battle.Monsters.Any(m => m.Monster.Boss)) y = Picture(column, y, "boss_down", 0.26f);
            y = Heading(column, y, Strings.Get(won ? "battle.result_won" : fled ? "battle.fled" : "battle.lost"), won ? Palette.Gold : fled ? Palette.Journal : Palette.Damage);
            foreach (string line in a.Report) y = Paragraph(column, y, "· " + line, 14);
            EndColumn(y);
            if (Ui.Button(Footer(48), Strings.Get("common.continue"), 17, true, Look.Primary, null, KeyCode.Space)) Act(a.Continue);
        }

        string _shareNote;

        // What the end screen's [기록 복사] copies: the run in a few plain lines.
        internal string RunSummary(Adventure a)
        {
            var lines = new List<string> { $"{Strings.Get("title.name")} · {BuildInfo.Version}" };
            bool won = a.Screen == AdventureScreen.Victory;
            lines.Add(won ? Strings.Format(a.FullRun ? "share.won" : "share.won_short", a.FirstChapter + 1, a.LastChapter + 1, a.Rank.Title)
                : Strings.Format("share.lost", a.Chapter.Index + 1, a.Chapter.Title));
            lines.Add(string.Join(" · ", a.Heroes.Select(h => $"{h.Class.Name} Lv{h.Level}")));
            lines.Add(Strings.Format("share.stats", a.ChaptersCleared, a.BattlesWon, a.Day, a.Renown));
            var marks = new List<string>();
            if (a.Difficulty == Difficulty.Easy) marks.Add(Strings.Get("difficulty.easy"));
            if (a.Hardcore) marks.Add(Strings.Get("party.hardcore_on"));
            if (a.Hard) marks.Add(_data.Perks.FirstOrDefault(p => p.Effect.Verb == "hard")?.Name ?? "hard");
            if (a.Combos.Count > 0) marks.Add(Strings.Format("party.combos", string.Join(", ", a.Combos.Select(c => c.Name))));
            if (a.Endings.Count > 0) marks.Add(Strings.Format("share.endings", a.Endings.Count(e => e == "kind"), a.Endings.Count(e => e == "grim")));
            if (marks.Count > 0) lines.Add(string.Join(" · ", marks));
            return string.Join("\n", lines);
        }

        void DrawEnd()
        {
            var a = _adventure;
            bool won = a.Screen == AdventureScreen.Victory;
            var column = BeginColumn("end:" + a.Screen, 56);
            string title = Strings.Get(won ? (a.FullRun ? "end.won" : "end.won_short") : a.Story?.Id == a.Chapter.Rift ? "end.rift" : "end.fallen");
            // The rift closed at dawn, or the banner left in the ash (Art/scenes.txt victory, defeat).
            float y = Picture(column, 4, won ? "victory" : "defeat", 0.3f);
            y = Heading(column, y, title, won ? Palette.Gold : Palette.Damage);
            y = Paragraph(column, y, won
                ? (a.FullRun
                    ? Strings.Format("end.won_text", a.ChaptersCleared, a.Rank.Title)
                    : Strings.Format("end.won_short_text", a.FirstChapter + 1, a.LastChapter + 1))
                : Strings.Format("end.lost_text", a.Chapter.Index + 1, a.Chapter.Title), 14, Palette.Journal);
            if (!won)
            {
                var causes = DefeatCauses(a);
                if (causes.Count > 0)
                {
                    y = Paragraph(column, y, Strings.Get("end.cause_heading"), 14, Palette.Gold);
                    foreach (string cause in causes) y = Paragraph(column, y, "· " + cause, 13, Palette.Journal);
                }
            }
            y = Paragraph(column, y, Strings.Format("end.stats", a.ChaptersCleared, a.MidbossesBeaten, a.BattlesWon, a.Renown, a.Rank.Title, a.Gold, a.Hope));
            if (_recorded) y = Paragraph(column, y, Strings.Format("end.pages", _lastPages, JournalNow.Pages) + (a.Hard && won ? " " + Strings.Get("end.pages_double") : ""), 14, Palette.Gold);
            if (a.Footsteps.Count > 0) y = Paragraph(column, y, Strings.Format("end.notes", a.Footsteps.Count), 13, Palette.Journal);
            if (_recorded)
                foreach (var achievement in JournalNow.NewAchievements)
                    y = Paragraph(column, y, $"★ {Strings.Get("journal.new_achievement")}: {achievement.Name} — {achievement.Text}", 13.5f, Palette.Gold);
            foreach (var hero in a.Heroes)
                y = Paragraph(column, y, Strings.Format("end.hero", hero.Name, hero.Level, hero.Hp, hero.MaxHp, hero.Upgrades.Count == 0 ? Strings.Get("common.none") : string.Join(", ", hero.Upgrades.Select(u => u.Name))), 13);
            // The run in a few lines to copy and pass on (Docs/WebDemo.md 기록 나누기).
            y += 4;
            float sw = Mathf.Min(260, column.width);
            if (Ui.Button(new Rect(column.x, y, sw, 44), Strings.Get("end.copy"), 14, true, Look.Normal, "기록"))
                _shareNote = WebBridge.Copy(RunSummary(a)) ? Strings.Get("end.copied") : Strings.Get("end.copy_failed");
            y += 48;
            if (_shareNote != null) y = Paragraph(column, y, _shareNote, 12, Palette.Journal);
            EndColumn(y);
            var footer = Footer(48);
            float third = (footer.width - 16) / 3;
            bool retry = _retrySave != null && !won;
            float bw = retry ? third : (footer.width - 8) / 2;
            if (Ui.Button(new Rect(footer.x, footer.y, bw, footer.height), Strings.Get("end.title"), 15, true, Look.Quiet, null, KeyCode.Escape)) BackToTitle();
            if (Ui.Button(new Rect(footer.x + bw + 8, footer.y, bw, footer.height), Strings.Get("end.again"), 15, true, retry ? Look.Normal : Look.Primary))
            {
                _shareNote = null;
                var perks = SaveStore.Slot == SaveStore.PracticeSlot ? new List<string>() : ChosenPerks();
                StartRun(a.ClassIds.ToList(), SaveStore.Slot == SaveStore.PracticeSlot ? PracticeSeed : (ulong?)null, perks, a.Difficulty, a.Hardcore, a.FirstChapter, a.LastChapter);
            }
            if (retry && Ui.Button(new Rect(footer.x + (bw + 8) * 2, footer.y, bw, footer.height), Strings.Get("end.retry"), 15, true, Look.Primary)) _titleError = RetryChapter();
        }
    }
}
