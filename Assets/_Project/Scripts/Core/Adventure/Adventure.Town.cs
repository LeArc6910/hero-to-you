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
            return Screen == AdventureScreen.Town ? null : "마을에 있지 않습니다.";
        }

        // The inn's price: dearer in later chapters, cheaper with the bard.
        public int InnPrice => PriceOf(InnCost * (Chapter.Index + 1));

        // The inn: everyone in town gets up, heals half and gets all resolve back.
        public string Rest()
        {
            string error = InTown();
            if (error != null) return error;
            int cost = InnPrice;
            if (Gold < cost) return $"금화가 모자랍니다 (여관 {cost}).";
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
                Report.Add($"{hero.Name}: 생명력 +{hero.Hp - before} ({hero.Hp}/{hero.MaxHp}), 결의 가득" + (wounded ? ", 상처가 나았다" : ""));
            }
            return null;
        }

        // Buys a thing: gear goes to the bag, a consumable to the hero (the one whose turn it is when not given).
        public string Buy(int index, int heroIndex = -1)
        {
            string error = InTown();
            if (error != null) return error;
            if (index < 0 || index >= Shop.Count || Shop[index].Sold) return "살 수 없는 물건입니다.";
            var goods = Shop[index];
            if (heroIndex < 0) heroIndex = MapHero;
            if (heroIndex >= Heroes.Count) return "영웅을 고르세요.";
            if (Gold < goods.Price) return $"금화가 모자랍니다 ({goods.Price}).";
            Gold -= goods.Price;
            Record($"buy {index} {heroIndex}");
            Report.Clear();
            var hero = Heroes[heroIndex];
            if (goods.Goods == "reagent")
            {
                var alchemist = Heroes.First(h => h.Class.Id == "alchemist");
                alchemist.GainReagents(2);
                Report.Add($"{alchemist.Name}: 재료 {alchemist.Reagents}/{alchemist.ReagentMax}");
            }
            else if (goods.Item.Consumable)
            {
                hero.AddConsumable(goods.Item.Id, 1);
                Report.Add($"{hero.Name}: {goods.Item.Name} +1 ({hero.Count(goods.Item.Id)}개)");
            }
            else
            {
                goods.Sold = true;
                Bag.Add(goods.Item);
                Report.Add($"{Korean.Object(goods.Item.Name)} 샀다 (가방)");
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
            if (!CanHearRumors) return "오늘은 들을 소문이 없습니다.";
            int cost = PriceOf(RumorCost);
            if (Gold < cost) return $"금화가 모자랍니다 (소문 {cost}).";
            Gold -= cost;
            _rumorDay = Day;
            Record("rumor");
            Report.Clear();
            var heard = Unknown().Take(RumorHexes).ToList();
            foreach (var tile in heard) _revealed.Add(tile.Hex);
            int enemies = heard.Count(t => t.Content == Content.Enemy);
            Report.Add($"소문으로 {heard.Count}곳을 알게 되었다" + (enemies > 0 ? $" (적 무리 {enemies})" : ""));
            return null;
        }

        public static int SellPrice(ItemDef item) => item.Price / 2;

        public string Sell(int bagIndex)
        {
            string error = InTown();
            if (error != null) return error;
            if (bagIndex < 0 || bagIndex >= Bag.Count) return "가방에 그런 물건이 없습니다.";
            var item = Bag[bagIndex];
            Bag.RemoveAt(bagIndex);
            Gold += SellPrice(item);
            Record($"sell {bagIndex}");
            Report.Clear();
            Report.Add($"{Korean.Object(item.Name)} 팔았다 (금화 +{SellPrice(item)})");
            return null;
        }

        // Helping the town rebuild, once a day: gold for renown (Docs/Narrative.md 명성). Costs more each chapter.
        public int DonationCost => DonateCost * (Chapter.Index + 1);

        public bool CanDonate => Screen == AdventureScreen.Town && _donateDay != Day;

        public string Donate()
        {
            string error = InTown();
            if (error != null) return error;
            if (!CanDonate) return "오늘은 이미 도왔습니다.";
            if (Gold < DonationCost) return $"금화가 모자랍니다 ({DonationCost}).";
            Gold -= DonationCost;
            _donateDay = Day;
            Record("donate");
            Report.Clear();
            Report.Add($"금화 {DonationCost}로 무너진 집을 고치는 일을 도왔다.");
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
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return "영웅을 고르세요.";
            var hero = Heroes[heroIndex];
            var item = slot == ItemSlot.Weapon ? hero.Weapon : slot == ItemSlot.Armor ? hero.Armor : hero.Trinket;
            if (!CanForge(item)) return item == null ? "끼고 있는 것이 없습니다." : $"{Korean.Topic(item.Name)} 더 강화할 수 없습니다.";
            int price = ForgePrice(item);
            if (Gold < price) return $"금화가 모자랍니다 ({price}).";
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
            Report.Add($"{hero.Name}: {Korean.Object(forged.Name)} 벼렸다 (금화 −{price})");
            return null;
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
            if (index < 0) return "새 동료를 맞을 자리가 없습니다.";
            var cls = Recruits().FirstOrDefault(c => c.Id == classId);
            if (cls == null) return "그 직업은 맞을 수 없습니다.";
            Record($"recruit {classId}");
            Report.Clear();
            int level = Math.Max(1, Heroes.Where(h => !h.Dead).Select(h => h.Level).DefaultIfEmpty(1).Min() - 1);
            var hero = new Hero(cls, index);
            Heroes[index] = hero;
            Positions[index] = Map.Tile(Site.Town).Hex;
            Report.Add($"{Korean.Subject(hero.Name)} 일지를 이어받아 함께 걷기로 했다.");
            if (level > 1) GainXp(hero, Hero.LevelXp[level - 1]);
            return null;
        }

        // The town's elder, once a chapter.
        public bool CanMeetElder => !_elderMet;

        public string Elder()
        {
            string error = InTown();
            if (error != null) return error;
            if (!CanMeetElder) return "장로와는 이미 이야기했습니다.";
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
            if (!CanChangeGear) return "지금은 장비를 바꿀 수 없습니다.";
            if (bagIndex < 0 || bagIndex >= Bag.Count) return "가방에 그런 물건이 없습니다.";
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return "영웅을 고르세요.";
            var item = Bag[bagIndex];
            var hero = Heroes[heroIndex];
            if (!hero.CanEquip(item)) return $"{Korean.Topic(hero.Name)} {item.Name}을 들 수 없습니다.";
            Bag.RemoveAt(bagIndex);
            var old = hero.Equip(item);
            if (old != null) Bag.Add(old);
            Record($"equip {bagIndex} {heroIndex}");
            return null;
        }

        // Takes off armor or a trinket into the bag. A weapon only changes for another.
        public string Unequip(int heroIndex, ItemSlot slot)
        {
            if (!CanChangeGear) return "지금은 장비를 바꿀 수 없습니다.";
            if (slot == ItemSlot.Weapon) return "무기는 다른 무기로만 바꿉니다.";
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return "영웅을 고르세요.";
            var hero = Heroes[heroIndex];
            var item = slot == ItemSlot.Armor ? hero.Armor : hero.Trinket;
            if (item == null) return "벗을 것이 없습니다.";
            hero.Unequip(slot);
            Bag.Add(item);
            Record($"unequip {heroIndex} {slot.ToString().ToLowerInvariant()}");
            return null;
        }

        // Consumables that work outside battle: healing, resolve, the cure and the charm (Docs/Combat.md 소모품).
        public static bool UsableOnMap(ItemDef item) => item.Consumable && item.Use.All(e => e.Verb == "heal" || e.Verb == "resolve" || e.Verb == "cleanse" || e.Verb == "luck");

        public string UseItem(int heroIndex, string itemId)
        {
            if (!CanChangeGear) return "지금은 쓸 수 없습니다.";
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return "영웅을 고르세요.";
            var hero = Heroes[heroIndex];
            if (hero.Down) return $"{Korean.Topic(hero.Name)} 쓰러져 있습니다.";
            if (!Data.Items.TryGetValue(itemId ?? "", out var item) || hero.Count(itemId) <= 0) return "그 소모품이 없습니다.";
            if (!UsableOnMap(item)) return $"{Korean.Topic(item.Name)} 전투에서만 씁니다.";
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
                        Report.Add($"{hero.Name}: 결의 +{e.Amount} ({hero.Resolve}/{hero.MaxResolve})");
                        break;
                    case "luck":
                        hero.Luck += e.Amount;
                        Report.Add($"{hero.Name}: 다음 판정 확정 +{e.Amount}");
                        break;
                }
            }
            return null;
        }
    }
}
