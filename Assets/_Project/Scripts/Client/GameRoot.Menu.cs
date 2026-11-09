using System.Linq;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    // The title and choosing the party of three (Docs/Classes.md).
    public sealed partial class GameRoot
    {
        string _titleError;
        float _partyScroll;
        bool _partyDragging;
        float _partyLastY;

        void DrawTitle()
        {
            float w = Mathf.Min(420, Ui.Width - 32);
            float x = (Ui.Width - w) / 2;
            float y = Mathf.Max(16, Ui.Height * 0.14f);
            float title = Ui.Short ? 30 : 40;
            Ui.Text(new Rect(0, y, Ui.Width, title + 10), "히어로 투 유", title, Palette.Gold, TextAnchor.MiddleCenter, true);
            y += title + 12;
            Ui.Text(new Rect(16, y, Ui.Width - 32, 20), "영웅에게서 너에게 · 1~3인 협동 턴제 모험", 14, Palette.Journal, TextAnchor.MiddleCenter);
            y += Ui.Short ? 30 : 48;

            float h = Ui.Short ? 44 : 50;
            float gap = Ui.Short ? 6 : 10;
            if (SaveStore.Exists)
            {
                string summary = SaveStore.Summary(_data);
                if (Ui.Button(new Rect(x, y, w, h), "이어하기", 17, true, Look.Primary))
                {
                    _titleError = Resume();
                    if (_titleError == null) Sounds.Play(Sound.Click);
                }
                if (summary != null) Ui.Text(new Rect(x, y + h, w, 16), summary, 11, Palette.Dim, TextAnchor.MiddleCenter);
                y += h + 18;
            }
            if (Ui.Button(new Rect(x, y, w, h), SaveStore.Exists ? "새 모험 (이어하던 것은 지워진다)" : "혼자 시작 (영웅 셋을 모두 조종)", 16, true, SaveStore.Exists ? Look.Normal : Look.Primary))
            {
                _party.Clear();
                _screen = ClientScreen.Party;
            }
            y += h + gap;
            float half = (w - 8) / 2;
            if (Ui.Button(new Rect(x, y, half, h), $"일지 ({JournalNow.Pages}장)", 15, true, Look.Normal))
            {
                _screen = ClientScreen.Journal;
                FirstTip("journal");
            }
            Ui.Button(new Rect(x + half + 8, y, half, h), "협동 (준비 중)", 14, false);
            y += h + gap;
            string speed = _speed >= 2 ? "빠르게" : "보통";
            float sh = Ui.Short ? 38 : 44;
            if (Ui.Button(new Rect(x, y, half, sh), $"전투 연출: {speed}", 13, true, Look.Quiet)) _speed = _speed >= 2 ? 1 : 2.5f;
            if (Ui.Button(new Rect(x + half + 8, y, half, sh), Sounds.Muted ? "소리: 끔" : "소리: 켬", 13, true, Look.Quiet)) Sounds.Muted = !Sounds.Muted;
            y += sh + gap;
            if (Ui.Button(new Rect(x, y, half, sh), $"음악: {Music.Volume}%", 13, true, Look.Quiet)) Music.Volume = Music.Volume >= 100 ? 0 : Music.Volume + 25;
            if (Ui.Button(new Rect(x + half + 8, y, half, sh), "도움말 다시 보기", 13, true, Look.Quiet))
            {
                Tips.Reset();
                _titleError = "처음 보는 화면마다 도움말이 다시 나온다.";
            }
            y += sh + 4;
            if (_titleError != null) Ui.Text(new Rect(x, y, w, 40), _titleError, 12, Palette.Damage, TextAnchor.UpperCenter, false, true);
            Ui.Text(new Rect(0, Ui.Height - 26, Ui.Width - 8, 20), $"{_data.Chapters.Count}개의 장 · " + BuildInfo.Version, 11, Palette.Dim, TextAnchor.MiddleRight);
        }

        void DrawParty()
        {
            var classes = _data.ClassList;
            float top = 8;
            Ui.Text(new Rect(12, top, Ui.Width - 24, 26), "영웅 셋을 고르세요", 20, Palette.Ink, TextAnchor.MiddleLeft, true);
            Ui.Text(new Rect(12, top + 24, Ui.Width - 24, 18), $"직업 {classes.Count}종 가운데 셋 · 카드를 누르면 고르고, 다시 누르면 뺀다", 12, Palette.Dim);
            float footer = 56;
            var area = new Rect(8, top + 48, Ui.Width - 16, Ui.Height - top - 48 - footer);

            int columns = Ui.Portrait ? 3 : Ui.Width >= 700 ? 6 : 4;
            float gap = 6;
            float cardW = (area.width - gap * (columns - 1)) / columns;
            float cardH = Mathf.Clamp(cardW * 1.05f, 120, 190);
            // Sideways, the two rows of six fit without scrolling.
            if (!Ui.Portrait) cardH = Mathf.Max(110, Mathf.Min(cardH, (area.height - gap) / 2));
            int rows = (classes.Count + columns - 1) / columns;
            float content = rows * (cardH + gap);
            _partyScroll = Ui.Scroll(area, _partyScroll, content - area.height, ref _partyDragging, ref _partyLastY);

            Ui.BeginArea(area, _partyScroll);
            for (int i = 0; i < classes.Count; i++)
            {
                var cls = classes[i];
                var card = new Rect(i % columns * (cardW + gap), i / columns * (cardH + gap), cardW, cardH);
                DrawClassCard(card, cls);
            }
            Ui.EndArea(_partyScroll);

            float by = Ui.Height - footer + 6;
            float bw = Mathf.Min(200, (Ui.Width - 40) / 3);
            if (Ui.Button(new Rect(12, by, bw, 44), "뒤로", 15, true, Look.Quiet)) _screen = ClientScreen.Title;
            if (Ui.Button(new Rect(20 + bw, by, bw, 44), "무작위", 15)) PickRandomParty();
            var perks = ChosenPerks();
            if (perks.Count > 0) Ui.Text(new Rect(12, by - 18, Ui.Width - 24, 16), "가져가는 장: " + string.Join(", ", perks.Select(p => _data.Perks.First(d => d.Id == p).Name)), 11, Palette.Gold);
            string start = _party.Count == 3 ? "모험 시작" : $"모험 시작 ({_party.Count}/3)";
            if (Ui.Button(new Rect(Ui.Width - 12 - bw, by, bw, 44), start, 16, _party.Count == 3, Look.Primary)) StartAdventure(_party);
        }

        // ---- The journal: pages, perks to unlock and take along, the old notes found (Docs/Journal.md) ----

        float _journalScroll;
        float _journalHeight;
        bool _journalDragging;
        float _journalLastY;
        string _journalError;

        void DrawJournal()
        {
            var journal = JournalNow;
            float w = Mathf.Min(680, Ui.Width - 24);
            var area = new Rect((Ui.Width - w) / 2, 8, w, Ui.Height - 72);
            _journalScroll = Ui.Scroll(area, _journalScroll, _journalHeight - area.height, ref _journalDragging, ref _journalLastY);
            Ui.BeginArea(area, _journalScroll);
            var column = new Rect(0, 0, w, area.height);
            float y = Heading(column, 4, "일지");
            y = Paragraph(column, y, $"남은 페이지 {journal.Pages}장 (모두 {journal.PagesEarned}장) · 모험 {journal.Runs}번 · 끝까지 간 모험 {journal.Victories}번 · 지난 장 {journal.ChaptersCleared} · 가장 높은 명성 {journal.BestRenown}", 13, Palette.Journal);
            y = Paragraph(column, y, "페이지로 장을 펼치고, 펼친 장 가운데 다음 모험에 가져갈 것을 고른다.", 12, Palette.Dim);
            var chosen = ChosenPerks();
            foreach (var perk in _data.Perks)
            {
                bool open = journal.Has(perk.Id);
                bool taking = chosen.Contains(perk.Id);
                var box = new Rect(0, y, w, 58);
                Ui.Panel(box, taking ? Palette.Selected : (Color?)null, taking ? Palette.Gold : (Color?)null);
                Ui.Text(new Rect(10, y + 4, w - 150, 20), $"{perk.Name} · {perk.Pages}장", 14, open ? Palette.Ink : Palette.Dim, TextAnchor.MiddleLeft, true);
                Ui.Text(new Rect(10, y + 26, w - 150, 28), perk.Text, 11, Palette.Journal, TextAnchor.UpperLeft, false, true);
                var button = new Rect(w - 134, y + 8, 126, 42);
                if (!open)
                {
                    if (Ui.Button(button, $"펼치기 ({perk.Pages}장)", 12.5f, journal.CanUnlock(perk), Look.Normal, perk.Name))
                    {
                        _journalError = journal.Unlock(perk);
                        if (_journalError == null)
                        {
                            SaveStore.WriteJournal(journal);
                            SetPerkChosen(perk.Id, true);
                            Sounds.Play(Sound.Level);
                        }
                    }
                }
                else if (Ui.Button(button, taking ? "가져간다 ✓" : "두고 간다", 13, true, taking ? Look.Selected : Look.Quiet, perk.Name))
                {
                    SetPerkChosen(perk.Id, !taking);
                    Sounds.Play(Sound.Click);
                }
                y += 64;
            }
            if (_journalError != null) y = Paragraph(column, y, _journalError, 12, Palette.Damage);
            y += 6;
            // The old hero's notes found so far, in the sheets' order; the rest are blank lines to find.
            var noted = _data.Events.Where(e => !string.IsNullOrEmpty(e.Journal)).ToList();
            int found = noted.Count(e => journal.Notes.Contains(e.Id));
            y = Heading(column, y, $"찾은 옛 메모 {found}/{noted.Count}");
            foreach (var card in noted)
            {
                if (!journal.Notes.Contains(card.Id)) continue;
                y = Paragraph(column, y, $"{card.Title} — {card.Journal}", 12.5f, Palette.Journal);
            }
            if (found < noted.Count) y = Paragraph(column, y, $"아직 찾지 못한 메모가 {noted.Count - found}개 있다. 길 위의 사건에서 찾는다.", 12, Palette.Dim);
            Ui.EndArea(_journalScroll);
            _journalHeight = y + 8;
            if (Ui.Button(new Rect((Ui.Width - 200) / 2, Ui.Height - 56, 200, 46), "뒤로", 16, true, Look.Primary)) _screen = ClientScreen.Title;
        }

        void DrawClassCard(Rect card, ClassDef cls)
        {
            int order = _party.IndexOf(cls.Id);
            bool chosen = order >= 0;
            Ui.Panel(card, chosen ? Palette.Selected : (Color?)null, chosen ? Palette.Gold : (Color?)null);
            float icon = Mathf.Min(card.width * 0.42f, 64);
            Ui.Image(new Rect(card.x + 6, card.y + 6, icon, icon), PixelArt.Hero(cls.Id, cls.Color), cls.Playable ? (Color?)null : new Color(0.35f, 0.35f, 0.4f, 1));
            float tx = card.x + icon + 10;
            float tw = card.xMax - tx - 4;
            Ui.Text(new Rect(tx, card.y + 6, tw, 20), cls.Name, 16, cls.Playable ? Palette.Ink : Palette.Dim, TextAnchor.MiddleLeft, true);
            Ui.Text(new Rect(tx, card.y + 26, tw, 16), cls.Role, 12, Palette.Journal);
            if (cls.Playable) Ui.Text(new Rect(tx, card.y + 42, tw, 16), $"생명력 {cls.Hp} · 결의 {cls.Resolve}", 11, Palette.Dim);
            else Ui.Text(new Rect(tx, card.y + 42, tw, 16), "준비 중 (M5)", 11, Palette.Dim);
            Ui.Text(new Rect(card.x + 6, card.y + icon + 10, card.width - 12, card.height - icon - 14), cls.Mechanic, 11, cls.Playable ? Palette.Ink : Palette.Dim, TextAnchor.UpperLeft, false, true);
            if (chosen)
            {
                var badge = new Rect(card.xMax - 26, card.y + 4, 22, 22);
                Ui.Fill(badge, Palette.Gold);
                Ui.Text(badge, (order + 1).ToString(), 13, Palette.Back, TextAnchor.MiddleCenter, true);
            }
            if (Ui.Hit(card, cls.Name, cls.Playable, "직업")) ToggleClass(cls.Id);
        }

        void ToggleClass(string id)
        {
            if (_party.Remove(id)) return;
            if (_party.Count >= 3) _party.RemoveAt(0);
            _party.Add(id);
        }

        void PickRandomParty()
        {
            var playable = _data.PlayableClasses.Select(c => c.Id).ToList();
            var random = new System.Random();
            _party.Clear();
            while (_party.Count < 3 && playable.Count > 0)
            {
                int i = random.Next(playable.Count);
                _party.Add(playable[i]);
                playable.RemoveAt(i);
            }
        }
    }
}
