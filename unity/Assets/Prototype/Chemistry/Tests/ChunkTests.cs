using System;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Chunked cell storage and the active-chunk set (docs/WORLD_STREAMING.md, part 1).
    /// </summary>
    public class ChunkTests
    {
        static Rules R => ChemistryTests.R;

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        /// <summary>Two chunks side by side (x 0..63 and 64..127) of grass.</summary>
        static World TwoChunks(string fill = "grass", uint seed = 5) => new World(R, 2 * Chunks.Size, 40, seed, fill);

        static bool Any(World w, int x0, int x1, Func<Cell, bool> test)
        {
            for (int y = 0; y < w.H; y++)
                for (int x = x0; x <= x1; x++)
                    if (test(w.At(x, y))) return true;
            return false;
        }

        static bool Burnt(Cell c) => c.Fire > 0 || c.T == "dirt";


        /// <summary>
        /// A valley-sized meadow (4×3 chunks, the last ones partial) with water, oil, walls and
        /// fires that cross chunk edges, plus wind, frost, lightning and earth shaping.
        /// </summary>
        internal static World Valley(uint seed = 11)
        {
            var w = new World(R, 200, 175, seed, "grass");
            for (int y = 0; y < w.H; y++)
            {
                for (int x = 0; x < w.W; x++)
                {
                    if ((x * 7 + y * 3) % 23 == 0) w.Paint(x, y, "tree");
                    else if ((x + y * 5) % 31 == 0) w.Paint(x, y, "brush");
                }
            }
            for (int y = 50; y < 80; y++)
                for (int x = 55; x < 75; x++) { w.Paint(x, y, "pit"); w.AddWater(x, y, 12); }
            for (int x = 0; x < w.W; x++) w.AddWater(x, 128, 6);
            for (int y = 60; y < 70; y++) w.Paint(130, y, "bedrock");
            for (int x = 110; x < 150; x++) w.SetCover(x, 100, "oil");
            w.Cast("fire-bolt", 63, 63, 0);
            w.Cast("fire-bolt", 128, 100, 0);
            w.Cast("fire-bolt", 10, 170, 0);
            w.Cast("fire-bolt", 190, 20, 0);
            return w;
        }

        internal static void ValleyScript(World w, int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                if (t == 20) w.Cast("gust-of-wind", 100, 64, 1);
                if (t == 40) w.Cast("ray-of-frost", 60, 60, 2);
                if (t == 60) w.Cast("shocking-grasp", 65, 65, 0);
                if (t == 80) w.Cast("move-earth", 64, 127, 0);
                if (t == 90) w.Cast("thunderwave", 128, 64, 0);
                w.Step();
            }
        }

        // ---------- all chunks active: exactly the old behaviour ----------

        [Test]
        public void ValleyAcrossChunksMatchesTheUnchunkedGrid()
        {
            // Recorded with the dense grid before chunks existed (c4bbf1c); again when Move Earth
            // began toppling the trees it heaves (one more log line: "a tree topples").
            var w = Valley();
            ValleyScript(w, 300);
            Assert.That(w.CellHash(), Is.EqualTo("f2de2b59"));
            Assert.That(w.RngState, Is.EqualTo(3574071908u));
            Assert.That(w.Log.Count, Is.EqualTo(37));
        }

        // ---------- chunk maths and layout ----------

        [Test]
        public void ChunkMathFloorsNegativeCoordinates()
        {
            Assert.That(Chunks.Size, Is.EqualTo(64));
            Assert.That(Chunks.ChunkOf(0), Is.EqualTo(0));
            Assert.That(Chunks.ChunkOf(63), Is.EqualTo(0));
            Assert.That(Chunks.ChunkOf(64), Is.EqualTo(1));
            Assert.That(Chunks.ChunkOf(-1), Is.EqualTo(-1));
            Assert.That(Chunks.ChunkOf(-64), Is.EqualTo(-1));
            Assert.That(Chunks.ChunkOf(-65), Is.EqualTo(-2));
            Assert.That(Chunks.LocalOf(-1), Is.EqualTo(63));
            Assert.That(Chunks.LocalOf(-64), Is.EqualTo(0));
            Assert.That(Chunks.LocalOf(-65), Is.EqualTo(63));
            for (int c = -300; c <= 300; c++)
                Assert.That(Chunks.ChunkOf(c) * Chunks.Size + Chunks.LocalOf(c), Is.EqualTo(c));
            var keys = new[] { (0, 0), (-1, 0), (0, -1), (-1, -1), (1, -1), (-1, 1), (int.MinValue, int.MaxValue) }
                .Select(k => Chunks.Key(k.Item1, k.Item2));
            Assert.That(keys.Distinct().Count(), Is.EqualTo(7), "every chunk coordinate gets its own key");
        }

        [Test]
        public void TheValleyIsFourByThreeChunksSharingCellsWithTheFlatView()
        {
            var w = new World(R, 200, 175);
            Assert.That(w.ChunkCoords.Count, Is.EqualTo(12));
            Assert.That(w.ChunkCoords[0], Is.EqualTo((0, 0)));
            Assert.That(w.ChunkCoords[4], Is.EqualTo((0, 1)), "sorted by row, then column");
            Assert.That(w.HasChunk(3, 2) && !w.HasChunk(4, 0) && !w.HasChunk(-1, 0));
            Assert.That(w.IsActive(2, 1), "chunks start active");
            Assert.Throws<ArgumentOutOfRangeException>(() => w.SetActive(-1, 0, false));
            foreach (var (x, y) in new[] { (0, 0), (63, 63), (64, 63), (199, 174), (128, 64) })
                Assert.That(w.At(x, y), Is.SameAs(w.Cells[w.Idx(x, y)]));
            Assert.That(w.At(200, 0), Is.Null);
            Assert.That(w.At(-1, 5), Is.Null);
        }

        // ---------- paused chunks ----------

        [Test]
        public void APausedChunkStaysByteIdenticalWhileItsNeighbourBurns()
        {
            var w = TwoChunks();
            w.SetActive(1, 0, false);
            string before = w.ChunkHash(1, 0);
            w.Apply(56, 20, "fire");
            for (int t = 0; t < 200; t++)
            {
                w.Step();
                Assert.That(w.ChunkHash(1, 0), Is.EqualTo(before), $"tick {t}");
            }
            Assert.That(Any(w, 63, 63, Burnt), "the fire reached the border");
            Assert.That(Any(w, 64, 127, Burnt), Is.False, "and stopped there");
        }

        [Test]
        public void FireCarriesOnIntoAChunkThatWakesUp()
        {
            var w = TwoChunks();
            w.SetActive(1, 0, false);
            w.Apply(56, 20, "fire");
            int ticks = 0;
            while (!Any(w, 63, 63, c => c.Fire > 0) && ticks++ < 200) w.Step();
            Assert.That(ticks, Is.LessThan(200), "fire is burning at the border");
            Run(w, 5);
            Assert.That(Any(w, 64, 127, Burnt), Is.False);
            w.SetActive(1, 0, true);
            Run(w, 40);
            Assert.That(Any(w, 64, 127, Burnt), "the fire crossed once the chunk woke");
        }

        [Test]
        public void WaterAndGasStopAtAPausedChunk()
        {
            var w = TwoChunks("dirt");
            for (int y = 10; y < 20; y++) w.AddWater(63, y, 16);
            w.At(63, 30).Gas = "smoke";
            w.At(63, 30).GasAmt = 8;
            w.SetActive(1, 0, false);
            string before = w.ChunkHash(1, 0);
            Run(w, 30);
            Assert.That(w.ChunkHash(1, 0), Is.EqualTo(before));
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(160), "water piles up against the paused chunk");

            w.SetActive(1, 0, true);
            Run(w, 30);
            Assert.That(Any(w, 64, 64, c => c.Water > 0), "water runs on once it wakes");
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(160));
        }

        [Test]
        public void SpellsAndBlastsLeaveAPausedChunkAlone()
        {
            var w = TwoChunks();
            w.SetActive(1, 0, false);
            string before = w.ChunkHash(1, 0);
            w.Cast("fire-bolt", 70, 20, 0);
            w.Cast("move-earth", 64, 10, 0);
            w.Cast("create-water", 64, 30, 0);
            w.Explode(64, 20, R.Entity("barrel"));
            Assert.That(w.ChunkHash(1, 0), Is.EqualTo(before));
            Assert.That(w.At(63, 10).Height, Is.Not.EqualTo(0), "the half of the spell in the live chunk still landed");
        }

        // ---------- hashes ----------

        [Test]
        public void ChunkHashChangesOnlyForTheChunkTouched()
        {
            var w = Valley();
            var before = w.ChunkCoords.ToDictionary(k => k, k => w.ChunkHash(k.cx, k.cy));
            string cells = w.CellHash();
            w.Paint(150, 100, "stone");
            foreach (var k in w.ChunkCoords)
                Assert.That(w.ChunkHash(k.cx, k.cy) != before[k], Is.EqualTo(k == (2, 1)), $"chunk {k}");
            Assert.That(w.CellHash(), Is.Not.EqualTo(cells));
        }

        // ---------- determinism ----------

        [Test]
        public void PausingIsDeterministicWhateverOrderChunksAreSwitchedIn()
        {
            World Play(params (int cx, int cy)[] paused)
            {
                var w = Valley();
                w.SetAllActive(false);
                w.SetAllActive(true);
                foreach (var (cx, cy) in paused) w.SetActive(cx, cy, false);
                ValleyScript(w, 150);
                w.SetActive(paused[0].cx, paused[0].cy, true);
                Run(w, 50);
                return w;
            }
            var a = Play((1, 1), (3, 0), (0, 2));
            var b = Play((1, 1), (0, 2), (3, 0));
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
            Assert.That(b.RngState, Is.EqualTo(a.RngState));
            Assert.That(a.CellHash(), Is.Not.EqualTo(Play((3, 0), (1, 1)).CellHash()), "and pausing does change the run");
        }
    }
}
