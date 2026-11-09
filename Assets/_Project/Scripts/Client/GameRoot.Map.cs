using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEngine;
using Terrain = Game.Core.Terrain;

namespace Game.Client
{
    // The chapter map (Docs/Map.md): hexes with their terrain and what they hold, the heroes on them, the day and the
    // rift, and the current hero's panel (roll, pick a hex, move; camp; stay; gear). A hex is picked with one tap
    // and moved to with [이동], so small hexes on a phone are not a problem. A map bigger than its area is dragged.
    public sealed partial class GameRoot
    {
        Hex? _picked;
        Vector2 _mapPan;
        bool _mapDragging;
        Vector2 _mapDragFrom;
        Vector2 _mapPanFrom;
        bool _mapDragged;
        int _panFor = -1;
        bool _gearOpen;

        static readonly Dictionary<Terrain, Color> TerrainColors = new Dictionary<Terrain, Color>
        {
            [Terrain.Plains] = Palette.Hex("#5F6B45"),
            [Terrain.Forest] = Palette.Hex("#3B5236"),
            [Terrain.Swamp] = Palette.Hex("#45574A"),
            [Terrain.Road] = Palette.Hex("#7C6C50"),
            [Terrain.Ruins] = Palette.Hex("#5E5A57"),
            [Terrain.Water] = Palette.Hex("#2A425C"),
            [Terrain.Mountain] = Palette.Hex("#4A4550"),
        };

        void DrawMap()
        {
            var a = _adventure;
            DrawPartyStrip(new Rect(4, 4, Ui.Width - 8, StripHeight - 6));
            float infoY = StripHeight;
            DrawDayLine(new Rect(6, infoY, Ui.Width - 12, 18));
            float top = infoY + 20;

            Rect area;
            Rect panel;
            if (Ui.Portrait)
            {
                float panelH = Mathf.Clamp(Ui.Height * 0.3f, 200, 260);
                area = new Rect(4, top, Ui.Width - 8, Ui.Height - top - panelH - 6);
                panel = new Rect(4, area.yMax + 4, Ui.Width - 8, panelH);
            }
            else
            {
                float pw = Mathf.Clamp(Ui.Width * 0.3f, 250, 330);
                area = new Rect(4, top, Ui.Width - pw - 12, Ui.Height - top - 4);
                panel = new Rect(area.xMax + 4, top, pw, Ui.Height - top - 4);
            }

            float size = HexSize(area, a.Map);
            var origin = MapOrigin(area, a.Map, size);
            Ui.Fill(area, Palette.WithAlpha(Palette.Back, 0.6f));
            // Dragging is read before the hexes' buttons, so a drag that starts on a hex moves the map instead.
            if (!_gearOpen && a.LevelUp == null) DragMap(area);
            Ui.BeginArea(area);
            var local = new Rect(0, 0, area.width, area.height);
            var reach = _gearOpen || a.LevelUp != null ? new Dictionary<Hex, int>() : a.Reachable();
            DrawHexes(local, origin, size, reach);
            Ui.EndArea();
            DrawMapPanel(panel, reach);
        }

        void DrawDayLine(Rect r)
        {
            var a = _adventure;
            string time = $"{a.Day}일째 {a.TimeName}" + (a.Night ? " (밤: 이동 −1)" : "");
            Ui.Text(new Rect(r.x, r.y, 170, r.height), time, 12, Palette.Ink, TextAnchor.MiddleLeft, true);
            float barX = r.x + 170;
            float barW = Mathf.Min(140, r.width * 0.2f);
            Ui.Text(new Rect(barX, r.y, 36, r.height), "균열", 11, Palette.Damage, TextAnchor.MiddleLeft, true);
            Ui.Bar(new Rect(barX + 34, r.y + 5, barW, 8), (float)a.Doom / Adventure.DoomMax, Palette.Damage);
            Ui.Text(new Rect(barX + barW + 38, r.y, 50, r.height), $"{a.Doom}/{Adventure.DoomMax}", 11, Palette.Dim);
            float ox = barX + barW + 84;
            if (r.xMax - ox > 120) Ui.Text(new Rect(ox, r.y, r.xMax - ox, r.height), "목표: " + a.Objective, 11, Palette.Journal);
        }

        // Hexes as big as fit, but not smaller than a finger can pick (then the map is dragged).
        static float HexSize(Rect area, WorldMap map)
        {
            float fitW = area.width / (1.7320508f * (map.Width + 0.5f));
            float fitH = area.height / (1.5f * (map.Height - 1) + 2f);
            float fit = Mathf.Min(fitW, fitH);
            float least = Ui.Touch ? 19 : 15;
            return Mathf.Clamp(fit, least, 40);
        }

        Vector2 MapOrigin(Rect area, WorldMap map, float size)
        {
            float w = 1.7320508f * size * (map.Width + 0.5f);
            float h = size * (1.5f * (map.Height - 1) + 2f);
            // Centred when it fits; otherwise it follows the drag, starting on the hero whose turn it is.
            if (_panFor != _adventure.MapHero + _adventure.Day * 10)
            {
                _panFor = _adventure.MapHero + _adventure.Day * 10;
                var hero = HexCenter(_adventure.Positions[_adventure.MapHero], size);
                _mapPan = new Vector2(area.width / 2 - hero.x, area.height / 2 - hero.y);
            }
            float x = w <= area.width ? (area.width - w) / 2 : Mathf.Clamp(_mapPan.x, area.width - w - 8, 8);
            float y = h <= area.height ? (area.height - h) / 2 : Mathf.Clamp(_mapPan.y, area.height - h - 8, 8);
            _mapPan = new Vector2(x, y);
            return _mapPan;
        }

        static Vector2 HexCenter(Hex hex, float size) =>
            new Vector2(1.7320508f * size * (hex.Col + 0.5f * (hex.Row & 1)) + 0.866f * size, size * (1.5f * hex.Row + 1f));

        void DragMap(Rect area)
        {
            var e = Event.current;
            var p = Ui.Pointer;
            switch (e.type)
            {
                case EventType.MouseDown when area.Contains(p):
                    _mapDragging = true;
                    _mapDragged = false;
                    _mapDragFrom = p;
                    _mapPanFrom = _mapPan;
                    break;
                case EventType.MouseDrag when _mapDragging:
                    if ((p - _mapDragFrom).magnitude > 8) _mapDragged = true;
                    if (_mapDragged)
                    {
                        _mapPan = _mapPanFrom + (p - _mapDragFrom);
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    _mapDragging = false;
                    break;
            }
        }

        void DrawHexes(Rect local, Vector2 origin, float size, Dictionary<Hex, int> reach)
        {
            var a = _adventure;
            var fill = PixelArt.Hex(false);
            var rim = PixelArt.Hex(true);
            float w = 1.7320508f * size;
            float h = 2 * size;
            foreach (var tile in a.Map.Tiles)
            {
                var c = origin + HexCenter(tile.Hex, size);
                var rect = new Rect(c.x - w / 2, c.y - h / 2, w, h);
                if (!local.Overlaps(rect)) continue;
                Ui.Image(rect, fill, TerrainColors[tile.Terrain]);
                string mark = TerrainMark(tile.Terrain);
                if (mark != null && tile.Site == Site.None && tile.Content == Content.None)
                    Ui.Image(new Rect(c.x - size * 0.45f, c.y - size * 0.4f, size * 0.9f, size * 0.9f), PixelArt.Icon(mark), new Color(1, 1, 1, 0.55f));
                DrawHexContent(tile, c, size);
                bool reachable = reach.ContainsKey(tile.Hex);
                if (reachable) Ui.Image(rect, fill, new Color(1, 1, 1, 0.18f));
                if (reachable) Ui.Image(rect, rim, Palette.WithAlpha(Palette.Gold, 0.7f));
                if (_picked == tile.Hex) Ui.Image(rect, rim, Palette.Accent);
            }
            // Heroes on top, side by side when they share a hex.
            for (int i = 0; i < a.Heroes.Count; i++)
            {
                var hero = a.Heroes[i];
                var at = a.Positions[i];
                var together = Enumerable.Range(0, a.Heroes.Count).Where(j => a.Positions[j] == at).ToList();
                int slot = together.IndexOf(i);
                float token = size * (together.Count > 1 ? 0.95f : 1.25f);
                var c = origin + HexCenter(at, size);
                float dx = together.Count > 1 ? (slot - (together.Count - 1) / 2f) * token * 0.6f : 0;
                var r = new Rect(c.x - token / 2 + dx, c.y - token * 0.62f, token, token);
                if (i == a.MapHero) Ui.Fill(new Rect(r.x + token * 0.2f, r.yMax - 2, token * 0.6f, 3), Palette.Gold);
                Ui.Image(r, PixelArt.Hero(hero.Class.Id, hero.Class.Color), hero.Down ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : (Color?)null);
            }
            // Picking: one tap picks a reachable hex (the panel shows it), [이동] goes.
            foreach (var hex in reach.Keys)
            {
                var c = origin + HexCenter(hex, size);
                var hit = new Rect(c.x - size * 0.75f, c.y - size * 0.75f, size * 1.5f, size * 1.5f);
                if (Ui.Hit(hit, hex.ToString(), true, "칸") && !_mapDragged) _picked = hex;
            }
        }

        static string TerrainMark(Terrain terrain) => terrain switch
        {
            Terrain.Forest => "tree",
            Terrain.Mountain => "peak",
            Terrain.Water => "wave",
            Terrain.Swamp => "reeds",
            Terrain.Ruins => "stones",
            _ => null,
        };

        void DrawHexContent(MapTile tile, Vector2 c, float size)
        {
            Texture2D icon = null;
            switch (tile.Content)
            {
                case Content.Enemy:
                    var monster = _data.Encounters[tile.Encounter].Monsters[0];
                    icon = PixelArt.Monster(monster.Id, monster.Color);
                    break;
                case Content.Event:
                    icon = PixelArt.Icon("event");
                    break;
                case Content.Treasure:
                    icon = PixelArt.Icon("chest");
                    break;
                case Content.Herb:
                    icon = PixelArt.Icon("herb");
                    break;
            }
            if (icon == null)
            {
                icon = tile.Site switch
                {
                    Site.Town => PixelArt.Icon("town"),
                    Site.Shrine => PixelArt.Icon("shrine"),
                    Site.Quest => PixelArt.Icon("quest"),
                    Site.Boss => PixelArt.Icon("boss"),
                    _ => null,
                };
            }
            if (icon == null) return;
            bool faded = tile.Used && tile.Site != Site.Town;
            float s = size * (tile.Content == Content.Enemy ? 1.15f : 1.1f);
            Ui.Image(new Rect(c.x - s / 2, c.y - s / 2, s, s), icon, faded ? new Color(1, 1, 1, 0.35f) : (Color?)null);
        }

        // What a hex holds, in words, for the panel.
        string Describe(Hex hex)
        {
            var tile = _adventure.Map.Tile(hex);
            string what = tile.Content switch
            {
                Content.Enemy => "적: " + string.Join(", ", _data.Encounters[tile.Encounter].Monsters.Select(m => m.Name)),
                Content.Event => "무슨 일이 일어난다(?)",
                Content.Treasure => "보물",
                Content.Herb => "약초",
                _ => tile.Site switch
                {
                    Site.Town => "마을: 여관, 상점, 장로",
                    Site.Shrine => tile.Used ? "성소(이미 다녀감)" : "성소",
                    Site.Quest => tile.Used ? "리엔의 비석(읽음)" : "리엔의 비석",
                    Site.Boss => "봉인석: 잿빛 파수꾼",
                    Site.Start => "출발한 곳",
                    _ => "",
                },
            };
            var downed = Enumerable.Range(0, _adventure.Heroes.Count).Where(i => _adventure.Heroes[i].Down && _adventure.Positions[i] == hex).ToList();
            if (downed.Count > 0) what += (what.Length > 0 ? " · " : "") + "쓰러진 " + string.Join(", ", downed.Select(i => _adventure.Heroes[i].Name)) + "(부축하면 일어난다)";
            string terrain = TerrainInfo.Name(tile.Terrain);
            return what.Length > 0 ? $"{terrain} · {what}" : terrain;
        }

        void DrawMapPanel(Rect r, Dictionary<Hex, int> reach)
        {
            var a = _adventure;
            var hero = a.Current;
            Ui.Panel(r);
            var inner = new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 12);
            float y = inner.y;
            Ui.Image(new Rect(inner.x, y, 26, 26), PixelArt.Hero(hero.Class.Id, hero.Class.Color));
            Ui.Text(new Rect(inner.x + 30, y, inner.width - 30, 26), $"{hero.Name}의 차례", 16, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 30;
            float bh = Ui.Short ? 40 : 44;
            float half = (inner.width - 6) / 2;

            if (a.LevelUp != null)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, 20), "레벨업 강화를 먼저 고르세요.", 12, Palette.Gold);
                return;
            }

            if (a.MovePoints < 0)
            {
                if (Ui.Button(new Rect(inner.x, y, inner.width, bh), $"이동 굴리기 (민첩 {Dice.ClampChance(hero.Stat(Stat.Agility))}% × {Adventure.MoveDice})", 15, true, Look.Primary))
                {
                    _picked = null;
                    if (Act(a.RollMove) == null) Sounds.Play(Sound.Dice);
                }
                y += bh + 6;
                if (Ui.Button(new Rect(inner.x, y, half, bh), "야영", 14, true, Look.Quiet)) Act(a.Camp);
                if (Ui.Button(new Rect(inner.x + half + 6, y, half, bh), "장비·가방", 14, true, Look.Quiet)) _gearOpen = true;
                y += bh + 4;
                Ui.Text(new Rect(inner.x, y, inner.width, 30), "이동력 = 2 + 성공 수. 야영은 움직이지 않고 생명력 20%와 결의 1을 회복한다.", 10.5f, Palette.Dim, TextAnchor.UpperLeft, false, true);
                y += 32;
            }
            else
            {
                DrawMoveDice(new Rect(inner.x, y, inner.width, 30), a.MoveRoll);
                y += 32;
                Ui.Text(new Rect(inner.x, y, inner.width, 18), $"이동력 {a.MovePoints} · 갈 칸을 누르세요 (금색 테두리)", 12, Palette.Ink);
                y += 20;
                if (_picked.HasValue && reach.TryGetValue(_picked.Value, out int cost))
                {
                    string about = Describe(_picked.Value);
                    float ah = Mathf.Min(Ui.TextHeight(about, 12, inner.width), 48);
                    Ui.Text(new Rect(inner.x, y, inner.width, ah), about, 12, Palette.Journal, TextAnchor.UpperLeft, false, true);
                    y += ah + 2;
                    Ui.Text(new Rect(inner.x, y, inner.width, 16), $"이동 비용 {cost}", 11, Palette.Dim);
                    y += 18;
                    if (Ui.Button(new Rect(inner.x, y, inner.width, bh), "이동", 16, true, Look.Primary))
                    {
                        var to = _picked.Value;
                        _picked = null;
                        Act(() => a.Move(to));
                    }
                    y += bh + 6;
                }
                if (Ui.Button(new Rect(inner.x, y, half, bh), "여기 머물기", 14, true, Look.Quiet))
                {
                    _picked = null;
                    Act(a.Stay);
                }
                if (Ui.Button(new Rect(inner.x + half + 6, y, half, bh), "장비·가방", 14, true, Look.Quiet)) _gearOpen = true;
                y += bh + 4;
            }
            if (_actError != null)
            {
                Ui.Text(new Rect(inner.x, y, inner.width, 30), _actError, 11, Palette.Damage, TextAnchor.UpperLeft, false, true);
                y += 32;
            }
            // What just happened (a new day, a hero helped up, a rest).
            var lines = a.Report.Concat(_toast != null ? new[] { _toast } : new string[0]).ToList();
            foreach (string line in lines)
            {
                if (y > inner.yMax - 14) break;
                float lh = Mathf.Min(Ui.TextHeight(line, 11, inner.width), 30);
                Ui.Text(new Rect(inner.x, y, inner.width, lh), line, 11, Palette.Journal, TextAnchor.UpperLeft, false, true);
                y += lh + 2;
            }
            if (Ui.Portrait || y > inner.yMax - 40) return;
            Ui.Text(new Rect(inner.x, inner.yMax - 34, inner.width, 34), "?: 사건 · 상자: 보물 · 풀: 약초 · 몬스터: 적 무리 · 집: 마을 · 비석: 리엔의 비석 · 붉은 돌: 봉인석", 9.5f, Palette.Dim, TextAnchor.LowerLeft, false, true);
        }

        static void DrawMoveDice(Rect r, CheckResult roll)
        {
            if (roll == null) return;
            float die = Mathf.Min(28, r.height);
            for (int i = 0; i < roll.Dice; i++)
            {
                var cell = new Rect(r.x + i * (die + 4), r.y, die, die);
                bool sure = roll.Faces[i] < 0;
                Ui.Fill(cell, sure ? Palette.Sure : roll.Hits[i] ? Palette.Success : Palette.Fail);
                Ui.Text(cell, sure ? "★" : roll.Faces[i].ToString(), 12, sure || roll.Hits[i] ? Palette.Back : Palette.Ink, TextAnchor.MiddleCenter, true);
            }
            Ui.Text(new Rect(r.x + roll.Dice * (die + 4) + 4, r.y, r.width - roll.Dice * (die + 4), die), $"{roll.Chance}% 아래면 성공 · 성공 {roll.Successes}", 11, Palette.Dim);
        }

        // ---- Who joins the battle ----

        readonly HashSet<int> _joining = new HashSet<int>();
        MapTile _joinFor;

        void DrawJoin()
        {
            var a = _adventure;
            if (_joinFor != a.JoinTile || _joining.Count == 0)
            {
                _joinFor = a.JoinTile;
                _joining.Clear();
                foreach (int i in a.JoinCandidates) _joining.Add(i);
            }
            var column = BeginColumn($"join:{a.Log.Count}", 56);
            float y = Heading(column, 4, a.BossBattle ? "봉인석의 파수꾼" : "전투");
            y = Paragraph(column, y, a.Encounter.Text, 14, Palette.Journal);
            float icon = 40;
            var monsters = a.Encounter.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                Ui.Image(new Rect(i * (icon + 8), y, icon, icon), PixelArt.Monster(m.Id, m.Color));
            }
            y += icon + 4;
            y = Paragraph(column, y, string.Join(", ", monsters.Select(m => $"{m.Name}(생명력 {m.Hp})")), 12, Palette.Dim);
            y = Paragraph(column, y, $"함께 싸울 영웅 (전투가 난 칸에서 {Adventure.JoinRange}칸 안). 싸운 영웅만 경험치를 받는다.", 13);
            foreach (int i in a.JoinCandidates)
            {
                var hero = a.Heroes[i];
                bool mover = i == a.MapHero;
                bool on = _joining.Contains(i);
                string label = $"{(on ? "■" : "□")} {hero.Name} Lv{hero.Level} · 생명력 {hero.Hp}/{hero.MaxHp}" + (mover ? " (이 칸에 들어간 영웅)" : "");
                if (Ui.Button(new Rect(0, y, column.width, 44), label, 13.5f, !mover, on ? Look.Selected : Look.Normal, "합류"))
                {
                    if (!_joining.Remove(i)) _joining.Add(i);
                }
                y += 50;
            }
            var far = Enumerable.Range(0, a.Heroes.Count).Where(i => !a.JoinCandidates.Contains(i)).Select(i => a.Heroes[i].Name).ToList();
            if (far.Count > 0) y = Paragraph(column, y, $"멀어서 못 오는 영웅: {string.Join(", ", far)}", 12, Palette.Dim);
            if (_actError != null) y = Paragraph(column, y, _actError, 12, Palette.Damage);
            EndColumn(y);
            if (Ui.Button(Footer(48), $"싸운다 ({_joining.Count}명)", 17, true, Look.Primary))
            {
                var chosen = _joining.ToList();
                if (Act(() => a.Join(chosen)) == null)
                {
                    _joining.Clear();
                    Sounds.Play(Sound.Battle);
                }
            }
        }
    }
}
