// ChunkGen (J-080 phase 4, docs/design/world-squares.md section 5, level 2): the nine's chunks from its world file
// (world/maps/nine/map.json) and plan. A chunk is the same made in any order or on any thread (goldens of its hash), its
// land is a function of position so the cells either side of every chunk side meet, the valley's chunks are its own
// folder's and the land beside it meets them smoothly, rivers wander downhill and widen, high ground is mountains, and a
// bake is a map that passes MapCheck; every square has a spawn, on open, level, dry ground and reached on foot from the
// valley's. CHUNK_GEN_PNG_OUT=<folder> dotnet test core --filter
// "FullyQualifiedName~ChunkGenTests" writes the nine's heights as a small picture, and prints the times.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class ChunkGenTests
    {
        static GeneratedMap nine;
        static GeneratedChunk[] all;

        static string NineDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "nine");

        static GeneratedMap Nine => nine ??= GeneratedMap.Load(NineDir);

        // The whole nine, made on every thread at once.
        static GeneratedChunk[] All => all ??= Nine.MakeAll();

        static GeneratedChunk At(int cx, int cy) => All[cy * Nine.Gen.ChunksW + cx];

        static int[] wholeLand;

        // The whole map is dropped once the fixture is done, so a full EditMode run doesn't keep it (#211).
        [OneTimeTearDown]
        public void DropTheMap()
        {
            nine = null;
            all = null;
            wholeLand = null;
        }

        // The nine's land, row-major over all its cells.
        static int[] Whole
        {
            get
            {
                if (wholeLand != null) return wholeLand;
                var gen = Nine.Gen;
                int n = ChunkGen.Size, w = gen.CellsW;
                var land = new int[w * gen.CellsH];
                foreach (var c in All)
                    for (int j = 0; j < n; j++) Array.Copy(c.Land, j * n, land, (c.Y * n + j) * w + c.X * n, n);
                return wholeLand = land;
            }
        }

        // The hash of every chunk's hash in row-major order: the nine's golden.
        internal static uint HashOfAll(IEnumerable<GeneratedChunk> chunks)
        {
            uint h = 2166136261u;
            foreach (var c in chunks.OrderBy(c => c.Y).ThenBy(c => c.X))
                unchecked
                {
                    for (int k = 0; k < 4; k++)
                    {
                        h ^= (byte)(c.Hash >> (8 * k));
                        h *= 16777619u;
                    }
                }
            return h;
        }

        // The goldens: the nine's chunks for its seed, and a few alone. A change here is a new generator version
        // (ChunkGen.Version), or a change to the world files the plan reads.
        internal const uint NineGolden = 0x69abb343u;
        static readonly (int x, int y, uint hash)[] ChunkGoldens = { (13, 13, 0xf3fe1169u), (9, 20, 0xaae09923u), (4, 4, 0x8ba5599du), (26, 0, 0xf8076225u) };

        [Test]
        public void TheNinesWorldFileSaysItsShape()
        {
            var m = Nine;
            Assert.That((m.SquaresW, m.SquaresH), Is.EqualTo((3, 3)));
            Assert.That(m.PinnedMap, Is.EqualTo("valley"));
            Assert.That((m.Gen.ChunksW, m.Gen.ChunksH), Is.EqualTo((27, 27)));
            Assert.That((m.Gen.PinnedX, m.Gen.PinnedY), Is.EqualTo((0, 1152)), "the valley at the south-west, no negative row");
            var map = MapFile.Load(NineDir);
            Assert.That((map.W, map.H), Is.EqualTo((m.Gen.CellsW, m.Gen.CellsH)));
            Assert.That(m.Plan.Problems, Is.Empty);
        }

        // Section 5's must-haves: the nine's world file asks for a volcano, the plan gives one a square of the map (kept
        // whatever the town roll), and its cone stands in the land, the same on every load.
        [Test]
        public void TheNinesPlanHoldsAVolcano()
        {
            var plan = Nine.Plan;
            Assert.That(plan.Inputs.Musts, Is.EqualTo(new[] { "volcano" }));
            Assert.That(plan.MustMisses, Is.Empty);
            int square = plan.MustSquares.Single();
            Assert.That(plan.InMap(square), Is.True, "in the map");
            Assert.That(square, Is.Not.EqualTo(plan.Valley));
            var v = Nine.Far.All.Single(l => l.Type == "volcano");
            Assert.That(v.Square, Is.EqualTo(square));
            Assert.That(v.Name, Is.Not.Null, "a named volcano, from its rows");
            // Its cone: the rim stands 40 m or more over the land 100 cells out.
            int w = Nine.Gen.CellsW, rim = int.MinValue;
            var foot = new List<int>();
            for (int dy = -100; dy <= 100; dy++)
                for (int dx = -100; dx <= 100; dx++)
                {
                    int d2 = dx * dx + dy * dy, h = Whole[(v.Y + dy) * w + v.X + dx];
                    if (d2 <= 40 * 40) rim = Math.Max(rim, h);
                    else if (d2 >= 95 * 95 && d2 <= 100 * 100) foot.Add(h);
                }
            foot.Sort();
            TestContext.Out.WriteLine($"VOLCANO {v.Name} in square ({plan.Grid.X(square)}, {plan.Grid.Y(square)}) {plan.TemplateAt(square).Id}, at cell ({v.X}, {v.Y}); rim {rim}, foot {foot[foot.Count / 2]} (land units)");
            Assert.That(rim - foot[foot.Count / 2], Is.GreaterThanOrEqualTo(320));
            var again = GeneratedMap.Load(NineDir).Plan;
            Assert.That(again.Hash, Is.EqualTo(plan.Hash), "same seed, same plan");
            Assert.That(again.MustSquares, Is.EqualTo(plan.MustSquares));
        }

        [Test]
        public void ChunksMadeInTwoOrdersAreTheSame()
        {
            // A fresh generator, chunks made one at a time from the far corner back: the same as the nine made on every thread.
            var other = GeneratedMap.Load(NineDir).Gen;
            var picks = new List<(int x, int y)>();
            for (int k = 0; k < 40; k++) picks.Add((26 - k * 7 % 27, 26 - k * 11 % 27));
            picks.AddRange(new[] { (8, 18), (9, 18), (8, 17), (9, 17), (13, 9), (14, 9), (13, 8) });
            foreach (var (x, y) in picks)
            {
                var c = other.Make(x, y);
                Assert.That(c.Hash, Is.EqualTo(At(x, y).Hash), $"chunk ({x}, {y})");
                Assert.That(c.Land, Is.EqualTo(At(x, y).Land));
                Assert.That(c.Cells, Is.EqualTo(At(x, y).Cells));
            }
            // And again on one thread, row-major.
            var again = GeneratedMap.Load(NineDir).MakeAll(1);
            Assert.That(again.Select(c => c.Hash), Is.EqualTo(All.Select(c => c.Hash)));
        }

        [Test]
        public void TheChunkHashesAreTheGoldens()
        {
            uint whole = HashOfAll(All);
            TestContext.Out.WriteLine($"NINE GOLDEN 0x{whole:x8}, plan 0x{Nine.Plan.Hash:x8}; " +
                string.Join(", ", ChunkGoldens.Select(g => $"({g.x}, {g.y}) 0x{At(g.x, g.y).Hash:x8}")));
            Assert.That(whole, Is.EqualTo(NineGolden), "the nine's chunks");
            foreach (var (x, y, hash) in ChunkGoldens) Assert.That(At(x, y).Hash, Is.EqualTo(hash), $"chunk ({x}, {y})");
        }

        [Test]
        public void EveryChunksEdgeIsItsLandByPosition()
        {
            // The land at a chunk's edge is what the position alone gives (the neighbour's view of it), where no stamp moved it.
            var gen = Nine.Gen;
            int checkedCells = 0;
            foreach (var c in All.Where(c => !c.Pinned && c.Landmarks.Count == 0 && (c.X + c.Y) % 3 == 0))
                for (int k = 0; k < ChunkGen.Size; k += 7)
                    foreach (var (i, j) in new[] { (0, k), (ChunkGen.Size - 1, k), (k, 0), (k, ChunkGen.Size - 1) })
                    {
                        Assert.That(c.Land[j * ChunkGen.Size + i], Is.EqualTo(gen.LandAt(c.X * ChunkGen.Size + i, c.Y * ChunkGen.Size + j)), $"chunk ({c.X}, {c.Y}) cell ({i}, {j})");
                        checkedCells++;
                    }
            Assert.That(checkedCells, Is.GreaterThan(1000));
        }

        [Test]
        public void SeamsMatchAcrossEveryChunkSide()
        {
            // Across every chunk side the generator made (the valley's own sides are its folder's), the step from one cell
            // to the next is no bigger than the land makes inside its chunks: no cliff where two chunks, made apart, meet.
            var gen = Nine.Gen;
            int n = ChunkGen.Size, w = gen.CellsW;
            var land = Whole;
            int inside = 0, across = 0, worstX = 0, worstY = 0;
            for (int y = 0; y < gen.CellsH; y++)
                for (int x = 0; x + 1 < w; x++)
                {
                    if (gen.IsPinned(Chunks.ChunkOf(x), Chunks.ChunkOf(y)) && gen.IsPinned(Chunks.ChunkOf(x + 1), Chunks.ChunkOf(Math.Min(y + 1, gen.CellsH - 1)))) continue;
                    int east = Math.Abs(land[y * w + x + 1] - land[y * w + x]);
                    int south = y + 1 < gen.CellsH ? Math.Abs(land[(y + 1) * w + x] - land[y * w + x]) : 0;
                    bool seamE = (x + 1) % n == 0, seamS = (y + 1) % n == 0;
                    if (seamE && east > across) (across, worstX, worstY) = (east, x, y);
                    else if (!seamE) inside = Math.Max(inside, east);
                    if (seamS && south > across) (across, worstX, worstY) = (south, x, y);
                    else if (!seamS) inside = Math.Max(inside, south);
                }
            TestContext.Out.WriteLine($"SEAMS the biggest step across a chunk side {across} land units at ({worstX}, {worstY}); inside a chunk {inside}");
            Assert.That(across, Is.LessThanOrEqualTo(inside), $"a step of {across} across a chunk side at ({worstX}, {worstY})");
            // The valley's sides meet the land beside them within a metre a cell where the valley's own land does.
            int valleyStep = 0;
            int x0 = gen.PinnedX, y0 = gen.PinnedY;
            for (int t = 0; t < ChunkGen.Square; t++)
            {
                valleyStep = Math.Max(valleyStep, Math.Abs(land[(y0 - 1) * w + x0 + t] - land[y0 * w + x0 + t]) - Math.Abs(land[(y0 + 1) * w + x0 + t] - land[y0 * w + x0 + t]));
                valleyStep = Math.Max(valleyStep, Math.Abs(land[(y0 + t) * w + x0 + ChunkGen.Square] - land[(y0 + t) * w + x0 + ChunkGen.Square - 1]) - Math.Abs(land[(y0 + t) * w + x0 + ChunkGen.Square - 2] - land[(y0 + t) * w + x0 + ChunkGen.Square - 1]));
            }
            TestContext.Out.WriteLine($"SEAMS the valley's sides step at most {valleyStep} land units more than the valley's own next cell");
            Assert.That(valleyStep, Is.LessThanOrEqualTo(8));
        }

        [Test]
        public void TheValleysMeetIsSmoothBothWays()
        {
            // Outside the valley's north and east sides, the land meeting it is smooth along the side and across it: no
            // comb of rows or columns each blended on its own (as the first meet made, bending by 3 to 10 land units on about
            // one cell in eight). A bend is a cell against its two neighbours (|left + right − 2 × middle|, and up and
            // down): a smooth slope bends by 0 or 1 however steep it is. Over 2 is rare, where the biomes' own relief folds;
            // by the rim no sharper than the rim itself. Land a river, road or lake carves, the coast's step, and chunks a
            // landmark stamps are left out; the land beyond the meet is printed to compare.
            var gen = Nine.Gen;
            var land = Whole;
            int w = gen.CellsW, x0 = gen.PinnedX, y0 = gen.PinnedY, sq = ChunkGen.Square;
            int rim = 0;
            for (int t = 1; t + 1 < sq; t++)
            {
                rim = Math.Max(rim, Bend(land, w, x0 + t, y0, true));
                rim = Math.Max(rim, Bend(land, w, x0 + sq - 1, y0 + t, false));
            }
            var near = new Bends();
            var meet = new Bends();
            var beyond = new Bends();
            for (int y = 1; y + 1 < gen.CellsH; y++)
                for (int x = 1; x + 1 < w; x++)
                {
                    int cx = Math.Max(x0, Math.Min(x0 + sq - 1, x)), cy = Math.Max(y0, Math.Min(y0 + sq - 1, y));
                    long d2 = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    Bends into = null;
                    if (d2 == 0) continue;
                    if (d2 < 8 * 8) into = near;
                    else if (d2 < 90 * 90) into = meet;
                    else if (d2 >= 250 * 250 && d2 < 330 * 330) into = beyond;
                    if (into == null || At(x / ChunkGen.Size, y / ChunkGen.Size).Landmarks.Count > 0) continue;
                    int k = y * w + x;
                    if (new[] { k, k - 1, k + 1, k - w, k + w }.Any(i => land[i] < ChunkGen.SeaLevel + 2) || gen.NearPath(x, y)) continue;
                    into.Add(Math.Max(Bend(land, w, x, y, true), Bend(land, w, x, y, false)), x, y);
                }
            TestContext.Out.WriteLine($"MEET the valley's rim bends at most {rim}; within 8 cells of it {near}; the meet (8 to 90 cells) {meet}; beyond it (250 to 330) {beyond}");
            Assert.That(meet.Cells, Is.GreaterThan(40000));
            Assert.That(meet.Worst, Is.LessThanOrEqualTo(8), $"the meet's worst bend at ({meet.X}, {meet.Y})");
            Assert.That(meet.Over2 * 1000, Is.LessThanOrEqualTo(meet.Cells * 2), "bends over 2 land units are rare in the meet");
            Assert.That(near.Worst, Is.LessThanOrEqualTo(rim), $"by the rim, no bend sharper than the rim's own, at ({near.X}, {near.Y})");
        }

        [Test]
        public void TheFirstCellOutMeetsItsPinnedNeighbour()
        {
            // #193: along the valley's north and east sides (and round the corner between them), the first generated cell is
            // within a height step (0.5 m, 4 land units) of the valley's rim cell beside it. The meet once read the rim as a
            // mean over three cells for that first cell, so a rough rim stepped up to 8 land units (1 m) onto the generated
            // land at x 107 on the north side. Printed too: the steepest step out across the next 16 cells, against the
            // steepest along the rim itself (the valley's own hillside, up to 3 m a cell, which the meet carries on).
            var gen = Nine.Gen;
            var land = Whole;
            int w = gen.CellsW, x0 = gen.PinnedX, y0 = gen.PinnedY, sq = ChunkGen.Square, e = sq - 1;
            int worst = 0, worstX = 0, worstY = 0, out16 = 0, alongRim = 0;
            void Pair(int ix, int iy, int ox, int oy)
            {
                int step = Math.Abs(land[oy * w + ox] - land[iy * w + ix]);
                if (step > worst) (worst, worstX, worstY) = (step, ox, oy);
                int dx = ox - ix, dy = oy - iy;
                for (int k = 0; k < 16; k++)
                    out16 = Math.Max(out16, Math.Abs(land[(oy + (k + 1) * dy) * w + ox + (k + 1) * dx] - land[(oy + k * dy) * w + ox + k * dx]));
            }
            foreach (int side in OpenSides(gen))
                for (int t = 0; t < sq; t++)
                {
                    var (a, b) = Across(gen, side, t);
                    Pair(a % w, a / w, b % w, b / w);
                    if (t > 0)
                    {
                        var (prev, _) = Across(gen, side, t - 1);
                        alongRim = Math.Max(alongRim, Math.Abs(land[a] - land[prev]));
                    }
                }
            // The north-east corner's cell out, against the valley's corner cell.
            int cornerStep = Math.Abs(land[(y0 - 1) * w + x0 + e + 1] - land[y0 * w + x0 + e]);
            TestContext.Out.WriteLine($"SEAM the biggest step onto the valley {worst} land units at ({worstX}, {worstY}); the corner {cornerStep}; " +
                $"out across the next 16 cells {out16}; along the valley's rim {alongRim}");
            Assert.That(OpenSides(gen).ToList(), Is.EqualTo(new[] { 0, 1 }), "the nine's valley has its north and east sides open");
            Assert.That(worst, Is.LessThanOrEqualTo(4), $"a step of {worst} onto the valley at ({worstX}, {worstY})");
            Assert.That(cornerStep, Is.LessThanOrEqualTo(4), "at the north-east corner");
        }

        static int Bend(int[] land, int w, int x, int y, bool alongX)
        {
            int k = y * w + x, step = alongX ? 1 : w;
            return Math.Abs(land[k - step] + land[k + step] - 2 * land[k]);
        }

        sealed class Bends
        {
            public int Cells, Over2, Worst, X, Y;

            public void Add(int bend, int x, int y)
            {
                Cells++;
                if (bend > 2) Over2++;
                if (bend > Worst) (Worst, X, Y) = (bend, x, y);
            }

            public override string ToString() => $"{Cells} cells, {Over2} bend over 2, the worst {Worst} at ({X}, {Y})";
        }

        [Test]
        public void RiversWanderDownhillAndWidenDownstream()
        {
            var gen = Nine.Gen;
            var rivers = gen.Rivers;
            Assert.That(rivers, Is.Not.Empty);
            int joins = 0;
            foreach (var r in rivers)
            {
                string name = $"the river in square {r.Square} from ({r.X[0] >> 16}, {r.Y[0] >> 16})";
                for (int k = 1; k < r.X.Length; k++)
                {
                    Assert.That(r.Surface[k], Is.LessThanOrEqualTo(r.Surface[k - 1]), $"{name}: its water never rises downstream");
                    Assert.That(r.Half[k], Is.GreaterThanOrEqualTo(r.Half[k - 1]), $"{name}: it never narrows downstream");
                }
                // In its square's corridor (a slot on the east or south side is the next square's first cell).
                var (sx, sy) = gen.MapCell(r.Square, 0, 0);
                for (int k = 0; k < r.X.Length; k++)
                {
                    long x = r.X[k] >> 16, y = r.Y[k] >> 16;
                    Assert.That(x >= sx && x <= sx + ChunkGen.Square && y >= sy && y <= sy + ChunkGen.Square, $"{name}: ({x}, {y}) is in its square");
                }
                // Not a ruler line: somewhere it strays from the straight line between its ends.
                long ax = r.X[0], ay = r.Y[0], bx = r.X[^1], by = r.Y[^1];
                double chord = Math.Sqrt((double)(bx - ax) * (bx - ax) + (double)(by - ay) * (by - ay)) / 65536;
                double stray = 0;
                for (int k = 0; k < r.X.Length; k++)
                    stray = Math.Max(stray, Math.Abs((double)(bx - ax) * (r.Y[k] - ay) - (double)(by - ay) * (r.X[k] - ax)) / 65536 / 65536 / Math.Max(1, chord));
                TestContext.Out.WriteLine($"RIVER {name}: {chord:F0} cells end to end, strays {stray:F0}, water {r.Surface[0] / 256.0 / 8:F1} m to {r.Surface[^1] / 256.0 / 8:F1} m, half its width {r.Half[0] / 65536.0:F1} to {r.Half[^1] / 65536.0:F1} cells");
                if (chord > 150) Assert.That(stray, Is.GreaterThan(10), $"{name} wanders");
                // Where the next river starts as this one ends (the hub, or the next square across a side), it carries on.
                foreach (var next in rivers.Where(o => o != r && o.X[0] == bx && o.Y[0] == by))
                {
                    Assert.That(next.Surface[0], Is.EqualTo(r.Surface[^1]), $"{name} runs on at the same level");
                    Assert.That(next.Half[0], Is.EqualTo(r.Half[^1]), $"{name} runs on as wide");
                    joins++;
                }
            }
            Assert.That(joins, Is.GreaterThanOrEqualTo(3), "the nine's river runs through squares and hubs");
            // And the water is there: a river's cells along its middle.
            int wet = 0, middles = 0;
            foreach (var r in rivers)
                for (int k = 1; k + 1 < r.X.Length; k++)
                {
                    int x = (int)(r.X[k] >> 16), y = (int)(r.Y[k] >> 16);
                    if (gen.IsPinned(x / ChunkGen.Size, y / ChunkGen.Size) || x >= gen.CellsW || y >= gen.CellsH) continue;
                    middles++;
                    var c = At(x / ChunkGen.Size, y / ChunkGen.Size);
                    if (c.Cells[(y % ChunkGen.Size) * ChunkGen.Size + x % ChunkGen.Size] == 'r') wet++;
                }
            Assert.That(wet * 10, Is.GreaterThanOrEqualTo(middles * 9), $"{wet} of {middles} river middles are water");
        }

        [Test]
        public void HighGroundReadsAsMountains()
        {
            // A square above the hills band is ranges, saddles and shoulders, not one cap: lines across it every 48 cells,
            // each way, rise and fall by 10 m more than twice on average (a cap: about once).
            var gen = Nine.Gen;
            var land = Whole;
            int w = gen.CellsW, sq = ChunkGen.Square;
            var high = Enumerable.Range(0, Nine.Plan.Count).Where(i => Nine.Plan.Band[i] >= 2).Select(i => gen.MapCell(i, 0, 0))
                .Where(p => p.x >= 0 && p.y >= 0 && p.x < gen.CellsW && p.y < gen.CellsH).ToList();
            Assert.That(high, Is.Not.Empty, "the nine has high ground");
            foreach (var (sx, sy) in high)
            {
                int turns = 0, lines = 0;
                for (int k = 24; k < sq; k += 48)
                {
                    turns += Turns(Enumerable.Range(0, sq / 4).Select(t => land[(sy + k) * w + sx + t * 4]), 80);
                    turns += Turns(Enumerable.Range(0, sq / 4).Select(t => land[(sy + t * 4) * w + sx + k]), 80);
                    lines += 2;
                }
                TestContext.Out.WriteLine($"MOUNTAINS the square at ({sx}, {sy}): {turns} turns of 10 m on {lines} lines across it");
                Assert.That(turns, Is.GreaterThan(2 * lines), $"the square at ({sx}, {sy}) rises and falls");
            }
        }

        // The peaks and troughs along a line, each counted once the land has come back by at least the given height.
        static int Turns(IEnumerable<int> line, int by)
        {
            int turns = 0, dir = 0, hi = int.MinValue, lo = int.MaxValue;
            foreach (int v in line)
            {
                hi = Math.Max(hi, v);
                lo = Math.Min(lo, v);
                if (dir >= 0 && hi - v >= by)
                {
                    if (dir > 0) turns++;
                    (dir, hi, lo) = (-1, v, v);
                }
                else if (dir <= 0 && v - lo >= by)
                {
                    if (dir < 0) turns++;
                    (dir, hi, lo) = (1, v, v);
                }
            }
            return turns;
        }

        [TestCase("valley-1")]
        [TestCase("seed-4")]
        [TestCase("seed-9")]
        public void EverySquaresSpawnIsOpenFlatAndDry(string seed)
        {
            // One spawn per map square (all nine are land below the peaks), the valley's its own map's spawn; every other
            // one beside a road where its square has one, near its anchor, on cleared, level grass: no water, tree, stone
            // or boulder in its clearing, off every landmark's footprint and above the sea.
            var m = seed == Nine.Seed ? Nine : GeneratedMap.Load(NineDir, seed);
            var gen = m.Gen;
            Assert.That(gen.Spawns.Count, Is.EqualTo(9), "a spawn a square");
            Assert.That(gen.Spawns.Select(s => (s.X / ChunkGen.Square, s.Y / ChunkGen.Square)).Distinct().Count(), Is.EqualTo(9));
            var valley = gen.Spawns.Single(s => s.Pinned);
            var map = MapFile.Load(NineDir);
            Assert.That((valley.X, valley.Y), Is.EqualTo(map.Spawn), "the valley's spawn is the map's");
            var made = new Dictionary<(int, int), GeneratedChunk>();
            GeneratedChunk ChunkAt(int x, int y)
            {
                var key = (x / ChunkGen.Size, y / ChunkGen.Size);
                if (!made.TryGetValue(key, out var c)) made[key] = c = seed == Nine.Seed ? At(key.Item1, key.Item2) : gen.Make(key.Item1, key.Item2);
                return c;
            }
            int LandOf(int x, int y) => ChunkAt(x, y).Land[(y % ChunkGen.Size) * ChunkGen.Size + x % ChunkGen.Size];
            char CellOf(int x, int y) => ChunkAt(x, y).Cells[(y % ChunkGen.Size) * ChunkGen.Size + x % ChunkGen.Size];
            foreach (var s in gen.Spawns.Where(s => !s.Pinned))
            {
                var p = m.Plan.Spawns.Single(q => q.Square == s.Square);
                string name = $"{seed}: the spawn at ({s.X}, {s.Y}) near the {s.Near}";
                TestContext.Out.WriteLine($"SPAWN {name}, room {m.Plan.SpawnRoom(s.Square, p.SiteX, p.SiteY)}, by a road {p.ByRoad}, land {LandOf(s.X, s.Y) / 8.0:F1} m");
                Assert.That(m.Plan.Band[s.Square], Is.LessThan(m.Plan.Library.Lands - 1), $"{name}: not on the peaks");
                int lo = int.MaxValue, hi = int.MinValue;
                for (int dy = -ChunkGen.ClearCells + 1; dy < ChunkGen.ClearCells; dy++)
                    for (int dx = -ChunkGen.ClearCells + 1; dx < ChunkGen.ClearCells; dx++)
                    {
                        if (dx * dx + dy * dy >= ChunkGen.ClearCells * ChunkGen.ClearCells) continue;
                        int x = s.X + dx, y = s.Y + dy;
                        char cell = CellOf(x, y);
                        Assert.That(cell == ',' || cell == '.', $"{name}: ({x}, {y}) is '{cell}', not open ground");
                        lo = Math.Min(lo, LandOf(x, y));
                        hi = Math.Max(hi, LandOf(x, y));
                    }
                Assert.That(hi - lo, Is.LessThanOrEqualTo(2), $"{name}: its clearing is level");
                Assert.That(lo, Is.GreaterThan(ChunkGen.SeaLevel + 8), $"{name}: above the sea");
                foreach (var t in ChunkAt(s.X, s.Y).Things.Select(t => (Newtonsoft.Json.Linq.JArray)t["cell"]))
                {
                    int tx = (s.X / ChunkGen.Size) * ChunkGen.Size + (int)t[0], ty = (s.Y / ChunkGen.Size) * ChunkGen.Size + (int)t[1];
                    Assert.That(Math.Max(Math.Abs(tx - s.X), Math.Abs(ty - s.Y)), Is.GreaterThanOrEqualTo(ChunkGen.ClearCells), $"{name}: a thing in its clearing");
                }
                foreach (var r in gen.LandmarkRows.Where(r => r != null))
                {
                    long dx = s.X - r.Centre.x, dy = s.Y - r.Centre.y;
                    Assert.That(dx * dx + dy * dy, Is.GreaterThan((long)(r.Reach + ChunkGen.ClearCells) * (r.Reach + ChunkGen.ClearCells)), $"{name}: inside {r.Name}'s footprint");
                }
                if (p.ByRoad)
                {
                    bool road = false;
                    for (int dy = -10; dy <= 10 && !road; dy++)
                        for (int dx = -10; dx <= 10 && !road; dx++) road = CellOf(s.X + dx, s.Y + dy) == '.' && gen.NearPath(s.X + dx, s.Y + dy);
                    Assert.That(road, $"{name}: beside its road");
                }
            }
        }

        // A cell a walker can stand on: dry land (no sea, lake or river) that isn't a wall. Stone, trees and brambles are
        // walls wherever they're read (ChemGrid's legend, materials.json's "blocking").
        static bool Walkable(int[] land, char[] cells, int k) =>
            land[k] >= ChunkGen.SeaLevel && cells[k] != 'w' && cells[k] != 'r' && cells[k] != '#' && cells[k] != 'T' && cells[k] != '"';

        // A step a walker takes between two side-by-side cells: both walkable, and no more than a metre up or down.
        static bool Steps(int[] land, char[] cells, int a, int b) =>
            Walkable(land, cells, a) && Walkable(land, cells, b) && Math.Abs(land[a] - land[b]) <= 8;

        // The valley's sides that face another square of the map (north 0, east 1, south 2, west 3; the nine's are north
        // and east), and for side s and a place along it the valley's rim cell and the map cell just outside it.
        static IEnumerable<int> OpenSides(ChunkGen gen)
        {
            int x0 = gen.PinnedX, y0 = gen.PinnedY, sq = ChunkGen.Square;
            if (y0 > 0) yield return 0;
            if (x0 + sq < gen.CellsW) yield return 1;
            if (y0 + sq < gen.CellsH) yield return 2;
            if (x0 > 0) yield return 3;
        }

        static (int inside, int outside) Across(ChunkGen gen, int s, int along)
        {
            int x0 = gen.PinnedX, y0 = gen.PinnedY, e = ChunkGen.Square - 1, w = gen.CellsW;
            var (ix, iy, ox, oy) = s switch
            {
                0 => (x0 + along, y0, x0 + along, y0 - 1),
                1 => (x0 + e, y0 + along, x0 + e + 1, y0 + along),
                2 => (x0 + along, y0 + e, x0 + along, y0 + e + 1),
                _ => (x0, y0 + along, x0 - 1, y0 + along),
            };
            return (iy * w + ix, oy * w + ox);
        }

        internal static (int[] land, char[] cells) WholeOf(ChunkGen gen, IEnumerable<GeneratedChunk> chunks)
        {
            int w = gen.CellsW, n = ChunkGen.Size;
            var land = new int[w * gen.CellsH];
            var cells = new char[w * gen.CellsH];
            foreach (var c in chunks)
                for (int j = 0; j < n; j++)
                {
                    Array.Copy(c.Land, j * n, land, (c.Y * n + j) * w + c.X * n, n);
                    Array.Copy(c.Cells, j * n, cells, (c.Y * n + j) * w + c.X * n, n);
                }
            return (land, cells);
        }

        [TestCase("valley-1")]
        [TestCase("seed-4")]
        [TestCase("seed-9")]
        public void EverySpawnIsReachableOnFootAcrossEachOfTheValleysOpenSides(string seed)
        {
            // From the valley's spawn, walking from cell to cell over dry land, never through a wall (stone, a tree,
            // brambles) and never up or down more than a metre a cell, every other square's spawn is reached; and it is
            // reached across each of the valley's open sides alone (the other closed), so neither side is a wall. Steep
            // generated land once laid stone on every face over 1.25 m a cell, which walled off nearly all of the valley's
            // north and east seam; this would have caught it.
            var m = seed == Nine.Seed ? Nine : GeneratedMap.Load(NineDir, seed);
            var gen = m.Gen;
            var (land, cells) = WholeOf(gen, seed == Nine.Seed ? All : m.MakeAll());
            int w = gen.CellsW, h = gen.CellsH, x0 = gen.PinnedX, y0 = gen.PinnedY, sq = ChunkGen.Square;
            bool InValley(int k) => k % w >= x0 && k % w < x0 + sq && k / w >= y0 && k / w < y0 + sq;
            var start = gen.Spawns.Single(s => s.Pinned);
            var sides = OpenSides(gen).ToList();
            Assert.That(sides, Is.Not.Empty, "the valley has a side on the map");
            foreach (int side in sides)
            {
                var seen = new bool[w * h];
                var queue = new Queue<int>();
                queue.Enqueue(start.Y * w + start.X);
                seen[start.Y * w + start.X] = true;
                int reached = 0;
                while (queue.Count > 0)
                {
                    int k = queue.Dequeue();
                    reached++;
                    int x = k % w, y = k / w;
                    foreach (var (nx, ny, s) in new[] { (x, y - 1, 0), (x + 1, y, 1), (x, y + 1, 2), (x - 1, y, 3) })
                    {
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (seen[n] || !Steps(land, cells, k, n)) continue;
                        // Into or out of the valley only across this side (s is the way the step goes, out of the valley).
                        if (InValley(k) != InValley(n) && (InValley(k) ? s : (s + 2) % 4) != side) continue;
                        seen[n] = true;
                        queue.Enqueue(n);
                    }
                }
                TestContext.Out.WriteLine($"REACH {seed} across side {side} alone: {reached} of {w * h} cells on foot from the valley's spawn");
                foreach (var s in gen.Spawns)
                    Assert.That(seen[s.Y * w + s.X], $"{seed}: the spawn at ({s.X}, {s.Y}) near the {s.Near} is reached across the valley's side {side}");
            }
        }

        [TestCase("valley-1")]
        [TestCase("seed-4")]
        [TestCase("seed-9")]
        public void TheValleysOpenSidesAreCrossableAlongMostOfTheirLength(string seed)
        {
            // Along each of the valley's open sides, a walker steps from the valley's rim cell to the cell outside it at
            // most places its rim is dry land (the valley's own sea and lakes are left out: its east side is sea south of
            // row 324), in every one of the plan's slots on that side (a chunk's width each) with dry rim, and at every
            // road that crosses it, on the road. Where it can't, it is the valley's own trees or a step over a metre (no
            // stone is laid on a steep face), and no stone at all stands outside the rim.
            var m = seed == Nine.Seed ? Nine : GeneratedMap.Load(NineDir, seed);
            var gen = m.Gen;
            var (land, cells) = WholeOf(gen, seed == Nine.Seed ? All : m.MakeAll());
            int sq = ChunkGen.Square, per = sq / m.Plan.Library.Slots;
            foreach (int side in OpenSides(gen))
            {
                var crossable = new bool[sq];
                var dry = new bool[sq];
                int stone = 0;
                for (int t = 0; t < sq; t++)
                {
                    var (a, b) = Across(gen, side, t);
                    dry[t] = land[a] >= ChunkGen.SeaLevel && cells[a] != 'w' && cells[a] != 'r';
                    crossable[t] = Steps(land, cells, a, b);
                    if (cells[b] == '#') stone++;
                }
                int open = crossable.Count(c => c), rim = dry.Count(d => d);
                TestContext.Out.WriteLine($"SEAM {seed} side {side}: {open} of its {rim} dry rim cells cross ({sq - rim} are the valley's water), {stone} outside are stone");
                Assert.That(rim, Is.GreaterThan(sq / 4), $"{seed}: side {side} is mostly land");
                Assert.That(open * 100, Is.GreaterThanOrEqualTo(rim * 90), $"{seed}: side {side} crosses along most of its dry rim");
                Assert.That(stone, Is.Zero, $"{seed}: no stone along side {side}, outside the valley");
                for (int slot = 0; slot < m.Plan.Library.Slots; slot++)
                {
                    var span = Enumerable.Range(slot * per, per).ToList();
                    if (span.Any(t => dry[t])) Assert.That(span.Any(t => crossable[t]), $"{seed}: side {side} crosses in slot {slot}");
                }
                foreach (var f in m.Plan.Features[m.Plan.Valley][side].Where(f => f.Kind == EdgeKind.Road))
                {
                    int mid = f.Slot * per + per / 2;
                    Assert.That(Enumerable.Range(mid - 3, 7).Any(t => crossable[t]), $"{seed}: side {side} crosses on its road at slot {f.Slot}");
                }
            }
        }

        [Test]
        public void TheValleysChunksAreItsOwnFolders()
        {
            string valley = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
            var land = LandFile.Read(File.ReadAllText(Path.Combine(valley, MapFile.LandName)), ChunkGen.Square, ChunkGen.Square);
            var rows = MapFile.ReadCells(valley).Replace("\r", "").Trim('\n').Split('\n');
            var gen = Nine.Gen;
            foreach (var c in All.Where(c => c.Pinned))
                for (int j = 0; j < ChunkGen.Size; j++)
                    for (int i = 0; i < ChunkGen.Size; i++)
                    {
                        int vx = c.X * ChunkGen.Size + i - gen.PinnedX, vy = c.Y * ChunkGen.Size + j - gen.PinnedY;
                        Assert.That(c.Land[j * ChunkGen.Size + i], Is.EqualTo(land[vy * ChunkGen.Square + vx]));
                        Assert.That(c.Cells[j * ChunkGen.Size + i], Is.EqualTo(rows[vy][vx]));
                    }
            Assert.That(All.Count(c => c.Pinned), Is.EqualTo(81));
            Assert.That(At(4, 22).Things.Count, Is.GreaterThan(0), "the valley's chunk (4, 4) keeps its things");
        }

        [Test]
        public void TheLandmarksAreMadeWhereThePlanSites()
        {
            var gen = Nine.Gen;
            var made = Enumerable.Range(0, gen.LandmarkRows.Count).Where(k => gen.LandmarkRows[k] != null).ToList();
            Assert.That(made, Is.Not.Empty, "the nine has a village");
            foreach (int k in made)
            {
                var l = Nine.Plan.Landmarks[k];
                var (x, y) = gen.MapCell(l.Square, l.SiteX, l.SiteY);
                Assert.That(All.Any(c => c.Landmarks.Contains(k) && c.X == Chunks.ChunkOf(x) && c.Y == Chunks.ChunkOf(y)), $"{l.Kind} at ({x}, {y})");
            }
        }

        [Test]
        public void TheBakeIsAMapThatPassesItsChecks()
        {
            string world = Path.Combine(Path.GetTempPath(), "chunk-gen-" + Guid.NewGuid().ToString("N"));
            string dir = Path.Combine(world, "nine");
            try
            {
                var report = Nine.Bake(dir, All);
                TestContext.Out.WriteLine($"BAKE writing the nine's folder {report.TotalMs - report.MakeMs:F0} ms");
                PictureOf(dir, report);
                Assert.That(MapCheck.Errors(dir), Is.Empty);
                var map = MapFile.Load(dir);
                // This folder is away from the map's own, as a build's bake is (the player's data), and the pinned map
                // can't be found from there: the bake says the full detail the pinned map gave it (#183: the nine's ground
                // was the valley's 257 samples a chunk on a layout of 65, and a build's world was never made).
                var own = MapFile.Load(Nine.MapDir);
                Assert.That(own.FullDetail, Is.EqualTo((3, 2, 5, 4)), "the valley's full detail, where its square is (terrain rows count from the south)");
                Assert.That(map.FullDetail, Is.EqualTo(own.FullDetail), "a bake away from the map's folder draws the chunks its map does");
                foreach (var (i, j) in new[] { (0, 0), (3, 2), (4, 3), (5, 4), (6, 4) })
                    Assert.That(map.Layout().Resolution(i, j), Is.EqualTo(own.Layout().Resolution(i, j)), $"chunk ({i}, {j})'s samples");
                var rows = MapFile.ReadCells(dir).Split('\n');
                Assert.That(rows.Length, Is.EqualTo(map.H));
                Assert.That(rows[map.Spawn.y][map.Spawn.x], Is.EqualTo('@'), "the valley's spawn, moved with it");
                Assert.That(rows.Sum(r => r.Count(ch => ch == '@')), Is.EqualTo(1));
                foreach (string name in GeneratedMap.Baked) Assert.That(File.Exists(Path.Combine(dir, name)), name);
                // A spawn place a square, named for it, at its spawn; the valley's the map's start.
                var spawns = WorldPlaces.Load(dir).All.Where(p => p.Kind == "spawn").ToList();
                TestContext.Out.WriteLine("BAKE spawns: " + string.Join(", ", spawns.Select(p => $"{p.Name} {p.Middle}")));
                Assert.That(spawns.Select(p => p.Middle), Is.EquivalentTo(Nine.Gen.Spawns.Select(s => (s.X, s.Y))));
                Assert.That(spawns.Select(p => p.Name).Distinct().Count(), Is.EqualTo(9));
                var start = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, WorldPlaces.FileName)))["places"].Single(p => (bool?)p["start"] == true);
                Assert.That(spawns.Single(p => p.Id == (string)start["id"]).Middle, Is.EqualTo(map.Spawn), "the valley's spawn is the map's start");
                // The rows the valley's buildings stand on are where the valley is now.
                var hold = WorldBuildings.Load(dir);
                Assert.That(hold, Is.Not.Null);
                // A build's bake is kept while it's of these inputs and this format; an older one (it has no "format",
                // and its map.json none of the pinned map's full detail) is made again.
                Assert.That(GeneratedMap.EnsureBakedInto(NineDir, dir), Is.False, "a bake of these inputs in this format is kept");
                File.Copy(Path.Combine(NineDir, MapFile.FileName), Path.Combine(dir, MapFile.FileName), true);
                var note = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, GeneratedMap.BakeName)));
                note.Remove("format");
                File.WriteAllText(Path.Combine(dir, GeneratedMap.BakeName), note.ToString());
                Assert.That(MapFile.Load(dir).FullDetail, Is.EqualTo((0, 0, -1, -1)), "the old bake, as a build before #183's fix left it");
                Assert.That(GeneratedMap.EnsureBakedInto(NineDir, dir), Is.True, "a bake of an older format is made again");
                Assert.That(MapFile.Load(dir).FullDetail, Is.EqualTo(own.FullDetail));
                Assert.That(MapCheck.Errors(dir), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(world)) Directory.Delete(world, true);
            }
        }

        [Test]
        public void ItsHashIsOfItsInputs()
        {
            string hash = GeneratedMap.InputHash(NineDir);
            Assert.That(MapIdentity.Hash(NineDir), Is.EqualTo(hash), "a generated map's identity is its inputs");
            Assert.That(hash, Is.Not.EqualTo(MapIdentity.Hash(Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley"))));
            Assert.That(GeneratedMap.InputHash(NineDir), Is.EqualTo(hash), "the same inputs, the same hash");
        }

        [Test]
        public void ItsTimes()
        {
            var gen = GeneratedMap.Load(NineDir).Gen;
            gen.Make(13, 13);
            var times = new List<double>();
            for (int k = 0; k < 27; k++)
            {
                var watch = Stopwatch.StartNew();
                gen.Make((k * 5 + 9) % 27, k % 18);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            long before = Heap();
            var watchAll = Stopwatch.StartNew();
            var chunks = GeneratedMap.Load(NineDir).MakeAll();
            double allMs = watchAll.Elapsed.TotalMilliseconds;
            long after = Heap();
            TestContext.Out.WriteLine($"TIMES a chunk median {times[times.Count / 2]:F1} ms, p95 {times[times.Count * 95 / 100]:F1} ms; " +
                $"the nine (729 chunks) {allMs:F0} ms on {Environment.ProcessorCount} threads, {(after - before) / (1024 * 1024)} MB held");
            Assert.That(chunks.Length, Is.EqualTo(729));
            GC.KeepAlive(chunks);
        }

        // What's live on the heap, collected first: a measuring test's only use of the collector.
#pragma warning disable S1215
        static long Heap() => GC.GetTotalMemory(true);
#pragma warning restore S1215

        // The nine's heights, a pixel for every three cells: sea blue by depth, land green to brown to white by height,
        // shaded from the north-west; rivers and lakes light blue, roads brown, the valley's edge drawn.
        static void PictureOf(string dir, BakeReport r)
        {
            string folder = Environment.GetEnvironmentVariable("CHUNK_GEN_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) return;
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), r.W, r.H);
            Directory.CreateDirectory(folder);
            const int scale = 3;
            int w = r.W / scale, h = r.H / scale;
            var rgb = new byte[w * h * 3];
            var cells = new char[r.W * r.H];
            foreach (var c in All)
                for (int j = 0; j < ChunkGen.Size; j++)
                    Array.Copy(c.Cells, j * ChunkGen.Size, cells, (c.Y * ChunkGen.Size + j) * r.W + c.X * ChunkGen.Size, ChunkGen.Size);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cx = x * scale + 1, cy = y * scale + 1, v = land[cy * r.W + cx];
                    char cell = cells[cy * r.W + cx];
                    int shade = v - land[(cy - 1) * r.W + cx - 1];
                    var col = WorldPlanPicture.Height(v);
                    if (v >= ChunkGen.SeaLevel && (cell == 'r' || cell == 'w')) col = (90, 160, 230);
                    else if (v >= ChunkGen.SeaLevel && cell == 'T') col = (col.R * 3 / 4, col.G * 3 / 4, col.B * 3 / 4);
                    else if (v >= ChunkGen.SeaLevel && cell == '#') col = (130, 125, 120);
                    WorldPlanPicture.Put(rgb, y * w + x, col, Math.Max(-60, Math.Min(60, shade * 6)));
                }
            // The squares' borders, faintly.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if ((x * scale) % ChunkGen.Square < scale || (y * scale) % ChunkGen.Square < scale)
                        for (int k = 0; k < 3; k++) rgb[(y * w + x) * 3 + k] = (byte)(rgb[(y * w + x) * 3 + k] * 3 / 4);
            string path = Path.Combine(folder, $"nine-{Nine.Seed}-heights.png");
            File.WriteAllBytes(path, LandmarkPicture.Png(rgb, w, h));
            TestContext.Out.WriteLine($"PICTURE {path} ({w} x {h}): land {land.Min()}..{land.Max()} land units");
            CellsOf(folder, cells, r.W, 1, 0, "north-middle");
            CellsOf(folder, cells, r.W, 1, 1, "middle");
        }

        // One world square's cells at a pixel a cell, by their Legend character: roads and paths are the dirt lines.
        static void CellsOf(string folder, char[] cells, int width, int sx, int sy, string name)
        {
            int n = ChunkGen.Square;
            var rgb = new byte[n * n * 3];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    (byte, byte, byte) col = cells[(sy * n + y) * width + sx * n + x] switch
                    {
                        ',' => (110, 160, 70),
                        'T' => (40, 90, 40),
                        '"' => (140, 150, 60),
                        '.' => (150, 110, 70),
                        '#' => (120, 120, 120),
                        'w' => (50, 90, 170),
                        'r' => (90, 160, 230),
                        'L' or 'P' or '=' => (90, 60, 30),
                        _ => (255, 0, 255),
                    };
                    int k = (y * n + x) * 3;
                    (rgb[k], rgb[k + 1], rgb[k + 2]) = col;
                }
            string path = Path.Combine(folder, $"nine-{Nine.Seed}-{name}-cells.png");
            File.WriteAllBytes(path, LandmarkPicture.Png(rgb, n, n));
            TestContext.Out.WriteLine($"PICTURE {path} ({n} x {n}): square ({sx}, {sy})'s cells");
        }

        // desert-canyons: the map's plan (canyons, the bridges over them, its spawns), then its bake: MapCheck, and the
        // canyons and bridges in its cells and land. A bake of the whole map: run it by name.
        [Test]
        [Explicit("bakes a 12 x 10 map: run it by name")]
        public void TheDesertCanyonsMap()
        {
            string world = LandmarkIntentTests.GameWorldDir();
            var m = GeneratedMap.Load(Path.Combine(world, "maps", "desert-canyons"));
            int canyonEdges = 0;
            for (int i = 0; i < m.Plan.Count; i++)
                for (int side = 0; side < 4; side++) canyonEdges += m.Plan.Features[i][side].Count(f => f.Kind == EdgeKind.Canyon);
            var bridges = m.Plan.Crossings.Where(x => x.Used && x.Kind == "bridge" && x.Over == "canyon").ToList();
            TestContext.Out.WriteLine($"DESERT plan: {m.SquaresW} x {m.SquaresH} squares, canyon edges {canyonEdges / 2} (between squares), "
                + $"bridges over canyons {bridges.Count}, spawns {m.Gen.Spawns.Count}, problems: {string.Join("; ", m.Plan.Problems)}");
            string root = Path.Combine(Path.GetTempPath(), "desert-canyons-bake");
            string dir = Path.Combine(root, "desert-canyons");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            m.Bake(dir);
            var errors = MapCheck.Errors(dir);
            string cells = File.ReadAllText(Path.Combine(dir, MapFile.CellsName));
            int deck = cells.Count(c => c == '=' ), railing = cells.Count(c => c == 'L');
            TestContext.Out.WriteLine($"DESERT baked in {watch.Elapsed.TotalSeconds:0} s: MapCheck {errors.Count} errors ({string.Join("; ", errors.Take(5))}); deck cells {deck}, railing cells {railing}");
            Assert.That(canyonEdges, Is.GreaterThan(0));
        }
    }
}
