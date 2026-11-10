using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // The things to do besides the chapter's main line (Docs/Map.md): the dungeon's rooms, the town's contracts,
    // the campfire talks between heroes at dawn, and where each hero stands in battle.
    public sealed partial class Adventure
    {
        public const int DelveHealPercent = 10;
        public const int ContractGold = 15;

        // ---- The dungeon ----

        // Rooms cleared in this chapter's dungeon; leaving keeps them cleared.
        public int DungeonRoom { get; private set; }
        public int DungeonRooms => Chapter.Dungeon.Count;

        // A room is a battle (an encounter id) or an event ("room:<pool>", Docs/Map.md 던전): a find, a trap, someone
        // lost down there. The event's result goes on to the next room; a fight it starts is that room's battle.
        public const string EventRoom = "room:";

        // The event on the screen is a room of the dungeon.
        public bool DungeonEvent { get; private set; }

        void EnterDungeon(MapTile tile)
        {
            if (Chapter.Dungeon.Count == 0 || DungeonRoom >= DungeonRooms)
            {
                EndTurn();
                return;
            }
            OpenRoom(tile);
        }

        void OpenRoom(MapTile tile)
        {
            string room = Chapter.Dungeon[DungeonRoom];
            if (!room.StartsWith(EventRoom))
            {
                PrepareJoin(tile, room, JoinKind.Dungeon);
                return;
            }
            JoinTile = tile;
            DungeonEvent = true;
            OpenEvent(PickEvent(room.Substring(EventRoom.Length)), tile);
            // Those near take part, and they are who rests between rooms (Delve) like the fighters of a battle room.
            _fighters = EventHeroes();
        }

        // An event room is done (its result read, or the fight it started won): on to the next room.
        bool LeaveEventRoom()
        {
            if (!DungeonEvent) return false;
            DungeonEvent = false;
            if (_pendingBattle != null)
            {
                string id = _pendingBattle;
                _pendingBattle = null;
                PrepareJoin(JoinTile, id, JoinKind.Dungeon);
                return true;
            }
            Report.Clear();
            DungeonRoomCleared();
            return true;
        }

        // After a room: the next one costs nothing to look at, but the party is not getting any less hurt.
        void DungeonRoomCleared()
        {
            DungeonRoom++;
            if (DungeonRoom < DungeonRooms)
            {
                Report.Add(Data.Line("adventure.dungeon.room", Chapter.DungeonName, DungeonRoom, DungeonRooms));
                Screen = AdventureScreen.DungeonRest;
                return;
            }
            JoinTile.Used = true;
            DungeonsCleared++;
            int gold = 20 * (Chapter.Index + 1);
            Gold += gold;
            var loot = Loot(Chapter.Index + 2);
            if (loot != null) AddToBag(loot);
            Report.Add(loot != null ? Data.Line("adventure.dungeon.cleared.loot", Chapter.DungeonName, gold, loot.Name) : Data.Line("adventure.dungeon.cleared", Chapter.DungeonName, gold));
            GainRenown(1, Report);
            Screen = AdventureScreen.BattleResult;
        }

        // Goes on to the next room; who fights is chosen again (heroes nearby may join).
        public string Delve()
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.DungeonRest) return Data.Line("adventure.error.not_in_dungeon");
            Record("delve");
            Report.Clear();
            foreach (var hero in _fighters.Where(h => !h.Down)) Report.Add(Data.Line("adventure.dungeon.breather", HealLine(hero, Math.Max(1, hero.MaxHp * DelveHealPercent / 100))));
            OpenRoom(JoinTile);
            return null;
        }

        public string LeaveDungeon()
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.DungeonRest) return Data.Line("adventure.error.not_in_dungeon");
            Record("leavedungeon");
            Report.Clear();
            Report.Add(Data.Line("adventure.dungeon.left", Chapter.DungeonName));
            EndTurn();
            return null;
        }

        // ---- Contracts ----

        // The town's job: clear one group of the map (it is marked and revealed), then come back for the reward.
        public sealed class ContractDef
        {
            internal ContractDef(MapTile tile, string encounter, int gold)
            {
                Tile = tile;
                Encounter = encounter;
                Gold = gold;
            }

            public MapTile Tile { get; }
            public string Encounter { get; }
            public int Gold { get; }
            public bool Done { get; internal set; }
        }

        public ContractDef Contract { get; private set; }

        public bool CanTakeContract => Screen == AdventureScreen.Town && Contract == null && ContractTargets().Any();

        IEnumerable<MapTile> ContractTargets()
        {
            var town = Map.Tile(Site.Town).Hex;
            return Map.Tiles.Where(t => t.Content == Content.Enemy && t.Hex.Distance(town) >= 3).OrderBy(t => t.Hex.Distance(town)).ThenBy(t => t.Hex.Row).ThenBy(t => t.Hex.Col);
        }

        public string TakeContract()
        {
            string error = InTown();
            if (error != null) return error;
            if (!CanTakeContract) return Contract != null ? Data.Line("adventure.error.contract_taken") : Data.Line("adventure.error.no_contract");
            var targets = ContractTargets().Take(3).ToList();
            var tile = targets[_shopRandom.Next(targets.Count)];
            Contract = new ContractDef(tile, tile.Encounter, ContractGold * (Chapter.Index + 2));
            _revealed.Add(tile.Hex);
            Record("contract");
            Report.Clear();
            var monsters = Data.Encounters[tile.Encounter].Monsters;
            Report.Add(Data.Line("adventure.contract.taken", tile.Hex, monsters[0].Name, Contract.Gold));
            return null;
        }

        // A group was beaten or scared off: the contract's target, if it was that one.
        void GroupCleared(MapTile tile)
        {
            if (Contract == null || Contract.Done || Contract.Tile != tile) return;
            Contract.Done = true;
            Report.Add(Data.Line("adventure.contract.done"));
        }

        void PayContract()
        {
            if (Contract == null || !Contract.Done) return;
            ContractsDone++;
            Gold += Contract.Gold;
            Report.Add(Data.Line("adventure.contract.paid", Contract.Gold));
            GainRenown(1, Report);
            Contract = null;
        }

        // ---- Campfire talks ----

        readonly List<(int day, TalkDef talk)> _talks = new List<(int, TalkDef)>();
        readonly HashSet<string> _heardTalks = new HashSet<string>();

        // Every talk heard this run, with its day; the last one is the newest.
        public IReadOnlyList<(int day, TalkDef talk)> Talks => _talks;

        // At dawn, two heroes who are both up may talk: an unheard talk for their classes and this chapter. Both
        // feel better for it (resolve +1).
        void DawnTalk()
        {
            int chapter = Chapter.Index + 1;
            var options = Data.Talks.Where(t => !_heardTalks.Contains(t.Id) && (t.Chapter == 0 || t.Chapter == chapter) && Up(t.A) && Up(t.B) && FlagOk(t.Requires)).ToList();
            if (options.Count == 0) return;
            // This chapter's own talks first.
            var own = options.Where(t => t.Chapter == chapter).ToList();
            var pick = (own.Count > 0 ? own : options)[_talkRandom.Next(own.Count > 0 ? own.Count : options.Count)];
            _heardTalks.Add(pick.Id);
            _talks.Add((Day, pick));
            foreach (string cls in new[] { pick.A, pick.B }) Heroes.First(h => h.Class.Id == cls).GainResolve(1);
            Report.Add(Data.Line("adventure.talk", Data.Classes[pick.A].Name, Data.Classes[pick.B].Name));
        }

        bool Up(string classId) => Heroes.Any(h => !h.Down && h.Class.Id == classId);

        // ---- Rows ----

        // Puts a hero in the front or the back row (Docs/Combat.md 줄); outside battle only.
        public string SetRow(int heroIndex, bool back)
        {
            if (!CanChangeGear) return Data.Line("adventure.error.cannot_change_row");
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return Data.Line("adventure.error.pick_hero");
            var hero = Heroes[heroIndex];
            if (hero.Back == back) return back ? Data.Line("adventure.error.already_back") : Data.Line("adventure.error.already_front");
            hero.Back = back;
            Record($"row {heroIndex} {(back ? "back" : "front")}");
            return null;
        }
    }
}
