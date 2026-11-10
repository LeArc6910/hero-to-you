using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    public enum ClientScreen
    {
        Title,
        Party,
        Journal,
        Settings,
        Slots,
        Adventure,
    }

    // The one object the game runs on (named "Game", so the web page's playtests can SendMessage to it). Loads the
    // sheets, holds the adventure (Core) and draws whichever screen is up. The screens are its partial files:
    // GameRoot.Menu.cs (title, party), GameRoot.Battle.cs, GameRoot.Story.cs (story, events, rest, ends);
    // Playtest.cs is the playtests' remote control. Rules live in Core; this only shows them and passes choices on.
    public sealed partial class GameRoot : MonoBehaviour
    {
        public const string ObjectName = "Game";

        GameData _data;
        string _loadError;
        Adventure _adventure;
        ClientScreen _screen = ClientScreen.Title;
        readonly List<string> _party = new List<string>();
        ulong? _nextSeed;

        // Battle playback speed: 1 normal, 2.5 fast (the title's switch), 0 instant (playtests).
        float _speed = 1;

        internal GameData Data => _data;
        internal Adventure Adventure => _adventure;
        internal ClientScreen Screen => _screen;

        // Makes the Game object (with a camera) when the scene has none, so the game runs from any scene.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<GameRoot>() != null) return;
            new GameObject(ObjectName, typeof(GameRoot));
        }

        void Awake()
        {
            gameObject.name = ObjectName;
            useGUILayout = false;
            Application.targetFrameRate = 60;
            EnsureCamera();
            ReloadData();
        }

        // The sheets, in the language the settings name (Docs/Localization.md); again when it changes.
        // The loaded sheets for the static helpers that name things (stats, statuses, terrain) in their language.
        internal static GameData Sheets { get; private set; }

        internal void ReloadData()
        {
            try
            {
                _data = GameData.Load(path => Sheet(path), Settings.Language);
                Sheets = _data;
                _loadError = null;
            }
            catch (Exception e)
            {
                _loadError = e.Message;
                Debug.LogError(e);
            }
        }

        static string Sheet(string path)
        {
            // Resources paths drop the extension: Data/Classes/classes for Classes/classes.csv.
            string resource = "Data/" + path.Substring(0, path.LastIndexOf('.'));
            var asset = Resources.Load<TextAsset>(resource);
            if (asset == null) throw new InvalidOperationException(Strings.Format("error.sheet_missing", "Resources/" + resource));
            return asset.text;
        }

        static void EnsureCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
                camera = go.GetComponent<Camera>();
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Palette.Back;
            camera.orthographic = true;
        }

        void Update()
        {
            TickBattle(Time.unscaledDeltaTime);
            Music.Play(MusicFor());
            Music.Ambient(AmbienceFor());
            Music.Tick(Time.unscaledDeltaTime);
            if (_autoBattle && AwaitingHero && _tipOpen == null) AutoCommand();
        }

        // The place's sound on the map and in events (the chapter's music name: forest, harbor, snow, tomb, rift).
        Ambience AmbienceFor()
        {
            var a = _adventure;
            if (_screen != ClientScreen.Adventure || a == null) return Ambience.None;
            if (a.Screen != AdventureScreen.Map && a.Screen != AdventureScreen.Event && a.Screen != AdventureScreen.EventResult) return Ambience.None;
            return System.Enum.TryParse(a.Chapter.Music, true, out Ambience ambience) ? ambience : Ambience.None;
        }

        // ---- Auto battle (Docs/WebDemo.md 편의): the battle bot gives the heroes' commands until switched off ----

        bool _autoBattle;
        float _autoAt;

        void AutoCommand()
        {
            if (Time.unscaledTime - _autoAt < 0.25f) return;
            _autoAt = Time.unscaledTime;
            var battle = _adventure.Battle;
            Act(() => _adventure.Fight(BattleBot.Choose(battle, AutoRandom)));
            _selectedFor = -1;
        }

        static readonly GameRandom AutoRandom = new GameRandom(7, 7);

        // ---- What happened (the history window) ----

        readonly List<string> _history = new List<string>();
        bool _historyOpen;
        float _historyScroll;
        bool _historyDragging;
        float _historyLastY;

        void Remember(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line) || (_history.Count > 0 && _history[_history.Count - 1] == line)) continue;
                _history.Add(line);
            }
            if (_history.Count > 300) _history.RemoveRange(0, _history.Count - 300);
        }

        void DrawHistory()
        {
            Ui.Fill(new Rect(0, 0, Ui.Width, Ui.Height), new Color(0, 0, 0, 0.6f));
            float w = Mathf.Min(640, Ui.Width - 24);
            var r = new Rect((Ui.Width - w) / 2, 12, w, Ui.Height - 24);
            Ui.Panel(r, Palette.Panel, Palette.Gold);
            Ui.Text(new Rect(r.x + 14, r.y + 8, w - 28, 26), Strings.Get("history.heading"), 19, Palette.Gold, TextAnchor.MiddleLeft, true);
            var area = new Rect(r.x + 14, r.y + 40, w - 28, r.height - 104);
            float total = _history.Sum(l => Ui.TextHeight(l, 12, area.width) + 4);
            _historyScroll = Ui.Scroll(area, _historyScroll, total - area.height, ref _historyDragging, ref _historyLastY);
            Ui.BeginArea(area, _historyScroll);
            float y = 0;
            foreach (string line in _history)
            {
                float h = Ui.TextHeight(line, 12, area.width);
                Ui.Text(new Rect(0, y, area.width, h), line, 12, Palette.Journal, TextAnchor.UpperLeft, false, true);
                y += h + 4;
            }
            Ui.EndArea(_historyScroll);
            if (_history.Count == 0) Ui.Text(area, Strings.Get("history.empty"), 13, Palette.Dim, TextAnchor.MiddleCenter);
            if (Ui.Button(new Rect(r.x + (w - 180) / 2, r.yMax - 56, 180, 46), Strings.Get("common.close"), 16, true, Look.Primary, "기록", KeyCode.Escape)) _historyOpen = false;
        }

        // ---- Bug report (Docs/Playtest.md 버그 제보): the save as text, to copy or download and send ----

        bool _reportOpen;
        string _reportNote;

        void DrawBugReport()
        {
            string save = SaveStore.Read() ?? (_adventure?.Save());
            Ui.Fill(new Rect(0, 0, Ui.Width, Ui.Height), new Color(0, 0, 0, 0.6f));
            float w = Mathf.Min(560, Ui.Width - 24);
            var r = new Rect((Ui.Width - w) / 2, 12, w, Ui.Height - 24);
            Ui.Panel(r, Palette.Panel, Palette.Gold);
            Ui.Text(new Rect(r.x + 14, r.y + 8, w - 28, 26), Strings.Get("report.heading"), 19, Palette.Gold, TextAnchor.MiddleLeft, true);
            float y = r.y + 40;
            string about = save == null ? Strings.Get("report.none") : Strings.Format("report.about", save.Split('\n').Length, BuildInfo.Version);
            float ah = Ui.TextHeight(about, 13, w - 28);
            Ui.Text(new Rect(r.x + 14, y, w - 28, ah), about, 13, Palette.Ink, TextAnchor.UpperLeft, false, true);
            y += ah + 8;
            if (save != null)
            {
                string preview = string.Join("\n", save.Split('\n').Take(8)) + "\n…";
                var box = new Rect(r.x + 14, y, w - 28, Mathf.Min(150, r.yMax - y - 170));
                Ui.Fill(box, new Color(0, 0, 0, 0.35f));
                Ui.Text(new Rect(box.x + 6, box.y + 4, box.width - 12, box.height - 8), preview, 11, Palette.Dim, TextAnchor.UpperLeft);
                y = box.yMax + 10;
                float half = (w - 36) / 2;
                if (Ui.Button(new Rect(r.x + 14, y, half, 46), Strings.Get("report.copy"), 14, true, Look.Normal, "제보"))
                {
                    _reportNote = WebBridge.Copy(BugReport(save)) ? Strings.Get("report.copied") : Strings.Get("report.copy_failed");
                }
                if (Ui.Button(new Rect(r.x + 22 + half, y, half, 46), Strings.Get("report.download"), 14, true, Look.Normal, "제보"))
                {
                    WebBridge.Download("hero-to-you-report.txt", BugReport(save));
                    _reportNote = Strings.Get("report.downloaded");
                }
                y += 54;
            }
            if (_reportNote != null) Ui.Text(new Rect(r.x + 14, y, w - 28, 40), _reportNote, 12, Palette.Journal, TextAnchor.UpperLeft, false, true);
            if (Ui.Button(new Rect(r.x + (w - 180) / 2, r.yMax - 56, 180, 46), Strings.Get("common.close"), 16, true, Look.Primary, "제보", KeyCode.Escape))
            {
                _reportOpen = false;
                _reportNote = null;
            }
        }

        // The save with a line about the build and the screen, for the cloud to replay (CoreSandbox --replay).
        string BugReport(string save) =>
            $"# hero-to-you bug report · {BuildInfo.Version} · {Ui.Width:0}x{Ui.Height:0} · {(Ui.Touch ? "touch" : "mouse")} · {Settings.Language}\n" + save;

        // The tune for what is on the screen (Art/MusicArt.cs).
        Tune MusicFor()
        {
            var a = _adventure;
            if (_screen != ClientScreen.Adventure || a == null) return Tune.Title;
            if (a.Battle != null && (a.Screen == AdventureScreen.Battle || BattlePlaying))
                return a.BossBattle ? (a.Chapter.Index == _data.Chapters.Count - 1 ? Tune.BossFinal : Tune.Boss) : Tune.Battle;
            return a.Screen switch
            {
                AdventureScreen.Town => Tune.Town,
                AdventureScreen.Victory => Tune.Title,
                AdventureScreen.Defeat => Tune.Title,
                AdventureScreen.Story => Tune.Story,
                _ => System.Enum.TryParse(a.Chapter.Music, true, out Tune tune) ? tune : Tune.Forest,
            };
        }

        void OnGUI()
        {
            Ui.Begin();
            DrawBackground();
            if (_loadError != null)
            {
                Ui.Text(new Rect(20, 20, Ui.Width - 40, Ui.Height - 40), Strings.Get("error.load") + "\n" + _loadError, 14, Palette.Damage, TextAnchor.UpperLeft, false, true);
                return;
            }
            // A tip card, a campfire talk, the history or the bug report is modal: the screen under it is drawn but
            // cannot be pressed.
            bool talk = TalkWaiting;
            bool modal = _tipOpen != null || talk || _historyOpen || _reportOpen;
            PrepareTutorial();
            if (modal) GUI.enabled = false;
            switch (_screen)
            {
                case ClientScreen.Title:
                    DrawTitle();
                    break;
                case ClientScreen.Party:
                    DrawParty();
                    break;
                case ClientScreen.Journal:
                    DrawJournal();
                    break;
                case ClientScreen.Settings:
                    DrawSettings();
                    break;
                case ClientScreen.Slots:
                    DrawSlots();
                    break;
                case ClientScreen.Adventure:
                    DrawAdventure();
                    break;
            }
            GUI.enabled = true;
            if (!modal) DrawTutorial();
            if (_tipOpen != null) DrawTipCard();
            else if (_reportOpen) DrawBugReport();
            else if (_historyOpen) DrawHistory();
            else if (talk) DrawTalk();
            else Ui.DrawTip();
        }

        // ---- Tips (Tips.cs) ----

        string _tipOpen;

        // A tip opened by hand (the help links, the playtests) counts as seen, so it does not open again by itself
        // the moment it is closed.
        internal void ShowTip(string id)
        {
            if (!Tips.Exists(id)) return;
            Tips.FirstTime(id);
            _tipOpen = id;
        }

        void FirstTip(string id)
        {
            if (_tipOpen == null && Tips.FirstTime(id)) _tipOpen = id;
        }

        void DrawTipCard()
        {
            var (title, text) = Tips.Of(_tipOpen);
            Ui.Fill(new Rect(0, 0, Ui.Width, Ui.Height), new Color(0, 0, 0, 0.55f));
            float w = Mathf.Min(460, Ui.Width - 24);
            float textH = Ui.TextHeight(text, 14, w - 32);
            float h = Mathf.Min(Ui.Height - 24, textH + 120);
            var r = new Rect((Ui.Width - w) / 2, (Ui.Height - h) / 2, w, h);
            Ui.Panel(r, Palette.Panel, Palette.Gold);
            Ui.Text(new Rect(r.x + 16, r.y + 10, w - 32, 28), title, 19, Palette.Gold, TextAnchor.MiddleLeft, true);
            Ui.Text(new Rect(r.x + 16, r.y + 44, w - 32, h - 110), text, 14, Palette.Ink, TextAnchor.UpperLeft, false, true);
            if (Ui.Button(new Rect(r.x + (w - 180) / 2, r.yMax - 56, 180, 46), Strings.Get("common.ok"), 16, true, Look.Primary, "도움말", KeyCode.Space) || Ui.Key(KeyCode.Escape)) _tipOpen = null;
        }

        static void DrawBackground()
        {
            Ui.Fill(new Rect(0, 0, Ui.Width, Ui.Height), Palette.Back);
            // A soft vignette of bands, darker at the edges.
            for (int i = 0; i < 6; i++)
            {
                float inset = i * Mathf.Min(Ui.Width, Ui.Height) * 0.06f;
                Ui.Fill(new Rect(inset, inset, Ui.Width - inset * 2, Ui.Height - inset * 2), new Color(1, 1, 1, 0.012f));
            }
        }

        // ---- Starting, saving and leaving ----

        // A new run with the party screen's choices: the perks taken along, difficulty, hardcore, the chapters.
        internal void StartAdventure(IReadOnlyList<string> classes, ulong? seed = null) =>
            StartRun(classes, seed, ChosenPerks(), _difficulty, _hardcore, _startChapter, _oneChapter ? _startChapter : -1);

        internal void StartRun(IReadOnlyList<string> classes, ulong? seed, List<string> perks, Difficulty difficulty, bool hardcore, int firstChapter, int lastChapter)
        {
            ulong value = seed ?? _nextSeed ?? (ulong)DateTime.Now.Ticks;
            _nextSeed = null;
            _recorded = false;
            _adventure = new Adventure(_data, classes.ToList(), value, perks, difficulty, hardcore, firstChapter, lastChapter);
            _talksShown = 0;
            _history.Clear();
            _autoBattle = false;
            _screen = ClientScreen.Adventure;
            ResetAdventureView();
            SaveStore.Write(_adventure.Save());
        }

        // The saved adventure, replayed (SaveStore, Docs/Saving.md). Its battle, if it was in one, shows as it is.
        internal string Resume()
        {
            var adventure = Adventure.Load(_data, SaveStore.Read(), out string error, out string note);
            if (adventure == null)
            {
                SaveStore.Delete();
                return error;
            }
            _adventure = adventure;
            _recorded = false;
            _talksShown = adventure.Talks.Count;
            _history.Clear();
            _autoBattle = false;
            _screen = ClientScreen.Adventure;
            ResetAdventureView();
            if (adventure.Battle != null)
            {
                BeginPlayback(adventure.Battle);
                SkipPlayback();
            }
            _toast = note;
            return null;
        }

        // Gives the adventure a choice the player made and saves when it took. Errors show on the screen.
        internal string Act(Func<string> command)
        {
            string error = command();
            if (error == null)
            {
                Remember(_adventure.Report);
                if (_adventure.Result != null && _adventure.Screen == AdventureScreen.EventResult)
                    Remember(new[] { $"[{_adventure.Event?.Title}] {_adventure.Result.Choice.Label}: {_adventure.Result.Text}" }.Concat(_adventure.Result.Changes));
                if (_adventure.Over)
                {
                    SaveStore.Delete();
                    RecordInJournal();
                }
                else SaveStore.Write(_adventure.Save());
                Sounds.Play(Sound.Click);
            }
            _actError = error;
            return error;
        }

        string _actError;
        string _toast;

        internal void BackToTitle()
        {
            _adventure = null;
            _battleShown = null;
            _screen = ClientScreen.Title;
        }

        void DrawAdventure()
        {
            var adventure = _adventure;
            if (adventure == null)
            {
                BackToTitle();
                return;
            }
            // A battle keeps showing until its playback has caught up, even when the adventure has moved on.
            if (adventure.Battle != null && (adventure.Screen == AdventureScreen.Battle || BattlePlaying))
            {
                DrawBattle(adventure.Battle);
                return;
            }
            string first = adventure.LevelUp != null ? "levelup" : adventure.Screen switch
            {
                AdventureScreen.Map => adventure.Heroes.Any(h => h.Wounds > 0) ? "wounds" : "map",
                AdventureScreen.Join => adventure.Kind == Adventure.JoinKind.Dungeon ? "dungeon" : "join",
                AdventureScreen.DungeonRest => "dungeon",
                AdventureScreen.Event => "event",
                AdventureScreen.Town => "town",
                _ => null,
            };
            if (first != null) FirstTip(first);
            if (first == "wounds") FirstTip("map");
            switch (adventure.Screen)
            {
                case AdventureScreen.Story:
                    DrawStory();
                    break;
                case AdventureScreen.Map:
                    DrawMap();
                    break;
                case AdventureScreen.Join:
                    DrawJoin();
                    break;
                case AdventureScreen.Event:
                    DrawEvent();
                    break;
                case AdventureScreen.EventResult:
                    DrawEventResult();
                    break;
                case AdventureScreen.BattleResult:
                    DrawReport();
                    break;
                case AdventureScreen.Town:
                    DrawTown();
                    break;
                case AdventureScreen.DungeonRest:
                    DrawDungeonRest();
                    break;
                case AdventureScreen.Victory:
                case AdventureScreen.Defeat:
                    DrawEnd();
                    break;
            }
            if (_gearOpen && (adventure.Screen == AdventureScreen.Map || adventure.Screen == AdventureScreen.Town)) DrawGear();
            if (adventure.LevelUp != null) DrawLevelUp();
        }

        // The party strip along the top of the non-battle screens: each hero's health, resolve and potions, then
        // hope and gold. On the map, the hero whose turn it is has a gold edge.
        void DrawPartyStrip(Rect r)
        {
            var heroes = _adventure.Heroes;
            float infoWidth = Ui.Width < 500 ? 84 : 110;
            float each = (r.width - infoWidth) / heroes.Count;
            for (int i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                var cell = new Rect(r.x + i * each, r.y, each - 6, r.height);
                bool turn = _adventure.Screen == AdventureScreen.Map && _adventure.MapHero == i;
                Ui.Panel(cell, turn ? Palette.Selected : (Color?)null, turn ? Palette.Gold : (Color?)null);
                float icon = Mathf.Min(cell.height - 8, 40);
                Ui.Image(new Rect(cell.x + 4, cell.y + (cell.height - icon) / 2, icon, icon), PixelArt.Hero(hero.Class.Id, hero.Class.Color), hero.Down ? new Color(1, 1, 1, 0.35f) : (Color?)null);
                float x = cell.x + icon + 8;
                float w = cell.xMax - x - 4;
                Ui.Text(new Rect(x, cell.y + 2, w, 16), $"{hero.Name} Lv{hero.Level}", 12, Palette.Ink, TextAnchor.MiddleLeft, true);
                Ui.Bar(new Rect(x, cell.y + 20, w, 6), hero.MaxHp > 0 ? (float)hero.Hp / hero.MaxHp : 0, Palette.HpHero);
                string line = hero.Down ? Strings.Get("hero.down") : w >= 150 ? Strings.Format("strip.hero", hero.Hp, hero.MaxHp, hero.Resolve, hero.MaxResolve, hero.Potions) : $"{hero.Hp}/{hero.MaxHp} · {Strings.Format("hero.resolve", hero.Resolve)}";
                if (hero.Wounds > 0) line += " · " + Strings.Format("hero.wounds", hero.Wounds);
                Ui.Text(new Rect(x, cell.y + 26, w, 14), line, 10, hero.Down ? Palette.Damage : hero.Wounds > 0 ? Palette.Intent : Palette.Dim);
                if (w >= 120) Ui.Text(new Rect(x, cell.y + 2, w, 16), Strings.Get(hero.Back ? "row.back" : "row.front"), 9.5f, Palette.Dim, TextAnchor.MiddleRight);
                Ui.Tip(cell, HeroTip(hero));
            }
            var info = new Rect(r.xMax - infoWidth, r.y, infoWidth, r.height);
            Ui.Panel(info);
            Ui.Image(new Rect(info.x + 6, info.y + 6, 14, 14), PixelArt.Icon("heart"));
            Ui.Text(new Rect(info.x + 24, info.y + 4, info.width - 28, 18), Strings.Format("strip.hope", _adventure.Hope), 12, Palette.Ink);
            Ui.Image(new Rect(info.x + 6, info.y + 24, 14, 14), PixelArt.Icon("coin"));
            Ui.Text(new Rect(info.x + 24, info.y + 22, info.width - 28, 18), Strings.Format("strip.gold", _adventure.Gold), 12, Palette.Ink);
            string combos = _adventure.Combos.Count > 0 ? "\n" + Strings.Format("party.combos", string.Join(", ", _adventure.Combos.Select(c => c.Name))) : "";
            Ui.Tip(info, Strings.Format("strip.tip", _adventure.Renown, _adventure.Rank.Title) + combos);
        }

        // A hero in a few lines, for the long-press tip on the party strip.
        string HeroTip(Hero hero)
        {
            string stats = string.Join(" ", Enumerable.Range(0, Stats.Count).Select(i => $"{Stats.Name((Stat)i, Sheets)} {hero.Stat((Stat)i)}"));
            string carried = string.Join(", ", hero.Consumables.Where(c => c.Value > 0).Select(c => $"{(_data.Items.TryGetValue(c.Key, out var item) ? item.Name : c.Key)} {c.Value}"));
            string wounds = hero.Wounds > 0 ? "\n" + Strings.Format("hero.tip_wounds", hero.Wounds, Hero.MaxWounds, Hero.WoundPercent * hero.Wounds) : "";
            return Strings.Format("hero.tip_head", hero.Name, hero.Level, hero.Xp, hero.XpToNext, Strings.Get(hero.Back ? "row.back" : "row.front"))
                + "\n" + stats + "\n" + Strings.Format("hero.tip_stats", hero.ArmorValue, hero.Resolve, hero.MaxResolve, hero.Grit, hero.GritCap) + wounds
                + "\n" + hero.Class.Mechanic + (carried.Length > 0 ? "\n" + Strings.Format("hero.tip_items", carried) : "");
        }

        // ---- Campfire talks (Docs/Narrative.md 동료 대화) ----

        int _talksShown;
        bool _hardcore;

        // After losing a chapter: the run as it was when the chapter began (Adventure.ChapterStartSave).
        internal string RetryChapter()
        {
            string save = _retrySave;
            if (save == null) return Strings.Get("end.retry_none");
            SaveStore.Write(save);
            _retrySave = null;
            return Resume();
        }

        string _retrySave;

        bool TalkWaiting => _screen == ClientScreen.Adventure && _adventure != null && _adventure.LevelUp == null
            && _adventure.Screen == AdventureScreen.Map && _talksShown < _adventure.Talks.Count;
        Difficulty _difficulty = Difficulty.Normal;

        // The newest talk not shown yet, as a card over the map: who said what, then [계속].
        void DrawTalk()
        {
            FirstTip("talk");
            var (day, talk) = _adventure.Talks[_talksShown];
            Ui.Fill(new Rect(0, 0, Ui.Width, Ui.Height), new Color(0, 0, 0, 0.55f));
            float w = Mathf.Min(520, Ui.Width - 24);
            float lineW = w - 72;
            float h = 70 + talk.Lines.Sum(l => Mathf.Max(40, Ui.TextHeight(l.text, 13.5f, lineW) + 22)) + 60;
            h = Mathf.Min(h, Ui.Height - 24);
            var r = new Rect((Ui.Width - w) / 2, (Ui.Height - h) / 2, w, h);
            Ui.Panel(r, new Color(0.13f, 0.09f, 0.07f, 0.98f), Palette.Gold);
            Ui.Text(new Rect(r.x + 16, r.y + 10, w - 32, 26), Strings.Format("talk.dawn", Strings.Get("talk.heading"), day), 17, Palette.Gold, TextAnchor.MiddleLeft, true);
            float y = r.y + 44;
            foreach (var (speaker, text) in talk.Lines)
            {
                var cls = _data.Classes[speaker];
                Ui.Image(new Rect(r.x + 14, y + 2, 36, 36), PixelArt.Hero(cls.Id, cls.Color));
                Ui.Text(new Rect(r.x + 58, y, lineW, 16), cls.Name, 11.5f, Palette.Gold, TextAnchor.MiddleLeft, true);
                float th = Ui.TextHeight(text, 13.5f, lineW);
                Ui.Text(new Rect(r.x + 58, y + 17, lineW, th), text, 13.5f, Palette.Ink, TextAnchor.UpperLeft, false, true);
                y += Mathf.Max(40, th + 22);
            }
            if (Ui.Button(new Rect(r.x + (w - 180) / 2, r.yMax - 54, 180, 46), Strings.Get("common.continue"), 16, true, Look.Primary, "대화", KeyCode.Space)) _talksShown++;
        }

        // ---- The journal (Docs/Journal.md) ----

        Journal _journal;
        bool _recorded;
        int _lastPages;

        internal Journal JournalNow => _journal ??= SaveStore.ReadJournal();

        void RecordInJournal()
        {
            if (_recorded || _adventure == null || !_adventure.Over) return;
            _recorded = true;
            _retrySave = _adventure.Screen == AdventureScreen.Defeat ? _adventure.ChapterStartSave() : null;
            _lastPages = JournalNow.Record(_adventure);
            SaveStore.WriteJournal(JournalNow);
            if (JournalNow.NewAchievements.Count > 0) Sounds.Play(Sound.Achievement);
        }

        // The perks the player picked on the journal screen and still has unlocked.
        const string PerksKey = "perks_chosen";

        internal List<string> ChosenPerks() =>
            PlayerPrefs.GetString(PerksKey, "").Split(',').Where(p => p.Length > 0 && JournalNow.Has(p) && _data.Perks.Any(d => d.Id == p)).ToList();

        internal void SetPerkChosen(string id, bool on)
        {
            var chosen = ChosenPerks();
            chosen.Remove(id);
            if (on) chosen.Add(id);
            PlayerPrefs.SetString(PerksKey, string.Join(",", chosen));
            PlayerPrefs.Save();
        }
    }
}
