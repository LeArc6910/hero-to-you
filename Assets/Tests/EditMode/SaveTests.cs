using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class SaveTests
    {
        static string State(Adventure a) =>
            $"{a.Screen} {a.Day} {a.TimeOfDay} {a.Doom} {a.Gold} {a.Hope} {a.MapHero} {a.MovePoints} " +
            string.Join(";", a.Heroes.Select(h => $"{h.Class.Id}:{h.Level}:{h.Hp}:{h.Resolve}:{h.Grit}:{h.Potions}:{h.Weapon.Id}:{string.Join("+", h.Upgrades.Select(u => u.Id))}")) + " " +
            string.Join(";", a.Positions) + " " + string.Join(";", a.Bag.Select(i => i.Id)) + " " +
            string.Join(";", a.Map.Tiles.Select(t => $"{t.Content}{t.Used}"));

        [TestCase(21UL)]
        [TestCase(22UL)]
        public void ASaveLoadsBackToTheSameState(ulong seed)
        {
            var adventure = new Adventure(TestData.Data, new[] { "knight", "alchemist", "seer" }, seed);
            var random = new GameRandom(seed, 4);
            // Stop partway, mid-chapter, and in the middle of something.
            for (int i = 0; i < 400 && adventure.Screen != AdventureScreen.Victory && adventure.Screen != AdventureScreen.Defeat; i++)
                Assert.That(AdventureBot.Step(adventure, random), Is.Null);
            string save = adventure.Save();
            var loaded = Adventure.Load(TestData.Data, save, out string error, out string note);
            Assert.That(error, Is.Null);
            Assert.That(note, Is.Null);
            Assert.That(State(loaded), Is.EqualTo(State(adventure)));
            Assert.That(loaded.Save(), Is.EqualTo(save));
        }

        [Test]
        public void ALoadedGameGoesOnLikeTheOriginal()
        {
            var a = new Adventure(TestData.Data, new[] { "rogue", "cleric", "beastmaster" }, 31);
            var random = new GameRandom(31, 4);
            for (int i = 0; i < 200; i++) AdventureBot.Step(a, random);
            var b = Adventure.Load(TestData.Data, a.Save(), out _, out _);
            var ra = new GameRandom(77);
            var rb = new GameRandom(77);
            for (int i = 0; i < 200 && a.Screen != AdventureScreen.Victory && a.Screen != AdventureScreen.Defeat; i++)
            {
                AdventureBot.Step(a, ra);
                AdventureBot.Step(b, rb);
            }
            Assert.That(State(b), Is.EqualTo(State(a)));
        }

        [Test]
        public void ABrokenSaveIsRefusedWithAReason()
        {
            Assert.That(Adventure.Load(TestData.Data, "hello", out string error, out _), Is.Null);
            Assert.That(error, Is.Not.Null);
            var a = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 5);
            string first = a.Save() + "move 99 99\n";
            Assert.That(Adventure.Load(TestData.Data, first, out error, out _), Is.Null, "첫 명령부터 어긋나면 거절");
            Assert.That(error, Does.Contain("move 99 99"));
            Assert.That(Adventure.Load(TestData.Data, a.Save().Replace("save " + Adventure.SaveVersion, "save 1"), out error, out _), Is.Null, "옛 형식은 거절");
        }

        // When the data changed under a save, the commands that still fit are kept (Docs/Saving.md).
        [Test]
        public void ASaveThatStopsFittingKeepsWhatFits()
        {
            var a = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 5);
            a.Continue();
            a.RollMove();
            string save = a.Save() + "move 99 99\nstay\n";
            var loaded = Adventure.Load(TestData.Data, save, out string error, out string note);
            Assert.That(error, Is.Null);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(note, Does.Contain("2개"));
            Assert.That(loaded.Log.Count, Is.EqualTo(a.Log.Count));
            Assert.That(loaded.MovePoints, Is.EqualTo(a.MovePoints));
        }

        // Adding a data row of one kind does not move the draws of another (separate random streams).
        [Test]
        public void TheMapDoesNotDependOnTheEventsDrawn()
        {
            var a = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 9);
            var b = new Adventure(TestData.Data, new[] { "knight", "hunter", "cleric" }, 9);
            b.OpenEventForTest(TestData.Data.Events[0]);
            typeof(Adventure).GetMethod("PickEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(b, new object[] { "wild" });
            var spawn = typeof(Adventure).GetMethod("Spawn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            spawn.Invoke(a, null);
            spawn.Invoke(b, null);
            string Contents(Adventure x) => string.Join(";", x.Map.Tiles.Where(t => t.Content != Content.None).Select(t => $"{t.Hex}:{t.Content}:{t.Encounter}"));
            Assert.That(Contents(b), Is.EqualTo(Contents(a)));
        }
    }
}
