using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Saving a world to disk (World.Save.cs, docs/WORLD_STREAMING.md "Part 2, third slice"):
    /// save, load and step equals stepping straight through; a save of a load is the save
    /// again; only changed chunks are saved; a save that doesn't fit is refused.
    /// </summary>
    public class SaveTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>A made-up land from a seed: grass and brush, stone outcrops, a pond and a bedrock rim.</summary>
        sealed class Meadow : IChunkSource
        {
            readonly int w, h;
            readonly uint seed;

            public Meadow(int w, int h, uint seed = 3, string version = "test-meadow-1")
            {
                this.w = w;
                this.h = h;
                this.seed = seed;
                Version = version;
            }

            public string Version { get; }

            public void Make(int x, int y, Cell cell)
            {
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                {
                    cell.T = "bedrock";
                    return;
                }
                uint n = Sky.Hash(seed, y * w + x, 9) % 100;
                cell.T = Lands.First(l => n < l.below).terrain;
                int dx = x - 150, dy = y - 40;
                if (dx * dx + dy * dy < 64) cell.Water = 6;
            }
        }

        const int W = 192, H = 128;

        // The meadow's terrains by a cell's hash out of 100.
        static readonly (uint below, string terrain)[] Lands = { (60, "grass"), (85, "dirt"), (95, "brush"), (100, "stone") };

        static World Fresh(IChunkSource source)
        {
            var w = new World(R, W, H, 5);
            w.MakeFrom(source);
            return w;
        }

        /// <summary>Fire across the meadow, water, dug and raised earth, a chunk let go and a defense field marching.</summary>
        static World Played(IChunkSource source)
        {
            var w = Fresh(source);
            w.ApplyDefense(new DefenseCommand(DefenseKind.Start, 4, 100, 60, 100));
            w.Cast("fireball", 30, 30, 1);
            w.Cast("create-water", 100, 20, 1);
            w.Cast("move-earth", 70, 90, 1);
            w.Cast("wall-of-stone", 20, 110, 1);
            for (int t = 0; t < 40; t++) w.Step();
            w.SetActive(2, 1, false);
            w.Cast("fire-bolt", 120, 70, 1);
            for (int t = 0; t < 20; t++) w.Step();
            return w;
        }

        static string State(World w) =>
            $"{w.Tick}/{w.RngState}/{w.CellHash()}/" +
            string.Join(",", w.ChunkCoords.Select(c => $"{(w.IsActive(c.cx, c.cy) ? 1 : 0)}:{w.LastLive(c.cx, c.cy)}")) + "/" +
            string.Join(",", w.Raiders.Select(r => $"{r.Id}@{r.X},{r.Y}:{r.Hp}"));

        [Test]
        public void FogPatchesComeBackTheSameFromASave()
        {
            var rules = R.WithMods(new[] { ModPatch.Parse("{ 'sim': { 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'periodMinutes': 60, " +
                "'weights': { 'clear': 0, 'cloudy': 0, 'windy': 0, 'fog': 1, 'rain': 0, 'storm': 0, 'snow': 0 } } } }") });
            var source = new Meadow(W, H);
            var straight = new World(rules, W, H, 11);
            straight.MakeFrom(source);
            for (int i = 0; i < 120; i++) straight.Step();
            Assert.That(straight.Cells.Any(c => c.Gas == "fog"), "it's foggy when saved");
            var loaded = World.Load(straight.Save(source), rules, source);
            for (int i = 0; i < 60; i++)
            {
                straight.Step();
                loaded.Step();
            }
            Assert.That(loaded.CellHash(), Is.EqualTo(straight.CellHash()));
            Assert.That(loaded.RngState, Is.EqualTo(straight.RngState));
        }

        [Test]
        public void SaveLoadAndStepIsTheSameAsSteppingStraightThrough()
        {
            var source = new Meadow(W, H);
            var straight = Played(source);
            var loaded = World.Load(straight.Save(source), R, source);
            Assert.That(State(loaded), Is.EqualTo(State(straight)), "as saved");
            Assert.That(straight.Raiders.Count, Is.GreaterThan(0), "raiders were marching when it saved");
            for (int t = 0; t < 120; t++)
            {
                straight.Step();
                loaded.Step();
                if (t % 20 == 0) Assert.That(State(loaded), Is.EqualTo(State(straight)), $"tick {straight.Tick}");
            }
            Assert.That(State(loaded), Is.EqualTo(State(straight)));
            Assert.That(loaded.Log.Count, Is.GreaterThan(0), "and it goes on logging");
        }

        [Test]
        public void ASaveOfALoadIsTheSaveAgain()
        {
            var source = new Meadow(W, H);
            string save = Played(source).Save(source);
            Assert.That(World.Load(save, R, source).Save(source), Is.EqualTo(save));
            // The rules' fingerprint left out: any change to rules/ moves it, not just one to the format.
            string pinned = save.Replace(R.Fingerprint, "rules");
            uint hash = 0x811c9dc5;
            foreach (char c in pinned) hash = (hash ^ c) * 0x01000193;
            Assert.That(hash.ToString("x", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("c1737215"), "the format (a change here means old saves need a new SaveFormat)");
        }

        [Test]
        public void ACellsRustHeatLightAndSlickAreSaved()
        {
            var source = new Meadow(W, H);
            var w = Fresh(source);
            w.At(5, 5).T = "metal";
            w.At(5, 5).Rust = 640;
            w.At(5, 5).Heat = 50;
            w.At(5, 5).Lit = 900;
            w.At(5, 5).Slick = 3;
            var loaded = World.Load(w.Save(source), R, source);
            Assert.That(loaded.At(5, 5).Rust, Is.EqualTo(640));
            Assert.That(loaded.At(5, 5).Heat, Is.EqualTo(50));
            Assert.That(loaded.At(5, 5).Lit, Is.EqualTo(900));
            Assert.That(loaded.At(5, 5).Slick, Is.EqualTo(3));
            Assert.That(loaded.CellHash(), Is.EqualTo(w.CellHash()));
        }

        [Test]
        public void OnlyChangedChunksAreSaved()
        {
            var source = new Meadow(W, H);
            var w = Fresh(source);
            Assert.That(w.ChangedChunks(source), Is.Empty);
            string empty = w.Save(source);
            Assert.That(empty, Does.Contain("\"chunks\":[]"), "an untouched world: nothing but the header");

            w.Cast("fire-bolt", 10, 10, 1);
            Assert.That(w.ChangedChunks(source), Is.EqualTo(new[] { (0, 0) }));
            string one = w.Save(source);
            Assert.That(one.Length, Is.LessThan(empty.Length + 400), "one burning cell is a few dozen bytes, not a chunk");
            Assert.That(World.Load(one, R, source).CellHash(), Is.EqualTo(w.CellHash()));

            w.SetActive(2, 1, false);
            Assert.That(w.Save(source), Does.Contain("\"x\":2,\"y\":1,\"inactive\":true"), "streaming state is saved even with no cells changed");
        }

        [Test]
        public void ASaveThatDoesntFitIsRefused()
        {
            var source = new Meadow(W, H);
            string save = Played(source).Save(source);
            Assert.Throws<SaveException>(() => World.Load(save, R, new Meadow(W, H, 4, "test-meadow-2")));
            var modded = R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"fire\": { \"spreadChance\": 0.9 } } }") });
            Assert.That(Assert.Throws<SaveException>(() => World.Load(save, modded, source)).Message, Does.Contain("other rules"));
            Assert.Throws<SaveException>(() => World.Load(save.Replace("\"format\":1", "\"format\":99"), R, source));
            Assert.Throws<SaveException>(() => World.Load("not a save", R, source));
        }
    }
}
