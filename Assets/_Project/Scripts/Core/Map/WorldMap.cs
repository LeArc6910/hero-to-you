using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    // A hex on the chapter map, in "odd-r" offset coordinates: rows of hexes, every odd row pushed half a hex right
    // (Docs/Map.md). Distances go through cube coordinates.
    public readonly struct Hex : IEquatable<Hex>
    {
        public Hex(int col, int row)
        {
            Col = col;
            Row = row;
        }

        public int Col { get; }
        public int Row { get; }

        static readonly int[,] EvenRow = { { 1, 0 }, { 0, -1 }, { -1, -1 }, { -1, 0 }, { -1, 1 }, { 0, 1 } };
        static readonly int[,] OddRow = { { 1, 0 }, { 1, -1 }, { 0, -1 }, { -1, 0 }, { 0, 1 }, { 1, 1 } };

        public IEnumerable<Hex> Neighbors()
        {
            var offsets = (Row & 1) == 0 ? EvenRow : OddRow;
            for (int i = 0; i < 6; i++) yield return new Hex(Col + offsets[i, 0], Row + offsets[i, 1]);
        }

        public int Distance(Hex other)
        {
            int ax = Col - (Row - (Row & 1)) / 2;
            int bx = other.Col - (other.Row - (other.Row & 1)) / 2;
            int dx = ax - bx;
            int dz = Row - other.Row;
            int dy = -dx - dz;
            return Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
        }

        public bool Equals(Hex other) => Col == other.Col && Row == other.Row;
        public override bool Equals(object obj) => obj is Hex other && Equals(other);
        public override int GetHashCode() => Col * 1000 + Row;
        public static bool operator ==(Hex a, Hex b) => a.Equals(b);
        public static bool operator !=(Hex a, Hex b) => !a.Equals(b);
        public override string ToString() => $"{Col},{Row}";
    }

    public enum Terrain
    {
        Plains,
        Forest,
        Swamp,
        Road,
        Ruins,
        Shallows,
        Sand,
        Water,
        Mountain,
        Snow,
        // Cracked ground near the rift.
        Rift,
    }

    // The fixed places of a chapter map.
    public enum Site
    {
        None,
        Start,
        Town,
        Shrine,
        Quest,
        Boss,
        // A clue for the quest (the watchtower), and the middle boss's camp.
        Clue,
        Camp,
        // A dungeon: rooms of battles one after another, treasure at the end (Docs/Map.md 던전).
        Dungeon,
    }

    // What a hex holds that the seed placed: an enemy group, an event (?), treasure or herbs.
    public enum Content
    {
        None,
        Enemy,
        Event,
        Treasure,
        Herb,
    }

    public static class TerrainInfo
    {
        public static string Name(Terrain terrain) => terrain switch
        {
            Terrain.Plains => "들판",
            Terrain.Forest => "숲",
            Terrain.Swamp => "늪",
            Terrain.Road => "길",
            Terrain.Ruins => "폐허",
            Terrain.Shallows => "얕은 물",
            Terrain.Sand => "모래톱",
            Terrain.Water => "물",
            Terrain.Mountain => "산",
            Terrain.Snow => "눈밭",
            Terrain.Rift => "갈라진 땅",
            _ => terrain.ToString(),
        };

        // Movement points to step into it; 0 for a hex nobody can enter.
        public static int Cost(Terrain terrain) => terrain switch
        {
            Terrain.Forest => 2,
            Terrain.Swamp => 2,
            Terrain.Shallows => 2,
            Terrain.Snow => 2,
            Terrain.Rift => 2,
            Terrain.Water => 0,
            Terrain.Mountain => 0,
            _ => 1,
        };

        public static string Code(Terrain terrain) => terrain.ToString().ToLowerInvariant();
    }

    // A chapter map as drawn in Data/Maps (one letter a hex), read once.
    public sealed class MapDef
    {
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        internal Terrain[] Terrains { get; set; }
        internal Site[] Sites { get; set; }

        public bool Inside(Hex hex) => hex.Col >= 0 && hex.Row >= 0 && hex.Col < Width && hex.Row < Height;
        public Terrain TerrainAt(Hex hex) => Terrains[hex.Row * Width + hex.Col];
        public Site SiteAt(Hex hex) => Sites[hex.Row * Width + hex.Col];
        public bool Passable(Hex hex) => Inside(hex) && TerrainInfo.Cost(TerrainAt(hex)) > 0;

        public IEnumerable<Hex> Hexes()
        {
            for (int row = 0; row < Height; row++)
                for (int col = 0; col < Width; col++)
                    yield return new Hex(col, row);
        }

        public Hex Find(Site site) => Hexes().First(h => SiteAt(h) == site);

        // Hexes reachable from `from` by stepping through passable hexes.
        public HashSet<Hex> Connected(Hex from)
        {
            var seen = new HashSet<Hex> { from };
            var queue = new Queue<Hex>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var hex = queue.Dequeue();
                foreach (var next in hex.Neighbors())
                    if (Passable(next) && seen.Add(next)) queue.Enqueue(next);
            }
            return seen;
        }

        // Reads the letters of Docs/Map.md. Throws with the line on a mistake.
        public static MapDef Parse(string text, string file)
        {
            var rows = (text ?? "").Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            if (rows.Count == 0) throw new FormatException($"{file}: 지도가 비었습니다.");
            int width = rows[0].Length;
            var map = new MapDef { Width = width, Height = rows.Count, Terrains = new Terrain[width * rows.Count], Sites = new Site[width * rows.Count] };
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Length != width) throw new FormatException($"{file}: 지도 {r + 1}번째 줄의 길이({rows[r].Length})가 첫 줄({width})과 다릅니다.");
                for (int c = 0; c < width; c++)
                {
                    char ch = rows[r][c];
                    var (terrain, site) = ch switch
                    {
                        '.' => (Terrain.Plains, Site.None),
                        'f' => (Terrain.Forest, Site.None),
                        's' => (Terrain.Swamp, Site.None),
                        'r' => (Terrain.Road, Site.None),
                        'u' => (Terrain.Ruins, Site.None),
                        'w' => (Terrain.Shallows, Site.None),
                        'b' => (Terrain.Sand, Site.None),
                        'n' => (Terrain.Snow, Site.None),
                        'x' => (Terrain.Rift, Site.None),
                        '~' => (Terrain.Water, Site.None),
                        '^' => (Terrain.Mountain, Site.None),
                        'S' => (Terrain.Plains, Site.Start),
                        'T' => (Terrain.Plains, Site.Town),
                        'H' => (Terrain.Plains, Site.Shrine),
                        'R' => (Terrain.Ruins, Site.Quest),
                        'B' => (Terrain.Ruins, Site.Boss),
                        'C' => (Terrain.Ruins, Site.Clue),
                        'M' => (Terrain.Plains, Site.Camp),
                        'D' => (Terrain.Ruins, Site.Dungeon),
                        _ => throw new FormatException($"{file}: 지도 {r + 1}번째 줄의 '{ch}'는 땅이나 장소 글자가 아닙니다."),
                    };
                    map.Terrains[r * width + c] = terrain;
                    map.Sites[r * width + c] = site;
                }
            }
            foreach (var site in new[] { Site.Start, Site.Town, Site.Quest, Site.Boss, Site.Clue, Site.Camp })
            {
                int count = map.Sites.Count(s => s == site);
                if (count != 1) throw new FormatException($"{file}: 장소 {site}가 {count}개입니다(하나여야 합니다).");
            }
            if (map.Sites.Count(s => s == Site.Dungeon) > 1) throw new FormatException($"{file}: 던전(D)은 하나까지입니다.");
            var reach = map.Connected(map.Find(Site.Start));
            foreach (var hex in map.Hexes())
                if (map.SiteAt(hex) != Site.None && !reach.Contains(hex)) throw new FormatException($"{file}: {hex}의 장소에 시작 칸에서 갈 수 없습니다.");
            return map;
        }
    }

    // One hex of the adventure's map now: what the seed put there and whether it is used up.
    public sealed class MapTile
    {
        internal MapTile(Hex hex, Terrain terrain, Site site)
        {
            Hex = hex;
            Terrain = terrain;
            Site = site;
        }

        public Hex Hex { get; }
        public Terrain Terrain { get; }
        public Site Site { get; }
        public Content Content { get; internal set; }

        // The encounter id of an enemy group.
        public string Encounter { get; internal set; }

        // A shrine or the quest stone once its event is done.
        public bool Used { get; internal set; }
    }

    // The chapter map during an adventure: tiles with their contents and movement over them.
    public sealed class WorldMap
    {
        readonly MapTile[] _tiles;

        internal WorldMap(MapDef def)
        {
            Def = def;
            _tiles = def.Hexes().Select(h => new MapTile(h, def.TerrainAt(h), def.SiteAt(h))).ToArray();
        }

        public MapDef Def { get; }
        public int Width => Def.Width;
        public int Height => Def.Height;
        public IReadOnlyList<MapTile> Tiles => _tiles;
        public MapTile Tile(Hex hex) => Def.Inside(hex) ? _tiles[hex.Row * Def.Width + hex.Col] : null;
        public MapTile Tile(Site site) => _tiles.First(t => t.Site == site);

        // Where a mover has to stop: something happens there.
        public bool Stops(MapTile tile) =>
            tile.Content != Content.None || (tile.Site != Site.None && tile.Site != Site.Start && !(tile.Used && tile.Site != Site.Town));

        // Every hex reachable with `points` of movement and what it costs. Moving passes through quiet hexes only;
        // a hex where something happens can be the end of the move but not a step on the way. `stops` says what
        // counts as something happening (the adventure leaves out what fog hides); `cost` the price of each terrain
        // for this mover (some classes walk forests lightly).
        public Dictionary<Hex, int> Reach(Hex from, int points, Func<MapTile, bool> stops = null, Func<Terrain, int> cost = null)
        {
            var (costs, _) = Search(from, points, stops ?? Stops, cost ?? TerrainInfo.Cost);
            costs.Remove(from);
            return costs;
        }

        // The hexes walked from `from` to `to` (both included), by the same rules as Reach; empty when out of reach.
        public List<Hex> Path(Hex from, Hex to, int points, Func<MapTile, bool> stops = null, Func<Terrain, int> cost = null)
        {
            var (costs, parents) = Search(from, points, stops ?? Stops, cost ?? TerrainInfo.Cost);
            var path = new List<Hex>();
            if (!costs.ContainsKey(to)) return path;
            for (var at = to; ; at = parents[at])
            {
                path.Add(at);
                if (at == from) break;
            }
            path.Reverse();
            return path;
        }

        (Dictionary<Hex, int>, Dictionary<Hex, Hex>) Search(Hex from, int points, Func<MapTile, bool> stops, Func<Terrain, int> price)
        {
            var cost = new Dictionary<Hex, int> { [from] = 0 };
            var parents = new Dictionary<Hex, Hex> { [from] = from };
            var open = new List<Hex> { from };
            while (open.Count > 0)
            {
                var hex = open.OrderBy(h => cost[h]).ThenBy(h => h.Row).ThenBy(h => h.Col).First();
                open.Remove(hex);
                if (hex != from && stops(Tile(hex))) continue;
                foreach (var next in hex.Neighbors())
                {
                    if (!Def.Passable(next)) continue;
                    int step = cost[hex] + Math.Max(1, price(Def.TerrainAt(next)));
                    if (step > points) continue;
                    if (cost.TryGetValue(next, out int had) && had <= step) continue;
                    cost[next] = step;
                    parents[next] = hex;
                    open.Add(next);
                }
            }
            return (cost, parents);
        }

        // Steps (by movement cost) from `from` to every hex, ignoring what stops a move: for planning far ahead.
        public Dictionary<Hex, int> Distances(Hex from)
        {
            var cost = new Dictionary<Hex, int> { [from] = 0 };
            var open = new List<Hex> { from };
            while (open.Count > 0)
            {
                var hex = open.OrderBy(h => cost[h]).ThenBy(h => h.Row).ThenBy(h => h.Col).First();
                open.Remove(hex);
                foreach (var next in hex.Neighbors())
                {
                    if (!Def.Passable(next)) continue;
                    int step = cost[hex] + TerrainInfo.Cost(Def.TerrainAt(next));
                    if (cost.TryGetValue(next, out int had) && had <= step) continue;
                    cost[next] = step;
                    open.Add(next);
                }
            }
            return cost;
        }
    }
}
