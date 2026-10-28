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
        // The mechanics are tested on a 200×175 grid over the valley's terrains, where the yard's cells
        // were before they covered the whole valley; ValleyCoversTheWholeTerrain checks the real layout.
        static readonly TerrainGrid G = new TerrainGrid(1.5f, (-419.25f, -368.25f), (9, 9), (192, 319), (200, 175), (3, 2, 5, 4));
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
        public void ValleyCoversTheWholeTerrain()
        {
            var v = TerrainGrid.Valley();
            Assert.AreEqual((576, 576), (v.GridW, v.GridH));
            Assert.AreEqual((-418.5f, 495f), v.CellCenter(0, 0));
            Assert.AreEqual((0f, 0f), v.CellCenter(279, 330), "the yard is where it was");
            Assert.AreEqual(G.CellCenter(87, 74), v.CellCenter(279, 330));
            Assert.AreEqual((0, 8), v.ChunkOfCell(0, 0));
            Assert.AreEqual((8, 0), v.ChunkOfCell(575, 575));
            Assert.AreEqual(81, v.GridChunks().Count);
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
            // The bank slopes evenly from the last trench cell's centre to the first meadow cell's.
            Assert.AreEqual(-0.75f, h[48 * 4 - 1, sx], "a quarter of a cell from the trench's last centre");
            Assert.AreEqual(-0.5f, h[48 * 4, sx], "bank: half way");
            Assert.AreEqual(-0.25f, h[48 * 4 + 1, sx], "a quarter of a cell from the meadow's first centre");
            Assert.AreEqual(0f, h[48 * 4 + 2, sx], "the meadow cell's centre");
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
        public void FlatUnderWallsLeavesTheGroundAtTheWallsFoot()
        {
            var w = Meadow();
            w.At(30, 20).Height = R.Sim.Height.BlockAt;
            w.At(32, 20).Height = 2;
            var field = new CellHeightfield(G, Step) { FlatUnderWalls = true };
            var (i, j, sx, sz) = G.CellSample(30, 20);
            var h = field.Compute(w, i, j);
            Assert.AreEqual(0f, h[sz, sx], "the wall's own ground stays down: its rocks are the wall");
            Assert.AreEqual(0f, h[sz - 2, sx - 2], "and its corners");
            (i, j, sx, sz) = G.CellSample(32, 20);
            Assert.AreEqual(2 * Step, field.Compute(w, i, j)[sz, sx], "a heap under a wall's height still rises");
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
        [TestCase("stump", CellPaint.Charred)]
        [TestCase("dirt", CellPaint.Earth)]
        [TestCase("pit", CellPaint.Earth)]
        [TestCase("wood", CellPaint.Earth)]
        public void TerrainPaintsItsLayer(string terrain, string layer)
        {
            Assert.AreEqual(layer, Top(CellPaint.Weights(new Cell(terrain))));
        }

        static string Top(IReadOnlyList<float> w) =>
            CellPaint.Layers[Enumerable.Range(0, w.Count).OrderByDescending(k => w[k]).First()];

        static int LayerIndex(string layer) => CellPaint.Layers.ToList().IndexOf(layer);

        [Test]
        public void ScorchAndAshPaintCharredButSlicksLeaveTheGround()
        {
            Assert.AreEqual(CellPaint.Charred, Top(CellPaint.Weights(new Cell("dirt"), scorched: true)));
            Assert.AreEqual(CellPaint.Charred, Top(CellPaint.Weights(new Cell("grass") { Cover = "ash" })));
            Assert.AreEqual(CellPaint.Grass, Top(CellPaint.Weights(new Cell("grass") { Cover = "oil" })), "the slick is drawn on top");
            Assert.AreEqual(0f, CellPaint.Weights(new Cell("dirt"))[LayerIndex(CellPaint.Charred)], "bare earth reads apart from scorch");
        }

        [Test]
        public void WallsAreRockAndDugGrassIsEarth()
        {
            var w = Meadow();
            var wall = w.At(5, 5);
            wall.Height = R.Sim.Height.BlockAt;
            Assert.AreEqual(CellPaint.Rock, Top(CellPaint.Mix(w, wall)));
            var trench = w.At(6, 5);
            trench.Height = -1;
            Assert.AreEqual(CellPaint.Earth, Top(CellPaint.Mix(w, trench)));
            Assert.AreEqual(CellPaint.Grass, Top(CellPaint.Mix(w, w.At(7, 5))));
        }

        // A 3×3 patch of rock at (20..22, 20..22) on a full-detail chunk; the rest keeps the base.
        static PaintBlock RockPatch(World w, out ChunkRect rect)
        {
            for (int y = 20; y <= 22; y++)
                for (int x = 20; x <= 22; x++) w.Paint(x, y, "rock");
            var cells = new List<(int x, int y)>();
            for (int y = 17; y <= 25; y++)
                for (int x = 17; x <= 25; x++) cells.Add((x, y));
            rect = CellPaint.DirtyRects(G, cells).Single();
            return CellPaint.Block(G, rect, (x, y) => w.At(x, y).T == "rock" ? CellPaint.Mix(w, w.At(x, y)) : null);
        }

        // The cover of the texel at a fraction (fx, fy) across grid cell (x, y) (y growing south).
        static float CoverAt(PaintBlock b, ChunkRect rect, int x, int y, float fx, float fy)
        {
            var cell = G.TexelRect(x, y);
            int tx = cell.X + (int)(fx * 4), tz = cell.ZMax - (int)(fy * 4);
            return b.Cover[tz - rect.Z, tx - rect.X];
        }

        [Test]
        public void PatchesCoverTheirCellsAndFadeSoftlyAcrossTheirBorders()
        {
            var b = RockPatch(Meadow(), out var rect);
            Assert.AreEqual(1f, CoverAt(b, rect, 21, 21, 0.5f, 0.5f), 1e-4f, "the middle is all rock");
            Assert.AreEqual(0f, CoverAt(b, rect, 18, 21, 0.5f, 0.5f), 1e-4f, "two cells away is the base");
            int rock = LayerIndex(CellPaint.Rock);
            float soft = 0f, painted = 0f;
            for (int z = 0; z < rect.Height; z++)
            {
                for (int x = 0; x < rect.Width; x++)
                {
                    float c = b.Cover[z, x];
                    if (c > 0.02f && c < 0.98f) soft++;
                    if (c > 0f) Assert.AreEqual(1f, b.Mix[z, x, rock], 1e-5f, "a painted texel is all rock");
                    painted += c;
                }
            }
            Assert.Greater(soft, 8, "some texels blend paint and base");
            Assert.AreEqual(9 * 16, painted, 9 * 16 * 0.25f, "about the patch's own texels are painted");
        }

        [Test]
        public void PatchEdgesWanderAcrossCellBorders()
        {
            var b = RockPatch(Meadow(), out var rect);
            // The west border of the patch: the cover of the texels just outside it differs from row to row.
            var outside = new List<float>();
            var inside = new List<float>();
            for (int y = 20; y <= 22; y++)
            {
                for (int k = 0; k < 4; k++)
                {
                    outside.Add(CoverAt(b, rect, 19, y, 0.875f, k / 4f));
                    inside.Add(CoverAt(b, rect, 20, y, 0.125f, k / 4f));
                }
            }
            Assert.Greater(outside.Max() - outside.Min(), 0.2f, "the edge isn't a straight line");
            Assert.Greater(outside.Max(), 0.1f, "paint reaches past the cell border somewhere");
            Assert.Less(inside.Min(), 0.9f, "and the base reaches inside it somewhere");
        }

        [Test]
        public void WallsSpreadOverTheirSideSlopes()
        {
            var w = Meadow();
            w.At(30, 30).Height = R.Sim.Height.BlockAt;
            var rect = CellPaint.DirtyRects(G, CellPaint.Around(G, new[] { (30, 30) })).Single();
            var b = CellPaint.Block(G, rect, (x, y) => w.IsRaisedWall(w.At(x, y)) ? CellPaint.Mix(w, w.At(x, y)) : null);
            // The first texel of each neighbour, toward the wall (where its slope is), is mostly rock.
            float east = CoverAt(b, rect, 31, 30, 0f, 0.5f), west = CoverAt(b, rect, 29, 30, 0.875f, 0.5f);
            float north = CoverAt(b, rect, 30, 29, 0.5f, 0.875f), south = CoverAt(b, rect, 30, 31, 0.5f, 0f);
            Assert.Greater(Math.Min(Math.Min(east, west), Math.Min(north, south)), 0.5f);
        }

        [Test]
        public void PaintIsDeterministicAndOneTexelOnCoarseChunks()
        {
            var w = Meadow();
            w.Paint(195, 10, "dirt");
            var coarse = G.TexelRect(195, 10);
            Assert.AreEqual((1, 1), (coarse.Width, coarse.Height));
            Func<int, int, IReadOnlyList<float>> dirt = (x, y) => w.At(x, y).T == "dirt" ? CellPaint.Mix(w, w.At(x, y)) : null;
            var a = CellPaint.Block(G, coarse, dirt);
            var again = CellPaint.Block(G, coarse, dirt);
            Assert.Greater(a.Cover[0, 0], 0.5f);
            Assert.AreEqual(a.Cover[0, 0], again.Cover[0, 0]);
            Assert.AreEqual(CellPaint.Earth, Top(Enumerable.Range(0, CellPaint.Layers.Count).Select(k => a.Mix[0, 0, k]).ToList()));
        }

        [Test]
        public void RunsJoinNeighboursInARowOfOneChunk()
        {
            // x 63 and 64 are u 255 and 256: the two sides of a chunk edge.
            var runs = CellPaint.Runs(G, new[] { (10, 10), (11, 10), (13, 10), (10, 11), (63, 10), (64, 10) });
            Assert.AreEqual(5, runs.Count);
            Assert.AreEqual(1, runs.Count(r => r.Width == 8 && r.Height == 4), "10 and 11 join");
            Assert.AreEqual(4, CellPaint.Around(G, new[] { (0, 0) }).Count, "the grid's corner has 3 neighbours");
            Assert.AreEqual(9, CellPaint.Around(G, new[] { (5, 5) }).Count);
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

        [Test]
        public void TheTerrainsInDetailRangeAreFoundInTheirOwnGrid()
        {
            var g = TerrainGrid.Valley();
            // The middle of terrain (4, 4): world cell x 288, y 575 - 288.
            var near = g.ChunksWithin(288, 575 - 288, 100);
            Assert.That(near.Count, Is.EqualTo(13), "the 3 x 3 round it and the 4 two away along the axes");
            Assert.That(near, Does.Contain((4, 2)).And.Contain((4, 6)).And.Contain((2, 4)).And.Contain((6, 4)));
            Assert.That(near, Does.Not.Contain((2, 2)));
            // Its own chunk is always in, wherever the camera is in it.
            for (int y = 0; y < 576; y += 37)
                for (int x = 0; x < 576; x += 41)
                    Assert.That(g.ChunksWithin(x, y, 0), Does.Contain(g.ChunkOfCell(x, y)), $"cell ({x}, {y})");
            // The terrains run north to south the other way: a camera at the north edge (y 0) is in row 8.
            Assert.That(g.ChunksWithin(288, 0, 10), Is.EqualTo(new[] { (4, 8) }));
        }
    }
}
