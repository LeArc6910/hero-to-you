using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEngine;

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
                    if (_speed > 0) Sounds.Play(e.Perfect ? Sound.Perfect : Sound.Dice);
                    break;
                case BattleEventKind.Damage:
                    _shownHp[e.Target] = Mathf.Max(0, _shownHp[e.Target] - e.Amount);
                    _hitAt[e.Target] = _clock;
                    Float(e.Target, "-" + e.Amount + (e.Perfect ? "!" : ""), Palette.Damage);
                    if (_speed > 0) Sounds.Play(Sound.Hit);
                    break;
                case BattleEventKind.Miss:
                    Float(e.Target, "빗나감", Palette.Dim);
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
                    Float(e.Target, "일어남", Palette.Heal);
                    break;
                case BattleEventKind.Down:
                    _shownHp[e.Target] = 0;
                    if (_speed > 0) Sounds.Play(Sound.Down);
                    break;
                case BattleEventKind.StatusOn:
                    Float(e.Target, StatusFloat(e), Palette.Gold);
                    break;
                case BattleEventKind.ResolveGained:
                    Float(e.Actor, "결의 +" + e.Amount, Palette.Resolve);
                    break;
                case BattleEventKind.Guard:
                    Float(e.Target, "수호", Palette.Gold);
                    break;
                case BattleEventKind.Phase:
                case BattleEventKind.Victory:
                case BattleEventKind.Defeat:
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

        static string StatusFloat(BattleEvent e) => e.Status switch
        {
            StatusKind.Taunt => "도발",
            StatusKind.Howl => "울부짖음",
            _ when e.Text != null && e.Text.Contains("축복") => "축복",
            _ when e.Text != null && e.Text.Contains("행운") => "행운",
            _ => StatusNames.Name(e.Status),
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
            Ui.Fill(new Rect(field.x, field.y + field.height * 0.62f, field.width, field.height * 0.38f), Palette.WithAlpha(Palette.Ground, 0.6f));
            DrawUnits(field, battle);
            DrawFloaters();
            DrawDice(field, battle);
            DrawBanner(field);
            DrawPanel(panel, battle);
        }

        void DrawTimeline(Rect r, Battle battle)
        {
            Ui.Panel(r);
            Ui.Text(new Rect(r.x + 6, r.y, 34, r.height), "순서", 12, Palette.Dim);
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
            if (infoW > 120) Ui.Text(new Rect(info.x + 90, info.y, 60, r.height), _adventure.BossBattle ? "보스" : $"{_adventure.Day}일째", 12, _adventure.BossBattle ? Palette.Damage : Palette.Dim, TextAnchor.MiddleRight);
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
            float shake = _hitAt.TryGetValue(unit.Id, out float at) && _clock - at < 0.25f ? Mathf.Sin((_clock - at) * 80) * 3 : 0;
            var spriteRect = new Rect(slot.x + (slot.width - sprite) / 2 + shake, slot.y + intentH + 2, sprite, sprite);
            _unitRects[unit.Id] = spriteRect;
            Ui.Fill(new Rect(spriteRect.x + sprite * 0.15f, spriteRect.yMax - sprite * 0.06f, sprite * 0.7f, sprite * 0.08f), new Color(0, 0, 0, 0.35f));
            Ui.Image(spriteRect, Sprite(unit), down ? new Color(0.4f, 0.4f, 0.45f, 0.45f) : (Color?)null);

            if (!unit.IsHero && !down && unit.Intent != null && !battle.Over) DrawIntent(new Rect(slot.x + 2, slot.y, slot.width - 4, intentH - 2), unit, battle);

            float y = spriteRect.yMax + 2;
            Ui.Text(new Rect(slot.x, y, slot.width, 16), unit.Name + (unit.IsHero ? $" Lv{unit.Hero.Level}" : ""), 12, down ? Palette.Dim : Palette.Ink, TextAnchor.MiddleCenter, true);
            y += 16;
            float barW = Mathf.Min(slot.width - 12, 150);
            var bar = new Rect(slot.x + (slot.width - barW) / 2, y + 1, barW, 7);
            Ui.Bar(bar, unit.MaxHp > 0 ? (float)hp / unit.MaxHp : 0, unit.IsHero ? Palette.HpHero : Palette.Hp);
            y += 9;
            string line = down ? "쓰러짐" : $"{hp}/{unit.MaxHp}";
            if (unit.IsHero && !down) line += $" · 결의 {unit.Hero.Resolve}" + (unit.Hero.Blessing + unit.Hero.Luck > 0 ? $" · 확정+{unit.Hero.Blessing + unit.Hero.Luck}" : "");
            string statuses = StatusLine(unit, battle);
            if (statuses.Length > 0) line += " · " + statuses;
            Ui.Text(new Rect(slot.x, y, slot.width, 15), line, 10.5f, down ? Palette.Damage : Palette.Dim, TextAnchor.MiddleCenter);

            if (targetable && Ui.Hit(slot, unit.Name, true, "대상")) _target = unit.Id;
        }

        static string StatusLine(Unit unit, Battle battle)
        {
            var parts = unit.Status.Keys.Select(k => k == StatusKind.Taunt ? $"도발(방어+{unit.TauntArmor})" : StatusNames.Name(k)).ToList();
            if (unit.GuardedBy >= 0) parts.Add("수호받음");
            if (unit.GuardTarget >= 0) parts.Add("수호 중");
            return string.Join(" ", parts);
        }

        void DrawIntent(Rect r, Unit monster, Battle battle)
        {
            var intent = monster.Intent;
            string whom = intent.Skill.Target switch
            {
                MonsterTarget.All => "모두",
                MonsterTarget.Self => "자신",
                _ => battle.IntentTarget(monster)?.Name ?? "?",
            };
            string what = intent.Skill.Damages ? $"{intent.Skill.Name} → {whom}" : intent.Skill.Name;
            string dice = intent.Skill.Damages ? $"{intent.Skill.Dice}개 × {intent.Skill.Power + (monster.Status.TryGetValue(StatusKind.Howl, out int howl) ? howl : 0)}" : "강화";
            Ui.Fill(r, new Color(0.25f, 0.12f, 0.08f, 0.85f));
            Ui.Frame(r, Palette.WithAlpha(Palette.Intent, 0.7f), 1);
            Ui.Text(new Rect(r.x + 3, r.y, r.width - 6, r.height * 0.55f), what, 10.5f, Palette.Intent, TextAnchor.MiddleCenter, true);
            Ui.Text(new Rect(r.x + 3, r.y + r.height * 0.5f, r.width - 6, r.height * 0.5f), dice, 9.5f, Palette.Ink, TextAnchor.MiddleCenter);
        }

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
            Ui.Text(new Rect(r.x, r.y + 2, r.width, 18), $"{actor?.Name}: {e.SkillName} · {check.Chance}% 아래면 성공", 12, Palette.Ink, TextAnchor.MiddleCenter, true);
            bool rolling = _diceAge < 0.45f;
            float x = r.x + (r.width - (die * check.Dice + 8 * (check.Dice - 1))) / 2;
            for (int i = 0; i < check.Dice; i++)
            {
                var cell = new Rect(x + i * (die + 8), r.y + 22, die, die);
                bool sure = check.Faces[i] < 0;
                Color fill = rolling && !sure ? Palette.Button : sure ? Palette.Sure : check.Hits[i] ? Palette.Success : Palette.Fail;
                Ui.Fill(cell, fill);
                Ui.Frame(cell, Palette.Back, 2);
                string face = sure ? "★" : rolling ? Random.Range(0, 100).ToString() : check.Faces[i].ToString();
                Ui.Text(cell, face, die * 0.42f, sure || (!rolling && check.Hits[i]) ? Palette.Back : Palette.Ink, TextAnchor.MiddleCenter, true);
            }
            if (!rolling)
            {
                string result = check.Perfect ? $"완벽! 성공 {check.Successes}/{check.Dice}" : $"성공 {check.Successes}/{check.Dice}";
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
            var skill = SkillDef(actor, skillId);
            if (skillId == BattleCommand.Potion)
            {
                _target = actor.Id;
                return;
            }
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

        static SkillDef SkillDef(Unit actor, string id) => actor.Hero.Skill(id);

        bool NeedsTarget(Battle battle)
        {
            var actor = battle.Current;
            if (actor == null || _skill == null || _skill == BattleCommand.Potion) return false;
            var skill = SkillDef(actor, _skill);
            return skill != null && Battle.FixedTarget(actor, skill) < 0;
        }

        bool IsTargetable(Battle battle, Unit unit)
        {
            if (!NeedsTarget(battle)) return false;
            var actor = battle.Current;
            return battle.Targets(actor, SkillDef(actor, _skill)).Contains(unit);
        }

        void DrawPanel(Rect r, Battle battle)
        {
            Ui.Panel(r);
            var inner = new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 12);
            if (!AwaitingHero)
            {
                string title = battle.Over ? (battle.State == BattleState.Won ? "승리!" : "패배…") : _turnActor >= 0 && !battle.Unit(_turnActor).IsHero ? "적의 차례" : "…";
                Ui.Text(new Rect(inner.x, inner.y, inner.width, 22), title, 16, Palette.Ink, TextAnchor.MiddleLeft, true);
                DrawLog(new Rect(inner.x, inner.y + 26, inner.width, inner.height - 26));
                return;
            }

            var actor = battle.Current;
            var hero = actor.Hero;
            float y = inner.y;
            Ui.Image(new Rect(inner.x, y, 26, 26), Sprite(actor));
            Ui.Text(new Rect(inner.x + 30, y, inner.width - 30, 26), $"{hero.Name}의 차례", 16, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 28;
            DrawResources(new Rect(inner.x, y, inner.width, 16), hero);
            y += 18;
            string special = ClassLine(battle, actor);
            if (special.Length > 0)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, 15), special, 11, Palette.Journal);
                y += 16;
            }
            y += 2;

            // Skills (and the potion) in two columns.
            var skills = hero.Skills.Select(s => s.Id).ToList();
            skills.Add(BattleCommand.Potion);
            int columns = 2;
            float bh = Ui.Short ? 40 : 44;
            float bw = (inner.width - 6) / columns;
            for (int i = 0; i < skills.Count; i++)
            {
                var cell = new Rect(inner.x + i % columns * (bw + 6), y + i / columns * (bh + 5), bw, bh);
                DrawSkillButton(cell, battle, actor, skills[i]);
            }
            y += (skills.Count + columns - 1) / columns * (bh + 5) + 2;

            // Resolve can only cover the dice not already sure (a new target may bring a mark or a combo).
            int resolveRoom = battle.Preview(new BattleCommand(actor.Id, _skill, _target, 0)).ResolveRoom;
            _resolve = Mathf.Clamp(_resolve, 0, resolveRoom);
            var preview = battle.Preview(new BattleCommand(actor.Id, _skill, _target, _resolve));
            var skill = SkillDef(actor, _skill);
            float rollH = 46;
            float room = inner.yMax - rollH - 4 - y;

            string about = _skill == BattleCommand.Potion ? $"생명력 {Battle.PotionHeal} 회복. 다음 차례가 빨리 온다." : skill?.Text;
            if (skill != null && skill.PerfectText.Length > 0) about += " " + skill.PerfectText + ".";
            float aboutH = Mathf.Min(Ui.TextHeight(about ?? "", 11.5f, inner.width), 34);
            if (room > 150)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, aboutH), about, 11.5f, Palette.Journal, TextAnchor.UpperLeft, false, true);
                y += aboutH + 4;
            }

            string targetLine = _skill == BattleCommand.Potion ? "대상: 자신" : skill == null ? "" : skill.Target == SkillTarget.AllEnemies ? "대상: 모든 적" : skill.Target == SkillTarget.Self ? "대상: 자신" : _target >= 0 && battle.Unit(_target) != null ? $"대상: {battle.Unit(_target).Name} (다른 대상을 눌러 바꾸기)" : "대상을 눌러 고르세요";
            Ui.Text(new Rect(inner.x, y, inner.width, 16), targetLine, 11.5f, Palette.Ink);
            y += 18;

            if (preview.Ok && preview.Dice > 0)
            {
                // Resolve: − n/room +
                float rh = 34;
                Ui.Text(new Rect(inner.x, y, 70, rh), "결의 쓰기", 12, Palette.Resolve, TextAnchor.MiddleLeft, true);
                if (Ui.Button(new Rect(inner.x + 72, y, 44, rh), "−", 18, _resolve > 0)) _resolve--;
                Ui.Text(new Rect(inner.x + 118, y, 54, rh), $"{_resolve}/{preview.ResolveRoom}", 14, Palette.Ink, TextAnchor.MiddleCenter, true);
                if (Ui.Button(new Rect(inner.x + 174, y, 44, rh), "+", 18, _resolve < preview.ResolveRoom)) _resolve++;
                y += rh + 4;
                DrawOdds(new Rect(inner.x, y, inner.width, inner.yMax - rollH - 6 - y), preview, skill);
            }
            else if (!preview.Ok)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, 32), preview.Error, 12, Palette.Damage, TextAnchor.UpperLeft, false, true);
            }
            else if (skill != null && !skill.Rolls)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, 20), "주사위 없이 바로 된다.", 12, Palette.Dim);
            }

            if (_battleError != null) Ui.Text(new Rect(inner.x, inner.yMax - rollH - 20, inner.width, 18), _battleError, 11, Palette.Damage);
            string go = preview.Ok && preview.Dice > 0 ? $"굴리기 ({preview.Dice}개)" : "사용";
            if (Ui.Button(new Rect(inner.x, inner.yMax - rollH, inner.width, rollH), go, 17, preview.Ok, Look.Primary))
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
                    parts.Add($"연계 {actor.Combo}/{battle.ComboMax(actor)}" + (actor.Has(StatusKind.Hidden) ? " · 숨음(기습 +1)" : ""));
                    break;
                case "necromancer":
                    parts.Add($"영혼 {actor.Souls}");
                    break;
                case "alchemist":
                    parts.Add($"재료 {actor.Hero.Reagents}/{actor.Hero.ReagentMax}");
                    break;
            }
            if (battle.Song != Song.None) parts.Add(battle.Song == Song.Courage ? "용기의 노래" : "안식의 노래");
            var foresight = battle.Foresee();
            if (foresight.Count > 0) parts.Add("다음 주사위: " + string.Join(" · ", foresight));
            return string.Join(" · ", parts);
        }

        static void DrawResources(Rect r, Hero hero)
        {
            float x = r.x;
            Ui.Text(new Rect(x, r.y, 34, r.height), "결의", 11, Palette.Resolve, TextAnchor.MiddleLeft, true);
            x += 30;
            for (int i = 0; i < hero.MaxResolve; i++)
            {
                var pip = new Rect(x + i * 13, r.y + 3, 10, 10);
                Ui.Fill(pip, i < hero.Resolve ? Palette.Resolve : Palette.BarBack);
                Ui.Frame(pip, Palette.Edge, 1);
            }
            x += hero.MaxResolve * 13 + 10;
            Ui.Text(new Rect(x, r.y, 34, r.height), "끈기", 11, Palette.Grit, TextAnchor.MiddleLeft, true);
            x += 30;
            Ui.Bar(new Rect(x, r.y + 4, 50, 8), (float)hero.Grit / hero.GritCap, Palette.Grit);
            Ui.Text(new Rect(x + 54, r.y, 40, r.height), $"{hero.Grit}/{hero.GritCap}", 10, Palette.Dim);
        }

        void DrawSkillButton(Rect cell, Battle battle, Unit actor, string id)
        {
            bool selected = _skill == id;
            if (id == BattleCommand.Potion)
            {
                bool usable = actor.Hero.Potions > 0;
                if (Ui.Button(cell, $"물약 ({actor.Hero.Potions})", 13, usable, selected ? Look.Selected : Look.Quiet)) Select(battle, id);
                return;
            }
            var skill = SkillDef(actor, id);
            int wait = battle.Cooldown(actor, skill);
            int dice = battle.DiceOf(actor, skill);
            string detail = !skill.Rolls ? "주사위 없음" : skill.FixedChance > 0 ? $"{skill.FixedChance}% × {dice}" : $"{Stats.Name(skill.Stat.Value)} {battle.ChanceOf(actor, skill)}% × {dice}";
            if (skill.CostKind == "reagent") detail += $" · 재료 {skill.CostAmount}";
            if (skill.CostKind == "soul") detail += $" · 영혼 {skill.CostAmount}";
            if (wait > 0) detail = $"대기 {wait}";
            Ui.Face(cell, selected ? Look.Selected : Look.Normal, wait == 0);
            Ui.Text(new Rect(cell.x + 4, cell.y + 3, cell.width - 8, cell.height * 0.5f), skill.Name, 13, wait == 0 ? Palette.Ink : Palette.Dim, TextAnchor.MiddleCenter, true);
            Ui.Text(new Rect(cell.x + 4, cell.y + cell.height * 0.5f, cell.width - 8, cell.height * 0.45f), detail, 10.5f, wait == 0 ? Palette.Journal : Palette.Dim, TextAnchor.MiddleCenter);
            if (Ui.Hit(cell, skill.Name, wait == 0, "기술")) Select(battle, id);
        }

        // The chance of each number of successes as columns, with what each gives.
        void DrawOdds(Rect r, SkillPreview preview, SkillDef skill)
        {
            var odds = preview.Odds;
            string unit = skill == null ? "" : skill.Kind == SkillKind.Heal ? " 회복" : skill.Kind == SkillKind.Revive ? " 생명력" : skill.Damages ? " 피해" : "";
            string sure = preview.Reasons.Count > 0 || preview.Resolve > 0
                ? "확정: " + string.Join(" · ", preview.Reasons.Concat(preview.Resolve > 0 ? new[] { $"결의 {preview.Resolve}" } : new string[0]))
                : "확정 성공 없음";
            Ui.Text(new Rect(r.x, r.y, r.width, 15), sure, 11, Palette.Sure);
            string summary = skill != null && skill.Damages
                ? $"예상{unit} {preview.Expected:0.0} · 빗나감 {Percent(odds.None)} · 완벽 {Percent(odds.Perfect)}"
                : $"예상{unit} {preview.Expected:0.0} · 완벽 {Percent(odds.Perfect)}";
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
                Ui.Fill(bar, s == n ? Palette.Gold : s == 0 ? Palette.Fail : Palette.Success);
                if (p >= 0.005) Ui.Text(new Rect(x0 + s * cw, top + height - bh - 14, cw, 14), Percent(p), 9, Palette.Ink, TextAnchor.MiddleCenter);
                Ui.Text(new Rect(x0 + s * cw, top + height + 1, cw, 12), $"{s}개", 9, Palette.Dim, TextAnchor.MiddleCenter);
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
