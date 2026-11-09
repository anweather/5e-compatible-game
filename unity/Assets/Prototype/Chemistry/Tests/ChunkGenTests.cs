// ChunkGen (J-080 phase 4, docs/design/world-squares.md section 5, level 2): the nine's chunks from its world file
// (world/maps/nine/map.json) and plan. A chunk is the same made in any order or on any thread (goldens of its hash), its
// land is a function of position so the cells either side of every chunk side meet, the valley's chunks are its own
// folder's, and a bake is a map that passes MapCheck. CHUNK_GEN_PNG_OUT=<folder> dotnet test core --filter
// "FullyQualifiedName~ChunkGenTests" writes the nine's heights as a small picture, and prints the times.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class ChunkGenTests
    {
        static GeneratedMap nine;
        static GeneratedChunk[] all;

        static string NineDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "nine");

        static GeneratedMap Nine => nine ??= GeneratedMap.Load(NineDir);

        // The whole nine, made on every thread at once.
        static GeneratedChunk[] All => all ??= Nine.MakeAll();

        static GeneratedChunk At(int cx, int cy) => All[cy * Nine.Gen.ChunksW + cx];

        // The hash of every chunk's hash in row-major order: the nine's golden.
        static uint HashOfAll(IEnumerable<GeneratedChunk> chunks)
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
        const uint NineGolden = 0x0416f8b3u;
        static readonly (int x, int y, uint hash)[] ChunkGoldens = { (13, 13, 0x8266fe6du), (9, 20, 0xa5cfdad9u), (4, 4, 0x9de4f5efu), (26, 0, 0x151ac700u) };

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
            var land = new int[w * gen.CellsH];
            foreach (var c in All)
                for (int j = 0; j < n; j++) Array.Copy(c.Land, j * n, land, (c.Y * n + j) * w + c.X * n, n);
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
                PictureOf(report);
                Assert.That(MapCheck.Errors(dir), Is.Empty);
                var map = MapFile.Load(dir);
                var rows = MapFile.ReadCells(dir).Split('\n');
                Assert.That(rows.Length, Is.EqualTo(map.H));
                Assert.That(rows[map.Spawn.y][map.Spawn.x], Is.EqualTo('@'), "the valley's spawn, moved with it");
                Assert.That(rows.Sum(r => r.Count(ch => ch == '@')), Is.EqualTo(1));
                foreach (string name in GeneratedMap.Baked) Assert.That(File.Exists(Path.Combine(dir, name)), name);
                // The rows the valley's buildings stand on are where the valley is now.
                var hold = WorldBuildings.Load(dir);
                Assert.That(hold, Is.Not.Null);
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
        static void PictureOf(BakeReport r)
        {
            string folder = Environment.GetEnvironmentVariable("CHUNK_GEN_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) return;
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
                    int cx = x * scale + 1, cy = y * scale + 1, v = r.Land[cy * r.W + cx];
                    char cell = cells[cy * r.W + cx];
                    int shade = v - r.Land[(cy - 1) * r.W + cx - 1];
                    (int R, int G, int B) col;
                    if (v < ChunkGen.SeaLevel) col = (30, 70 + Math.Max(0, 60 + v / 6), 150 + Math.Max(0, 60 + v / 8));
                    else if (cell == 'r' || cell == 'w') col = (90, 160, 230);
                    else
                    {
                        int t = Math.Min(255, Math.Max(0, v * 255 / 1800));
                        col = t < 128 ? (60 + t, 130 + t / 3, 50) : (188 + (t - 128) / 2, 172 + (t - 128) * 2 / 3, 50 + (t - 128) * 3 / 2);
                        if (cell == 'T') col = (col.R * 3 / 4, col.G * 3 / 4, col.B * 3 / 4);
                        if (cell == '#') col = (130, 125, 120);
                    }
                    int s = Math.Max(-60, Math.Min(60, shade * 6));
                    int k = (y * w + x) * 3;
                    rgb[k] = (byte)Math.Max(0, Math.Min(255, col.R + s));
                    rgb[k + 1] = (byte)Math.Max(0, Math.Min(255, col.G + s));
                    rgb[k + 2] = (byte)Math.Max(0, Math.Min(255, col.B + s));
                }
            // The squares' borders, faintly.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if ((x * scale) % ChunkGen.Square < scale || (y * scale) % ChunkGen.Square < scale)
                        for (int k = 0; k < 3; k++) rgb[(y * w + x) * 3 + k] = (byte)(rgb[(y * w + x) * 3 + k] * 3 / 4);
            string path = Path.Combine(folder, $"nine-{Nine.Seed}-heights.png");
            File.WriteAllBytes(path, LandmarkPicture.Png(rgb, w, h));
            TestContext.Out.WriteLine($"PICTURE {path} ({w} x {h}): land {r.Land.Min()}..{r.Land.Max()} land units");
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
    }
}
