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
            float y = Heading(column, 4, a.Chapter.Town);
            var townHeroes = a.TownHeroes();
            var here = townHeroes.Select(i => a.Heroes[i]).ToList();
            y = Paragraph(column, y, $"마을에 있는 영웅: {string.Join(", ", here.Select(h => h.Name + (h.Down ? "(쓰러짐)" : "")))} · 금화 {a.Gold}" + (a.Discount ? " · 음유시인 덕에 값이 20% 싸다" : ""), 13, Palette.Journal);
            float bh = 46;
            float half = (column.width - 8) / 2;
            if (Ui.Button(new Rect(0, y, half, bh), $"여관에서 쉬기 (금화 {a.InnPrice})", 14, a.Gold >= a.InnPrice, Look.Normal)) Act(a.Rest);
            if (Ui.Button(new Rect(half + 8, y, half, bh), a.CanMeetElder ? "장로를 만난다" : "장로(이미 만남)", 14, a.CanMeetElder)) Act(a.Elder);
            y += bh + 6;
            int rumor = a.Discount ? Adventure.RumorCost * (100 - Adventure.BardDiscount) / 100 : Adventure.RumorCost;
            if (Ui.Button(new Rect(0, y, half, bh), a.CanHearRumors ? $"소문을 듣는다 (금화 {rumor})" : "소문(오늘은 끝)", 14, a.CanHearRumors && a.Gold >= rumor)) Act(a.Rumor);
            if (Ui.Button(new Rect(half + 8, y, half, bh), a.CanDonate ? $"마을을 돕는다 (금화 {a.DonationCost})" : "돕기(오늘은 끝)", 14, a.CanDonate && a.Gold >= a.DonationCost)) Act(a.Donate);
            y += bh + 6;
            // The contract board: one job at a time, paid when you come back.
            string contract = a.Contract == null ? (a.CanTakeContract ? "의뢰를 맡는다" : "의뢰 없음")
                : a.Contract.Done ? "의뢰 완료(보상을 받았다)" : $"의뢰 중: {a.Contract.Tile.Hex} 칸의 무리 (보상 {a.Contract.Gold})";
            if (Ui.Button(new Rect(0, y, column.width, bh), contract, 14, a.CanTakeContract)) Act(a.TakeContract);
            y += bh + 4;
            y = Paragraph(column, y, $"여관: 마을에 있는 영웅이 모두 일어나 생명력 {Adventure.InnPercent}%와 결의를 모두 회복하고 상처가 낫는다. 소문: 마을에서 가까운 안개 속 {Adventure.RumorHexes}곳을 알게 된다. 돕기: 명성 +1. 의뢰: 지도에 깃발로 표시한 무리를 쫓아내고 돌아오면 금화와 명성 +1.", 11, Palette.Dim);
            foreach (string line in a.Report) y = Paragraph(column, y, "· " + line, 12, Palette.Journal);
            if (_actError != null) y = Paragraph(column, y, _actError, 12, Palette.Damage);

            y += 4;
            Ui.Text(new Rect(0, y, column.width, 22), $"상점 ({a.Day}일째 물건)", 16, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 26;
            for (int i = 0; i < a.Shop.Count; i++)
            {
                var item = a.Shop[i];
                int index = i;
                bool consumable = item.Item != null && item.Item.Consumable;
                string detail = item.Item == null ? "연금술사의 재료 +2" : consumable ? item.Item.Text : ItemLine(item.Item);
                if (!consumable)
                {
                    var row = new Rect(0, y, column.width, 44);
                    Ui.Panel(row);
                    Ui.Text(new Rect(8, y + 2, column.width - 120, 20), item.Name + (item.Sold ? " (팔림)" : ""), 13, item.Sold ? Palette.Dim : Palette.Ink, TextAnchor.MiddleLeft, true);
                    Ui.Text(new Rect(8, y + 22, column.width - 120, 18), detail, 10.5f, Palette.Dim);
                    if (Ui.Button(new Rect(column.width - 104, y + 4, 100, 36), $"사기 {item.Price}", 13, !item.Sold && a.Gold >= item.Price, Look.Normal, item.Name)) Act(() => a.Buy(index));
                    y += 50;
                    continue;
                }
                // A consumable goes to one of the heroes in town: a button each.
                var box = new Rect(0, y, column.width, 72);
                Ui.Panel(box);
                Ui.Text(new Rect(8, y + 2, column.width - 16, 20), $"{item.Name} · 금화 {item.Price}", 13, Palette.Ink, TextAnchor.MiddleLeft, true);
                Ui.Text(new Rect(8, y + 20, column.width - 16, 16), detail, 10.5f, Palette.Dim);
                float bx = 8;
                float bw = Mathf.Min(150, (column.width - 16 - 6 * (townHeroes.Length - 1)) / Mathf.Max(1, townHeroes.Length));
                foreach (int h in townHeroes)
                {
                    var hero = a.Heroes[h];
                    int who = h;
                    if (Ui.Button(new Rect(bx, y + 38, bw, 30), $"{hero.Name}에게 ({hero.Count(item.Item.Id)})", 11.5f, a.Gold >= item.Price, Look.Normal, item.Name)) Act(() => a.Buy(index, who));
                    bx += bw + 6;
                }
                y += 78;
            }
            // The smithy (Docs/Map.md 대장간): each hero's worn gear, one level better for gold.
            y += 4;
            Ui.Text(new Rect(0, y, column.width, 22), Strings.Get("town.smithy"), 16, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 26;
            for (int h = 0; h < a.Heroes.Count; h++)
            {
                var hero = a.Heroes[h];
                if (hero.Dead) continue;
                int who = h;
                var slots = new[] { (ItemSlot.Weapon, hero.Weapon), (ItemSlot.Armor, hero.Armor), (ItemSlot.Trinket, hero.Trinket) }.Where(x => x.Item2 != null).ToList();
                var box = new Rect(0, y, column.width, 30 + 46 * ((slots.Count + 1) / 2));
                Ui.Panel(box);
                Ui.Text(new Rect(8, y + 2, column.width - 16, 24), hero.Name, 13, Palette.Ink, TextAnchor.MiddleLeft, true);
                float sw = (column.width - 22) / 2;
                for (int i = 0; i < slots.Count; i++)
                {
                    var (slot, item) = slots[i];
                    bool can = Adventure.CanForge(item);
                    int price = can ? a.ForgePrice(item) : 0;
                    string label = can ? $"{item.Name} → +{item.Plus + 1} (금화 {price})" : $"{item.Name} (끝까지 벼렸다)";
                    var cell = new Rect(8 + i % 2 * (sw + 6), y + 28 + i / 2 * 46, sw, 44);
                    if (Ui.Button(cell, label, 11.5f, can && a.Gold >= price, Look.Normal, "대장간"))
                    {
                        var s2 = slot;
                        if (Act(() => a.Forge(who, s2)) == null) Sounds.Play(Sound.Forge);
                    }
                    Ui.Tip(cell, slot == ItemSlot.Weapon ? "무기: 위력 +2" : slot == ItemSlot.Armor ? "갑옷: 방어도 +1" : "장신구: 생명력 +4");
                }
                y += box.height + 6;
            }
            // A new companion (hardcore): a class not in the party takes the fallen hero's place.
            var recruits = a.Recruits();
            if (recruits.Count > 0)
            {
                y = Paragraph(column, y, Strings.Get("town.recruit"), 13, Palette.Gold);
                float rw = (column.width - 12) / 3;
                for (int i = 0; i < recruits.Count; i++)
                {
                    var cls = recruits[i];
                    var cell = new Rect(i % 3 * (rw + 6), y + i / 3 * 50, rw, 44);
                    if (Ui.Button(cell, cls.Name, 13, true, Look.Normal, "새 동료")) Act(() => a.Recruit(cls.Id));
                    Ui.Tip(cell, cls.Mechanic);
                }
                y += (recruits.Count + 2) / 3 * 50 + 4;
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
            if (Ui.Button(new Rect(footer.x, footer.y, fh, footer.height), Strings.Get("map.gear"), 15, true, Look.Quiet)) _gearOpen = true;
            if (Ui.Button(new Rect(footer.x + fh + 8, footer.y, fh, footer.height), Strings.Get("town.leave"), 16, true, Look.Primary)) Act(a.LeaveTown);
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
            Ui.Text(new Rect(0, y, w, 26), Strings.Get("gear.heading"), 20, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 30;
            for (int h = 0; h < a.Heroes.Count; h++)
            {
                var hero = a.Heroes[h];
                int heroIndex = h;
                var carried = hero.Consumables.Where(c => c.Value > 0).ToList();
                float extra = carried.Count == 0 ? 0 : 36 * ((carried.Count + 2) / 3);
                var box = new Rect(0, y, w, 92 + extra);
                Ui.Panel(box);
                Ui.Image(new Rect(6, y + 6, 36, 36), PixelArt.Hero(hero.Class.Id, hero.Class.Color));
                Ui.Text(new Rect(48, y + 4, w - 160, 18), $"{hero.Name} Lv{hero.Level} · 생명력 {hero.Hp}/{hero.MaxHp} · 방어도 {hero.ArmorValue} · 결의 {hero.MaxResolve}" + (hero.Wounds > 0 ? $" · 상처 {hero.Wounds}" : ""), 12.5f, Palette.Ink, TextAnchor.MiddleLeft, true);
                // Front or back row, a tap to switch (Docs/Combat.md 줄).
                if (Ui.Button(new Rect(w - 106, y + 4, 100, 44), Strings.Get(hero.Back ? "row.back" : "row.front"), 13, true, hero.Back ? Look.Quiet : Look.Normal, hero.Name + " 줄"))
                    Act(() => a.SetRow(heroIndex, !hero.Back));
                Ui.Tip(new Rect(w - 106, y + 4, 100, 44), "앞줄은 적의 무작위 공격을 받는다. 뒷줄은 힘·민첩 근접 공격의 위력이 25% 줄어든다.");
                string stats = string.Join(" ", Enumerable.Range(0, Stats.Count).Select(i => $"{Stats.Name((Stat)i)} {hero.Stat((Stat)i)}"));
                Ui.Text(new Rect(48, y + 22, w - 160, 16), stats, 10.5f, Palette.Dim);
                Ui.Text(new Rect(48, y + 38, w - 52, 16), $"무기: {hero.Weapon.Name} ({ItemLine(hero.Weapon)})", 11, Palette.Journal);
                float sy = y + 56;
                string armor = hero.Armor != null ? $"갑옷: {hero.Armor.Name}" : "갑옷: 없음";
                string trinket = hero.Trinket != null ? $"장신구: {hero.Trinket.Name}" : "장신구: 없음";
                Ui.Text(new Rect(48, sy, (w - 52) / 2 - 60, 30), armor, 11, Palette.Journal);
                if (hero.Armor != null && Ui.Button(new Rect(48 + (w - 52) / 2 - 58, sy, 54, 30), "벗기", 11, true, Look.Quiet, hero.Name + " 갑옷")) Act(() => a.Unequip(h, ItemSlot.Armor));
                Ui.Text(new Rect(48 + (w - 52) / 2, sy, (w - 52) / 2 - 60, 30), trinket, 11, Palette.Journal);
                if (hero.Trinket != null && Ui.Button(new Rect(w - 60, sy, 54, 30), "벗기", 11, true, Look.Quiet, hero.Name + " 장신구")) Act(() => a.Unequip(h, ItemSlot.Trinket));
                // Consumables: the ones that work outside battle can be used here.
                float cw = (w - 52 - 12) / 3;
                for (int c = 0; c < carried.Count; c++)
                {
                    string id = carried[c].Key;
                    var item = _data.Items.TryGetValue(id, out var def) ? def : null;
                    if (item == null) continue;
                    var cell = new Rect(48 + c % 3 * (cw + 6), y + 92 + c / 3 * 36, cw, 32);
                    bool usable = Adventure.UsableOnMap(item) && !hero.Down;
                    string label = usable ? $"{item.Name} {carried[c].Value} · 쓰기" : $"{item.Name} {carried[c].Value} (전투용)";
                    if (Ui.Button(cell, label, 11, usable, Look.Quiet, hero.Name + " 소모품")) Act(() => a.UseItem(heroIndex, id));
                    Ui.Tip(cell, item.Text);
                }
                y += 98 + extra;
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
            if (Ui.Button(new Rect((Ui.Width - 200) / 2, r.yMax - 54, 200, 46), Strings.Get("common.close"), 16, true, Look.Primary)) _gearOpen = false;
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
            // Many waiting (a later start): one button takes the first card of each of the rest.
            int waiting = a.LevelUpsWaiting;
            float bottom = waiting > 0 ? 54 : 0;
            float ch = (r.yMax - cy - 12 - bottom - 8 * (choice.Options.Count - 1)) / choice.Options.Count;
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
            if (waiting > 0 && Ui.Button(new Rect(r.x + 12, r.yMax - 56, w - 24, 46), Strings.Format("levelup.auto", waiting + 1), 14, true, Look.Quiet, "강화"))
            {
                for (int guard = 0; guard < 200 && a.LevelUp != null; guard++)
                    if (Act(() => a.ChooseUpgrade(0)) != null) break;
                Sounds.Play(Sound.Level);
            }
        }
    }
}
