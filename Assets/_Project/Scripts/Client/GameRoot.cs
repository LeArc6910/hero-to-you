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
            switch (_screen)
            {
                case ClientScreen.Title:
                    DrawTitle();
                    break;
                case ClientScreen.Party:
                    DrawParty();
                    break;
                case ClientScreen.Adventure:
                    DrawAdventure();
                    break;
            }
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

        // ---- Starting and leaving ----

        internal void StartAdventure(IReadOnlyList<string> classes, ulong? seed = null)
        {
            ulong value = seed ?? _nextSeed ?? (ulong)DateTime.Now.Ticks;
            _nextSeed = null;
            _adventure = new Adventure(_data, classes.ToList(), value);
            _screen = ClientScreen.Adventure;
            ResetAdventureView();
        }

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
            switch (adventure.Screen)
            {
                case AdventureScreen.Story:
                    DrawStory();
                    break;
                case AdventureScreen.Event:
                    DrawEvent();
                    break;
                case AdventureScreen.EventResult:
                    DrawEventResult();
                    break;
                case AdventureScreen.BattleResult:
                case AdventureScreen.Rest:
                    DrawReport();
                    break;
                case AdventureScreen.Victory:
                case AdventureScreen.Defeat:
                    DrawEnd();
                    break;
            }
        }

        // The party strip along the top of the non-battle screens: each hero's health, resolve and potions, then
        // hope and gold.
        void DrawPartyStrip(Rect r)
        {
            var heroes = _adventure.Heroes;
            float infoWidth = Ui.Width < 500 ? 84 : 110;
            float each = (r.width - infoWidth) / heroes.Count;
            for (int i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                var cell = new Rect(r.x + i * each, r.y, each - 6, r.height);
                Ui.Panel(cell);
                float icon = Mathf.Min(cell.height - 8, 40);
                Ui.Image(new Rect(cell.x + 4, cell.y + (cell.height - icon) / 2, icon, icon), PixelArt.Hero(hero.Class.Id, hero.Class.Color), hero.Down ? new Color(1, 1, 1, 0.35f) : (Color?)null);
                float x = cell.x + icon + 8;
                float w = cell.xMax - x - 4;
                Ui.Text(new Rect(x, cell.y + 2, w, 16), $"{hero.Name} Lv{hero.Level}", 12, Palette.Ink, TextAnchor.MiddleLeft, true);
                Ui.Bar(new Rect(x, cell.y + 20, w, 6), hero.MaxHp > 0 ? (float)hero.Hp / hero.MaxHp : 0, Palette.HpHero);
                string line = hero.Down ? "쓰러짐" : w >= 150 ? $"{hero.Hp}/{hero.MaxHp} · 결의 {hero.Resolve}/{hero.MaxResolve} · 물약 {hero.Potions}" : $"{hero.Hp}/{hero.MaxHp} · 결의 {hero.Resolve}";
                Ui.Text(new Rect(x, cell.y + 26, w, 14), line, 10, hero.Down ? Palette.Damage : Palette.Dim);
            }
            var info = new Rect(r.xMax - infoWidth, r.y, infoWidth, r.height);
            Ui.Panel(info);
            Ui.Image(new Rect(info.x + 6, info.y + 6, 14, 14), PixelArt.Icon("heart"));
            Ui.Text(new Rect(info.x + 24, info.y + 4, info.width - 28, 18), $"희망 {_adventure.Hope}", 12, Palette.Ink);
            Ui.Image(new Rect(info.x + 6, info.y + 24, 14, 14), PixelArt.Icon("coin"));
            Ui.Text(new Rect(info.x + 24, info.y + 22, info.width - 28, 18), $"금화 {_adventure.Gold}", 12, Palette.Ink);
        }
    }
}
