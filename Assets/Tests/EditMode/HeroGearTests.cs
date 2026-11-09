using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class HeroGearTests
    {
        static ItemDef Item(string id) => TestData.Data.Items[id];

        [Test]
        public void TheWeaponIsTheBasicAttack()
        {
            var knight = TestData.Hero("knight");
            Assert.That(knight.Skills[0].Name, Is.EqualTo("베기"));
            Assert.That(knight.Skills[0].Power, Is.EqualTo(8));
            knight.Equip(Item("sword_steel"));
            Assert.That(knight.Skills[0].Power, Is.EqualTo(10));
            Assert.That(knight.Weapon.Id, Is.EqualTo("sword_steel"));
        }

        [Test]
        public void AClassHoldsOnlyItsWeaponKinds()
        {
            var knight = TestData.Hero("knight");
            Assert.That(knight.CanEquip(Item("bow_long")), Is.False);
            Assert.That(knight.CanEquip(Item("mace_iron")), Is.True);
            Assert.That(knight.CanEquip(Item("armor_plate")), Is.True);
        }

        [Test]
        public void ArmorAndTrinketsChangeTheNumbers()
        {
            var hunter = TestData.Hero("hunter");
            int armor = hunter.ArmorValue;
            int agi = hunter.Stat(Stat.Agility);
            int maxHp = hunter.MaxHp;
            Assert.That(hunter.Equip(Item("armor_chain")), Is.Null);
            Assert.That(hunter.ArmorValue, Is.EqualTo(armor + 2));
            Assert.That(hunter.Stat(Stat.Agility), Is.EqualTo(agi - 5));
            hunter.Equip(Item("heart_stone"));
            Assert.That(hunter.MaxHp, Is.EqualTo(maxHp + 8));
            Assert.That(hunter.Hp, Is.EqualTo(hunter.MaxHp), "새 장비의 생명력도 찬다(잃은 양은 그대로)");
        }

        [Test]
        public void UpgradesChangeTheHerosOwnSkills()
        {
            var knight = TestData.Hero("knight");
            var other = TestData.Hero("knight");
            knight.ApplyUpgrade(TestData.Data.Upgrades["k_bash"]);
            Assert.That(knight.Skill("knight_bash").Dice, Is.EqualTo(3));
            Assert.That(knight.Skill("knight_bash").Cooldown, Is.EqualTo(1));
            Assert.That(other.Skill("knight_bash").Dice, Is.EqualTo(2), "다른 기사는 그대로");
            knight.ApplyUpgrade(TestData.Data.Upgrades["k_heavy"]);
            knight.Equip(TestData.Data.Items["sword_steel"]);
            Assert.That(knight.Skill(Hero.AttackId).Power, Is.EqualTo(13), "무기를 바꿔도 공격 강화는 남는다");
            int resolve = knight.MaxResolve;
            knight.ApplyUpgrade(TestData.Data.Upgrades["k_oath"]);
            Assert.That(knight.MaxResolve, Is.EqualTo(resolve + 1));
        }
    }
}
