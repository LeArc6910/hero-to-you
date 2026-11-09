using System.Linq;
using Game.Core;
using UnityEngine;

namespace Game.Client
{
    // The town (inn, shop, elder), the gear window (equipment and the bag) and the level-up choice (Docs/Map.md,
    // Docs/Classes.md).
    public sealed partial class GameRoot
    {
        void DrawTown()
        {
            var a = _adventure;
            var column = BeginColumn($"town:{a.Day}", 56);
            float y = Heading(column, 4, "잿빛 숲 어귀 마을");
            var here = a.TownHeroes().Select(i => a.Heroes[i]).ToList();
            y = Paragraph(column, y, $"마을에 있는 영웅: {string.Join(", ", here.Select(h => h.Name + (h.Down ? "(쓰러짐)" : "")))} · 금화 {a.Gold}", 13, Palette.Journal);
            float bh = 46;
            float half = (column.width - 8) / 2;
            if (Ui.Button(new Rect(0, y, half, bh), $"여관에서 쉬기 (금화 {Adventure.InnCost})", 14, a.Gold >= Adventure.InnCost, Look.Normal)) Act(a.Rest);
            if (Ui.Button(new Rect(half + 8, y, half, bh), a.CanMeetElder ? "장로를 만난다" : "장로(이미 만남)", 14, a.CanMeetElder)) Act(a.Elder);
            y += bh + 4;
            y = Paragraph(column, y, $"여관: 마을에 있는 영웅이 모두 일어나 생명력 {Adventure.InnPercent}%와 결의를 모두 회복한다.", 11, Palette.Dim);
            foreach (string line in a.Report) y = Paragraph(column, y, "· " + line, 12, Palette.Journal);
            if (_actError != null) y = Paragraph(column, y, _actError, 12, Palette.Damage);

            y += 4;
            Ui.Text(new Rect(0, y, column.width, 22), $"상점 ({a.Day}일째 물건)", 16, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 26;
            for (int i = 0; i < a.Shop.Count; i++)
            {
                var item = a.Shop[i];
                string detail = item.Item != null ? ItemLine(item.Item) : item.Goods == "potion" ? $"{a.Current.Name}에게 · 생명력 {Battle.PotionHeal} 회복" : "연금술사의 재료 +2";
                var row = new Rect(0, y, column.width, 44);
                Ui.Panel(row);
                Ui.Text(new Rect(8, y + 2, column.width - 120, 20), item.Name + (item.Sold ? " (팔림)" : ""), 13, item.Sold ? Palette.Dim : Palette.Ink, TextAnchor.MiddleLeft, true);
                Ui.Text(new Rect(8, y + 22, column.width - 120, 18), detail, 10.5f, Palette.Dim);
                if (Ui.Button(new Rect(column.width - 104, y + 4, 100, 36), $"사기 {item.Price}", 13, !item.Sold && a.Gold >= item.Price, Look.Normal, item.Name)) Act(() => a.Buy(i));
                y += 50;
            }
            if (a.Bag.Count > 0)
            {
                y += 4;
                Ui.Text(new Rect(0, y, column.width, 22), "가방 (팔기)", 16, Palette.Gold, TextAnchor.MiddleLeft, true);
                y += 26;
                for (int i = 0; i < a.Bag.Count; i++)
                {
                    var item = a.Bag[i];
                    var row = new Rect(0, y, column.width, 40);
                    Ui.Panel(row);
                    Ui.Text(new Rect(8, y, column.width - 120, 40), $"{item.Name} · {ItemLine(item)}", 12, Palette.Ink);
                    if (Ui.Button(new Rect(column.width - 104, y + 2, 100, 36), $"팔기 {Adventure.SellPrice(item)}", 13, true, Look.Quiet, item.Name)) Act(() => a.Sell(i));
                    y += 46;
                }
            }
            EndColumn(y);
            var footer = Footer(48);
            float fh = (footer.width - 8) / 2;
            if (Ui.Button(new Rect(footer.x, footer.y, fh, footer.height), "장비·가방", 15, true, Look.Quiet)) _gearOpen = true;
            if (Ui.Button(new Rect(footer.x + fh + 8, footer.y, fh, footer.height), "마을을 떠난다", 16, true, Look.Primary)) Act(a.LeaveTown);
        }

        // "베기 · 힘 3개 · 위력 10" for a weapon, its bonuses for the rest.
        static string ItemLine(ItemDef item)
        {
            if (item.Attack != null)
            {
                var attack = item.Attack;
                string chance = attack.FixedChance > 0 ? $"{attack.FixedChance}%" : Stats.Name(attack.Stat.Value);
                string line = $"{KindName(item.Kind)} · {attack.Name}: {chance} {attack.Dice}개 · 위력 {attack.Power}" + (attack.Delay != 100 ? $" · 지연 {attack.Delay}%" : "");
                return item.Bonuses.Length > 0 ? line + " · " + item.Bonuses : line;
            }
            return item.Bonuses;
        }

        static string KindName(string kind) => kind switch
        {
            "sword" => "검",
            "axe" => "도끼",
            "bow" => "활",
            "staff" => "지팡이",
            "orb" => "구슬",
            "mace" => "철퇴",
            "cards" => "카드",
            "dagger" => "단검",
            "lute" => "류트",
            "sling" => "투석",
            "spear" => "창",
            _ => kind,
        };

        // ---- Equipment and the bag ----

        void DrawGear()
        {
            var a = _adventure;
            var r = new Rect(8, 8, Ui.Width - 16, Ui.Height - 16);
            Ui.Fill(new Rect(0, 0, Ui.Width, Ui.Height), new Color(0, 0, 0, 0.55f));
            Ui.Panel(r, Palette.Panel);
            float w = Mathf.Min(720, r.width - 16);
            var area = new Rect((Ui.Width - w) / 2, r.y + 8, w, r.height - 70);
            _gearScroll = Ui.Scroll(area, _gearScroll, _gearHeight - area.height, ref _gearDragging, ref _gearLastY);
            Ui.BeginArea(area, _gearScroll);
            float y = 0;
            Ui.Text(new Rect(0, y, w, 26), "장비와 가방", 20, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 30;
            for (int h = 0; h < a.Heroes.Count; h++)
            {
                var hero = a.Heroes[h];
                var box = new Rect(0, y, w, 92);
                Ui.Panel(box);
                Ui.Image(new Rect(6, y + 6, 36, 36), PixelArt.Hero(hero.Class.Id, hero.Class.Color));
                Ui.Text(new Rect(48, y + 4, w - 52, 18), $"{hero.Name} Lv{hero.Level} · 생명력 {hero.Hp}/{hero.MaxHp} · 방어도 {hero.ArmorValue} · 결의 {hero.MaxResolve}", 12.5f, Palette.Ink, TextAnchor.MiddleLeft, true);
                string stats = string.Join(" ", Enumerable.Range(0, Stats.Count).Select(i => $"{Stats.Name((Stat)i)} {hero.Stat((Stat)i)}"));
                Ui.Text(new Rect(48, y + 22, w - 52, 16), stats, 10.5f, Palette.Dim);
                Ui.Text(new Rect(48, y + 38, w - 52, 16), $"무기: {hero.Weapon.Name} ({ItemLine(hero.Weapon)})", 11, Palette.Journal);
                float sy = y + 56;
                string armor = hero.Armor != null ? $"갑옷: {hero.Armor.Name}" : "갑옷: 없음";
                string trinket = hero.Trinket != null ? $"장신구: {hero.Trinket.Name}" : "장신구: 없음";
                Ui.Text(new Rect(48, sy, (w - 52) / 2 - 60, 30), armor, 11, Palette.Journal);
                if (hero.Armor != null && Ui.Button(new Rect(48 + (w - 52) / 2 - 58, sy, 54, 30), "벗기", 11, true, Look.Quiet, hero.Name + " 갑옷")) Act(() => a.Unequip(h, ItemSlot.Armor));
                Ui.Text(new Rect(48 + (w - 52) / 2, sy, (w - 52) / 2 - 60, 30), trinket, 11, Palette.Journal);
                if (hero.Trinket != null && Ui.Button(new Rect(w - 60, sy, 54, 30), "벗기", 11, true, Look.Quiet, hero.Name + " 장신구")) Act(() => a.Unequip(h, ItemSlot.Trinket));
                y += 98;
            }
            Ui.Text(new Rect(0, y, w, 22), a.Bag.Count == 0 ? "가방이 비었다. 사건과 상점에서 물건을 얻는다." : "가방: 누구에게 끼울지 고르세요", 13, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 26;
            for (int i = 0; i < a.Bag.Count; i++)
            {
                var item = a.Bag[i];
                var box = new Rect(0, y, w, 64);
                Ui.Panel(box);
                Ui.Text(new Rect(8, y + 2, w - 16, 20), item.Name, 13, Palette.Ink, TextAnchor.MiddleLeft, true);
                Ui.Text(new Rect(8, y + 20, w - 16, 16), ItemLine(item), 10.5f, Palette.Dim);
                float bx = 8;
                for (int h = 0; h < a.Heroes.Count; h++)
                {
                    var hero = a.Heroes[h];
                    if (!hero.CanEquip(item)) continue;
                    if (Ui.Button(new Rect(bx, y + 38, 110, 24), $"{hero.Name}에게", 11, true, Look.Normal, item.Name)) Act(() => a.Equip(i, h));
                    bx += 116;
                }
                if (bx == 8) Ui.Text(new Rect(8, y + 38, w - 16, 24), "이 파티에는 들 수 있는 영웅이 없다", 10.5f, Palette.Dim);
                y += 70;
            }
            if (_actError != null)
            {
                Ui.Text(new Rect(0, y, w, 20), _actError, 12, Palette.Damage);
                y += 22;
            }
            Ui.EndArea(_gearScroll);
            _gearHeight = y;
            if (Ui.Button(new Rect((Ui.Width - 200) / 2, r.yMax - 54, 200, 46), "닫기", 16, true, Look.Primary)) _gearOpen = false;
        }

        float _gearScroll;
        float _gearHeight;
        bool _gearDragging;
        float _gearLastY;

        // ---- Level up ----

        void DrawLevelUp()
        {
            var a = _adventure;
            var choice = a.LevelUp;
            var hero = a.Heroes[choice.Hero];
            Ui.Fill(new Rect(0, 0, Ui.Width, Ui.Height), new Color(0, 0, 0, 0.6f));
            float w = Mathf.Min(560, Ui.Width - 24);
            float h = Mathf.Min(360, Ui.Height - 24);
            var r = new Rect((Ui.Width - w) / 2, (Ui.Height - h) / 2, w, h);
            Ui.Panel(r, Palette.Panel, Palette.Gold);
            Ui.Image(new Rect(r.x + 12, r.y + 10, 40, 40), PixelArt.Hero(hero.Class.Id, hero.Class.Color));
            Ui.Text(new Rect(r.x + 58, r.y + 10, w - 70, 24), $"{hero.Name} 레벨 {choice.Level}!", 20, Palette.Gold, TextAnchor.MiddleLeft, true);
            Ui.Text(new Rect(r.x + 58, r.y + 34, w - 70, 18), "강화 하나를 고르세요. 판마다 다르게 자란다.", 12, Palette.Journal);
            float cy = r.y + 62;
            float ch = (r.yMax - cy - 12 - 8 * (choice.Options.Count - 1)) / choice.Options.Count;
            for (int i = 0; i < choice.Options.Count; i++)
            {
                var upgrade = choice.Options[i];
                var card = new Rect(r.x + 12, cy, w - 24, Mathf.Min(ch, 120));
                Ui.Face(card, Look.Normal);
                Ui.Text(new Rect(card.x + 12, card.y + 6, card.width - 24, 24), upgrade.Name, 17, Palette.Ink, TextAnchor.MiddleLeft, true);
                Ui.Text(new Rect(card.x + 12, card.y + 32, card.width - 24, card.height - 36), upgrade.Text, 13, Palette.Journal, TextAnchor.UpperLeft, false, true);
                if (Ui.Hit(card, upgrade.Name, true, "강화"))
                {
                    int option = i;
                    if (Act(() => a.ChooseUpgrade(option)) == null) Sounds.Play(Sound.Level);
                }
                cy += Mathf.Min(ch, 120) + 8;
            }
        }
    }
}
