using System;
using System.Linq;

namespace Game.Core
{
    // The town (Docs/Map.md): the inn, the shop and the elder; and the bag and equipment, which can be changed on
    // the map or in town.
    public sealed partial class Adventure
    {
        public const int ShopItems = 6;

        void OpenTown()
        {
            if (_shopDay != Day) Restock();
            Screen = AdventureScreen.Town;
        }

        // A new stock each day: six items of tier 1–2 (more of tier 2 as days pass), potions and, for an alchemist,
        // reagents.
        void Restock()
        {
            _shopDay = Day;
            Shop.Clear();
            var pool = Data.ItemList.Where(i => i.Price > 0 && i.Tier <= (Day >= 3 ? 3 : 2)).ToList();
            for (int i = 0; i < ShopItems && pool.Count > 0; i++)
            {
                int pick = _random.Next(pool.Count);
                Shop.Add(new ShopItem { Item = pool[pick], Price = pool[pick].Price });
                pool.RemoveAt(pick);
            }
            Shop.Add(new ShopItem { Goods = "potion", Price = PotionPrice });
            if (Heroes.Any(h => h.Class.Id == "alchemist")) Shop.Add(new ShopItem { Goods = "reagent", Price = ReagentPrice });
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

        // The inn: everyone in town gets up, heals half and gets all resolve back.
        public string Rest()
        {
            string error = InTown();
            if (error != null) return error;
            if (Gold < InnCost) return $"금화가 모자랍니다 (여관 {InnCost}).";
            Gold -= InnCost;
            Record("rest");
            Report.Clear();
            foreach (int i in TownHeroes())
            {
                var hero = Heroes[i];
                if (hero.Down) hero.Hp = 1;
                int before = hero.Hp;
                hero.Heal(Math.Max(1, hero.MaxHp * InnPercent / 100));
                hero.GainResolve(hero.MaxResolve);
                Report.Add($"{hero.Name}: 생명력 +{hero.Hp - before} ({hero.Hp}/{hero.MaxHp}), 결의 가득");
            }
            return null;
        }

        public string Buy(int index)
        {
            string error = InTown();
            if (error != null) return error;
            if (index < 0 || index >= Shop.Count || Shop[index].Sold) return "살 수 없는 물건입니다.";
            var item = Shop[index];
            if (Gold < item.Price) return $"금화가 모자랍니다 ({item.Price}).";
            Gold -= item.Price;
            Record($"buy {index}");
            Report.Clear();
            switch (item.Goods)
            {
                case "potion":
                    Current.Potions++;
                    Report.Add($"{Current.Name}: 회복 물약 +1 ({Current.Potions}개)");
                    break;
                case "reagent":
                    var alchemist = Heroes.First(h => h.Class.Id == "alchemist");
                    alchemist.GainReagents(2);
                    Report.Add($"{alchemist.Name}: 재료 {alchemist.Reagents}/{alchemist.ReagentMax}");
                    break;
                default:
                    item.Sold = true;
                    Bag.Add(item.Item);
                    Report.Add($"{Korean.Object(item.Item.Name)} 샀다 (가방)");
                    break;
            }
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

        public bool CanMeetElder => !HasFlag("elder_met");

        public string Elder()
        {
            string error = InTown();
            if (error != null) return error;
            if (!CanMeetElder) return "장로와는 이미 이야기했습니다.";
            Record("elder");
            _backToTown = true;
            OpenEvent(PickEvent("elder"), Map.Tile(Site.Town));
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
    }
}
