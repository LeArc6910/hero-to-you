using System.Linq;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    // The title and choosing the party of three (Docs/Classes.md).
    public sealed partial class GameRoot
    {
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
            if (Ui.Button(new Rect(x, y, w, h), "혼자 시작 (영웅 셋을 모두 조종)", 17, true, Look.Primary))
            {
                _party.Clear();
                _screen = ClientScreen.Party;
            }
            y += h + 10;
            Ui.Button(new Rect(x, y, w, h), "협동 (온라인 방은 M4에서)", 15, false);
            y += h + 10;
            string speed = _speed >= 2 ? "빠르게" : "보통";
            if (Ui.Button(new Rect(x, y, w, Ui.Short ? 40 : 44), $"전투 연출: {speed}", 14, true, Look.Quiet)) _speed = _speed >= 2 ? 1 : 2.5f;
            Ui.Text(new Rect(0, Ui.Height - 26, Ui.Width - 8, 20), "세로 조각 M1 · " + BuildInfo.Version, 11, Palette.Dim, TextAnchor.MiddleRight);
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
            string start = _party.Count == 3 ? "모험 시작" : $"모험 시작 ({_party.Count}/3)";
            if (Ui.Button(new Rect(Ui.Width - 12 - bw, by, bw, 44), start, 16, _party.Count == 3, Look.Primary)) StartAdventure(_party);
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
