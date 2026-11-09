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
            try
            {
                _data = GameData.Load(path => Sheet(path));
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
            if (asset == null) throw new InvalidOperationException($"시트가 없습니다: Resources/{resource}");
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
            Music.Tick(Time.unscaledDeltaTime);
        }

        // The tune for what is on the screen (Art/MusicArt.cs).
        Tune MusicFor()
        {
            var a = _adventure;
            if (_screen != ClientScreen.Adventure || a == null) return Tune.Title;
            if (a.Battle != null && (a.Screen == AdventureScreen.Battle || BattlePlaying)) return a.BossBattle ? Tune.Boss : Tune.Battle;
            return a.Screen switch
            {
                AdventureScreen.Town => Tune.Town,
                AdventureScreen.Victory => Tune.Title,
                AdventureScreen.Defeat => Tune.Title,
                AdventureScreen.Story => Tune.Story,
                _ => a.Chapter.Index == 0 ? Tune.Forest : Tune.Harbor,
            };
        }

        void OnGUI()
        {
            Ui.Begin();
            DrawBackground();
            if (_loadError != null)
            {
                Ui.Text(new Rect(20, 20, Ui.Width - 40, Ui.Height - 40), "데이터를 불러오지 못했습니다.\n" + _loadError, 14, Palette.Damage, TextAnchor.UpperLeft, false, true);
                return;
            }
            // A tip card is modal: the screen under it is drawn but cannot be pressed.
            bool modal = _tipOpen != null;
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
                case ClientScreen.Adventure:
                    DrawAdventure();
                    break;
            }
            GUI.enabled = true;
            if (modal) DrawTipCard();
            else Ui.DrawTip();
        }

        // ---- Tips (Tips.cs) ----

        string _tipOpen;

        internal void ShowTip(string id)
        {
            if (Tips.All.ContainsKey(id)) _tipOpen = id;
        }

        void FirstTip(string id)
        {
            if (_tipOpen == null && Tips.FirstTime(id)) _tipOpen = id;
        }

        void DrawTipCard()
        {
            var (title, text) = Tips.All[_tipOpen];
            Ui.Fill(new Rect(0, 0, Ui.Width, Ui.Height), new Color(0, 0, 0, 0.55f));
            float w = Mathf.Min(460, Ui.Width - 24);
            float textH = Ui.TextHeight(text, 14, w - 32);
            float h = Mathf.Min(Ui.Height - 24, textH + 120);
            var r = new Rect((Ui.Width - w) / 2, (Ui.Height - h) / 2, w, h);
            Ui.Panel(r, Palette.Panel, Palette.Gold);
            Ui.Text(new Rect(r.x + 16, r.y + 10, w - 32, 28), title, 19, Palette.Gold, TextAnchor.MiddleLeft, true);
            Ui.Text(new Rect(r.x + 16, r.y + 44, w - 32, h - 110), text, 14, Palette.Ink, TextAnchor.UpperLeft, false, true);
            if (Ui.Button(new Rect(r.x + (w - 180) / 2, r.yMax - 56, 180, 46), "알겠다", 16, true, Look.Primary, "도움말")) _tipOpen = null;
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

        internal void StartAdventure(IReadOnlyList<string> classes, ulong? seed = null)
        {
            ulong value = seed ?? _nextSeed ?? (ulong)DateTime.Now.Ticks;
            _nextSeed = null;
            _recorded = false;
            _adventure = new Adventure(_data, classes.ToList(), value, ChosenPerks());
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
                AdventureScreen.Map => "map",
                AdventureScreen.Join => "join",
                AdventureScreen.Event => "event",
                AdventureScreen.Town => "town",
                _ => null,
            };
            if (first != null) FirstTip(first);
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
                string line = hero.Down ? "쓰러짐" : w >= 150 ? $"{hero.Hp}/{hero.MaxHp} · 결의 {hero.Resolve}/{hero.MaxResolve} · 물약 {hero.Potions}" : $"{hero.Hp}/{hero.MaxHp} · 결의 {hero.Resolve}";
                Ui.Text(new Rect(x, cell.y + 26, w, 14), line, 10, hero.Down ? Palette.Damage : Palette.Dim);
                Ui.Tip(cell, HeroTip(hero));
            }
            var info = new Rect(r.xMax - infoWidth, r.y, infoWidth, r.height);
            Ui.Panel(info);
            Ui.Image(new Rect(info.x + 6, info.y + 6, 14, 14), PixelArt.Icon("heart"));
            Ui.Text(new Rect(info.x + 24, info.y + 4, info.width - 28, 18), $"희망 {_adventure.Hope}", 12, Palette.Ink);
            Ui.Image(new Rect(info.x + 6, info.y + 24, 14, 14), PixelArt.Icon("coin"));
            Ui.Text(new Rect(info.x + 24, info.y + 22, info.width - 28, 18), $"금화 {_adventure.Gold}", 12, Palette.Ink);
            Ui.Tip(info, $"희망: 쓰러진 영웅이 전투가 끝나거나 아침이 오면 희망 하나로 일어난다. 금화: 마을에서 쓴다. 명성 {_adventure.Renown} (「{_adventure.Rank.Title}」)");
        }

        // A hero in a few lines, for the long-press tip on the party strip.
        string HeroTip(Hero hero)
        {
            string stats = string.Join(" ", Enumerable.Range(0, Stats.Count).Select(i => $"{Stats.Name((Stat)i)} {hero.Stat((Stat)i)}"));
            string carried = string.Join(", ", hero.Consumables.Where(c => c.Value > 0).Select(c => $"{(_data.Items.TryGetValue(c.Key, out var item) ? item.Name : c.Key)} {c.Value}"));
            return $"{hero.Name} Lv{hero.Level} (경험치 {hero.Xp}, 다음까지 {hero.XpToNext})\n{stats}\n방어도 {hero.ArmorValue} · 결의 {hero.Resolve}/{hero.MaxResolve} · 끈기 {hero.Grit}/{hero.GritCap}\n{hero.Class.Mechanic}" + (carried.Length > 0 ? "\n소모품: " + carried : "");
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
            _lastPages = JournalNow.Record(_adventure);
            SaveStore.WriteJournal(JournalNow);
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
