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

        void EnterDungeon(MapTile tile)
        {
            if (Chapter.Dungeon.Count == 0 || DungeonRoom >= DungeonRooms)
            {
                EndTurn();
                return;
            }
            PrepareJoin(tile, Chapter.Dungeon[DungeonRoom], JoinKind.Dungeon);
        }

        // After a room: the next one costs nothing to look at, but the party is not getting any less hurt.
        void DungeonRoomCleared()
        {
            DungeonRoom++;
            if (DungeonRoom < DungeonRooms)
            {
                Report.Add($"{Chapter.DungeonName}: 방 {DungeonRoom}/{DungeonRooms}을 지났다. 더 들어가거나 나갈 수 있다.");
                Screen = AdventureScreen.DungeonRest;
                return;
            }
            JoinTile.Used = true;
            int gold = 20 * (Chapter.Index + 1);
            Gold += gold;
            var loot = Loot(Chapter.Index + 2);
            if (loot != null) Bag.Add(loot);
            Report.Add($"{Chapter.DungeonName}의 가장 깊은 방: 금화 +{gold}" + (loot != null ? $", {Korean.Object(loot.Name)} 얻었다 (가방)" : ""));
            GainRenown(1, Report);
            Screen = AdventureScreen.BattleResult;
        }

        // Goes on to the next room; who fights is chosen again (heroes nearby may join).
        public string Delve()
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.DungeonRest) return "던전 안이 아닙니다.";
            Record("delve");
            Report.Clear();
            foreach (var hero in _fighters.Where(h => !h.Down)) Report.Add(HealLine(hero, Math.Max(1, hero.MaxHp * DelveHealPercent / 100)) + " (숨 고르기)");
            PrepareJoin(JoinTile, Chapter.Dungeon[DungeonRoom], JoinKind.Dungeon);
            return null;
        }

        public string LeaveDungeon()
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.DungeonRest) return "던전 안이 아닙니다.";
            Record("leavedungeon");
            Report.Clear();
            Report.Add($"{Chapter.DungeonName}에서 나왔다. 지난 방은 다시 싸우지 않아도 된다.");
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
            if (!CanTakeContract) return Contract != null ? "이미 맡은 의뢰가 있습니다." : "맡을 의뢰가 없습니다.";
            var targets = ContractTargets().Take(3).ToList();
            var tile = targets[_shopRandom.Next(targets.Count)];
            Contract = new ContractDef(tile, tile.Encounter, ContractGold * (Chapter.Index + 2));
            _revealed.Add(tile.Hex);
            Record("contract");
            Report.Clear();
            var monsters = Data.Encounters[tile.Encounter].Monsters;
            Report.Add($"의뢰를 맡았다: {tile.Hex} 칸의 {monsters[0].Name} 무리를 쫓아낸다 (보상 금화 {Contract.Gold}, 명성 +1)");
            return null;
        }

        // A group was beaten or scared off: the contract's target, if it was that one.
        void GroupCleared(MapTile tile)
        {
            if (Contract == null || Contract.Done || Contract.Tile != tile) return;
            Contract.Done = true;
            Report.Add("의뢰한 무리를 쫓아냈다. 마을에 가면 보상을 받는다.");
        }

        void PayContract()
        {
            if (Contract == null || !Contract.Done) return;
            Gold += Contract.Gold;
            Report.Add($"의뢰 보상: 금화 +{Contract.Gold}");
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
            Report.Add($"모닥불 곁에서 {Data.Classes[pick.A].Name}와 {Data.Classes[pick.B].Name}가 이야기를 나눴다 (둘 다 결의 +1)");
        }

        bool Up(string classId) => Heroes.Any(h => !h.Down && h.Class.Id == classId);

        // ---- Rows ----

        // Puts a hero in the front or the back row (Docs/Combat.md 줄); outside battle only.
        public string SetRow(int heroIndex, bool back)
        {
            if (!CanChangeGear) return "지금은 줄을 바꿀 수 없습니다.";
            if (heroIndex < 0 || heroIndex >= Heroes.Count) return "영웅을 고르세요.";
            var hero = Heroes[heroIndex];
            if (hero.Back == back) return back ? "이미 뒷줄입니다." : "이미 앞줄입니다.";
            hero.Back = back;
            Record($"row {heroIndex} {(back ? "back" : "front")}");
            return null;
        }
    }
}
