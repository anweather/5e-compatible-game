using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The chunk terrains' layout, the heights the cells give them and the paint
    /// (TerrainGrid, CellHeightfield, CellPaint; docs/WORLD_STREAMING.md, part 1).
    /// </summary>
    public class TerrainTests
    {
        static Rules R => ChemistryTests.R;
        static readonly TerrainGrid G = TerrainGrid.Valley();
        const float Step = 0.5f;

        static World Meadow() => new World(R, G.GridW, G.GridH, 1, "grass");

        /// <summary>A meadow with random heights, pits, rock and walls.</summary>
        static World Rugged(int seed)
        {
            var w = Meadow();
            var rng = new Random(seed);
            string[] terrains = { "grass", "pit", "rock", "dirt", "tree" };
            foreach (var c in w.Cells)
            {
                c.T = terrains[rng.Next(terrains.Length)];
                c.Height = rng.Next(R.Sim.Height.Min, R.Sim.Height.Max + 1);
            }
            return w;
        }

        static List<(int x, int y)> RandomCells(Random rng, int n)
        {
            var list = new List<(int x, int y)>();
            for (int k = 0; k < n; k++) list.Add((rng.Next(G.GridW), rng.Next(G.GridH)));
            // Cells on chunk edges and corners: x 63/64 is u 255/256, y 63/64 is v 256/255.
            list.AddRange(new[] { (0, 0), (63, 63), (64, 64), (199, 174), (0, 174), (127, 127), (128, 128) });
            return list;
        }

        static void AssertSame(float[,] expected, float[,] actual, string what)
        {
            for (int z = 0; z < expected.GetLength(0); z++)
                for (int x = 0; x < expected.GetLength(1); x++)
                    Assert.AreEqual(expected[z, x], actual[z, x], $"{what} at [{z}, {x}]");
        }

        // ---------- layout ----------

        [Test]
        public void ValleyCellsSitWhereTheYardWas()
        {
            Assert.AreEqual((-130.5f, 111f), G.CellCenter(0, 0));
            Assert.AreEqual((0f, 0f), G.CellCenter(87, 74));
            Assert.AreEqual(96f, G.ChunkSize);
            Assert.AreEqual((3, 4), G.ChunkOfCell(0, 0));
            Assert.AreEqual((6, 2), G.ChunkOfCell(199, 174));
        }

        [Test]
        public void GridCellZeroCornerIsAChunkCorner()
        {
            // Cell (0, 0)'s north-west corner is the corner of chunks (3, 4) and (3, 5).
            var (cx, cz) = G.CellCenter(0, 0);
            var (kx, kz) = G.ChunkCorner(3, 5);
            Assert.AreEqual(kx, cx - G.CellSize / 2);
            Assert.AreEqual(kz, cz + G.CellSize / 2);
            Assert.AreEqual(G.SamplePosition(3, 5, 0, 0), G.SamplePosition(3, 4, 0, 256));
        }

        [Test]
        public void ResolutionsFollowTheLayout()
        {
            Assert.AreEqual(257, G.Resolution(3, 2));
            Assert.AreEqual(257, G.Resolution(5, 4));
            Assert.AreEqual(65, G.Resolution(6, 4));
            Assert.AreEqual(65, G.Resolution(0, 0));
            Assert.AreEqual(256, G.TexelResolution(4, 3));
            Assert.AreEqual(64, G.TexelResolution(6, 3));
            var expected = new List<(int, int)>();
            for (int j = 2; j <= 5; j++) for (int i = 2; i <= 6; i++) expected.Add((i, j));
            CollectionAssert.AreEqual(expected, G.GridChunks());
        }

        [Test]
        public void CellToSampleToCellRoundTrips()
        {
            foreach (var (x, y) in G.Cells())
            {
                var (i, j, sx, sz) = G.CellSample(x, y);
                Assert.AreEqual((x, y), G.CellOfSample(i, j, sx, sz));
                var r = G.TexelRect(x, y);
                Assert.AreEqual((x, y), G.CellOfTexel(r.I, r.J, r.XMax, r.ZMax));
            }
        }

        [Test]
        public void WorldToCellRoundTrips()
        {
            float q = G.CellSize * 0.49f;
            foreach (var (x, y) in G.Cells())
            {
                var (wx, wz) = G.CellCenter(x, y);
                Assert.AreEqual((x, y), G.CellAt(wx, wz));
                Assert.AreEqual((x, y), G.CellAt(wx - q, wz + q));
                Assert.AreEqual((x, y), G.CellAt(wx + q, wz - q));
            }
            Assert.IsFalse(G.InGrid(G.CellAt(-200f, 0f).x, 0));
        }

        // ---------- heights ----------

        [Test]
        public void ChunkEdgesMatchAcrossEveryKindOfSeam([Values(1, 2, 3)] int seed)
        {
            var w = Rugged(seed);
            var field = new CellHeightfield(G, Step);
            var maps = new Dictionary<(int, int), float[,]>();
            for (int j = 0; j < G.ChunksZ; j++) for (int i = 0; i < G.ChunksX; i++) maps[(i, j)] = field.Compute(w, i, j);
            var kinds = new HashSet<string>();
            for (int j = 0; j < G.ChunksZ; j++)
            {
                for (int i = 0; i < G.ChunksX; i++)
                {
                    if (i + 1 < G.ChunksX) kinds.Add(CheckSeam(maps[(i, j)], maps[(i + 1, j)], (a, k) => a[k, a.GetLength(1) - 1], (b, k) => b[k, 0]));
                    if (j + 1 < G.ChunksZ) kinds.Add(CheckSeam(maps[(i, j)], maps[(i, j + 1)], (a, k) => a[a.GetLength(0) - 1, k], (b, k) => b[0, k]));
                }
            }
            CollectionAssert.AreEquivalent(new[] { "257/257", "257/65", "65/257", "65/65" }, kinds);
            Assert.IsTrue(maps[(4, 3)].Cast<float>().Any(h => h != 0f), "the heights did something");
        }

        /// <summary>
        /// Checks the shared edge of two chunks (a west or south of b): equal at every shared
        /// sample, and on a full-detail edge facing a coarse one, the samples between are on the
        /// line between the coarse ones. Returns the kind of seam.
        /// </summary>
        static string CheckSeam(float[,] a, float[,] b, Func<float[,], int, float> edgeA, Func<float[,], int, float> edgeB)
        {
            int ra = a.GetLength(0), rb = b.GetLength(0);
            var (fine, coarse, edgeFine, edgeCoarse) = ra >= rb ? (a, b, edgeA, edgeB) : (b, a, edgeB, edgeA);
            int p = (fine.GetLength(0) - 1) / (coarse.GetLength(0) - 1);
            for (int k = 0; k < fine.GetLength(0); k++)
            {
                int c = k / p, m = k % p;
                float expected = m == 0 ? edgeCoarse(coarse, c)
                    : edgeCoarse(coarse, c) + (edgeCoarse(coarse, c + 1) - edgeCoarse(coarse, c)) * m / p;
                Assert.AreEqual(expected, edgeFine(fine, k), $"seam sample {k} ({ra}/{rb})");
            }
            return $"{ra}/{rb}";
        }

        [Test]
        public void TrenchHasSlopedBanks()
        {
            var w = Meadow();
            for (int y = 80; y <= 82; y++) for (int x = 50; x <= 59; x++) w.At(x, y).Height = -2;
            var field = new CellHeightfield(G, Step);
            // Cell (55, 81) is u 247, v 238: chunk (3, 3), full detail, local cell (55, 46).
            var (i, j, sx, sz) = G.CellSample(55, 81);
            Assert.AreEqual((3, 3, 55 * 4 + 2, 46 * 4 + 2), (i, j, sx, sz));
            var h = field.Compute(w, i, j);
            Assert.AreEqual(-1f, h[sz, sx], "inside the trench");
            Assert.AreEqual(-1f, h[sz, 55 * 4], "between two trench cells");
            // The trench's north edge: y 80 is v 239 (local 47), its top the corner at local 48.
            Assert.AreEqual(-1f, h[48 * 4 - 1, sx], "last sample inside the trench");
            Assert.AreEqual(-0.5f, h[48 * 4, sx], "bank: half way");
            Assert.AreEqual(0f, h[48 * 4 + 1, sx], "first sample inside the meadow");
            // The trench's north-west corner (x 50 is u 242, local 50).
            Assert.AreEqual(-0.25f, h[48 * 4, 50 * 4], "outer corner: one cell of four");
            Assert.AreEqual(0f, h[100, 100], "untouched meadow");
        }

        [Test]
        public void PitTerrainSinksAndWallsStayWalls()
        {
            var w = Meadow();
            w.Paint(20, 20, "pit");
            w.At(30, 20).Height = R.Sim.Height.BlockAt;
            var field = new CellHeightfield(G, Step);
            var (i, j, sx, sz) = G.CellSample(20, 20);
            var h = field.Compute(w, i, j);
            Assert.AreEqual(-Step, h[sz, sx]);
            Assert.AreEqual(-Step / 4, h[sz - 2, sx - 2], "pit corner: a quarter");
            float top = R.Sim.Height.BlockAt * Step;
            (i, j, sx, sz) = G.CellSample(30, 20);
            h = field.Compute(w, i, j);
            Assert.AreEqual(top, h[sz, sx]);
            Assert.AreEqual(top, h[sz - 2, sx - 2], "a wall's corners stand at full height");
            Assert.AreEqual(top, h[sz + 2, sx], "and its edges");
            Assert.AreEqual(0f, h[sz + 3, sx], "the ground beside it doesn't rise");
        }

        [Test]
        public void ChangedCellOnAnEdgeDirtiesBothChunks()
        {
            var field = new CellHeightfield(G, Step);
            // x 63 is u 255, the last column of chunk 3; its east edge is chunk 4's first column.
            var rects = field.DirtyRects(new[] { (63, 100) });
            CollectionAssert.AreEquivalent(new[] { (3, 3), (4, 3) }, rects.Select(r => (r.I, r.J)));
            Assert.AreEqual(0, rects.Single(r => r.I == 4).X);
            // Cell (0, 0)'s corner is shared by four chunks, two of them coarse.
            var corner = field.DirtyRects(new[] { (0, 0) }).Select(r => (r.I, r.J));
            CollectionAssert.AreEquivalent(new[] { (2, 4), (3, 4), (2, 5), (3, 5) }, corner);
        }

        [Test]
        public void IncrementalUpdateEqualsFullRecompute([Values(4, 5)] int seed)
        {
            var w = Rugged(seed);
            var field = new CellHeightfield(G, Step);
            field.Reset(w);
            var rng = new Random(seed);
            var changed = RandomCells(rng, 60);
            foreach (var (x, y) in changed)
            {
                var c = w.At(x, y);
                c.Height = rng.Next(R.Sim.Height.Min, R.Sim.Height.Max + 1);
                if (rng.Next(3) == 0) c.T = "pit";
            }
            field.Update(w, changed, snap: true);
            foreach (var (i, j) in field.Chunks)
            {
                var full = field.Compute(w, i, j);
                AssertSame(full, field.Target(i, j), $"target ({i}, {j})");
                AssertSame(full, field.Current(i, j), $"current ({i}, {j})");
            }
        }

        [Test]
        public void EasingReachesTheTargetAtItsSpeed()
        {
            var w = Meadow();
            var field = new CellHeightfield(G, Step, easeSpeed: 1f);
            field.Reset(w);
            Assert.IsTrue(field.Settled);
            var cell = (55, 81);
            w.At(55, 81).Height = -3;
            var rects = field.Update(w, new[] { cell });
            var (i, j, sx, sz) = G.CellSample(55, 81);
            Assert.AreEqual(0f, field.Current(i, j)[sz, sx], "no jump");
            Assert.AreEqual(-1.5f, field.Target(i, j)[sz, sx]);
            int frames = 0;
            while (!field.Settled && frames < 100)
            {
                var moved = field.Advance(0.25f);
                Assert.IsNotEmpty(moved);
                Assert.IsTrue(moved.All(m => rects.Any(r => r.I == m.I && r.J == m.J)));
                frames++;
            }
            Assert.AreEqual(6, frames, "1.5 m at 1 m/s in quarter-second frames");
            AssertSame(field.Target(i, j), field.Current(i, j), "settled");
            Assert.IsEmpty(field.Advance(0.25f));
        }

        // ---------- paint ----------

        [Test]
        public void EveryTerrainPaintsWholeWeights()
        {
            foreach (var t in R.Terrain.Keys)
            {
                var w = CellPaint.Weights(new Cell(t));
                Assert.AreEqual(CellPaint.Layers.Count, w.Count, t);
                Assert.AreEqual(1f, w.Sum(), 1e-6f, t);
            }
        }

        [TestCase("grass", CellPaint.Grass)]
        [TestCase("tree", CellPaint.Moss)]
        [TestCase("brush", CellPaint.Moss)]
        [TestCase("rock", CellPaint.Rock)]
        [TestCase("stone", CellPaint.Rock)]
        [TestCase("rubble", CellPaint.Rock)]
        [TestCase("dirt", CellPaint.Soil)]
        [TestCase("pit", CellPaint.Soil)]
        [TestCase("wood", CellPaint.Soil)]
        public void TerrainPaintsItsLayer(string terrain, string layer)
        {
            var w = CellPaint.Weights(new Cell(terrain));
            int top = Enumerable.Range(0, w.Count).OrderByDescending(k => w[k]).First();
            Assert.AreEqual(layer, CellPaint.Layers[top]);
        }

        [Test]
        public void ScorchAndCoversPaintDarkSoil()
        {
            int soil = CellPaint.Layers.ToList().IndexOf(CellPaint.Soil);
            Assert.AreEqual(1f, CellPaint.Weights(new Cell("dirt"), scorched: true)[soil]);
            Assert.AreEqual(1f, CellPaint.Weights(new Cell("grass") { Cover = "ash" })[soil]);
            Assert.AreEqual(1f, CellPaint.Weights(new Cell("grass") { Cover = "oil" })[soil]);
            Assert.Less(CellPaint.Weights(new Cell("dirt"))[soil], 1f, "bare earth reads apart from scorch");
        }

        [Test]
        public void PaintBlocksAreFourByFourOrOneTexel()
        {
            var w = Meadow();
            w.Paint(10, 10, "rock");
            w.Paint(11, 10, "dirt");
            w.Paint(195, 10, "rock");
            var rects = CellPaint.DirtyRects(G, new[] { (10, 10), (11, 10), (195, 10) });
            Assert.AreEqual(2, rects.Count);
            var fine = rects.Single(r => r.I == 3);
            Assert.AreEqual((8, 4), (fine.Width, fine.Height), "two 4×4 blocks side by side");
            var coarse = rects.Single(r => r.I == 6);
            Assert.AreEqual((1, 1), (coarse.Width, coarse.Height));
            var block = CellPaint.Block(G, w, fine);
            int rock = CellPaint.Layers.ToList().IndexOf(CellPaint.Rock);
            for (int z = 0; z < 4; z++)
            {
                for (int x = 0; x < 4; x++)
                {
                    Assert.AreEqual(1f, block[z, x, rock]);
                    Assert.AreEqual(0f, block[z, x + 4, rock]);
                }
            }
            var scorched = CellPaint.Block(G, w, coarse, (x, y) => x == 195);
            Assert.AreEqual(1f, scorched[0, 0, CellPaint.Layers.ToList().IndexOf(CellPaint.Soil)]);
        }

        [Test]
        public void GridRectsCoverTheCellsChunks()
        {
            var rects = CellPaint.GridRects(G);
            Assert.AreEqual(12, rects.Count, "chunks i 3..6, j 2..4");
            int texels = rects.Sum(r => r.Width * r.Height);
            int expected = G.Cells().Sum(c => { var t = G.TexelRect(c.x, c.y); return t.Width * t.Height; });
            Assert.AreEqual(expected, texels, "the rectangles hold exactly the grid's texels");
        }
    }
}
