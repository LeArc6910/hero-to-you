using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class MapTests
    {
        [Test]
        public void HexNeighborsAreAtDistanceOne()
        {
            foreach (var hex in new[] { new Hex(3, 2), new Hex(4, 3), new Hex(0, 0) })
            {
                var neighbors = hex.Neighbors().ToList();
                Assert.That(neighbors.Distinct().Count(), Is.EqualTo(6));
                Assert.That(neighbors.All(n => n.Distance(hex) == 1), Is.True, hex.ToString());
            }
            Assert.That(new Hex(0, 0).Distance(new Hex(3, 0)), Is.EqualTo(3));
            Assert.That(new Hex(0, 0).Distance(new Hex(0, 2)), Is.EqualTo(2));
        }

        [Test]
        public void TheChapterMapHasItsPlacesAllReachable()
        {
            var map = TestData.Data.Map;
            Assert.That(map.Width, Is.EqualTo(15));
            Assert.That(map.Height, Is.EqualTo(9));
            var reach = map.Connected(map.Find(Site.Start));
            foreach (var site in new[] { Site.Town, Site.Quest, Site.Boss, Site.Shrine }) Assert.That(reach.Contains(map.Find(site)), Is.True, site.ToString());
        }

        [Test]
        public void AMapWithUnevenRowsSaysWhichLine()
        {
            var error = Assert.Throws<System.FormatException>(() => MapDef.Parse("S.T\n.R\n..B", "test"));
            Assert.That(error.Message, Does.Contain("2번째 줄"));
        }

        [Test]
        public void ForestCostsTwoAndAnEventStopsTheWay()
        {
            var adventure = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 1);
            var map = adventure.Map;
            var from = map.Tile(Site.Start).Hex;
            var reach = map.Reach(from, 6);
            foreach (var (hex, cost) in reach)
            {
                var forest = map.Def.TerrainAt(hex) == Terrain.Forest || map.Def.TerrainAt(hex) == Terrain.Swamp;
                Assert.That(cost, Is.GreaterThanOrEqualTo(forest ? 2 : 1));
            }
            var stop = reach.Keys.FirstOrDefault(h => map.Stops(map.Tile(h)));
            if (stop != default)
                Assert.That(map.Reach(stop, 0).Count, Is.Zero);
        }
    }
}
