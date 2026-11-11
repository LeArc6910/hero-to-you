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
            y = Paragraph(column, y, Strings.Format("town.heroes", string.Join(", ", here.Select(h => h.Down ? Strings.Format("hero.name_down", h.Name) : h.Name)), a.Gold) + (a.Discount ? " · " + Strings.Get("town.discount") : ""), 13, Palette.Journal);
            float bh = 46;
            float half = (column.width - 8) / 2;
            if (Ui.Button(new Rect(0, y, half, bh), Strings.Format("town.inn", a.InnPrice), 14, a.Gold >= a.InnPrice, Look.Normal)) Act(a.Rest);
            if (Ui.Button(new Rect(half + 8, y, half, bh), Strings.Get(a.CanMeetElder ? "town.elder" : "town.elder_done"), 14, a.CanMeetElder)) Act(a.Elder);
            y += bh + 6;
            int rumor = a.Discount ? Adventure.RumorCost * (100 - Adventure.BardDiscount) / 100 : Adventure.RumorCost;
            if (Ui.Button(new Rect(0, y, half, bh), a.CanHearRumors ? Strings.Format("town.rumor", rumor) : Strings.Get("town.rumor_done"), 14, a.CanHearRumors && a.Gold >= rumor)) Act(a.Rumor);
            if (Ui.Button(new Rect(half + 8, y, half, bh), a.CanDonate ? Strings.Format("town.donate", a.DonationCost) : Strings.Get("town.donate_done"), 14, a.CanDonate && a.Gold >= a.DonationCost)) Act(a.Donate);
            y += bh + 6;
            // The contract board: one job at a time, paid when you come back.
            string contract = a.Contract == null ? Strings.Get(a.CanTakeContract ? "town.contract_take" : "town.contract_none")
                : a.Contract.Done ? Strings.Get("town.contract_done") : Strings.Format("town.contract_on", a.Contract.Tile.Hex, a.Contract.Gold);
            if (Ui.Button(new Rect(0, y, column.width, bh), contract, 14, a.CanTakeContract)) Act(a.TakeContract);
            y += bh + 4;
            y = Paragraph(column, y, Strings.Format("town.help", Adventure.InnPercent, Adventure.RumorHexes), 11, Palette.Dim);
            foreach (string line in a.Report) y = Paragraph(column, y, "· " + line, 12, Palette.Journal);
            if (_actError != null) y = Paragraph(column, y, _actError, 12, Palette.Damage);

            y += 4;
            Ui.Text(new Rect(0, y, column.width, 22), Strings.Format("town.shop", a.Day), 16, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 26;
            for (int i = 0; i < a.Shop.Count; i++)
            {
                var item = a.Shop[i];
                int index = i;
                bool consumable = item.Item != null && item.Item.Consumable;
                string detail = item.Item == null ? Strings.Get("town.reagents") : consumable ? item.Item.Text : ItemLine(item.Item);
                if (!consumable)
                {
                    var row = new Rect(0, y, column.width, 44);
                    Ui.Panel(row);
                    float tx = ItemIcon(new Rect(4, y + 4, 36, 36), item.Item);
                    Ui.Text(new Rect(tx, y + 2, column.width - 120 - tx, 20), item.Name + (item.Sold ? " " + Strings.Get("town.sold") : ""), 13, item.Sold ? Palette.Dim : Palette.Ink, TextAnchor.MiddleLeft, true);
                    ItemMark(new Rect(column.width - 128, y + 4, 18, 18), item.Item);
                    Ui.Text(new Rect(tx, y + 22, column.width - 120 - tx, 18), detail, 10.5f, Palette.Dim);
                    if (Ui.Button(new Rect(column.width - 104, y + 4, 100, 36), Strings.Format("town.buy", item.Price), 13, !item.Sold && a.Gold >= item.Price, Look.Normal, item.Name) && Act(() => a.Buy(index)) == null) Sounds.Play(Sound.Coin);
                    y += 50;
                    continue;
                }
                // A consumable goes to one of the heroes in town: a button each.
                var box = new Rect(0, y, column.width, 72);
                Ui.Panel(box);
                float ix = ItemIcon(new Rect(4, y + 3, 32, 32), item.Item);
                Ui.Text(new Rect(ix, y + 2, column.width - 8 - ix, 20), Strings.Format("town.price", item.Name, item.Price), 13, Palette.Ink, TextAnchor.MiddleLeft, true);
                Ui.Text(new Rect(ix, y + 20, column.width - 8 - ix, 16), detail, 10.5f, Palette.Dim);
                float bx = 8;
                float bw = Mathf.Min(150, (column.width - 16 - 6 * (townHeroes.Length - 1)) / Mathf.Max(1, townHeroes.Length));
                foreach (int h in townHeroes)
                {
                    var hero = a.Heroes[h];
                    int who = h;
                    if (Ui.Button(new Rect(bx, y + 38, bw, 30), Strings.Format("town.buy_for", hero.Name, hero.Count(item.Item.Id)), 11.5f, a.Gold >= item.Price, Look.Normal, item.Name) && Act(() => a.Buy(index, who)) == null) Sounds.Play(Sound.Coin);
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
                    string label = can ? Strings.Format("town.forge", item.Name, item.Plus + 1, price) : Strings.Format("town.forge_max", item.Name);
                    var cell = new Rect(8 + i % 2 * (sw + 6), y + 28 + i / 2 * 46, sw, 44);
                    if (Ui.Button(cell, label, 11.5f, can && a.Gold >= price, Look.Normal, "대장간"))
                    {
                        var s2 = slot;
                        if (Act(() => a.Forge(who, s2)) == null) Sounds.Play(Sound.Forge);
                    }
                    Ui.Tip(cell, Strings.Get(slot == ItemSlot.Weapon ? "town.forge_weapon" : slot == ItemSlot.Armor ? "town.forge_armor" : "town.forge_trinket"));
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
                Ui.Text(new Rect(0, y, column.width, 22), Strings.Get("town.bag"), 16, Palette.Gold, TextAnchor.MiddleLeft, true);
                y += 26;
                for (int i = 0; i < a.Bag.Count; i++)
                {
                    var item = a.Bag[i];
                    var row = new Rect(0, y, column.width, 40);
                    Ui.Panel(row);
                    float sx = ItemIcon(new Rect(4, y + 4, 32, 32), item);
                    Ui.Text(new Rect(sx, y, column.width - 112 - sx, 40), $"{item.Name} · {ItemLine(item)}", 12, Palette.Ink);
                    if (Ui.Button(new Rect(column.width - 104, y + 2, 100, 36), Strings.Format("town.sell", Adventure.SellPrice(item)), 13, true, Look.Quiet, item.Name)) Act(() => a.Sell(i));
                    y += 46;
                }
            }
            EndColumn(y);
            var footer = Footer(48);
            float fh = (footer.width - 8) / 2;
            if (Ui.Button(new Rect(footer.x, footer.y, fh, footer.height), Strings.Get("map.gear"), 15, true, Look.Quiet, null, KeyCode.G)) _gearOpen = true;
            if (Ui.Button(new Rect(footer.x + fh + 8, footer.y, fh, footer.height), Strings.Get("town.leave"), 16, true, Look.Primary, null, KeyCode.Space)) Act(a.LeaveTown);
        }

        // "베기 · 힘 3개 · 위력 10" for a weapon, its bonuses for the rest.
        static string ItemLine(ItemDef item)
        {
            string effect = EffectLine(item);
            if (item.Attack != null)
            {
                var attack = item.Attack;
                string chance = attack.FixedChance > 0 ? $"{attack.FixedChance}%" : Stats.Name(attack.Stat.Value, Sheets);
                string line = Strings.Format("item.attack", KindName(item.Kind), attack.Name, chance, attack.Dice, attack.Power) + (attack.Delay != 100 ? " · " + Strings.Format("item.delay", attack.Delay) : "");
                if (item.Bonuses.Length > 0) line += " · " + item.Bonuses;
                return effect.Length > 0 ? line + " · " + effect : line;
            }
            return effect.Length > 0 ? (item.Bonuses.Length > 0 ? item.Bonuses + " · " : "") + effect : item.Bonuses;
        }

        // ---- A piece of gear's own effect (Docs/Combat.md 고유 효과): a word and a small mark ----

        static string EffectOf(ItemDef item) => item.Attack != null ? item.Attack.Effect : item.Effect;

        internal static string EffectIcon(string effect) => effect switch
        {
            "burn" => "flame",
            "chill" => "snowflake",
            "lifesteal" => "drop",
            "weaken" => "down",
            "corrode" => "acid",
            "thorns" => "thorn",
            "regen" => "leaf",
            _ => null,
        };

        static string EffectLine(ItemDef item)
        {
            string effect = EffectOf(item);
            return EffectIcon(effect) == null ? "" : Strings.Format("item.effect." + effect, item.EffectAmount);
        }

        // The item's picture (Art/icons.txt item_*): gear by its kind (sword, light armour, ring…), a consumable by its id.
        static Texture2D ItemPicture(ItemDef item)
        {
            if (item == null) return null;
            string key = item.Consumable ? (item.Id == "charm" ? "luckycharm" : item.Id) : item.Kind;
            return PixelArt.Icon("item_" + key);
        }

        // Draws the item's picture at r and gives back where the words beside it start (at r when there is none).
        static float ItemIcon(Rect r, ItemDef item)
        {
            var picture = ItemPicture(item);
            if (picture == null) return r.x + 4;
            Ui.Image(r, picture);
            return r.xMax + 6;
        }

        // The effect's mark at r (nothing for gear without one), with its word as the tip.
        static void ItemMark(Rect r, ItemDef item)
        {
            string icon = item == null ? null : EffectIcon(EffectOf(item));
            if (icon == null) return;
            Ui.Image(r, PixelArt.Icon(icon));
            Ui.Tip(r, EffectLine(item));
        }

        // A weapon's kind (sword, bow…) in words; a kind the sheet does not name shows as its id.
        static string KindName(string kind) => Strings.Has("item.kind." + kind) ? Strings.Get("item.kind." + kind) : kind;

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
                Ui.TextFit(new Rect(48, y + 4, w - 172, 18), Strings.Format("gear.hero", hero.Name, hero.Level, hero.Hp, hero.MaxHp, hero.ArmorValue, hero.MaxResolve) + (hero.Wounds > 0 ? " · " + Strings.Format("hero.wounds", hero.Wounds) : ""), 12.5f, Palette.Ink, TextAnchor.MiddleLeft, true);
                // Front or back row, a tap to switch (Docs/Combat.md 줄).
                if (Ui.Button(new Rect(w - 106, y + 4, 100, 44), Strings.Get(hero.Back ? "row.back" : "row.front"), 13, true, hero.Back ? Look.Quiet : Look.Normal, hero.Name + " 줄"))
                    Act(() => a.SetRow(heroIndex, !hero.Back));
                Ui.Tip(new Rect(w - 106, y + 4, 100, 44), Strings.Get("gear.row_tip"));
                string stats = string.Join(" ", Enumerable.Range(0, Stats.Count).Select(i => $"{Stats.Name((Stat)i, Sheets)} {hero.Stat((Stat)i)}"));
                Ui.TextFit(new Rect(48, y + 22, w - 172, 16), stats, 10.5f, Palette.Dim);
                Ui.Image(new Rect(48, y + 38, 16, 16), ItemPicture(hero.Weapon));
                Ui.Text(new Rect(66, y + 38, w - 70, 16), Strings.Format("gear.weapon", hero.Weapon.Name, ItemLine(hero.Weapon)), 11, Palette.Journal);
                float sy = y + 56;
                string armor = Strings.Format("gear.armor", hero.Armor != null ? hero.Armor.Name : Strings.Get("common.none"));
                string trinket = Strings.Format("gear.trinket", hero.Trinket != null ? hero.Trinket.Name : Strings.Get("common.none"));
                if (hero.Armor != null) Ui.Image(new Rect(48, sy + 7, 16, 16), ItemPicture(hero.Armor));
                Ui.Text(new Rect(hero.Armor != null ? 66 : 48, sy, (w - 52) / 2 - 78, 30), armor, 11, Palette.Journal);
                if (hero.Armor != null && Ui.Button(new Rect(48 + (w - 52) / 2 - 58, sy, 54, 30), Strings.Get("gear.unequip"), 11, true, Look.Quiet, hero.Name + " 갑옷")) Act(() => a.Unequip(h, ItemSlot.Armor));
                if (hero.Trinket != null) Ui.Image(new Rect(48 + (w - 52) / 2, sy + 7, 16, 16), ItemPicture(hero.Trinket));
                Ui.Text(new Rect(48 + (w - 52) / 2 + (hero.Trinket != null ? 18 : 0), sy, (w - 52) / 2 - 78, 30), trinket, 11, Palette.Journal);
                if (hero.Trinket != null && Ui.Button(new Rect(w - 60, sy, 54, 30), Strings.Get("gear.unequip"), 11, true, Look.Quiet, hero.Name + " 장신구")) Act(() => a.Unequip(h, ItemSlot.Trinket));
                // Consumables: the ones that work outside battle can be used here.
                float cw = (w - 52 - 12) / 3;
                for (int c = 0; c < carried.Count; c++)
                {
                    string id = carried[c].Key;
                    var item = _data.Items.TryGetValue(id, out var def) ? def : null;
                    if (item == null) continue;
                    var cell = new Rect(48 + c % 3 * (cw + 6), y + 92 + c / 3 * 36, cw, 32);
                    bool usable = Adventure.UsableOnMap(item) && !hero.Down;
                    string label = Strings.Format(usable ? "gear.use" : "gear.battle_only", item.Name, carried[c].Value);
                    if (Ui.Button(cell, label, 11, usable, Look.Quiet, hero.Name + " 소모품")) Act(() => a.UseItem(heroIndex, id));
                    Ui.Tip(cell, item.Text);
                }
                y += 98 + extra;
            }
            Ui.Text(new Rect(0, y, w, 22), Strings.Get(a.Bag.Count == 0 ? "gear.bag_empty" : "gear.bag"), 13, Palette.Gold, TextAnchor.MiddleLeft, true);
            y += 26;
            for (int i = 0; i < a.Bag.Count; i++)
            {
                var item = a.Bag[i];
                var box = new Rect(0, y, w, 64);
                Ui.Panel(box);
                float gx = ItemIcon(new Rect(4, y + 3, 32, 32), item);
                Ui.Text(new Rect(gx, y + 2, w - 8 - gx, 20), item.Name, 13, Palette.Ink, TextAnchor.MiddleLeft, true);
                ItemMark(new Rect(w - 26, y + 4, 18, 18), item);
                Ui.Text(new Rect(gx, y + 20, w - 8 - gx, 16), ItemLine(item), 10.5f, Palette.Dim);
                float bx = 8;
                for (int h = 0; h < a.Heroes.Count; h++)
                {
                    var hero = a.Heroes[h];
                    if (!hero.CanEquip(item)) continue;
                    if (Ui.Button(new Rect(bx, y + 38, 110, 24), Strings.Format("gear.give", hero.Name), 11, true, Look.Normal, item.Name) && Act(() => a.Equip(i, h)) == null) Sounds.Play(Sound.Equip);
                    bx += 116;
                }
                if (bx == 8) Ui.Text(new Rect(8, y + 38, w - 16, 24), Strings.Get("gear.nobody"), 10.5f, Palette.Dim);
                y += 70;
            }
            if (_actError != null)
            {
                Ui.Text(new Rect(0, y, w, 20), _actError, 12, Palette.Damage);
                y += 22;
            }
            Ui.EndArea(_gearScroll);
            _gearHeight = y;
            if (Ui.Button(new Rect((Ui.Width - 200) / 2, r.yMax - 54, 200, 46), Strings.Get("common.close"), 16, true, Look.Primary, null, KeyCode.Escape)) _gearOpen = false;
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
            Ui.Text(new Rect(r.x + 58, r.y + 10, w - 70, 24), Strings.Format("levelup.heading", hero.Name, choice.Level), 20, Palette.Gold, TextAnchor.MiddleLeft, true);
            Ui.Text(new Rect(r.x + 58, r.y + 34, w - 70, 18), Strings.Get("levelup.hint"), 12, Palette.Journal);
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
                if (Ui.Hit(card, upgrade.Name, true, "강화", Ui.Number(i)))
                {
                    int option = i;
                    if (Act(() => a.ChooseUpgrade(option)) == null) Sounds.Play(Sound.Level);
                }
                cy += Mathf.Min(ch, 120) + 8;
            }
            if (waiting > 0 && Ui.Button(new Rect(r.x + 12, r.yMax - 56, w - 24, 46), Strings.Format("levelup.auto", waiting + 1), 14, true, Look.Quiet, "강화", KeyCode.A))
            {
                for (int guard = 0; guard < 200 && a.LevelUp != null; guard++)
                    if (Act(() => a.ChooseUpgrade(0)) != null) break;
                Sounds.Play(Sound.Level);
            }
        }
    }
}
