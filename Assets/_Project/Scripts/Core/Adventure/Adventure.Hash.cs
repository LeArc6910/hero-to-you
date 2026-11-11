using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // One number for the whole state of a run (2026-11-11): the same state always gives the same number. Tests use it
    // to check that a save loads back exactly as it was (Docs/Saving.md), and a co-op game can compare it after each
    // command to catch players drifting apart. FNV-1a over the state in a fixed order; nothing here is saved.
    public sealed partial class Adventure
    {
        public ulong StateHash()
        {
            var h = new StateHasher();
            h.Add(Seed).Add(Chapter?.Id).Add((int)Screen).Add(Day).Add(TimeOfDay).Add(MapHero).Add(MovePoints);
            h.Add(Gold).Add(Hope).Add(Renown).Add(Doom).Add(BattlesWon).Add(ChaptersCleared).Add(MidbossesBeaten).Add(Falls);
            h.Add(DungeonRoom).Add(DungeonEvent).Add(DungeonsCleared).Add(ContractsDone).Add(RestedOnDay).Add(_log.Count);
            h.Add(Story?.Id).Add(Event?.Id).Add(Encounter?.Id).Add((int)Kind).Add(LevelUp != null);
            foreach (var at in Positions) h.Add(at.Col).Add(at.Row);
            foreach (var hero in Heroes)
            {
                h.Add(hero.Class.Id).Add(hero.Level).Add(hero.Xp).Add(hero.Hp).Add(hero.MaxHp).Add(hero.Resolve).Add(hero.Grit);
                h.Add(hero.Wounds).Add(hero.Down).Add(hero.Dead).Add(hero.Back).Add(hero.Reagents).Add(hero.Blessing).Add(hero.Luck);
                h.Add(hero.Weapon?.Id).Add(hero.Armor?.Id).Add(hero.Trinket?.Id);
                foreach (var carried in hero.Consumables) h.Add(carried.Key).Add(carried.Value);
                foreach (var upgrade in hero.Upgrades) h.Add(upgrade.Id);
            }
            foreach (var item in Bag) h.Add(item.Id);
            foreach (var item in Shop) h.Add(item.Item?.Id).Add(item.Goods).Add(item.Price).Add(item.Sold);
            foreach (string flag in _flags.OrderBy(f => f, System.StringComparer.Ordinal)) h.Add(flag);
            foreach (string seen in _seen.OrderBy(f => f, System.StringComparer.Ordinal)) h.Add(seen);
            if (Map != null)
                foreach (var tile in Map.Tiles) h.Add((int)tile.Content).Add(tile.Used).Add(tile.Encounter).Add(Knows(tile));
            if (Battle != null)
            {
                h.Add((int)Battle.State).Add(Battle.Events.Count);
                foreach (var unit in Battle.Units) h.Add(unit.Id).Add(unit.Hp).Add(unit.Status.Count);
            }
            foreach (var stream in new[] { _eventRandom, _lootRandom, _levelRandom, _shopRandom, _talkRandom, _mapRandom })
                h.Add(stream?.State ?? 0);
            return h.Value;
        }

        sealed class StateHasher
        {
            public ulong Value { get; private set; } = 14695981039346656037UL;

            public StateHasher Add(ulong value)
            {
                for (int i = 0; i < 8; i++)
                {
                    Value ^= (value >> (8 * i)) & 0xFF;
                    Value *= 1099511628211UL;
                }
                return this;
            }

            public StateHasher Add(int value) => Add((ulong)(uint)value);

            public StateHasher Add(bool value) => Add(value ? 1 : 0);

            public StateHasher Add(string value)
            {
                if (value == null) return Add(ulong.MaxValue);
                Add(value.Length);
                foreach (char c in value) Add((int)c);
                return this;
            }
        }
    }
}
