using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // The town (Docs/Map.md): the inn, the shop and the elder; and the bag and equipment, which can be changed on
    // the map or in town.
    public sealed partial class Adventure
    {
        public const int ShopItems = 6;
        public const int InnPercent = 50;
        public const int InnCost = 10;
        public const int ReagentPrice = 4;
        public const int RumorCost = 5;
        public const int RumorHexes = 6;
        public const int BardDiscount = 20;
        public const int DonateCost = 20;

        int _shopDay;
        int _rumorDay;
        int _donateDay;
        bool _elderMet;

        public List<ItemDef> Bag { get; } = new List<ItemDef>();
        public List<ShopItem> Shop { get; } = new List<ShopItem>();

        void OpenTown()
        {
            if (_shopDay != Day) Restock();
            PayContract();
            Screen = AdventureScreen.Town;
        }

        // The bard talks the prices down (Docs/Classes.md 지도 특기).
        public bool Discount => Standing("bard");

        int PriceOf(int price) => Discount ? price * (100 - BardDiscount) / 100 : price;

        // A new stock each day: six pieces of gear (better ones as the days and chapters go), every consumable and, for
        // an alchemist, reagents.
        void Restock()
        {
            _shopDay = Day;
            Shop.Clear();
            int tier = Math.Min(4, Chapter.Index + (Day >= 3 ? 3 : 2));
            var pool = Data.ItemList.Where(i => i.Price > 0 && !i.Consumable && i.Tier <= tier).ToList();
            for (int i = 0; i < ShopItems && pool.Count > 0; i++)
            {
                int pick = _shopRandom.Next(pool.Count);
                Shop.Add(new ShopItem { Item = pool[pick], Price = PriceOf(pool[pick].Price) });
                pool.RemoveAt(pick);
            }
            foreach (var item in Data.ItemList.Where(i => i.Consumable && i.Price > 0)) Shop.Add(new ShopItem { Item = item, Price = PriceOf(item.Price) });
            if (Heroes.Any(h => h.Class.Id == "alchemist")) Shop.Add(new ShopItem { Goods = "reagent", Price = PriceOf(ReagentPrice) });
        }

        // Heroes standing in the town now (downed ones too: the inn takes them in).
        public int[] TownHeroes()
        {
            var town = Map.Tile(Site.Town).Hex;
            return Enumerable.Range(0, Heroes.Count).Where(i => Positions[i] == town).ToArray();
        }

        string InTown()
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            return Screen == AdventureScreen.Town ? null : Data.Line("town.not_in_town");
        }

        // The inn's price: dearer in later chapters, cheaper with the bard.
        public int InnPrice => PriceOf(InnCost * (Chapter.Index + 1));

        // The inn: everyone in town gets up, heals half and gets all resolve back.
        public string Rest()
        {
            string error = InTown();
            if (error != null) return error;
            int cost = InnPrice;
            if (Gold < cost) return Data.Line("town.no_gold_inn", cost);
            Gold -= cost;
            Record("rest");
            Report.Clear();
            foreach (int i in TownHeroes())
            {
                var hero = Heroes[i];
                if (hero.Dead) continue;
                if (hero.Down) hero.Hp = 1;
                bool wounded = hero.Wounds > 0;
                hero.HealWounds();
                int before = hero.Hp;
                hero.Heal(Math.Max(1, hero.MaxHp * InnPercent / 100));
                hero.GainResolve(hero.MaxResolve);
                Report.Add(wounded
                    ? Data.Line("town.inn_rest_healed", hero.Name, hero.Hp - before, hero.Hp, hero.MaxHp)
                    : Data.Line("town.inn_rest", hero.Name, hero.Hp - before, hero.Hp, hero.MaxHp));
            }
            return null;
        }

        // Buys a thing: gear goes to the bag, a consumable to the hero (the one whose turn it is when not given).
        public string Buy(int index, int heroIndex = -1)
        {
            string error = InTown();
            if (error != null) return error;
            if (index < 0 || index >= Shop.Count || Shop[index].Sold) return Data.Line("town.cant_buy");
            var goods = Shop[index];
            if (heroIndex < 0) heroIndex = MapHero;
            if (heroIndex >= Heroes.Count) return Data.Line("town.pick_hero");
            if (Gold < goods.Price) return Data.Line("town.no_gold", goods.Price);
            Gold -= goods.Price;
            Record($"buy {index} {heroIndex}");
            Report.Clear();
            var hero = Heroes[heroIndex];
            if (goods.Goods == "reagent")
            {
                var alchemist = Heroes.First(h => h.Class.Id == "alchemist");
                alchemist.GainReagents(2);
                Report.Add(Data.Line("town.bought_reagents", alchemist.Name, alchemist.Reagents, alchemist.ReagentMax));
            }
            else if (goods.Item.Consumable)
            {
                hero.AddConsumable(goods.Item.Id, 1);
                Report.Add(Data.Line("town.bought_consumable", hero.Name, goods.Item.Name, hero.Count(goods.Item.Id)));
            }
            else
            {
                goods.Sold = true;
                AddToBag(goods.Item);
                Report.Add(Data.Line("town.bought_gear", goods.Item.Name));
            }
            return null;
        }

        // Rumors at the inn, once a day: what the nearest hexes the party does not know yet hold.
        public bool CanHearRumors => Screen == AdventureScreen.Town && _rumorDay != Day && Unknown().Any();

        IEnumerable<MapTile> Unknown()
        {
            var town = Map.Tile(Site.Town).Hex;
            return Map.Tiles.Where(t => !Knows(t)).OrderBy(t => t.Hex.Distance(town)).ThenBy(t => t.Hex.Row).ThenBy(t => t.Hex.Col);
        }

        public string Rumor()
        {
            string error = InTown();
            if (error != null) return error;
            if (!CanHearRumors) return Data.Line("town.no_rumors");
            int cost = PriceOf(RumorCost);
            if (Gold < cost) return Data.Line("town.no_gold_rumor", cost);
            Gold -= cost;
            _rumorDay = Day;
            Record("rumor");
            Report.Clear();
            var heard = Unknown().Take(RumorHexes).ToList();
            foreach (var tile in heard) _revealed.Add(tile.Hex);
            int enemies = heard.Count(t => t.Content == Content.Enemy);
            Report.Add(enemies > 0 ? Data.Line("town.rumor_enemies", heard.Count, enemies) : Data.Line("town.rumor", heard.Count));
            return null;
        }

        public static int SellPrice(ItemDef item) => item.Price / 2;

        public string Sell(int bagIndex)
        {
            string error = InTown();
            if (error != null) return error;
            if (bagIndex < 0 || bagIndex >= Bag.Count) return Data.Line("town.not_in_bag");
            var item = Bag[bagIndex];
            Bag.RemoveAt(bagIndex);
            Gold += SellPrice(item);
            Record($"sell {bagIndex}");
            Report.Clear();
            Report.Add(Data.Line("town.sold", item.Name, SellPrice(item)));
            return null;
        }

        // Helping the town rebuild, once a day: gold for renown (Docs/Narrative.md 명성). Costs more each chapter.
        public int DonationCost => DonateCost * (Chapter.Index + 1);

        public bool CanDonate => Screen == AdventureScreen.Town && _donateDay != Day;

        public string Donate()
        {
            string error = InTown();
            if (error != null) return error;
            if (!CanDonate) return Data.Line("town.helped_today");
            if (Gold < DonationCost) return Data.Line("town.no_gold", DonationCost);
            Gold -= DonationCost;
            _donateDay = Day;
            Record("donate");
            Report.Clear();
            Report.Add(Data.Line("town.donated", DonationCost));
            GainRenown(1, Report);
            return null;
        }

        // ---- The smithy (Docs/Map.md 대장간) ----

        public const int MaxPlus = 3;
        public const int ForgeBase = 30;
        public const int EngineerDiscount = 30;

        // What forging the item one more level costs: more for better items and higher levels; the bard and the
        // engineer bargain.
        public int ForgePrice(ItemDef item)
        {
            int price = ForgeBase * (item.Tier + item.Plus + 1);
            if (Standing("engineer")) price = price * (100 - EngineerDiscount) / 100;
            return PriceOf(price);
        }

        public static bool CanForge(ItemDef item) => item != null && !item.Consumable && item.Plus < MaxPlus;

        // Makes a worn weapon (power +2), armor (armor +1) or trinket (health +4) one level better.
        public string Forge(int heroIndex, ItemSlot slot)
        {
            string error = InTown();
            if (error != null) return error;
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return Data.Line("town.pick_hero");
            var hero = Heroes[heroIndex];
            var item = slot == ItemSlot.Weapon ? hero.Weapon : slot == ItemSlot.Armor ? hero.Armor : hero.Trinket;
            if (!CanForge(item)) return item == null ? Data.Line("town.nothing_worn") : Data.Line("town.forge_maxed", item.Name);
            int price = ForgePrice(item);
            if (Gold < price) return Data.Line("town.no_gold", price);
            Gold -= price;
            var forged = item.Clone();
            forged.Plus = item.Plus + 1;
            string baseName = item.Plus > 0 ? item.Name.Substring(0, item.Name.LastIndexOf(" +", StringComparison.Ordinal)) : item.Name;
            forged.Name = $"{baseName} +{forged.Plus}";
            forged.Price = item.Price + price / 2;
            switch (slot)
            {
                case ItemSlot.Weapon:
                    forged.Attack.Power += 2;
                    break;
                case ItemSlot.Armor:
                    forged.Armor += 1;
                    break;
                default:
                    forged.Hp += 4;
                    break;
            }
            hero.Equip(forged);
            Record($"forge {heroIndex} {slot.ToString().ToLowerInvariant()}");
            Report.Clear();
            Report.Add(Data.Line("town.forged", hero.Name, forged.Name, price));
            return null;
        }

        // A free level for a journal perk.
        void ForgeFree(Hero hero, ItemSlot slot)
        {
            var item = slot == ItemSlot.Weapon ? hero.Weapon : slot == ItemSlot.Armor ? hero.Armor : hero.Trinket;
            if (!CanForge(item)) return;
            var forged = item.Clone();
            forged.Plus = item.Plus + 1;
            string baseName = item.Plus > 0 ? item.Name.Substring(0, item.Name.LastIndexOf(" +", StringComparison.Ordinal)) : item.Name;
            forged.Name = $"{baseName} +{forged.Plus}";
            if (slot == ItemSlot.Weapon) forged.Attack.Power += 2;
            else if (slot == ItemSlot.Armor) forged.Armor += 1;
            else forged.Hp += 4;
            hero.Equip(forged);
        }

        // ---- New companions (the hardcore mode) ----

        // Classes that could take a fallen hero's place: playable and not in the party now.
        public List<ClassDef> Recruits() =>
            !Hardcore || !Heroes.Any(h => h.Dead) ? new List<ClassDef>() : Data.PlayableClasses.Where(c => Heroes.All(h => h.Class.Id != c.Id)).ToList();

        // A new companion takes the first fallen hero's place, a level below the lowest of the others.
        public string Recruit(string classId)
        {
            string error = InTown();
            if (error != null) return error;
            int index = Heroes.FindIndex(h => h.Dead);
            if (index < 0) return Data.Line("town.no_recruit_place");
            var cls = Recruits().FirstOrDefault(c => c.Id == classId);
            if (cls == null) return Data.Line("town.cant_recruit");
            Record($"recruit {classId}");
            Report.Clear();
            int level = Math.Max(1, Heroes.Where(h => !h.Dead).Select(h => h.Level).DefaultIfEmpty(1).Min() - 1);
            var hero = new Hero(cls, index);
            Heroes[index] = hero;
            Positions[index] = Map.Tile(Site.Town).Hex;
            Report.Add(Data.Line("town.recruited", hero.Name));
            if (level > 1) GainXp(hero, Hero.LevelXp[level - 1]);
            return null;
        }

        // The town's elder, once a chapter.
        public bool CanMeetElder => !_elderMet;

        public string Elder()
        {
            string error = InTown();
            if (error != null) return error;
            if (!CanMeetElder) return Data.Line("town.elder_met");
            Record("elder");
            _elderMet = true;
            _backToTown = true;
            OpenEvent(PickEvent(Chapter.ElderPool), Map.Tile(Site.Town));
            return null;
        }

        public string LeaveTown()
        {
            string error = InTown();
            if (error != null) return error;
            Record("leave");
            Report.Clear();
            EndTurn();
            return null;
        }

        // ---- The bag and equipment ----

        bool CanChangeGear => LevelUp == null && (Screen == AdventureScreen.Map || Screen == AdventureScreen.Town);

        // Puts a bag item on a hero; what was in that slot goes into the bag.
        public string Equip(int bagIndex, int heroIndex)
        {
            if (!CanChangeGear) return Data.Line("town.cant_change_gear");
            if (bagIndex < 0 || bagIndex >= Bag.Count) return Data.Line("town.not_in_bag");
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return Data.Line("town.pick_hero");
            var item = Bag[bagIndex];
            var hero = Heroes[heroIndex];
            if (!hero.CanEquip(item)) return Data.Line("town.cant_equip", hero.Name, item.Name);
            Bag.RemoveAt(bagIndex);
            var old = hero.Equip(item);
            if (old != null) AddToBag(old);
            Record($"equip {bagIndex} {heroIndex}");
            return null;
        }

        // Takes off armor or a trinket into the bag. A weapon only changes for another.
        public string Unequip(int heroIndex, ItemSlot slot)
        {
            if (!CanChangeGear) return Data.Line("town.cant_change_gear");
            if (slot == ItemSlot.Weapon) return Data.Line("town.weapon_swap_only");
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return Data.Line("town.pick_hero");
            var hero = Heroes[heroIndex];
            var item = slot == ItemSlot.Armor ? hero.Armor : hero.Trinket;
            if (item == null) return Data.Line("town.nothing_to_remove");
            hero.Unequip(slot);
            AddToBag(item);
            Record($"unequip {heroIndex} {slot.ToString().ToLowerInvariant()}");
            return null;
        }

        // Consumables that work outside battle: healing, resolve, the cure and the charm (Docs/Combat.md 소모품).
        public static bool UsableOnMap(ItemDef item) => item.Consumable && item.Use.All(e => e.Verb == "heal" || e.Verb == "resolve" || e.Verb == "cleanse" || e.Verb == "luck");

        public string UseItem(int heroIndex, string itemId)
        {
            if (!CanChangeGear) return Data.Line("town.cant_use_now");
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return Data.Line("town.pick_hero");
            var hero = Heroes[heroIndex];
            if (hero.Down) return Data.Line("town.hero_down", hero.Name);
            if (!Data.Items.TryGetValue(itemId ?? "", out var item) || hero.Count(itemId) <= 0) return Data.Line("town.no_consumable");
            if (!UsableOnMap(item)) return Data.Line("town.battle_only", item.Name);
            hero.AddConsumable(itemId, -1);
            Record($"use {heroIndex} {itemId}");
            Report.Clear();
            foreach (var e in item.Use)
            {
                switch (e.Verb)
                {
                    case "heal":
                        int amount = hero.Class.Id == "alchemist" ? e.Amount * 3 / 2 : e.Amount;
                        Report.Add(HealLine(hero, amount));
                        break;
                    case "resolve":
                        hero.GainResolve(e.Amount);
                        Report.Add(Data.Line("town.resolve_gain", hero.Name, e.Amount, hero.Resolve, hero.MaxResolve));
                        break;
                    case "luck":
                        hero.Luck += e.Amount;
                        Report.Add(Data.Line("town.luck_gain", hero.Name, e.Amount));
                        break;
                }
            }
            return null;
        }
    }
}
