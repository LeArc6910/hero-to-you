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
            var area = new Rect((Ui.Width - w) / 2, StripHeight + 6, w, Ui.Height - StripHeight - 12 - footer);
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
            Ui.Text(new Rect(0, y, column.width, 30), title, 22, color ?? Palette.Gold, TextAnchor.MiddleLeft, true);
            return y + 34;
        }

        float Paragraph(Rect column, float y, string text, float size = 14.5f, Color? color = null)
        {
            if (string.IsNullOrEmpty(text)) return y;
            float h = Ui.TextHeight(text, size, column.width);
            Ui.Text(new Rect(0, y, column.width, h), text, size, color ?? Palette.Ink, TextAnchor.UpperLeft, false, true);
            return y + h + 8;
        }

        void DrawStory()
        {
            var story = _adventure.Story;
            var column = BeginColumn("story:" + story.Id, 56);
            float y = Heading(column, 4, story.Title);
            y = Paragraph(column, y, story.Text, 15);
            EndColumn(y);
            if (Ui.Button(Footer(48), "계속", 17, true, Look.Primary)) Act(_adventure.Continue);
        }

        void DrawEvent()
        {
            var card = _adventure.Event;
            var choices = _adventure.Choices();
            var picking = choices.FirstOrDefault(c => c.Index == _choice);
            var column = BeginColumn($"event:{card.Id}:{_adventure.Log.Count}:{_choice}", picking != null ? 56 : 8);
            float y = Heading(column, 4, card.Title);
            y = Paragraph(column, y, card.Text);
            if (!string.IsNullOrEmpty(card.Journal)) y = Paragraph(column, y, "일지: " + card.Journal, 13, Palette.Journal);
            y += 4;

            if (picking == null)
            {
                foreach (var choice in choices)
                {
                    bool affordable = _adventure.Affordable(choice);
                    string label = Label(choice);
                    float h = Ui.Short ? 44 : 50;
                    var r = new Rect(0, y, column.width, h);
                    Ui.Face(r, choice.ClassId != null ? Look.Selected : Look.Normal, affordable);
                    Ui.Text(new Rect(10, y + 2, column.width - 20, h * 0.55f), label, 14, affordable ? Palette.Ink : Palette.Dim, TextAnchor.MiddleLeft, true);
                    Ui.Text(new Rect(10, y + h * 0.52f, column.width - 20, h * 0.45f), Requirement(choice), 11, affordable ? Palette.Journal : Palette.Dim);
                    if (Ui.Hit(r, choice.Label, affordable, "선택지")) PickChoice(choice);
                    y += h + 6;
                }
                if (_eventError != null) y = Paragraph(column, y, _eventError, 12, Palette.Damage);
                EndColumn(y);
                return;
            }

            // Who tries it, and how much resolve.
            y = Paragraph(column, y, $"「{picking.Label}」 누가 해 볼까?", 14, Palette.Gold);
            var heroes = _adventure.CanAttempt(picking);
            foreach (var hero in heroes)
            {
                int index = _adventure.Heroes.IndexOf(hero);
                int resolve = index == _chooser ? _eventResolve : 0;
                var odds = Adventure.Odds(picking, hero, resolve);
                string chance = picking.FixedChance > 0 ? $"{picking.FixedChance}%" : $"{Stats.Name(picking.Stat.Value)} {Adventure.ChanceOf(picking, hero)}%";
                string label = $"{hero.Name}: {chance} × {picking.Dice} → 성공 확률 {Percent(odds.AtLeast(picking.Need))}";
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
                Ui.Text(new Rect(0, y, 80, 36), "결의 쓰기", 13, Palette.Resolve, TextAnchor.MiddleLeft, true);
                if (Ui.Button(new Rect(84, y, 44, 36), "−", 18, _eventResolve > 0)) _eventResolve--;
                Ui.Text(new Rect(130, y, 60, 36), $"{_eventResolve}/{room}", 14, Palette.Ink, TextAnchor.MiddleCenter, true);
                if (Ui.Button(new Rect(192, y, 44, 36), "+", 18, _eventResolve < room)) _eventResolve++;
                y += 42;
                int free = Adventure.FreeOf(picking, hero);
                if (free > 0) y = Paragraph(column, y, $"확정 성공 {free} (축복·행운)", 12, Palette.Sure);
            }
            if (_eventError != null) y = Paragraph(column, y, _eventError, 12, Palette.Damage);
            EndColumn(y);

            var footer = Footer(48);
            float half = (footer.width - 8) / 2;
            if (Ui.Button(new Rect(footer.x, footer.y, half, footer.height), "다른 선택지", 15, true, Look.Quiet)) ResetEventView();
            if (Ui.Button(new Rect(footer.x + half + 8, footer.y, half, footer.height), "굴려 본다", 17, _chooser >= 0, Look.Primary))
                Choose(picking, _chooser, _eventResolve);
        }

        string Label(EventChoice choice) =>
            choice.ClassId != null ? $"[{(_data.Classes.TryGetValue(choice.ClassId, out var cls) ? cls.Name : choice.ClassId)}] {choice.Label}" : choice.Label;

        string Requirement(EventChoice choice)
        {
            int cost = Adventure.Cost(choice);
            string costText = cost > 0 ? $"금화 {cost}" + (_adventure.Gold < cost ? " (모자람)" : "") : "";
            if (!choice.HasCheck) return costText.Length > 0 ? costText : choice.ClassId != null ? "판정 없이 된다" : "판정 없음";
            string stat = choice.FixedChance > 0 ? $"{choice.FixedChance}%" : Stats.Name(choice.Stat.Value);
            var best = _adventure.CanAttempt(choice).Select(h => Adventure.Odds(choice, h, 0).AtLeast(choice.Need)).DefaultIfEmpty(0).Max();
            return $"{stat} 주사위 {choice.Dice}개 · 성공 {choice.Need}개 이상 · 가장 잘하는 영웅 {Percent(best)}";
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
            string title = result.Check == null ? result.Choice.Label : result.Success ? "성공!" : "실패…";
            y = Heading(column, y, title, result.Check == null || result.Success ? Palette.Gold : Palette.Damage);
            if (result.Check != null)
            {
                Ui.Text(new Rect(0, y, column.width, 18), $"{result.Hero.Name}: {result.Check.Chance}% 아래면 성공 · 성공 {result.Check.Successes}/{result.Check.Dice} (필요 {result.Choice.Need})", 12, Palette.Journal);
                y += 22;
                float die = 34;
                for (int i = 0; i < result.Check.Dice; i++)
                {
                    var cell = new Rect(i * (die + 6), y, die, die);
                    bool sure = result.Check.Faces[i] < 0;
                    Ui.Fill(cell, sure ? Palette.Sure : result.Check.Hits[i] ? Palette.Success : Palette.Fail);
                    Ui.Text(cell, sure ? "★" : result.Check.Faces[i].ToString(), 14, sure || result.Check.Hits[i] ? Palette.Back : Palette.Ink, TextAnchor.MiddleCenter, true);
                }
                y += die + 10;
            }
            y = Paragraph(column, y, result.Text);
            foreach (string line in result.Changes) y = Paragraph(column, y, "· " + line, 13, Palette.Journal);
            EndColumn(y);
            if (Ui.Button(Footer(48), "계속", 17, true, Look.Primary)) Act(_adventure.Continue);
        }

        // After a battle: rewards, levels, hope; or who fell.
        void DrawReport()
        {
            var a = _adventure;
            bool won = a.Battle == null || a.Battle.State == BattleState.Won;
            var column = BeginColumn($"report:{a.Log.Count}", 56);
            float y = Heading(column, 4, won ? "전투 승리" : "패배…", won ? Palette.Gold : Palette.Damage);
            foreach (string line in a.Report) y = Paragraph(column, y, "· " + line, 14);
            EndColumn(y);
            if (Ui.Button(Footer(48), "계속", 17, true, Look.Primary)) Act(a.Continue);
        }

        void DrawEnd()
        {
            var a = _adventure;
            bool won = a.Screen == AdventureScreen.Victory;
            var column = BeginColumn("end:" + a.Screen, 56);
            string title = won ? "첫 번째 봉인을 지켰다" : a.Story?.Id == "rift_open" ? "균열이 열렸다" : "모두 쓰러졌다";
            float y = Heading(column, 4, title, won ? Palette.Gold : Palette.Damage);
            y = Paragraph(column, y, won
                ? "챕터 1의 끝이다. 다음 챕터(가라앉은 항구)는 다음 단계에서 이어진다."
                : "일지가 바닥에 떨어진다. 하지만 일지는 언제나 다음 사람을 찾는다.", 14, Palette.Journal);
            y = Paragraph(column, y, $"{a.Day}일째 · 균열 {a.Doom}/{Adventure.DoomMax} · 이긴 전투 {a.BattlesWon} · 금화 {a.Gold} · 남은 희망 {a.Hope}");
            foreach (var hero in a.Heroes)
                y = Paragraph(column, y, $"· {hero.Name} 레벨 {hero.Level}, 생명력 {hero.Hp}/{hero.MaxHp}, 강화: {(hero.Upgrades.Count == 0 ? "없음" : string.Join(", ", hero.Upgrades.Select(u => u.Name)))}", 13);
            EndColumn(y);
            var footer = Footer(48);
            float half = (footer.width - 8) / 2;
            if (Ui.Button(new Rect(footer.x, footer.y, half, footer.height), "처음으로", 15, true, Look.Quiet)) BackToTitle();
            if (Ui.Button(new Rect(footer.x + half + 8, footer.y, half, footer.height), "같은 파티로 다시", 16, true, Look.Primary))
                StartAdventure(a.ClassIds.ToList());
        }
    }
}
