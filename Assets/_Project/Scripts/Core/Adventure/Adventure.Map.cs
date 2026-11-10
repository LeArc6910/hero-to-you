using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // The map (Docs/Map.md): filling it from the seed, fog and sight, map turns with the movement roll, each class's
    // map perk, moving (and running into what fog hid), camping, arriving, and the turn of the day.
    public sealed partial class Adventure
    {
        public const int MoveBase = 2;
        public const int MoveDice = 3;
        public const int CampPercent = 20;
        public const int Sight = 2;
        public const int SenseRange = 4;
        public const int StartEnemies = 6;
        public const int StartEvents = 10;
        public const int StartTreasures = 3;
        public const int StartHerbs = 3;

        readonly HashSet<Hex> _explored = new HashSet<Hex>();
        readonly HashSet<Hex> _revealed = new HashSet<Hex>();
        int _rerollDay;
        Hex _cameFrom;

        public WorldMap Map { get; private set; }
        public List<Hex> Positions { get; } = new List<Hex>();

        // Whose map turn it is, and the movement rolled (-1 before rolling, 0 once moved).
        public int MapHero { get; private set; }
        public int MovePoints { get; private set; } = -1;
        public CheckResult MoveRoll { get; private set; }
        public Hero Current => Heroes[MapHero];

        bool Standing(string classId) => Heroes.Any(h => !h.Down && h.Class.Id == classId);

        // ---- Filling the map ----

        void Populate()
        {
            var start = Map.Tile(Site.Start).Hex;
            var free = Map.Tiles.Where(t => Map.Def.Passable(t.Hex) && t.Site == Site.None && t.Hex.Distance(start) >= 2).ToList();
            Place(free, StartEnemies, Content.Enemy, t => t.Hex.Distance(start) >= 3, 1);
            Place(free, StartEvents, Content.Event, t => t.Terrain != Terrain.Road, 0);
            Place(free, StartTreasures, Content.Treasure, t => true, 0);
            Place(free, StartHerbs, Content.Herb, t => t.Terrain != Terrain.Road && t.Terrain != Terrain.Ruins, 0);
        }

        void Place(List<MapTile> free, int count, Content content, Func<MapTile, bool> fits, int danger)
        {
            for (int i = 0; i < count; i++)
            {
                var options = free.Where(t => t.Content == Content.None && fits(t)).ToList();
                if (options.Count == 0) return;
                var tile = options[_mapRandom.Next(options.Count)];
                tile.Content = content;
                if (content == Content.Enemy) tile.Encounter = PickEncounter(tile, danger);
            }
        }

        string PickEncounter(MapTile tile, int danger)
        {
            int chapter = Chapter.Index + 1;
            var mine = Data.Encounters.Values.Where(e => e.Danger == danger && (e.Chapter == 0 || e.Chapter == chapter)).OrderBy(e => e.Id).ToList();
            string terrain = TerrainInfo.Code(tile.Terrain);
            var fitting = mine.Where(e => e.Terrains.Contains(terrain)).ToList();
            if (fitting.Count == 0) fitting = mine;
            return fitting[_mapRandom.Next(fitting.Count)].Id;
        }

        // A new group somewhere quiet, away from every hero (each new day).
        void Spawn()
        {
            var candidates = Map.Tiles.Where(t => Map.Def.Passable(t.Hex) && t.Site == Site.None && t.Content == Content.None
                && Positions.All(p => p.Distance(t.Hex) >= 3)).ToList();
            if (candidates.Count == 0) return;
            var tile = candidates[_mapRandom.Next(candidates.Count)];
            tile.Content = Content.Enemy;
            tile.Encounter = PickEncounter(tile, Doom >= 4 ? 2 : 1);
            Report.Add(Doom >= 4 ? Data.Line("adventure.spawn.strong") : Data.Line("adventure.spawn.new"));
        }

        // ---- Fog and sight ----

        // How far a hero sees: two hexes, the hunter one more, one less at night (not the necromancer).
        public int Vision(int hero)
        {
            var h = Heroes[hero];
            int sight = Sight + (h.Class.Id == "hunter" ? 1 : 0) - (Night && h.Class.Id != "necromancer" ? 1 : 0);
            return Math.Max(1, sight);
        }

        public bool Explored(Hex hex) => _explored.Contains(hex);

        // Whether the party knows what the hex holds: the places are on the journal's map; the rest once seen, heard of
        // in a rumor, or felt by the seer (events and treasure anywhere) and the beastmaster (enemies near).
        public bool Knows(MapTile tile)
        {
            if (tile.Content == Content.None || tile.Site != Site.None) return true;
            if (_explored.Contains(tile.Hex) || _revealed.Contains(tile.Hex)) return true;
            if (tile.Content != Content.Enemy && Standing("seer")) return true;
            if (tile.Content == Content.Enemy)
                for (int i = 0; i < Heroes.Count; i++)
                    if (!Heroes[i].Down && Heroes[i].Class.Id == "beastmaster" && Positions[i].Distance(tile.Hex) <= SenseRange) return true;
            return false;
        }

        void UpdateSight()
        {
            for (int i = 0; i < Heroes.Count; i++)
            {
                if (Heroes[i].Down) continue;
                int sight = Vision(i);
                foreach (var hex in Map.Def.Hexes())
                    if (hex.Distance(Positions[i]) <= sight) _explored.Add(hex);
            }
        }

        // ---- Moving ----

        // Movement points per terrain for the hero: the hunter walks forests, the beastmaster forests and swamps, as
        // if they were open ground.
        public int TerrainCost(Hero hero, Terrain terrain)
        {
            int cost = TerrainInfo.Cost(terrain);
            if (cost <= 1) return cost;
            if (hero.Class.Id == "hunter" && terrain == Terrain.Forest) return 1;
            if (hero.Class.Id == "beastmaster" && (terrain == Terrain.Forest || terrain == Terrain.Swamp)) return 1;
            if (hero.Class.Id == "monk" && (terrain == Terrain.Snow || terrain == Terrain.Sand || terrain == Terrain.Shallows)) return 1;
            return cost;
        }

        // The chance of one movement die: agility, or the elementalist's intellect when higher.
        public int MoveChance(Hero hero)
        {
            int stat = hero.Stat(Stat.Agility);
            if (hero.Class.Id == "elementalist") stat = Math.Max(stat, hero.Stat(Stat.Intellect));
            return Dice.ClampChance(stat);
        }

        void StartMapTurn(int hero)
        {
            MapHero = hero;
            MovePoints = -1;
            MoveRoll = null;
            Screen = AdventureScreen.Map;
        }

        string MapTurn()
        {
            string blocked = Blocked();
            if (blocked != null) return blocked;
            if (Screen != AdventureScreen.Map) return Data.Line("adventure.error.not_map_turn");
            if (Current.Down) return Data.Line("adventure.error.hero_down", Current.Name);
            return null;
        }

        // The movement roll (Docs/Map.md): 2 + successes of three dice, one less at night (not the necromancer), at
        // least 1.
        public string RollMove()
        {
            string error = MapTurn();
            if (error != null) return error;
            if (MovePoints >= 0) return Data.Line("adventure.error.already_rolled");
            DoRoll();
            Record("roll");
            return null;
        }

        void DoRoll()
        {
            var hero = Current;
            int sure = Math.Min(MoveDice, hero.Blessing + hero.Luck);
            hero.Blessing = 0;
            hero.Luck = 0;
            MoveRoll = _moveFate.Roll(MoveDice, MoveChance(hero), sure);
            hero.AddGrit(MoveRoll.RolledFailures);
            bool tired = Night && hero.Class.Id != "necromancer";
            MovePoints = Math.Max(1, MoveBase + MoveRoll.Successes - (tired ? 1 : 0));
        }

        // The gambler's perk: once a day, roll the movement again.
        public bool CanReroll => Screen == AdventureScreen.Map && MovePoints > 0 && Current.Class.Id == "gambler" && _rerollDay != Day;

        public string Reroll()
        {
            string error = MapTurn();
            if (error != null) return error;
            if (!CanReroll) return Data.Line("adventure.error.cannot_reroll");
            _rerollDay = Day;
            DoRoll();
            Record("reroll");
            return null;
        }

        bool StopsFor(MapTile tile) => Map.Stops(tile) && Knows(tile);

        // Where the current hero can move with the rolled points, and what each costs.
        public Dictionary<Hex, int> Reachable()
        {
            if (Screen != AdventureScreen.Map || MovePoints <= 0) return new Dictionary<Hex, int>();
            var hero = Current;
            return Map.Reach(Positions[MapHero], MovePoints, StopsFor, t => TerrainCost(hero, t));
        }

        // The hexes the last move walked through, start to stop, and whose they were: for the screen to show the walk
        // (a new list each move). Not part of the state that is saved or decided by.
        public IReadOnlyList<Hex> LastWalk { get; private set; } = new List<Hex>();
        public int LastWalker { get; private set; } = -1;

        public string Move(Hex to)
        {
            string error = MapTurn();
            if (error != null) return error;
            if (MovePoints < 0) return Data.Line("adventure.error.roll_first");
            if (!Reachable().ContainsKey(to)) return Data.Line("adventure.error.unreachable");
            var hero = Current;
            var path = Map.Path(Positions[MapHero], to, MovePoints, StopsFor, t => TerrainCost(hero, t));
            Record($"move {to.Col} {to.Row}");
            Report.Clear();
            MovePoints = 0;
            // Walk the way; what fog hid stops the hero where it is.
            var stop = to;
            var walked = new List<Hex> { path[0] };
            int walker = MapHero;
            for (int i = 1; i < path.Count; i++)
            {
                _cameFrom = path[i - 1];
                Positions[MapHero] = path[i];
                walked.Add(path[i]);
                UpdateSight();
                if (i < path.Count - 1 && Map.Stops(Map.Tile(path[i])))
                {
                    stop = path[i];
                    Report.Add(Data.Line("adventure.move.interrupted"));
                    break;
                }
            }
            LastWalk = walked;
            LastWalker = walker;
            Arrive(stop);
            return null;
        }

        // Rests where the hero stands instead of moving: some health and one resolve. The knight keeps watch (the
        // others on the hex +1 resolve and 10%), the cleric tends (40% for itself, 20% for the others), the alchemist
        // gathers (a reagent).
        public string Camp()
        {
            string error = MapTurn();
            if (error != null) return error;
            if (MovePoints >= 0) return Data.Line("adventure.error.camp_after_roll");
            var hero = Current;
            Record("camp");
            Report.Clear();
            int percent = hero.Class.Id == "cleric" ? CampPercent * 2 : CampPercent;
            Report.Add(Data.Line("adventure.camp.rest", HealLine(hero, Math.Max(1, hero.MaxHp * percent / 100))));
            hero.GainResolve(1);
            var here = Positions[MapHero];
            foreach (var other in Heroes.Where((h, i) => i != MapHero && !h.Down && Positions[i] == here))
            {
                if (hero.Class.Id == "cleric") Report.Add(Data.Line("adventure.camp.cleric", HealLine(other, Math.Max(1, other.MaxHp * CampPercent / 100))));
                if (hero.Class.Id == "knight")
                {
                    other.GainResolve(1);
                    Report.Add(Data.Line("adventure.camp.knight", HealLine(other, Math.Max(1, other.MaxHp / 10))));
                }
            }
            if (hero.Class.Id == "alchemist")
            {
                hero.GainReagents(1);
                Report.Add(Data.Line("adventure.camp.gather", hero.Name, hero.Reagents, hero.ReagentMax));
            }
            EndTurn();
            return null;
        }

        // Ends the turn where the hero stands.
        public string Stay()
        {
            string error = MapTurn();
            if (error != null) return error;
            Record("stay");
            Report.Clear();
            EndTurn();
            return null;
        }

        void Arrive(Hex hex)
        {
            foreach (var other in Heroes.Where((h, i) => i != MapHero && h.Down && !h.Dead && Positions[i] == hex))
            {
                other.Hp = 1;
                Report.Add(Data.Line("adventure.help_up", Current.Name, other.Name));
            }
            var tile = Map.Tile(hex);
            switch (tile.Content)
            {
                case Content.Enemy:
                    PrepareJoin(tile, tile.Encounter, JoinKind.Field);
                    return;
                case Content.Event:
                    OpenEvent(PickEvent(Chapter.WildPool), tile);
                    return;
                case Content.Treasure:
                    OpenEvent(PickEvent(Chapter.ChestPool), tile);
                    return;
                case Content.Herb:
                    OpenEvent(PickEvent(Chapter.HerbPool), tile);
                    return;
            }
            switch (tile.Site)
            {
                case Site.Town:
                    OpenTown();
                    return;
                case Site.Shrine when !tile.Used:
                    OpenEvent(PickEvent(Chapter.ShrinePool), tile);
                    return;
                case Site.Quest when !tile.Used:
                    OpenEvent(PickEvent(Chapter.QuestPool), tile);
                    return;
                case Site.Clue when !tile.Used:
                    OpenEvent(PickEvent(Chapter.CluePool), tile);
                    return;
                case Site.Camp when !tile.Used:
                    PrepareJoin(tile, Chapter.Midboss, JoinKind.Camp);
                    return;
                case Site.Dungeon when !tile.Used:
                    EnterDungeon(tile);
                    return;
                case Site.Boss when !tile.Used:
                    PrepareJoin(tile, Chapter.Boss, JoinKind.Boss);
                    return;
            }
            EndTurn();
        }

        void EndTurn()
        {
            if (Heroes.All(h => h.Down))
            {
                Screen = AdventureScreen.Defeat;
                return;
            }
            int next = MapHero;
            for (int step = 0; step < Heroes.Count; step++)
            {
                next = (next + 1) % Heroes.Count;
                if (next == 0) AdvanceTime();
                if (Screen == AdventureScreen.Story || Screen == AdventureScreen.Defeat) return;
                if (!Heroes[next].Down) break;
            }
            StartMapTurn(next);
        }

        void AdvanceTime()
        {
            TimeOfDay++;
            if (TimeOfDay < TimesOfDay) return;
            TimeOfDay = 0;
            Day++;
            Doom++;
            Report.Add(Data.Line("adventure.dawn", Day, Doom, DoomMax));
            foreach (var hero in Heroes.Where(h => h.Down && !h.Dead))
            {
                if (Hope <= 0) continue;
                Hope--;
                hero.Hp = 1;
                Report.Add(Data.Line("adventure.dawn.hope_revive", hero.Name, Hope));
            }
            if (Doom >= DoomMax)
            {
                ShowStory(Chapter.Rift, Then.Defeat);
                return;
            }
            Spawn();
            DawnTalk();
            UpdateSight();
        }

        // ---- Test and playtest set-up ----

        internal void Teleport(int hero, Hex hex)
        {
            Positions[hero] = hex;
            UpdateSight();
        }

        internal void SetFlag(string flag) => _flags.Add(flag);

        internal void AddRenown(int amount) => GainRenown(amount, Report);

        // The chapter's boss beaten without the battle.
        internal void EndChapterForTest()
        {
            Map.Tile(Site.Boss).Used = true;
            EndChapter();
        }
    }
}
