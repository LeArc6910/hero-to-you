using System.Collections.Generic;
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
            bool any = SaveStore.Any;
            // Four rows of buttons (three with nothing saved) under the name; smaller when the screen is short, so
            // everything fits an 800×600 window at 130% text and a phone held sideways (Docs/WebDemo.md 작은 화면).
            int rows = any ? 4 : 3;
            float h = 50, gap = 10, title = 40, sub = 40;
            float Need() => title + 12 + 20 + sub + rows * h + (rows - 1) * gap + (any ? 16 : 0) + 34;
            if (Need() > Ui.Height - 16)
            {
                h = 44;
                gap = 6;
                title = 30;
                sub = 22;
            }
            // Still too tall (a phone held sideways at big text): the four small buttons share one row.
            bool oneRow = Need() > Ui.Height - 16;
            if (oneRow) rows--;
            float y = Mathf.Max(10, (Ui.Height - Need()) * 0.4f);
            Ui.Text(new Rect(0, y, Ui.Width, title + 10), Strings.Get("title.name"), title, Palette.Gold, TextAnchor.MiddleCenter, true);
            y += title + 12;
            Ui.Text(new Rect(16, y, Ui.Width - 32, 20), Strings.Get("title.subtitle"), 14, Palette.Journal, TextAnchor.MiddleCenter);
            y += 20 + sub;

            if (any)
            {
                // Continue the slot last played; with that one empty, choose among the others.
                bool here = SaveStore.Exists;
                if (Ui.Button(new Rect(x, y, w, h), Strings.Get("title.continue"), 17, true, Look.Primary))
                {
                    if (here)
                    {
                        _titleError = Resume();
                        if (_titleError == null) Sounds.Play(Sound.Click);
                    }
                    else _screen = ClientScreen.Slots;
                }
                string summary = here ? Strings.Format("title.slot_summary", SlotName(SaveStore.Slot), SaveStore.Summary(_data, SaveStore.Slot)) : Strings.Get("title.pick_slot");
                Ui.Text(new Rect(x - 16, y + h, w + 32, 16), summary, 11, Palette.Dim, TextAnchor.MiddleCenter);
                y += h + 16 + gap;
            }
            if (Ui.Button(new Rect(x, y, w, h), Strings.Get("title.new"), 16, true, any ? Look.Normal : Look.Primary)) NewAdventure();
            y += h + gap;
            // Practice and slots, then journal and settings: two rows of two, or one row of four.
            int across = oneRow ? 4 : 2;
            float cell = (w - 8 * (across - 1)) / across;
            Rect Cell(int i) => new Rect(x + i % across * (cell + 8), y + i / across * (h + gap), cell, h);
            float label = oneRow ? 12.5f : 14;
            if (Ui.Button(Cell(0), Strings.Get("title.practice"), label, true, Look.Normal)) StartPractice();
            Ui.Tip(Cell(0), Strings.Get("title.practice_tip"));
            if (Ui.Button(Cell(1), Strings.Get("title.slots"), label, true, Look.Normal)) _screen = ClientScreen.Slots;
            if (Ui.Button(Cell(2), Strings.Format("title.journal", JournalNow.Pages), label, true, Look.Normal))
            {
                _screen = ClientScreen.Journal;
                FirstTip("journal");
            }
            if (Ui.Button(Cell(3), Strings.Get("title.settings"), label, true, Look.Normal)) _screen = ClientScreen.Settings;
            y += (oneRow ? 1 : 2) * (h + gap) - gap + 4;
            Ui.Text(new Rect(x, y, w, 16), Strings.Get("title.coop"), 11, Palette.Dim, TextAnchor.MiddleCenter);
            y += 18;
            if (_titleError != null) Ui.Text(new Rect(x, y, w, 40), _titleError, 12, Palette.Damage, TextAnchor.UpperCenter, false, true);
            Ui.Text(new Rect(0, Ui.Height - 18, Ui.Width - 8, 16), Strings.Format("title.footer", _data.Chapters.Count) + " · " + BuildInfo.Version, 10.5f, Palette.Dim, TextAnchor.MiddleRight);
        }

        static string SlotName(int slot) => slot == SaveStore.PracticeSlot ? Strings.Get("slots.practice") : Strings.Format("slots.slot", slot);

        // A new adventure goes in the first empty slot; with all three full, the slots screen asks which to clear.
        void NewAdventure()
        {
            for (int slot = 1; slot <= SaveStore.Slots; slot++)
            {
                if (SaveStore.ExistsIn(slot)) continue;
                SaveStore.Slot = slot;
                _party.Clear();
                _screen = ClientScreen.Party;
                return;
            }
            _slotsNote = Strings.Get("slots.full");
            _screen = ClientScreen.Slots;
        }

        // ---- Save slots (SaveStore): three adventures side by side, and the practice run's ----

        string _slotsNote;
        int _slotConfirm = -1;

        void DrawSlots()
        {
            float w = Mathf.Min(560, Ui.Width - 24);
            float x = (Ui.Width - w) / 2;
            float y = 8;
            Ui.Text(new Rect(x, y, w, 30), Strings.Get("slots.heading"), 21, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 32;
            string hint = _slotsNote ?? Strings.Get("slots.hint");
            float hw = Ui.Short ? w - 116 : w;
            if (Ui.Short) y = 8;
            float hh = Ui.Short ? 0 : Ui.TextHeight(hint, 12, hw);
            if (Ui.Short) Ui.Text(new Rect(x + 150, 8, hw - 150, 40), hint, 10.5f, _slotsNote != null ? Palette.Intent : Palette.Dim, TextAnchor.MiddleLeft, false, true);
            else Ui.Text(new Rect(x, y, w, hh), hint, 12, _slotsNote != null ? Palette.Intent : Palette.Dim, TextAnchor.UpperLeft, false, true);
            if (Ui.Short) y = 52;
            y += hh + 8;
            var slots = new List<int> { 1, 2, 3 };
            if (SaveStore.ExistsIn(SaveStore.PracticeSlot)) slots.Add(SaveStore.PracticeSlot);
            // On a short screen [뒤로] sits by the heading, so the rows get the bottom too.
            var backRect = Ui.Short ? new Rect(x + w - 110, 4, 110, 44) : new Rect((Ui.Width - 200) / 2, Ui.Height - 56, 200, 46);
            float rowH = Mathf.Clamp((Ui.Height - y - (Ui.Short ? 8 : 66)) / slots.Count - 6, 48, 74);
            bool narrow = w < 420;
            foreach (int slot in slots)
            {
                var box = new Rect(x, y, w, rowH);
                bool saved = SaveStore.ExistsIn(slot);
                Ui.Panel(box, slot == SaveStore.Slot && saved ? Palette.Selected : (Color?)null, slot == SaveStore.Slot && saved ? Palette.Gold : (Color?)null);
                float bw = narrow ? 92 : 112;
                float textW = w - 20 - (saved ? bw * 2 + 8 : bw);
                Ui.Text(new Rect(x + 10, y + 4, textW, 20), SlotName(slot), 14, Palette.Ink, TextAnchor.MiddleLeft, true);
                string about = saved ? SaveStore.Summary(_data, slot) : Strings.Get("slots.empty");
                Ui.Text(new Rect(x + 10, y + 24, textW, rowH - 28), about, 11, saved ? Palette.Journal : Palette.Dim, TextAnchor.UpperLeft, false, true);
                var right = new Rect(box.xMax - bw - 6, y + (rowH - 44) / 2, bw, 44);
                string row = SlotName(slot);
                if (saved)
                {
                    if (Ui.Button(right, Strings.Get("slots.continue"), 13.5f, true, Look.Primary, row))
                    {
                        SaveStore.Slot = slot;
                        _slotsNote = null;
                        _titleError = Resume();
                        if (_titleError != null) _screen = ClientScreen.Title;
                    }
                    var del = new Rect(right.x - bw - 8, right.y, bw, 44);
                    bool confirming = _slotConfirm == slot;
                    if (Ui.Button(del, Strings.Get(confirming ? "slots.confirm" : "slots.delete"), confirming ? 11.5f : 13.5f, true, confirming ? Look.Danger : Look.Quiet, row))
                    {
                        if (confirming)
                        {
                            SaveStore.DeleteSlot(slot);
                            _slotConfirm = -1;
                            _slotsNote = null;
                        }
                        else _slotConfirm = slot;
                    }
                }
                else if (slot != SaveStore.PracticeSlot && Ui.Button(right, Strings.Get("slots.new"), 13.5f, true, Look.Normal, row))
                {
                    SaveStore.Slot = slot;
                    _slotsNote = null;
                    _party.Clear();
                    _screen = ClientScreen.Party;
                }
                y += rowH + 6;
            }
            if (Ui.Button(backRect, Strings.Get("common.back"), 16, true, Look.Primary, null, KeyCode.Escape))
            {
                _slotsNote = null;
                _slotConfirm = -1;
                _screen = ClientScreen.Title;
            }
        }

        // The practice run (Docs/WebDemo.md 연습 판): the first chapter only, easy, a fixed party and map, every tip
        // shown again, in its own slot.
        public const ulong PracticeSeed = 20261110;
        static readonly string[] PracticeParty = { "knight", "hunter", "cleric" };

        internal void StartPractice()
        {
            Tips.Reset();
            TutorialAgain();
            SaveStore.Slot = SaveStore.PracticeSlot;
            StartRun(PracticeParty, PracticeSeed, new List<string>(), Difficulty.Easy, false, 0, 0);
        }

        // ---- Settings (Settings.cs): text size, vibration, sound, music, battle playback, tips, language ----

        string _settingsNote;
        float _settingsScroll;
        float _settingsHeight;
        bool _settingsDragging;
        float _settingsLastY;

        void DrawSettings()
        {
            float w = Mathf.Min(460, Ui.Width - 32);
            float x = (Ui.Width - w) / 2;
            float top = Mathf.Max(8, Ui.Height * 0.04f);
            Ui.Text(new Rect(x, top, w, 30), Strings.Get("settings.heading"), 22, Palette.Gold, TextAnchor.MiddleLeft, true);
            // The rows scroll when they do not fit (a phone held sideways, big text).
            var area = new Rect(x, top + 36, w, Ui.Height - top - 36 - 62);
            _settingsScroll = Ui.Scroll(area, _settingsScroll, _settingsHeight - area.height, ref _settingsDragging, ref _settingsLastY);
            Ui.BeginArea(area, _settingsScroll);
            float y = 0;
            float h = Ui.Short ? 44 : 48;
            float gap = Ui.Short ? 5 : 8;
            float half = (w - 8) / 2;
            if (Ui.Button(new Rect(0, y, half, h), Strings.Format("settings.text", Settings.TextSize), 14, true, Look.Normal, "설정")) Settings.TextSize = Settings.NextTextSize();
            if (Ui.Button(new Rect(half + 8, y, half, h), Strings.Get(Settings.Vibration ? "settings.vibration_on" : "settings.vibration_off"), 14, true, Look.Normal, "설정"))
            {
                Settings.Vibration = !Settings.Vibration;
                WebBridge.Buzz(60);
            }
            y += h + gap;
            if (Ui.Button(new Rect(0, y, half, h), Strings.Get(Sounds.Muted ? "settings.sound_off" : "settings.sound_on"), 14, true, Look.Normal, "설정")) Sounds.Muted = !Sounds.Muted;
            if (Ui.Button(new Rect(half + 8, y, half, h), Strings.Format("settings.music", Music.Volume), 14, true, Look.Normal, "설정")) Music.Volume = Music.Volume >= 100 ? 0 : Music.Volume + 25;
            y += h + gap;
            if (Ui.Button(new Rect(0, y, half, h), Strings.Get(_speed >= 2 ? "settings.speed_fast" : "settings.speed_normal"), 14, true, Look.Normal, "설정")) _speed = _speed >= 2 ? 1 : 2.5f;
            if (Ui.Button(new Rect(half + 8, y, half, h), Strings.Get("settings.tips"), 14, true, Look.Normal, "설정"))
            {
                Tips.Reset();
                _settingsNote = Strings.Get("settings.tips_done");
            }
            y += h + gap;
            if (Ui.Button(new Rect(0, y, half, h), Strings.Get(Settings.ColorMarks ? "settings.colors_on" : "settings.colors_off"), 14, true, Look.Normal, "설정")) Settings.ColorMarks = !Settings.ColorMarks;
            if (Ui.Button(new Rect(half + 8, y, half, h), Strings.Get(Settings.LessMotion ? "settings.motion_off" : "settings.motion_on"), 14, true, Look.Normal, "설정")) Settings.LessMotion = !Settings.LessMotion;
            y += h + gap;
            if (Ui.Button(new Rect(0, y, half, h), Strings.Get("settings.report"), 14, true, Look.Normal, "설정")) _reportOpen = true;
            if (Ui.Button(new Rect(half + 8, y, half, h), Strings.Get("settings.language"), 14, true, Look.Normal, "설정"))
            {
                Settings.Language = Settings.Language == "en" ? "ko" : "en";
                ReloadData();
                _settingsNote = Strings.Get("settings.language_note");
            }
            y += h + gap;
            if (_settingsNote != null)
            {
                float nh = Ui.TextHeight(_settingsNote, 12, w);
                Ui.Text(new Rect(0, y, w, nh), _settingsNote, 12, Palette.Journal, TextAnchor.UpperLeft, false, true);
                y += nh + 4;
            }
            Ui.EndArea(_settingsScroll);
            _settingsHeight = y;
            if (Ui.Button(new Rect((Ui.Width - 200) / 2, Ui.Height - 56, 200, 46), Strings.Get("settings.back"), 16, true, Look.Primary, null, KeyCode.Escape))
            {
                _settingsNote = null;
                _screen = ClientScreen.Title;
            }
        }

        // ---- Choosing the party, and how the run goes: difficulty, hardcore, where it starts and how long it is ----

        int _startChapter;
        bool _oneChapter;

        void DrawParty()
        {
            var classes = _data.ClassList;
            var journal = JournalNow;
            int reached = Mathf.Clamp(journal.Reached, 1, _data.Chapters.Count);
            _startChapter = Mathf.Clamp(_startChapter, 0, reached - 1);
            bool chapters = reached > 1;
            float top = 8;
            Ui.Text(new Rect(12, top, Ui.Width - 24, 26), Strings.Get("party.heading"), 20, Palette.Ink, TextAnchor.MiddleLeft, true);
            Ui.Text(new Rect(12, top + 24, Ui.Width - 24, 18), Strings.Format("party.hint", classes.Count), 12, Palette.Dim);

            // The footer, bottom up: [뒤로] [무작위] … [모험 시작]; the run's options in one row (two on narrow
            // screens); then the pages taken along and the pairings the chosen classes make.
            int options = chapters ? 4 : 2;
            bool twoRows = options == 4 && Ui.Width < 560;
            float optionsH = (twoRows ? 2 : 1) * 50;
            var perks = ChosenPerks();
            var combos = _data.Combos.Where(c => c.Classes.All(_party.Contains)).ToList();
            float infoH = (perks.Count > 0 ? 17 : 0) + (combos.Count > 0 ? 17 : 0);
            float footer = 56 + optionsH + infoH;
            var area = new Rect(8, top + 48, Ui.Width - 16, Ui.Height - top - 48 - footer);

            int columns = Ui.Portrait ? 3 : Ui.Width >= 700 ? 6 : 4;
            float gap = 6;
            float cardW = (area.width - gap * (columns - 1)) / columns;
            float cardH = Mathf.Clamp(cardW * 1.05f, 120, 190);
            // Sideways, the two rows of six fit without scrolling when there is room.
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

            float y = area.yMax + 4;
            if (combos.Count > 0)
            {
                var line = new Rect(12, y, Ui.Width - 24, 16);
                Ui.Text(line, Strings.Format("party.combos", string.Join(", ", combos.Select(c => c.Name))), 11, Palette.Gold);
                Ui.Tip(line, string.Join("\n", combos.Select(c => $"{c.Name}: {c.Text}")));
                y += 17;
            }
            if (perks.Count > 0)
            {
                Ui.Text(new Rect(12, y, Ui.Width - 24, 16), Strings.Format("party.perks", string.Join(", ", perks.Select(p => _data.Perks.First(d => d.Id == p).Name))), 11, Palette.Journal);
                y += 17;
            }
            float ow = (Ui.Width - 24 - 6 * ((twoRows ? 2 : options) - 1)) / (twoRows ? 2 : options);
            Rect Option(int i) => twoRows ? new Rect(12 + i % 2 * (ow + 6), y + i / 2 * 50, ow, 44) : new Rect(12 + i * (ow + 6), y, ow, 44);
            var levelRect = Option(0);
            string level = Strings.Format("party.difficulty", Strings.Get(_difficulty == Difficulty.Easy ? "difficulty.easy" : "difficulty.normal"));
            if (Ui.Button(levelRect, level, 13, true, _difficulty == Difficulty.Easy ? Look.Selected : Look.Normal, "난이도"))
                _difficulty = _difficulty == Difficulty.Easy ? Difficulty.Normal : Difficulty.Easy;
            Ui.Tip(levelRect, Strings.Get("party.difficulty_tip"));
            // Hardcore: a hero who falls with no hope is gone (Docs/Combat.md 하드코어).
            var coreRect = Option(1);
            if (Ui.Button(coreRect, Strings.Get(_hardcore ? "party.hardcore_on" : "party.hardcore_off"), 13, true, _hardcore ? Look.Danger : Look.Quiet, "하드코어")) _hardcore = !_hardcore;
            Ui.Tip(coreRect, Strings.Get("party.hardcore_tip"));
            if (chapters)
            {
                // Where the run starts (any chapter reached before) and whether it goes on to the end (Docs/Map.md 짧은 판).
                var fromRect = Option(2);
                if (Ui.Button(fromRect, Strings.Format("party.from", _startChapter + 1), 13, true, _startChapter > 0 ? Look.Selected : Look.Normal, "시작 장"))
                    _startChapter = (_startChapter + 1) % reached;
                Ui.Tip(fromRect, Strings.Get("party.chapters_tip"));
                var lengthRect = Option(3);
                if (Ui.Button(lengthRect, Strings.Get(_oneChapter ? "party.length_one" : "party.length_all"), 13, true, _oneChapter ? Look.Selected : Look.Normal, "길이"))
                    _oneChapter = !_oneChapter;
                Ui.Tip(lengthRect, Strings.Get("party.chapters_tip"));
            }

            float by = Ui.Height - 50;
            float bw = Mathf.Min(200, (Ui.Width - 40) / 3);
            if (Ui.Button(new Rect(12, by, bw, 44), Strings.Get("party.back"), 15, true, Look.Quiet, null, KeyCode.Escape)) _screen = ClientScreen.Title;
            if (Ui.Button(new Rect(20 + bw, by, bw, 44), Strings.Get("party.random"), 15)) PickRandomParty();
            string start = _party.Count == 3 ? Strings.Get("party.start") : $"{Strings.Get("party.start")} ({_party.Count}/3)";
            if (Ui.Button(new Rect(Ui.Width - 12 - bw, by, bw, 44), start, 16, _party.Count == 3, Look.Primary, null, KeyCode.Space)) StartAdventure(_party);
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
            // The codex (Docs/Journal.md 도감): what any run has met, the rest still ???.
            y += 6;
            y = DrawCodex(column, y, journal);
            // Achievements and the classes' records (Docs/Journal.md 업적).
            y += 6;
            var achievements = _data.Achievements;
            y = Heading(column, y, $"{Strings.Get("journal.achievements")} {journal.Achieved.Count}/{achievements.Count}");
            foreach (var achievement in achievements)
            {
                bool got = journal.Achieved.Contains(achievement.Id);
                y = Paragraph(column, y, (got ? "★ " : "☆ ") + $"{achievement.Name} — {achievement.Text}", 12.5f, got ? Palette.Gold : Palette.Dim);
            }
            y += 6;
            y = Heading(column, y, Strings.Get("journal.classes"));
            foreach (var cls in _data.PlayableClasses)
                y = Paragraph(column, y, $"{cls.Name}: 모험 {journal.RunsWith(cls.Id)} · 끝까지 {journal.WinsWith(cls.Id)}", 12.5f, journal.WinsWith(cls.Id) > 0 ? Palette.Journal : Palette.Dim);
            Ui.EndArea(_journalScroll);
            _journalHeight = y + 8;
            if (Ui.Button(new Rect((Ui.Width - 200) / 2, Ui.Height - 56, 200, 46), Strings.Get("common.back"), 16, true, Look.Primary, null, KeyCode.Escape)) _screen = ClientScreen.Title;
        }

        float DrawCodex(Rect column, float y, Journal journal)
        {
            var summons = new[] { GameData.SkeletonId, GameData.CompanionWolfId, GameData.TurretId };
            var monsters = _data.Monsters.Values.Where(m => !summons.Contains(m.Id)).ToList();
            var items = _data.ItemList.Where(i => !i.Consumable).ToList();
            var combos = _data.Combos;
            int endingsAll = _data.Chapters.Sum(c => new[] { c.Epilogue, c.EpilogueKind, c.EpilogueGrim }.Count(id => !string.IsNullOrEmpty(id)));
            var seenMonsters = journal.Codex("monsters");
            var seenItems = journal.Codex("items");
            var seenCombos = journal.Codex("combos");
            var stories = journal.Codex("stories");
            y = Heading(column, y, Strings.Format("codex.heading", seenMonsters.Count(id => monsters.Any(m => m.Id == id)) + seenItems.Count(id => items.Any(i => i.Id == id)) + seenCombos.Count + journal.EndingsSeen(_data), monsters.Count + items.Count + combos.Count + endingsAll));
            string Names<T>(IEnumerable<T> all, System.Func<T, string> id, System.Func<T, string> name, IReadOnlyCollection<string> seen) =>
                string.Join(", ", all.Select(x => seen.Contains(id(x)) ? name(x) : "???"));
            y = Paragraph(column, y, Strings.Format("codex.monsters", seenMonsters.Count(id => monsters.Any(m => m.Id == id)), monsters.Count), 13, Palette.Gold);
            y = Paragraph(column, y, Names(monsters, m => m.Id, m => m.Name, seenMonsters), 11.5f, Palette.Journal);
            y = Paragraph(column, y, Strings.Format("codex.items", seenItems.Count(id => items.Any(i => i.Id == id)), items.Count), 13, Palette.Gold);
            y = Paragraph(column, y, Names(items, i => i.Id, i => i.Name, seenItems), 11.5f, Palette.Journal);
            y = Paragraph(column, y, Strings.Format("codex.combos", seenCombos.Count, combos.Count), 13, Palette.Gold);
            y = Paragraph(column, y, Names(combos, c => c.Id, c => $"{c.Name}({string.Join("·", c.Classes.Select(cls => _data.Classes[cls].Name))})", seenCombos), 11.5f, Palette.Journal);
            y = Paragraph(column, y, Strings.Format("codex.endings", journal.EndingsSeen(_data), endingsAll), 13, Palette.Gold);
            foreach (var chapter in _data.Chapters)
            {
                string Mark(string id, string kind) => string.IsNullOrEmpty(id) ? "" : (stories.Contains(id) ? "★" : "☆") + Strings.Get("codex.ending_" + kind);
                y = Paragraph(column, y, $"{chapter.Index + 1}. {chapter.Title} — {string.Join(" ", new[] { Mark(chapter.EpilogueKind, "kind"), Mark(chapter.Epilogue, "plain"), Mark(chapter.EpilogueGrim, "grim") }.Where(m => m.Length > 0))}", 11.5f, Palette.Journal);
            }
            return y;
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
