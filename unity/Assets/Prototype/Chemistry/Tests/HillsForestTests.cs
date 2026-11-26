// The hills' woods (world/maps/hills-12x10, docs/design/hills.md "Forest"): patches of forest made in the generator, a
// cell's tree a pure function of the cell. The woods are patches, not a carpet, more of them on the foothills and none
// over the tree line; no tree stands on a road, a river, a lake or their banks, nor in a pass; chunks meet at their edges
// and over both seams; the trees are the same on every load and every thread; known chunks say where the woods are; and
// peers and late joiners agree on a wood's chunks while it burns.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class HillsForestTests
    {

        internal static string HillsDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "hills-12x10");

        string root;
        GeneratedMap map;
        HillsForest forest;
        (int cx, int cy) heart;

        [OneTimeSetUp]
        public void LoadTheHills()
        {
            root = Path.Combine(Path.GetTempPath(), "hills-forest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            map = GeneratedMap.Load(HillsDir);
            forest = map.Hills.Forest;
            heart = forest.Heart(map.Hills.At);
        }

        [OneTimeTearDown]
        public void DropThem()
        {
            map = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        const int Grid = 16;
        // A tree cell's character (ChunkGen's legend).
        const char Tree = 'T';

        // Every Grid cells over the map: whether the woods' noise (with the foothills') makes it a wood there.
        bool[] WoodGrid(out int gw, out int gh)
        {
            gw = map.Gen.CellsW / Grid;
            gh = map.Gen.CellsH / Grid;
            var wood = new bool[gw * gh];
            for (int j = 0; j < gh; j++)
                for (int i = 0; i < gw; i++)
                {
                    int x = i * Grid, y = j * Grid;
                    wood[j * gw + i] = forest.Density(x, y, map.Hills.At(x, y)) >= forest.Least;
                }
            return wood;
        }

        // The woods as 8-connected patches of the grid's wood samples (round the torus), their sizes in samples.
        static List<int> Patches(bool[] wood, int gw, int gh)
        {
            var seen = new bool[wood.Length];
            var sizes = new List<int>();
            var stack = new Stack<int>();
            for (int s = 0; s < wood.Length; s++)
            {
                if (!wood[s] || seen[s]) continue;
                int n = 0;
                seen[s] = true;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    n++;
                    int i = k % gw, j = k / gw;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int q = PlanGrid.Mod(j + dj, gh) * gw + PlanGrid.Mod(i + di, gw);
                            if (!wood[q] || seen[q]) continue;
                            seen[q] = true;
                            stack.Push(q);
                        }
                }
                sizes.Add(n);
            }
            return sizes;
        }

        [Test]
        public void TheWoodsArePatchesNotACarpet()
        {
            Assert.That(forest, Is.Not.Null, "map.json asks for woods");
            var wood = WoodGrid(out int gw, out int gh);
            var sizes = Patches(wood, gw, gh).Where(n => n >= 16).OrderByDescending(n => n).ToList();
            double share = wood.Count(b => b) / (double)wood.Length;
            double hectares = Grid * Grid * 1.5 * 1.5 / 10000;
            TestContext.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "FOREST {0:0.0}% of the map wooded, {1} woods of 3.6 ha or more, the largest {2:0} ha, then {3}",
                100 * share, sizes.Count, sizes[0] * hectares, string.Join(", ", sizes.Skip(1).Take(12).Select(n => (n * hectares).ToString("0", CultureInfo.InvariantCulture)))));
            Assert.That(share, Is.InRange(0.08, 0.35), "woods, not a carpet nor none");
            Assert.That(sizes.Count, Is.GreaterThanOrEqualTo(12), "a few woods round the map");
            Assert.That(sizes[0], Is.LessThan(wood.Count(b => b) / 3), "no one wood is most of the forest");
            Assert.That(sizes[sizes.Count / 2], Is.LessThan(sizes[0] / 4), "woods of varied size");
        }

        [Test]
        public void MoreWoodsOnTheFoothillsThinningToNoneOverTheTreeLine()
        {
            var m = map.Hills.Massif;
            var land = map.Hills;
            int foot = 0, footWood = 0, grass = 0, grassWood = 0, high = 0;
            long meadowDensity = 0, footDensity = 0;
            int meadow = 0;
            for (int y = 0; y < map.Gen.CellsH; y += Grid)
                for (int x = 0; x < map.Gen.CellsW; x += Grid)
                {
                    int h = land.At(x, y), d = forest.Density(x, y, h);
                    long r = m.Touches(x, y) ? m.Radius(x, y) : long.MaxValue;
                    if (h >= forest.TreeLineTo)
                    {
                        high++;
                        Assert.That(d, Is.EqualTo(0), $"({x}, {y}) over the tree line");
                    }
                    if (r < (long)(m.Meadow - m.Warp) * BiomeMath.One)
                    {
                        meadow++;
                        meadowDensity += d;
                    }
                    else if (m.FootBand(r) > BiomeMath.One / 2)
                    {
                        foot++;
                        footDensity += d;
                        if (d >= forest.Least) footWood++;
                    }
                    else if (r == long.MaxValue)
                    {
                        grass++;
                        if (d >= forest.Least) grassWood++;
                    }
                }
            TestContext.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "FOREST wooded: grassland {0:0.0}%, the foothills' middle {1:0.0}%; mean density foothills {2:0.0}, meadow {3:0.0} per thousand; {4} samples over the tree line",
                100.0 * grassWood / grass, 100.0 * footWood / foot, (double)footDensity / foot, (double)meadowDensity / meadow, high));
            Assert.That((double)footWood / foot, Is.GreaterThan((double)grassWood / grass), "the foothills are wooded more");
            Assert.That(high, Is.GreaterThan(100), "the massif's mountains stand over the tree line");
            Assert.That((double)meadowDensity / meadow, Is.LessThan((double)footDensity / foot / 2), "the high meadow thinner than the foothills");
        }

        // The chunks along the main roads' lines and the rivers', and those round the densest wood.
        List<(int cx, int cy)> Chunks()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size;
            var set = new SortedSet<(int, int)>();
            foreach (var (lx, ly) in map.Hills.Roads.MainLines())
                for (int k = 0; k < lx.Length; k += 3)
                    set.Add((PlanGrid.Mod((int)BiomeMath.FloorDiv(lx[k], BiomeMath.One), gen.CellsW) / s, gen.WrapCellY((int)BiomeMath.FloorDiv(ly[k], BiomeMath.One)) / s));
            foreach (var r in map.Hills.Rivers.Rivers)
                for (int k = 0; k < r.X.Length; k += 2)
                    set.Add((PlanGrid.Mod((int)BiomeMath.FloorDiv(r.X[k], BiomeMath.One), gen.CellsW) / s, gen.WrapCellY((int)BiomeMath.FloorDiv(r.Y[k], BiomeMath.One)) / s));
            var list = set.Where((_, k) => k % 4 == 0).ToList();
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++) list.Add((gen.WrapChunk(heart.cx + dx), gen.WrapChunkY(heart.cy + dy)));
            return list.Distinct().ToList();
        }

        [Test]
        public void NoTreeOnARoadOrWaterOrTheirBanks()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size, trees = 0, checkedRoads = 0;
            foreach (var (cx, cy) in Chunks())
            {
                var c = gen.Make(cx, cy);
                for (int j = 0; j < s; j++)
                    for (int i = 0; i < s; i++)
                    {
                        if (c.Cells[j * s + i] != Tree) continue;
                        trees++;
                        int x = cx * s + i, y = cy * s + j;
                        // Nothing laid within two cells: no road, river, lake or ford (their banks are clear further still).
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                gen.HillsCellAt(x + dx, y + dy, out char kind, out bool ford);
                                Assert.That(kind, Is.EqualTo('\0'), $"a tree at ({x}, {y}) by a '{kind}' at ({x + dx}, {y + dy})");
                                Assert.That(ford, Is.False);
                                checkedRoads++;
                            }
                        // Not in a pass.
                        Assert.That(map.Hills.Massif.PassAt(x, y), Is.LessThan(BiomeMath.One), $"a tree at ({x}, {y}) in a pass");
                    }
            }
            TestContext.Out.WriteLine($"FOREST {trees} trees in the road, river and wood chunks, none within two cells of a road or water ({checkedRoads} cells looked at)");
            Assert.That(trees, Is.GreaterThan(3000));
        }

        [Test]
        public void ChunksMeetAtTheirEdgesAndOverTheSeams()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size, edges = 0, edgeTrees = 0;
            var chunks = Chunks();
            // The chunks along both seams too, either side.
            for (int cy = 0; cy < gen.ChunksH; cy += 9)
            {
                chunks.Add((0, cy));
                chunks.Add((gen.ChunksW - 1, cy));
            }
            for (int cx = 0; cx < gen.ChunksW; cx += 9)
            {
                chunks.Add((cx, 0));
                chunks.Add((cx, gen.ChunksH - 1));
            }
            foreach (var (cx, cy) in chunks.Distinct())
            {
                var c = gen.Make(cx, cy);
                for (int k = 0; k < s; k++)
                    foreach (var (i, j) in new[] { (0, k), (s - 1, k), (k, 0), (k, s - 1) })
                    {
                        int x = cx * s + i, y = cy * s + j;
                        char cell = c.Cells[j * s + i];
                        Assert.That(cell, Is.EqualTo(gen.HillsGroundAt(x, y)), $"({x}, {y})");
                        edges++;
                        if (cell == Tree) edgeTrees++;
                    }
            }
            // A cell and its copies a world off either way read the same.
            for (int y = 0; y < gen.CellsH; y += 37)
            {
                Assert.That(gen.HillsGroundAt(gen.CellsW - 1, y), Is.EqualTo(gen.HillsGroundAt(-1, y)), $"row {y} over the east seam");
                Assert.That(gen.HillsGroundAt(0, y), Is.EqualTo(gen.HillsGroundAt(gen.CellsW, y)), $"row {y} over the east seam");
            }
            for (int x = 0; x < gen.CellsW; x += 37)
            {
                Assert.That(gen.HillsGroundAt(x, gen.CellsH - 1), Is.EqualTo(gen.HillsGroundAt(x, -1)), $"column {x} over the south seam");
                Assert.That(gen.HillsGroundAt(x, 0), Is.EqualTo(gen.HillsGroundAt(x, gen.CellsH)), $"column {x} over the south seam");
            }
            // The woods' chance tiles round the torus.
            for (int k = 0; k < 400; k++)
            {
                int x = (int)(WorldHash.Hash4(1, 2, k, 0) % (uint)gen.CellsW), y = (int)(WorldHash.Hash4(1, 3, k, 0) % (uint)gen.CellsH);
                Assert.That(forest.Noise(x + gen.CellsW, y - gen.CellsH), Is.EqualTo(forest.Noise(x, y)));
            }
            TestContext.Out.WriteLine($"FOREST {edges} edge cells as a cell alone works them out, {edgeTrees} of them trees");
            Assert.That(edgeTrees, Is.GreaterThan(100));
        }

        [Test]
        public void TheWoodsAreTheSameEveryLoadAndThread()
        {
            var chunks = Chunks();
            var first = chunks.ToDictionary(c => c, c => map.Gen.Make(c.cx, c.cy).Hash);
            var again = new uint[chunks.Count];
            var gen = GeneratedMap.Load(HillsDir).Gen;
            Assert.That(gen.Hills.Forest.Heart(gen.Hills.At), Is.EqualTo(heart));
            Parallel.For(0, chunks.Count, k =>
            {
                var (cx, cy) = chunks[chunks.Count - 1 - k];
                again[chunks.Count - 1 - k] = gen.Make(cx, cy).Hash;
            });
            for (int k = 0; k < chunks.Count; k++) Assert.That(again[k], Is.EqualTo(first[chunks[k]]), $"chunk {chunks[k]}");
        }

        [Test]
        public void KnownChunksSayWhereTheWoodsAre()
        {
            var gen = map.Gen;
            int s = ChunkGen.Size;
            var c = gen.Make(heart.cx, heart.cy);
            var known = gen.Know(heart.cx, heart.cy);
            int trees = c.Cells.Count(ch => ch == Tree);
            double cover = known.Wood.Average(b => (double)b);
            TestContext.Out.WriteLine(string.Format(CultureInfo.InvariantCulture, "FOREST the densest wood's chunk {0}: {1} trees ({2:0.0}%), its known samples' mean cover {3:0}", heart, trees, 100.0 * trees / (s * s), cover));
            Assert.That(trees, Is.GreaterThan(s * s * forest.Most / 1000 / 2), "the heart of a wood is dense");
            Assert.That(cover, Is.GreaterThan(128), "and looks it from far off");
            // A chunk with no tree looks open.
            var bare = Chunks().First(k => !gen.Make(k.cx, k.cy).Cells.Contains(Tree));
            Assert.That(gen.Know(bare.cx, bare.cy).Wood.Average(b => (double)b), Is.LessThan(16), $"the treeless chunk {bare}");
            // Where the samples are laid (road, water) they're never wooded.
            for (int q = 0; q < known.Wood.Length; q++)
                if (known.Water[q] != KnownChunk.Dry) Assert.That(known.Wood[q], Is.EqualTo(0));
        }

        [Test, Explicit("places in the woods, for stills and walks")]
        public void Places()
        {
            var m = map.Hills.Massif;
            // Round the massif: where a wood stands highest on the foothills' way up (the tree line), by bearing.
            for (int a = 0; a < 360; a += 15)
            {
                double ang = a * Math.PI / 180;
                int best = -1, bx = 0, by = 0, bh = 0;
                for (int r = m.Reach; r > m.Meadow; r -= 16)
                {
                    int x = m.CentreX + (int)Math.Round(r * Math.Cos(ang)), y = m.CentreY + (int)Math.Round(r * Math.Sin(ang));
                    int h = map.Hills.At(x, y), d = forest.Density(x, y, h);
                    if (d >= forest.Least && h > best)
                        (best, bx, by, bh) = (h, x, y, d);
                }
                if (best > 0) TestContext.Out.WriteLine($"PLACES tree line at bearing {a}: ({bx}, {by}) land {best / 8} m, chance {bh}");
            }
            Assert.Pass();
        }

        [Test]
        public void TheWoodsGolden()
        {
            string Hex((int cx, int cy) c) => map.Gen.Make(c.cx, c.cy).Hash.ToString("x8", CultureInfo.InvariantCulture);
            TestContext.Out.WriteLine($"FOREST golden: the densest wood's chunk {heart} {Hex(heart)}");
            Assert.That((heart, Hex(heart)), Is.EqualTo(((27, 6), "436ef221")), "a change to the woods is a change to the map");
        }
    }
}
