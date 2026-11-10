using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEngine;
using Terrain = Game.Core.Terrain;

namespace Game.Client
{
    // The battle screen (Docs/Combat.md): the turn order along the top, heroes facing monsters with their intents,
    // and the current hero's panel (skills, target, resolve, odds, roll). Core resolves a command at once; the screen
    // then plays the battle's events back one by one (dice, damage numbers, statuses), keeping its own copy of the
    // health bars so they move when the numbers do, not before.
    public sealed partial class GameRoot
    {
        sealed class Floater
        {
            public int Unit;
            public string Text;
            public Color Color;
            public float Age;
        }

        Battle _battleShown;
        int _played;
        float _eventTimer;
        int _turnActor = -1;
        readonly Dictionary<int, int> _shownHp = new Dictionary<int, int>();
        readonly Dictionary<int, float> _hitAt = new Dictionary<int, float>();

        // When each unit last attacked (it lunges toward the other side) and which way.
        readonly Dictionary<int, float> _lungeAt = new Dictionary<int, float>();
        readonly List<Floater> _floaters = new List<Floater>();
        readonly List<string> _log = new List<string>();
        BattleEvent _diceEvent;
        float _diceAge;
        string _banner;
        float _bannerAge;
        float _clock;

        // The current hero's choices on the panel.
        int _selectedFor = -1;
        string _skill;
        int _target = -1;
        int _resolve;
        string _battleError;

        internal bool BattlePlaying => _battleShown != null && (_played < _battleShown.Events.Count || _eventTimer > 0);

        internal bool AwaitingHero => _adventure?.Battle != null && _adventure.Screen == AdventureScreen.Battle && !BattlePlaying && _adventure.Battle.State == BattleState.HeroTurn;

        void ResetAdventureView()
        {
            _battleShown = null;
            _selectedFor = -1;
            _skill = null;
            _battleError = null;
            _picked = null;
            _gearOpen = false;
            _actError = null;
            _toast = null;
            _panFor = -1;
            ResetEventView();
        }

        // ---- Playback ----

        void TickBattle(float dt)
        {
            _clock += dt;
            var battle = _adventure?.Battle;
            if (battle == null) return;
            if (battle != _battleShown) BeginPlayback(battle);
            FirstTip("battle");
            float step = _speed <= 0 ? float.MaxValue : dt * _speed;
            foreach (var floater in _floaters) floater.Age += step;
            _floaters.RemoveAll(f => f.Age > 1.2f);
            _diceAge += step;
            _bannerAge += step;
            _eventTimer -= step;
            int guard = 0;
            while (_eventTimer <= 0 && _played < battle.Events.Count && guard++ < 1000)
            {
                var e = battle.Events[_played++];
                Play(e);
                _eventTimer += _speed <= 0 ? 0 : Duration(e, battle);
            }
            if (_played >= battle.Events.Count && _eventTimer < 0) _eventTimer = 0;
            if (!BattlePlaying) SyncShown(battle);
        }

        void BeginPlayback(Battle battle)
        {
            _battleShown = battle;
            _played = 0;
            _eventTimer = 0;
            _turnActor = -1;
            _floaters.Clear();
            _log.Clear();
            _diceEvent = null;
            _banner = _adventure.Encounter?.Text;
            _bannerAge = 0;
            _selectedFor = -1;
            // The health each unit had when the battle began: undo what the events so far did.
            _shownHp.Clear();
            foreach (var unit in battle.Units) _shownHp[unit.Id] = unit.Hp;
            for (int i = battle.Events.Count - 1; i >= 0; i--)
            {
                var e = battle.Events[i];
                switch (e.Kind)
                {
                    case BattleEventKind.Damage:
                        _shownHp[e.Target] += e.Amount;
                        break;
                    case BattleEventKind.Heal:
                    case BattleEventKind.Potion:
                        _shownHp[e.Target] -= e.Amount;
                        break;
                    case BattleEventKind.Revive:
                        _shownHp[e.Target] = 0;
                        break;
                }
            }
        }

        // Shows the battle as it stands now, without playing back what already happened (a loaded save).
        void SkipPlayback()
        {
            if (_battleShown == null) return;
            _played = _battleShown.Events.Count;
            _eventTimer = 0;
            _diceEvent = null;
            _banner = null;
            SyncShown(_battleShown);
            for (int i = _battleShown.Events.Count - 1; i >= 0; i--)
            {
                if (_battleShown.Events[i].Kind != BattleEventKind.Turn) continue;
                _turnActor = _battleShown.Events[i].Actor;
                break;
            }
        }

        void SyncShown(Battle battle)
        {
            foreach (var unit in battle.Units) _shownHp[unit.Id] = unit.Hp;
        }

        static float Duration(BattleEvent e, Battle battle)
        {
            bool hero = e.Actor >= 0 && battle.Unit(e.Actor).IsHero;
            return e.Kind switch
            {
                BattleEventKind.Turn => hero ? 0.1f : 0.3f,
                // The heroes' own dice get the longer look; monsters' rolls move the game along.
                BattleEventKind.Roll => hero ? 1.15f : 0.8f,
                BattleEventKind.Damage => 0.45f,
                BattleEventKind.Miss => 0.4f,
                BattleEventKind.Heal => 0.45f,
                BattleEventKind.Potion => 0.45f,
                BattleEventKind.Down => 0.45f,
                BattleEventKind.Revive => 0.55f,
                BattleEventKind.StatusOn => 0.35f,
                BattleEventKind.StatusOff => 0.15f,
                BattleEventKind.Skip => 0.6f,
                BattleEventKind.Phase => 1.8f,
                BattleEventKind.ResolveGained => 0.6f,
                BattleEventKind.Guard => 0.45f,
                BattleEventKind.Victory => 0.9f,
                BattleEventKind.Defeat => 1.2f,
                BattleEventKind.Fled => 1.0f,
                _ => 0.02f,
            };
        }

        void Play(BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.Turn:
                    _turnActor = e.Actor;
                    break;
                case BattleEventKind.Roll:
                    _diceEvent = e;
                    _diceAge = 0;
                    if (e.Actor >= 0 && e.Target != e.Actor) _lungeAt[e.Actor] = _clock;
                    if (_speed > 0) Sounds.Play(e.Perfect ? Sound.Perfect : Sound.Dice);
                    _blowSound = BlowSound(e);
                    break;
                case BattleEventKind.Damage:
                    _shownHp[e.Target] = Mathf.Max(0, _shownHp[e.Target] - e.Amount);
                    _hitAt[e.Target] = _clock;
                    if (_speed > 0 && _battleShown.Unit(e.Target).IsHero) WebBridge.Buzz(40);
                    Float(e.Target, "-" + e.Amount + (e.Perfect ? "!" : ""), Palette.Damage);
                    if (_speed > 0) Sounds.Play(_blowSound);
                    break;
                case BattleEventKind.Miss:
                    Float(e.Target, Strings.Get("battle.float_miss"), Palette.Dim);
                    if (_speed > 0) Sounds.Play(Sound.Miss);
                    break;
                case BattleEventKind.Heal:
                case BattleEventKind.Potion:
                    _shownHp[e.Target] += e.Amount;
                    Float(e.Target, "+" + e.Amount, Palette.Heal);
                    if (_speed > 0) Sounds.Play(Sound.Heal);
                    break;
                case BattleEventKind.Revive:
                    _shownHp[e.Target] = e.Amount;
                    Float(e.Target, Strings.Get("battle.float_up"), Palette.Heal);
                    break;
                case BattleEventKind.Down:
                    _shownHp[e.Target] = 0;
                    if (_speed > 0) Sounds.Play(Sound.Down);
                    break;
                case BattleEventKind.StatusOn:
                    Float(e.Target, StatusFloat(e), Palette.Gold);
                    break;
                case BattleEventKind.ResolveGained:
                    Float(e.Actor, Strings.Format("battle.float_resolve", e.Amount), Palette.Resolve);
                    break;
                case BattleEventKind.Guard:
                    Float(e.Target, Strings.Get("battle.float_guard"), Palette.Gold);
                    break;
                case BattleEventKind.Phase:
                case BattleEventKind.Victory:
                case BattleEventKind.Defeat:
                case BattleEventKind.Fled:
                    _banner = e.Text;
                    _bannerAge = 0;
                    if (_speed > 0) Sounds.Play(e.Kind == BattleEventKind.Victory ? Sound.Victory : e.Kind == BattleEventKind.Defeat ? Sound.Defeat : Sound.Battle);
                    break;
            }
            if (e.Kind != BattleEventKind.Turn && e.Kind != BattleEventKind.Intent && !string.IsNullOrEmpty(e.Text))
            {
                _log.Add(e.Text);
                if (_log.Count > 40) _log.RemoveAt(0);
            }
        }

        // The sound of the blows from the last roll: by the weapon or the skill, and a boss's rule its own.
        Sound _blowSound = Sound.Hit;

        Sound BlowSound(BattleEvent e)
        {
            var unit = _battleShown?.Unit(e.Actor);
            if (unit == null || string.IsNullOrEmpty(e.Skill)) return Sound.Hit;
            if (unit.IsHero)
            {
                var skill = unit.Hero.Skill(e.Skill);
                if (skill == null) return Sound.Hit;
                if (skill.Kind == SkillKind.Magic) return Sound.Spell;
                if (skill.Kind != SkillKind.Physical) return Sound.Hit;
                if (e.Skill != Hero.AttackId) return Sound.Slash;
                return unit.Hero.Weapon?.Kind switch
                {
                    "bow" or "crossbow" or "sling" or "cards" => Sound.Arrow,
                    "fist" => Sound.Punch,
                    _ => Sound.Slash,
                };
            }
            if (!_data.MonsterSkills.TryGetValue(e.Skill, out var blow)) return Sound.Hit;
            return blow.Effect switch
            {
                "seal" => Sound.Seal,
                "erase" => Sound.Erase,
                "freeze" => Sound.Freeze,
                "scorch" => Sound.Brand,
                "daze" => Sound.Bell,
                _ => blow.Kind == "magic" ? Sound.Spell : Sound.Slash,
            };
        }

        static string StatusFloat(BattleEvent e) => e.Note switch
        {
            "bless" => Strings.Get("battle.float_bless"),
            "lucky" => Strings.Get("battle.float_lucky"),
            _ => StatusNames.Name(e.Status, Sheets),
        };

        void Float(int unit, string text, Color color)
        {
            if (unit < 0) return;
            _floaters.Add(new Floater { Unit = unit, Text = text, Color = color });
        }

        // ---- Drawing ----

        // Where each unit was drawn this frame, for floaters and targeting.
        readonly Dictionary<int, Rect> _unitRects = new Dictionary<int, Rect>();

        void DrawBattle(Battle battle)
        {
            if (battle != _battleShown) BeginPlayback(battle);
            _unitRects.Clear();
            float topH = 46;
            var top = new Rect(4, 4, Ui.Width - 8, topH - 6);
            Rect field;
            Rect panel;
            if (Ui.Portrait)
            {
                field = new Rect(0, topH, Ui.Width, (Ui.Height - topH) * 0.47f);
                panel = new Rect(6, field.yMax + 2, Ui.Width - 12, Ui.Height - field.yMax - 8);
            }
            else
            {
                float pw = Mathf.Clamp(Ui.Width * 0.37f, 300, 440);
                field = new Rect(0, topH, Ui.Width - pw - 8, Ui.Height - topH);
                panel = new Rect(Ui.Width - pw - 4, topH + 2, pw, Ui.Height - topH - 6);
            }

            PrepareSelection(battle);
            DrawTimeline(top, battle);
            DrawBackdrop(field);
            DrawUnits(field, battle);
            DrawFloaters();
            DrawDice(field, battle);
            DrawBanner(field);
            DrawPanel(panel, battle);
        }

        // The place the battle happens, behind the units: the chapter's sky, the ground of the hex it started on and
        // a few shapes of it (trees, stones, waves), drawn from code like the rest of the art.
        void DrawBackdrop(Rect field)
        {
            var a = _adventure;
            var tile = a.JoinTile;
            var terrain = tile?.Terrain ?? Terrain.Plains;
            bool boss = a.BossBattle;
            var sky = BackdropSky(a.Chapter.Index, a.Night, boss);
            int bands = 6;
            float skyH = field.height * 0.62f;
            for (int i = 0; i < bands; i++)
                Ui.Fill(new Rect(field.x, field.y + skyH * i / bands, field.width, skyH / bands + 1), Color.Lerp(sky.top, sky.bottom, i / (float)(bands - 1)));
            var ground = TerrainColors.TryGetValue(terrain, out var g) ? g : Palette.Ground;
            Ui.Fill(new Rect(field.x, field.y + skyH, field.width, field.height - skyH), Color.Lerp(ground, Palette.Back, 0.35f));
            Ui.Fill(new Rect(field.x, field.y + skyH, field.width, 2), Color.Lerp(ground, Color.white, 0.15f));
            string mark = TerrainMark(terrain) ?? (terrain == Terrain.Road ? "stones" : null);
            if (boss) mark = (a.Chapter.Index % 3) switch { 0 => "stones", 1 => "wave", _ => "peak" };
            if (mark == null) return;
            var icon = PixelArt.Icon(mark);
            float s = Mathf.Min(field.height * 0.22f, 64);
            // Far shapes along the horizon, fixed per battle so they do not jump around.
            var random = new System.Random(_adventure.Log.Count / 50 + (int)terrain * 7 + a.Chapter.Index * 31);
            for (int i = 0; i < 7; i++)
            {
                float x = field.x + (float)random.NextDouble() * (field.width - s);
                float y = field.y + skyH - s * (0.75f + (float)random.NextDouble() * 0.2f);
                Ui.Image(new Rect(x, y, s, s), icon, new Color(1, 1, 1, 0.22f));
            }
        }

        static (Color top, Color bottom) BackdropSky(int chapter, bool night, bool boss)
        {
            if (boss) return (chapter % 3) switch
            {
                0 => (Palette.Hex("#2A1712"), Palette.Hex("#6B3322")),
                1 => (Palette.Hex("#0E1A2A"), Palette.Hex("#2E5A6E")),
                _ => (Palette.Hex("#1A2230"), Palette.Hex("#8DA6BC")),
            };
            if (night) return (Palette.Hex("#0B0D18"), Palette.Hex("#232842"));
            return (chapter % 3) switch
            {
                0 => (Palette.Hex("#3A3530"), Palette.Hex("#7A6E5E")),
                1 => (Palette.Hex("#2E4252"), Palette.Hex("#7895A0")),
                _ => (Palette.Hex("#5C6E80"), Palette.Hex("#C9D6E0")),
            };
        }

        void DrawTimeline(Rect r, Battle battle)
        {
            Ui.Panel(r);
            Ui.Text(new Rect(r.x + 6, r.y, 34, r.height), Strings.Get("battle.order"), 12, Palette.Dim);
            float infoW = Ui.Width < 500 ? 96 : 150;
            var order = BattlePlaying ? new List<Unit>() : battle.Timeline(8);
            float size = r.height - 8;
            float x = r.x + 40;
            for (int i = 0; i < order.Count && x + size < r.xMax - infoW; i++)
            {
                var unit = order[i];
                var cell = new Rect(x, r.y + 4, size, size);
                Ui.Fill(cell, unit.IsHero ? Palette.WithAlpha(Palette.HpHero, 0.25f) : Palette.WithAlpha(Palette.Hp, 0.25f));
                if (i == 0) Ui.Frame(cell, Palette.Gold, 2);
                Ui.Image(cell, Sprite(unit));
                x += size + 4;
            }
            if (BattlePlaying) Ui.Text(new Rect(r.x + 40, r.y, r.width - 40 - infoW, r.height), "…", 14, Palette.Dim);
            var info = new Rect(r.xMax - infoW, r.y, infoW - 4, r.height);
            Ui.Image(new Rect(info.x, info.y + (r.height - 14) / 2, 14, 14), PixelArt.Icon("heart"));
            Ui.Text(new Rect(info.x + 16, info.y, 40, r.height), _adventure.Hope.ToString(), 13, Palette.Ink);
            Ui.Image(new Rect(info.x + 44, info.y + (r.height - 14) / 2, 14, 14), PixelArt.Icon("coin"));
            Ui.Text(new Rect(info.x + 60, info.y, 50, r.height), _adventure.Gold.ToString(), 13, Palette.Ink);
            if (infoW > 120) Ui.Text(new Rect(info.x + 90, info.y, 60, r.height), _adventure.BossBattle ? Strings.Get("battle.boss") : Strings.Format("common.day", _adventure.Day), 12, _adventure.BossBattle ? Palette.Damage : Palette.Dim, TextAnchor.MiddleRight);
        }

        static Texture2D Sprite(Unit unit) => unit.IsHero
            ? PixelArt.Hero(unit.Hero.Class.Id, unit.Hero.Class.Color)
            : PixelArt.Monster(unit.Monster.Id, unit.Monster.Color);

        void DrawUnits(Rect field, Battle battle)
        {
            // The party side: heroes, then their summons (the skeleton, the wolf).
            var heroes = battle.Party.ToList();
            var monsters = battle.Monsters.ToList();
            if (Ui.Portrait)
            {
                var monsterArea = new Rect(field.x + 4, field.y + 4, field.width - 8, field.height * 0.56f);
                var heroArea = new Rect(field.x + 4, monsterArea.yMax, field.width - 8, field.yMax - monsterArea.yMax - 2);
                LayoutRow(monsterArea, monsters, battle);
                LayoutRow(heroArea, heroes, battle);
            }
            else
            {
                var heroArea = new Rect(field.x + 6, field.y + 4, field.width * 0.42f, field.height - 8);
                var monsterArea = new Rect(heroArea.xMax + 8, field.y + 4, field.xMax - heroArea.xMax - 12, field.height - 8);
                int heroColumns = heroes.Count > 3 ? 2 : 1;
                int heroRows = (heroes.Count + heroColumns - 1) / heroColumns;
                float hw = heroArea.width / heroColumns;
                for (int i = 0; i < heroes.Count; i++)
                {
                    float h = heroArea.height / heroRows;
                    DrawUnit(new Rect(heroArea.x + i / heroRows * hw, heroArea.y + i % heroRows * h, hw - 4, h - 4), heroes[i], battle);
                }
                int columns = monsters.Count > 3 ? 2 : 1;
                int perColumn = (monsters.Count + columns - 1) / columns;
                float cw = monsterArea.width / columns;
                for (int i = 0; i < monsters.Count; i++)
                {
                    int column = i / perColumn;
                    int row = i % perColumn;
                    float h = monsterArea.height / perColumn;
                    DrawUnit(new Rect(monsterArea.x + column * cw, monsterArea.y + row * h, cw - 4, h - 4), monsters[i], battle);
                }
            }
        }

        void LayoutRow(Rect area, List<Unit> units, Battle battle)
        {
            int columns = Mathf.Min(units.Count, 3);
            int rows = (units.Count + columns - 1) / columns;
            float w = area.width / columns;
            float h = area.height / rows;
            for (int i = 0; i < units.Count; i++)
                DrawUnit(new Rect(area.x + i % columns * w, area.y + i / columns * h, w - 4, h - 4), units[i], battle);
        }

        void DrawUnit(Rect slot, Unit unit, Battle battle)
        {
            int hp = _shownHp.TryGetValue(unit.Id, out int shown) ? shown : unit.Hp;
            bool down = hp <= 0;
            bool acting = unit.Id == _turnActor && !battle.Over;
            bool targetable = AwaitingHero && IsTargetable(battle, unit);
            bool targeted = AwaitingHero && _target == unit.Id && NeedsTarget(battle);

            if (acting) Ui.Fill(slot, Palette.WithAlpha(Palette.Gold, 0.10f));
            if (targeted) Ui.Frame(slot, Palette.Accent, 2.5f);
            else if (targetable) Ui.Frame(slot, Palette.WithAlpha(Palette.Accent, 0.55f), 1.5f);
            else if (acting) Ui.Frame(slot, Palette.Gold, 1.5f);

            bool big = !unit.IsHero && unit.Monster.Size > 1;
            float textH = 44;
            float intentH = unit.IsHero ? 0 : 30;
            float sprite = Mathf.Min(slot.height - textH - intentH - 4, slot.width * (big ? 0.8f : 0.5f));
            sprite = Mathf.Max(16, Mathf.Floor(sprite / 8) * 8);
            // Motion (Docs/Design.md 그림): a slow breath while standing, a lunge toward the other side when attacking,
            // a shake and a red flash when hit, lying low when down.
            float at = -10;
            bool hit = !Settings.LessMotion && _hitAt.TryGetValue(unit.Id, out at) && _clock - at < 0.25f;
            float shake = hit ? Mathf.Sin((_clock - at) * 80) * 3 : 0;
            float lunge = 0;
            if (!Settings.LessMotion && _lungeAt.TryGetValue(unit.Id, out float swung) && _clock - swung < 0.3f) lunge = Mathf.Sin((_clock - swung) / 0.3f * Mathf.PI) * sprite * 0.18f;
            var toward = unit.IsHero || unit.Side == Side.Party ? (Ui.Portrait ? new Vector2(0, -1) : new Vector2(1, 0)) : (Ui.Portrait ? new Vector2(0, 1) : new Vector2(-1, 0));
            float breath = down || Settings.LessMotion ? 0 : Mathf.Sin(_clock * 2.2f + unit.Id * 1.3f) * sprite * 0.015f;
            var spriteRect = new Rect(slot.x + (slot.width - sprite) / 2 + shake + toward.x * lunge, slot.y + intentH + 2 + toward.y * lunge + breath + (down ? sprite * 0.18f : 0), sprite, sprite - (down ? sprite * 0.18f : 0));
            _unitRects[unit.Id] = spriteRect;
            Ui.Fill(new Rect(spriteRect.x + sprite * 0.15f, spriteRect.yMax - sprite * 0.06f, sprite * 0.7f, sprite * 0.08f), new Color(0, 0, 0, 0.35f));
            var tint = down ? new Color(0.4f, 0.4f, 0.45f, 0.45f) : hit ? new Color(1f, 0.55f, 0.5f, 1f) : (Color?)null;
            Ui.Image(spriteRect, Sprite(unit), tint);

            if (!unit.IsHero && !down && unit.Intent != null && !battle.Over) DrawIntent(new Rect(slot.x + 2, slot.y, slot.width - 4, intentH - 2), unit, battle);

            float y = spriteRect.yMax + 2;
            Ui.Text(new Rect(slot.x, y, slot.width, 16), unit.Name + (unit.IsHero ? $" Lv{unit.Hero.Level} · {Strings.Get(unit.Hero.Back ? "row.back" : "row.front")}" : ""), 12, down ? Palette.Dim : Palette.Ink, TextAnchor.MiddleCenter, true);
            y += 16;
            float barW = Mathf.Min(slot.width - 12, 150);
            var bar = new Rect(slot.x + (slot.width - barW) / 2, y + 1, barW, 7);
            Ui.Bar(bar, unit.MaxHp > 0 ? (float)hp / unit.MaxHp : 0, unit.IsHero ? Palette.HpHero : Palette.Hp);
            y += 9;
            string line = down ? Strings.Get("hero.down") : $"{hp}/{unit.MaxHp}";
            if (unit.IsHero && !down) line += " · " + Strings.Format("hero.resolve", unit.Hero.Resolve) + (unit.Hero.Blessing + unit.Hero.Luck > 0 ? " · " + Strings.Format("battle.sure_plus", unit.Hero.Blessing + unit.Hero.Luck) : "");
            string statuses = StatusLine(unit, battle);
            if (statuses.Length > 0) line += " · " + statuses;
            Ui.Text(new Rect(slot.x, y, slot.width, 15), line, 10.5f, down ? Palette.Damage : Palette.Dim, TextAnchor.MiddleCenter);

            if (targetable && Ui.Hit(slot, unit.Name, true, "대상")) _target = unit.Id;
            if (!unit.IsHero)
            {
                string about = unit.Monster.Ward > 0 ? Strings.Format("battle.monster_ward", unit.Name, unit.Monster.Text, unit.Monster.Armor, unit.Monster.Ward)
                    : Strings.Format("battle.monster", unit.Name, unit.Monster.Text, unit.Monster.Armor);
                Ui.Tip(slot, about + (unit.Intent != null && !down ? "\n" + Strings.Format("battle.next_action", unit.Intent.Skill.Name, unit.Intent.Skill.Text) : ""));
            }
        }

        static string StatusLine(Unit unit, Battle battle)
        {
            var parts = unit.Status.Keys.Select(k => k == StatusKind.Taunt ? Strings.Format("battle.status_taunt", StatusNames.Name(k, Sheets), unit.TauntArmor) : StatusNames.Name(k, Sheets)).ToList();
            if (unit.GuardedBy >= 0) parts.Add(Strings.Get("battle.guarded"));
            if (unit.GuardTarget >= 0) parts.Add(Strings.Get("battle.guarding"));
            return string.Join(" ", parts);
        }

        void DrawIntent(Rect r, Unit monster, Battle battle)
        {
            var intent = monster.Intent;
            string whom = intent.Skill.Target switch
            {
                MonsterTarget.All => Strings.Get("battle.intent_all"),
                MonsterTarget.Self => Strings.Get("battle.intent_self"),
                _ => battle.IntentTarget(monster)?.Name ?? "?",
            };
            string what = intent.Skill.Damages ? $"{intent.Skill.Name} → {whom}" : intent.Skill.Name;
            string dice = intent.Skill.Damages ? Strings.Format("battle.intent_dice", intent.Skill.Dice, intent.Skill.Power + (monster.Status.TryGetValue(StatusKind.Howl, out int howl) ? howl : 0)) : Strings.Get("battle.intent_buff");
            Ui.Fill(r, new Color(0.25f, 0.12f, 0.08f, 0.85f));
            Ui.Frame(r, Palette.WithAlpha(Palette.Intent, 0.7f), 1);
            Ui.Text(new Rect(r.x + 3, r.y, r.width - 6, r.height * 0.55f), what, 10.5f, Palette.Intent, TextAnchor.MiddleCenter, true);
            Ui.Text(new Rect(r.x + 3, r.y + r.height * 0.5f, r.width - 6, r.height * 0.5f), dice, 9.5f, Palette.Ink, TextAnchor.MiddleCenter);
            // A boss rule on this blow: a mark, a gold edge, and what it does on a long press.
            string rule = RuleIcon(intent.Skill.Effect);
            if (rule != null)
            {
                Ui.Frame(r, Palette.Gold, 2);
                Ui.Image(new Rect(r.xMax - 15, r.y + 1, 13, 13), PixelArt.Icon(rule));
                Ui.Tip(r, Strings.Get("rule." + intent.Skill.Effect) + "\n" + intent.Skill.Text);
            }
        }

        static string RuleIcon(string effect) => effect switch
        {
            "seal" => "lock",
            "erase" => "erase",
            "freeze" => "ice",
            "scorch" => "flame",
            "daze" => "daze",
            _ => null,
        };

        void DrawFloaters()
        {
            foreach (var floater in _floaters)
            {
                if (!_unitRects.TryGetValue(floater.Unit, out var at)) continue;
                float rise = floater.Age * 30;
                var color = Palette.WithAlpha(floater.Color, Mathf.Clamp01(1.3f - floater.Age));
                Ui.Text(new Rect(at.x - 40, at.y + at.height * 0.2f - rise, at.width + 80, 24), floater.Text, 18, color, TextAnchor.MiddleCenter, true);
            }
        }

        // The dice of the latest roll, over the field: each die's value, green under the chance, gold when sure.
        void DrawDice(Rect field, Battle battle)
        {
            var e = _diceEvent;
            if (e?.Check == null || _diceAge > 1.6f) return;
            var check = e.Check;
            float die = Mathf.Min(40, (field.width - 40) / Mathf.Max(4, check.Dice));
            float w = Mathf.Max(220, die * check.Dice + 8 * (check.Dice - 1) + 24);
            var r = new Rect(field.x + (field.width - w) / 2, field.y + field.height * 0.36f, w, die + 52);
            Ui.Panel(r, new Color(0.08f, 0.07f, 0.1f, 0.94f), Palette.Gold);
            var actor = battle.Unit(e.Actor);
            Ui.Text(new Rect(r.x, r.y + 2, r.width, 18), $"{actor?.Name}: {e.SkillName} · {Strings.Format("dice.under", check.Chance)}", 12, Palette.Ink, TextAnchor.MiddleCenter, true);
            bool rolling = _diceAge < 0.45f;
            float x = r.x + (r.width - (die * check.Dice + 8 * (check.Dice - 1))) / 2;
            for (int i = 0; i < check.Dice; i++)
            {
                var cell = new Rect(x + i * (die + 8), r.y + 22, die, die);
                bool sure = check.Faces[i] < 0;
                Color fill = rolling && !sure ? Palette.Button : sure ? Palette.Sure : check.Hits[i] ? Settings.Success : Settings.Fail;
                Ui.Fill(cell, fill);
                Ui.Frame(cell, Palette.Back, 2);
                string face = sure ? "★" : rolling ? Random.Range(0, 100).ToString() : Settings.Face(check.Faces[i], check.Hits[i]);
                Ui.Text(cell, face, die * 0.42f, sure || (!rolling && check.Hits[i]) ? Palette.Back : Palette.Ink, TextAnchor.MiddleCenter, true);
            }
            if (!rolling)
            {
                string result = Strings.Format(check.Perfect ? "battle.dice_perfect" : "dice.successes_of", check.Successes, check.Dice);
                Ui.Text(new Rect(r.x, r.yMax - 22, r.width, 20), result, 13, check.Perfect ? Palette.Gold : Palette.Ink, TextAnchor.MiddleCenter, true);
            }
        }

        void DrawBanner(Rect field)
        {
            if (string.IsNullOrEmpty(_banner) || _bannerAge > 2.6f) return;
            float alpha = Mathf.Clamp01(2.6f - _bannerAge);
            var r = new Rect(field.x + 10, field.y + 6, field.width - 20, 30);
            Ui.Fill(r, new Color(0, 0, 0, 0.6f * alpha));
            Ui.Text(r, _banner, 14, Palette.WithAlpha(Palette.Journal, alpha), TextAnchor.MiddleCenter, true);
        }

        // ---- The current hero's panel ----

        void PrepareSelection(Battle battle)
        {
            if (!AwaitingHero)
            {
                return;
            }
            var actor = battle.Current;
            if (_selectedFor == actor.Id && _skill != null) return;
            _selectedFor = actor.Id;
            _resolve = 0;
            _battleError = null;
            Select(battle, Hero.AttackId);
        }

        void Select(Battle battle, string skillId)
        {
            var actor = battle.Current;
            _skill = skillId;
            _resolve = 0;
            _battleError = null;
            if (IsItemOrFlee(skillId))
            {
                _target = actor.Id;
                return;
            }
            var skill = SkillDef(actor, skillId);
            int fixedTarget = Battle.FixedTarget(actor, skill);
            if (fixedTarget >= 0)
            {
                _target = fixedTarget;
                return;
            }
            var targets = battle.Targets(actor, skill);
            if (targets.Any(t => t.Id == _target)) return;
            _target = skill.Target switch
            {
                SkillTarget.Enemy => targets.OrderBy(t => t.Has(StatusKind.Mark) ? 0 : 1).ThenBy(t => t.Hp).Select(t => t.Id).DefaultIfEmpty(-1).First(),
                _ => targets.OrderBy(t => t.MaxHp > 0 ? t.Hp * 100 / t.MaxHp : 0).Select(t => t.Id).DefaultIfEmpty(-1).First(),
            };
        }

        // The skill a button stands for, as the battle sees it (the mnemonist's echo becomes the ally's skill).
        SkillDef SkillDef(Unit actor, string id) => IsItemOrFlee(id) ? null : _adventure?.Battle != null ? _adventure.Battle.SkillOf(actor, id) : actor.Hero.Skill(id);

        static bool IsItemOrFlee(string id) => BattleCommand.IsUse(id) || id == BattleCommand.Flee;

        ItemDef ItemOf(string id) => BattleCommand.IsUse(id) && _data.Items.TryGetValue(id.Substring(BattleCommand.UsePrefix.Length), out var item) ? item : null;

        bool NeedsTarget(Battle battle)
        {
            var actor = battle.Current;
            if (actor == null || _skill == null || IsItemOrFlee(_skill)) return false;
            var skill = SkillDef(actor, _skill);
            return skill != null && Battle.FixedTarget(actor, skill) < 0;
        }

        bool IsTargetable(Battle battle, Unit unit)
        {
            if (!NeedsTarget(battle)) return false;
            var actor = battle.Current;
            return battle.Targets(actor, SkillDef(actor, _skill)).Contains(unit);
        }

        // The preview of the command being set up, worked out again only when something in it or the battle changed
        // (OnGUI runs several times a frame; a preview works out the odds of every number of successes).
        SkillPreview _preview;
        (Battle, int, int, string, int, int) _previewKey;

        SkillPreview PreviewOf(Battle battle, Unit actor, int resolve)
        {
            var key = (battle, battle.Events.Count, actor.Id, _skill, _target, resolve);
            if (_preview != null && _previewKey.Equals(key)) return _preview;
            _preview = battle.Preview(new BattleCommand(actor.Id, _skill, _target, resolve));
            _previewKey = key;
            return _preview;
        }

        void DrawPanel(Rect r, Battle battle)
        {
            Ui.Panel(r);
            var inner = new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 12);
            // Auto battle: the bot gives the heroes' commands until it is switched off again.
            var autoRect = new Rect(inner.xMax - 92, inner.y, 92, 44);
            if (Ui.Button(autoRect, Strings.Get(_autoBattle ? "battle.auto_on" : "battle.auto_off"), 12.5f, !battle.Over, _autoBattle ? Look.Selected : Look.Quiet, "자동", KeyCode.A)) _autoBattle = !_autoBattle;
            Ui.Tip(autoRect, Strings.Get("battle.auto_tip"));
            if (!AwaitingHero)
            {
                string title = battle.Over ? Strings.Get(battle.State == BattleState.Won ? "battle.won" : battle.State == BattleState.Fled ? "battle.fled" : "battle.lost") : _turnActor >= 0 && !battle.Unit(_turnActor).IsHero ? Strings.Get("battle.enemy_turn") : "…";
                Ui.Text(new Rect(inner.x, inner.y, inner.width - 96, 44), title, 16, Palette.Ink, TextAnchor.MiddleLeft, true);
                DrawLog(new Rect(inner.x, inner.y + 48, inner.width, inner.height - 48));
                return;
            }

            var actor = battle.Current;
            var hero = actor.Hero;
            float y = inner.y;
            Ui.Image(new Rect(inner.x, y, 26, 26), Sprite(actor));
            Ui.Text(new Rect(inner.x + 30, y, inner.width - 126, 44), Strings.Format("common.turn", hero.Name), 16, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 46;
            DrawResources(new Rect(inner.x, y, inner.width, 16), hero);
            y += 18;
            string special = ClassLine(battle, actor);
            if (special.Length > 0)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, 15), special, 11, Palette.Journal);
                y += 16;
            }
            y += 2;

            // Skills, then the consumables the hero carries (the potion always) and running away, in two columns.
            var skills = hero.Skills.Select(s => s.Id).ToList();
            skills.Add(BattleCommand.Potion);
            foreach (var pair in hero.Consumables)
                if (pair.Key != Hero.PotionId && pair.Value > 0) skills.Add(BattleCommand.Use(pair.Key));
            if (battle.CanFlee) skills.Add(BattleCommand.Flee);
            // Two columns; three when two would push the roll button off a short panel (Docs/WebDemo.md 작은 화면).
            int columns = 2;
            float bh = Ui.Short ? 40 : 44;
            float room = inner.yMax - 46 - 4 - 44 - y;
            int rowsOf(int c) => (skills.Count + c - 1) / c;
            if (rowsOf(2) * (bh + 5) > room && inner.width >= 300) columns = 3;
            if (rowsOf(columns) * (bh + 5) > room) bh = Mathf.Max(38, room / rowsOf(columns) - 5);
            float bw = (inner.width - 6 * (columns - 1)) / columns;
            for (int i = 0; i < skills.Count; i++)
            {
                var cell = new Rect(inner.x + i % columns * (bw + 6), y + i / columns * (bh + 5), bw, bh);
                DrawSkillButton(cell, battle, actor, skills[i], Ui.Number(i));
            }
            y += (skills.Count + columns - 1) / columns * (bh + 5) + 2;

            // Tab picks the next target (Docs/WebDemo.md 키보드).
            if (NeedsTarget(battle) && Ui.Key(KeyCode.Tab))
            {
                var targets = battle.Targets(actor, SkillDef(actor, _skill)).ToList();
                if (targets.Count > 0)
                {
                    int at = targets.FindIndex(u => u.Id == _target);
                    _target = targets[(at + 1) % targets.Count].Id;
                }
            }

            // Resolve can only cover the dice not already sure (a new target may bring a mark or a combo).
            int resolveRoom = PreviewOf(battle, actor, 0).ResolveRoom;
            _resolve = Mathf.Clamp(_resolve, 0, resolveRoom);
            var preview = PreviewOf(battle, actor, _resolve);
            var skill = SkillDef(actor, _skill);
            float rollH = 46;
            room = inner.yMax - rollH - 4 - y;

            var item = ItemOf(_skill);
            string about = item != null ? item.Text + " " + Strings.Get("battle.item_quick")
                : _skill == BattleCommand.Flee ? Strings.Format("battle.flee_about", Battle.FleeDice, Battle.FleeNeed)
                : skill?.Text;
            if (skill != null && skill.PerfectText.Length > 0) about += " " + skill.PerfectText + ".";
            float aboutH = Mathf.Min(Ui.TextHeight(about ?? "", 11.5f, inner.width), 34);
            if (room > 150)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, aboutH), about, 11.5f, Palette.Journal, TextAnchor.UpperLeft, false, true);
                y += aboutH + 4;
            }

            string targetLine = item != null ? Strings.Get(item.Use.Any(u => u.Verb == "bomb") ? "battle.target_enemies" : "battle.target_self")
                : _skill == BattleCommand.Flee ? Strings.Get("battle.target_party")
                : skill == null ? ""
                : skill.Target == SkillTarget.AllEnemies ? Strings.Get("battle.target_enemies")
                : skill.Target == SkillTarget.Self ? Strings.Get("battle.target_self")
                : skill.Target == SkillTarget.AllAllies ? Strings.Get("battle.target_allies")
                : _target >= 0 && battle.Unit(_target) != null ? Strings.Format("battle.target_unit", battle.Unit(_target).Name)
                : Strings.Get("battle.target_pick");
            Ui.Text(new Rect(inner.x, y, inner.width, 16), targetLine, 11.5f, Palette.Ink);
            y += 18;

            if (preview.Ok && preview.Dice > 0 && preview.Need > 0)
            {
                float rh = 34;
                Ui.Text(new Rect(inner.x, y, 70, rh), Strings.Get("common.spend_resolve"), 12, Palette.Resolve, TextAnchor.MiddleLeft, true);
                if (Ui.Button(new Rect(inner.x + 72, y, 44, rh), "−", 18, _resolve > 0, Look.Normal, null, KeyCode.Minus)) _resolve--;
                Ui.Text(new Rect(inner.x + 118, y, 54, rh), $"{_resolve}/{preview.ResolveRoom}", 14, Palette.Ink, TextAnchor.MiddleCenter, true);
                if (Ui.Button(new Rect(inner.x + 174, y, 44, rh), "+", 18, _resolve < preview.ResolveRoom, Look.Normal, null, KeyCode.Equals)) _resolve++;
                y += rh + 4;
                Ui.Text(new Rect(inner.x, y, inner.width, 16), Strings.Format("battle.need_odds", preview.Need, Percent(preview.Odds.AtLeast(preview.Need))), 13, Palette.Ink, TextAnchor.MiddleLeft, true);
            }
            else if (preview.Ok && preview.Dice > 0)
            {
                // Resolve: − n/room +
                float rh = 34;
                Ui.Text(new Rect(inner.x, y, 70, rh), Strings.Get("common.spend_resolve"), 12, Palette.Resolve, TextAnchor.MiddleLeft, true);
                if (Ui.Button(new Rect(inner.x + 72, y, 44, rh), "−", 18, _resolve > 0, Look.Normal, null, KeyCode.Minus)) _resolve--;
                Ui.Text(new Rect(inner.x + 118, y, 54, rh), $"{_resolve}/{preview.ResolveRoom}", 14, Palette.Ink, TextAnchor.MiddleCenter, true);
                if (Ui.Button(new Rect(inner.x + 174, y, 44, rh), "+", 18, _resolve < preview.ResolveRoom, Look.Normal, null, KeyCode.Equals)) _resolve++;
                y += rh + 4;
                DrawOdds(new Rect(inner.x, y, inner.width, inner.yMax - rollH - 6 - y), preview, skill);
            }
            else if (!preview.Ok)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, 32), preview.Error, 12, Palette.Damage, TextAnchor.UpperLeft, false, true);
            }
            else if (skill != null && !skill.Rolls)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, 20), Strings.Get("battle.no_roll"), 12, Palette.Dim);
            }

            if (_battleError != null) Ui.Text(new Rect(inner.x, inner.yMax - rollH - 20, inner.width, 18), _battleError, 11, Palette.Damage);
            string go = preview.Ok && preview.Dice > 0 ? Strings.Format("battle.roll_dice", Strings.Get("battle.roll"), preview.Dice) : Strings.Get("battle.use");
            if (Ui.Button(new Rect(inner.x, inner.yMax - rollH, inner.width, rollH), go, 17, preview.Ok, Look.Primary, null, KeyCode.Space))
            {
                var command = new BattleCommand(actor.Id, _skill, _target, _resolve);
                string error = Act(() => _adventure.Fight(command));
                _battleError = error;
                if (error == null)
                {
                    _selectedFor = -1;
                    _skill = null;
                }
            }
        }

        // The class's own state for the panel: combo, souls, reagents, the song, and the seer's next dice.
        static string ClassLine(Battle battle, Unit actor)
        {
            var parts = new System.Collections.Generic.List<string>();
            switch (actor.Hero.Class.Id)
            {
                case "rogue":
                    parts.Add(Strings.Format("battle.class_combo", actor.Combo, battle.ComboMax(actor)) + (actor.Has(StatusKind.Hidden) ? " · " + Strings.Get("battle.class_hidden") : ""));
                    break;
                case "necromancer":
                    parts.Add(Strings.Format("battle.class_souls", actor.Souls));
                    break;
                case "alchemist":
                    parts.Add(Strings.Format("battle.class_reagents", actor.Hero.Reagents, actor.Hero.ReagentMax));
                    break;
                case "monk":
                    parts.Add(Strings.Format("battle.class_ki", actor.Ki));
                    break;
                case "engineer":
                    var turret = battle.Summons.FirstOrDefault(u => u.Owner == actor.Id);
                    parts.Add(turret == null ? Strings.Get("battle.turret_none") : turret.Down ? Strings.Get("battle.turret_broken") : Strings.Format("battle.turret_hp", turret.Hp, turret.MaxHp));
                    break;
                case "mnemonist":
                    var echo = battle.SkillOf(actor, Battle.EchoId);
                    parts.Add(echo.Effect == "echo" ? Strings.Get("battle.echo_none") : Strings.Format("battle.echo", echo.Name));
                    break;
            }
            if (battle.Song != Song.None) parts.Add(Strings.Get(battle.Song == Song.Courage ? "battle.song_courage" : "battle.song_rest"));
            var foresight = battle.Foresee();
            if (foresight.Count > 0) parts.Add(Strings.Format("battle.foresight", string.Join(" · ", foresight)));
            return string.Join(" · ", parts);
        }

        static void DrawResources(Rect r, Hero hero)
        {
            float x = r.x;
            // The labels make room for a longer word (English) but keep the Korean layout.
            string resolve = Strings.Get("hero.resolve_label");
            float resolveW = Mathf.Max(30, Ui.TextWidth(resolve, 11, true) + 4);
            Ui.Text(new Rect(x, r.y, resolveW + 4, r.height), resolve, 11, Palette.Resolve, TextAnchor.MiddleLeft, true);
            x += resolveW;
            for (int i = 0; i < hero.MaxResolve; i++)
            {
                var pip = new Rect(x + i * 13, r.y + 3, 10, 10);
                Ui.Fill(pip, i < hero.Resolve ? Palette.Resolve : Palette.BarBack);
                Ui.Frame(pip, Palette.Edge, 1);
            }
            x += hero.MaxResolve * 13 + 10;
            string grit = Strings.Get("hero.grit_label");
            float gritW = Mathf.Max(30, Ui.TextWidth(grit, 11, true) + 4);
            Ui.Text(new Rect(x, r.y, gritW + 4, r.height), grit, 11, Palette.Grit, TextAnchor.MiddleLeft, true);
            x += gritW;
            Ui.Bar(new Rect(x, r.y + 4, 50, 8), (float)hero.Grit / hero.GritCap, Palette.Grit);
            Ui.Text(new Rect(x + 54, r.y, 40, r.height), $"{hero.Grit}/{hero.GritCap}", 10, Palette.Dim);
        }

        void DrawSkillButton(Rect cell, Battle battle, Unit actor, string id, KeyCode key)
        {
            bool selected = _skill == id;
            if (BattleCommand.IsUse(id))
            {
                var item = ItemOf(id);
                int count = item == null ? 0 : actor.Hero.Count(item.Id);
                string name = item?.Name ?? id;
                if (Ui.Button(cell, $"{name} ({count})", 13, count > 0, selected ? Look.Selected : Look.Quiet, "소모품", key)) Select(battle, id);
                return;
            }
            if (id == BattleCommand.Flee)
            {
                if (Ui.Button(cell, $"{Strings.Get("battle.flee")} ({Stats.Name(Stat.Agility, Sheets)} {Dice.ClampChance(actor.Hero.Stat(Stat.Agility))}%)", 13, true, selected ? Look.Selected : Look.Danger, null, key)) Select(battle, id);
                return;
            }
            var skill = SkillDef(actor, id);
            int wait = battle.Cooldown(actor, skill);
            int dice = battle.DiceOf(actor, skill);
            string detail = !skill.Rolls ? Strings.Get("battle.no_dice") : skill.FixedChance > 0 ? $"{skill.FixedChance}% × {dice}" : $"{Stats.Name(skill.Stat.Value, Sheets)} {battle.ChanceOf(actor, skill)}% × {dice}";
            if (skill.CostKind == "reagent") detail += " · " + Strings.Format("battle.cost_reagent", skill.CostAmount);
            if (skill.CostKind == "soul") detail += " · " + Strings.Format("battle.cost_soul", skill.CostAmount);
            // A skill a boss took away says so, with its mark (Docs/Combat.md 보스의 고유 규칙).
            string blocked = battle.BlockedBy(actor, skill);
            if (wait > 0) detail = blocked != null ? Strings.Format("battle.blocked_" + blocked, wait) : Strings.Format("battle.cooldown", wait);
            Ui.Face(cell, selected ? Look.Selected : Look.Normal, wait == 0);
            if (blocked != null)
            {
                Ui.Frame(cell, Palette.Damage, 2);
                Ui.Image(new Rect(cell.x + 3, cell.y + 3, 14, 14), PixelArt.Icon(blocked == "seal" ? "lock" : "erase"));
            }
            Ui.Text(new Rect(cell.x + 4, cell.y + 3, cell.width - 8, cell.height * 0.5f), skill.Name, 13, wait == 0 ? Palette.Ink : Palette.Dim, TextAnchor.MiddleCenter, true);
            Ui.Text(new Rect(cell.x + 4, cell.y + cell.height * 0.5f, cell.width - 8, cell.height * 0.45f), detail, 10.5f, blocked != null ? Palette.Damage : wait == 0 ? Palette.Journal : Palette.Dim, TextAnchor.MiddleCenter);
            if (Ui.Hit(cell, skill.Name, wait == 0, "기술", key)) Select(battle, id);
            Ui.Tip(cell, (blocked != null ? Strings.Get("battle.blocked_tip_" + blocked) + "\n" : "") + skill.Text + (skill.PerfectText.Length > 0 ? " " + skill.PerfectText + "." : ""));
        }

        // The chance of each number of successes as columns, with what each gives.
        void DrawOdds(Rect r, SkillPreview preview, SkillDef skill)
        {
            var odds = preview.Odds;
            string expect = skill == null ? "battle.expect" : skill.Kind == SkillKind.Heal ? "battle.expect_heal" : skill.Kind == SkillKind.Revive ? "battle.expect_revive" : skill.Damages ? "battle.expect_damage" : "battle.expect";
            string sure = preview.Reasons.Count > 0 || preview.Resolve > 0
                ? Strings.Format("battle.sure", string.Join(" · ", preview.Reasons.Concat(preview.Resolve > 0 ? new[] { Strings.Format("hero.resolve", preview.Resolve) } : new string[0])))
                : Strings.Get("battle.sure_none");
            Ui.Text(new Rect(r.x, r.y, r.width, 15), sure, 11, Palette.Sure);
            string summary = Strings.Format(expect, preview.Expected.ToString("0.0"))
                + (skill != null && skill.Damages ? " · " + Strings.Format("battle.odds_miss", Percent(odds.None)) : "")
                + " · " + Strings.Format("battle.odds_perfect", Percent(odds.Perfect));
            Ui.Text(new Rect(r.x, r.y + 15, r.width, 15), summary, 11, Palette.Ink);
            float top = r.y + 34;
            float height = r.height - 34 - 26;
            if (height < 14) return;
            int n = odds.Dice;
            float cw = Mathf.Min(48, r.width / (n + 1));
            float x0 = r.x + (r.width - cw * (n + 1)) / 2;
            double max = odds.BySuccesses.Max();
            for (int s = 0; s <= n; s++)
            {
                double p = odds.BySuccesses[s];
                float bh = max > 0 ? (float)(p / max) * height : 0;
                var bar = new Rect(x0 + s * cw + 3, top + height - bh, cw - 6, bh);
                Ui.Fill(new Rect(x0 + s * cw + 3, top, cw - 6, height), Palette.BarBack);
                Ui.Fill(bar, s == n ? Palette.Gold : s == 0 ? Settings.Fail : Settings.Success);
                // The chance over its bar, or inside the bar's top when the bar reaches the summary line above.
                if (p >= 0.005) Ui.Text(new Rect(x0 + s * cw, Mathf.Max(top, top + height - bh - 14), cw, 14), Percent(p), 9, Palette.Ink, TextAnchor.MiddleCenter);
                Ui.Text(new Rect(x0 + s * cw, top + height + 1, cw, 12), Strings.Format("battle.successes_n", s), 9, Palette.Dim, TextAnchor.MiddleCenter);
                Ui.Text(new Rect(x0 + s * cw, top + height + 12, cw, 13), preview.ValueBySuccesses.Length > s ? preview.ValueBySuccesses[s].ToString() : "", 11, Palette.Ink, TextAnchor.MiddleCenter, true);
            }
        }

        static string Percent(double p) => p >= 0.995 ? "100%" : p < 0.005 && p > 0 ? "<1%" : $"{p * 100:0}%";

        void DrawLog(Rect r)
        {
            float line = 16;
            int count = Mathf.Max(0, Mathf.FloorToInt(r.height / line));
            int start = Mathf.Max(0, _log.Count - count);
            for (int i = start; i < _log.Count; i++)
            {
                bool latest = i == _log.Count - 1;
                Ui.Text(new Rect(r.x, r.y + (i - start) * line, r.width, line), _log[i], 11, latest ? Palette.Ink : Palette.Dim);
            }
        }
    }
}
