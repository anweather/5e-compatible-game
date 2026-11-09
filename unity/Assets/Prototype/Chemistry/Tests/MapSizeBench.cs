// What a bigger map costs in the core (map-size, J-080): a world for the valley (576 x 576) and for the flat 3 x 3 test map
// (1728 x 1728, test-fixtures/maps), loaded from their folders as the game does, every cell a Cell object ("cells everywhere"): the memory, the
// time to load, and a full heightfield rebuild on a terrain layout of that size. Explicit: run it by name.
// dotnet test core -c Release --filter "FullyQualifiedName~MapSizeBench" --logger "console;verbosity=detailed"
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class MapSizeBench
    {
        static Rules R => ChemistryTests.R;

        /// <summary>The flat 1728 x 1728 test map, kept out of world/ so builds don't ship it and joins don't hash it.</summary>
        internal static string BigMap => Path.Combine(ChemistryTests.Folders.Repo, "test-fixtures", "maps", "flat-1728");

        // The map as made: grass everywhere, what a save is told apart from.
        sealed class FlatGrass : IChunkSource
        {
            public string Version => "map-size-bench";
            public void Make(int x, int y, Cell cell) => cell.T = "grass";
        }

        // The world from a map's folder: its cells (grass, the valley's water as water, its trees), its land.
        static World Load(string dir, out double ms)
        {
            var sw = Stopwatch.StartNew();
            var map = MapFile.Load(dir);
            var rows = MapFile.ReadCells(dir).Replace("\r", "").Split('\n');
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H);
            var w = new World(R, map.W, map.H, 7, "grass");
            for (int y = 0; y < map.H; y++)
                for (int x = 0; x < map.W; x++)
                {
                    char c = rows[y][x];
                    if (c == 'T') w.Paint(x, y, "tree");
                    else if (c == 'w') w.At(x, y).Water = 4;
                }
            w.SetLand(land);
            ms = sw.Elapsed.TotalMilliseconds;
            return w;
        }

        // A terrain layout of the map's size as the valley's is laid out: 64-cell chunks of 1.5 m cells, the same 3 x 3 of
        // them at high resolution in its middle.
        static TerrainGrid Layout(int size)
        {
            int chunks = size / Chunks.Size, mid = chunks / 2;
            return new TerrainGrid(1.5f, (-size * 0.75f, -size * 0.75f), (chunks, chunks), (0, size - 1), (size, size), (mid - 1, mid - 1, mid + 1, mid + 1));
        }

        // What's live on the heap, collected first: a measuring bench's only use of the collector.
#pragma warning disable S1215
        static long Heap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(true);
        }

        // How long a full collection takes with what's alive now: every Cell is an object the collector walks.
        static double FullCollect()
        {
            var sw = Stopwatch.StartNew();
            GC.Collect(2, GCCollectionMode.Forced, true);
            return sw.Elapsed.TotalMilliseconds;
        }
#pragma warning restore S1215

        static double Median(Func<double> run, int n)
        {
            var times = new List<double>();
            for (int k = 0; k < n; k++) times.Add(run());
            return times.OrderBy(t => t).ElementAt(n / 2);
        }

        // Where a world's load goes (cell-memory): the time and the bytes of each step, as Load and ChemGrid.StartWorld take
        // them, the cells' allocation apart from what's laid on them.
        [Test, Explicit("a benchmark: run it by name")]
        public void MapSizePhases()
        {
            foreach (string dir in new[] { ChemistryTests.WorldDir(), BigMap })
            {
                Phases(dir, false); // warms the JIT and the file cache
                Heap();
                Phases(dir, true);
            }
            Assert.Pass();
        }

        // One load, step by step; its own method, so nothing of it is still held when the next one measures.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Phases(string dir, bool tell)
        {
            long h0 = Heap();
            int gen0 = GC.CollectionCount(0), gen2 = GC.CollectionCount(2);
            var sw = Stopwatch.StartNew();
            var map = MapFile.Load(dir);
            var rows = MapFile.ReadCells(dir).Replace("\r", "").Split('\n');
            double readCells = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H);
            double readLand = sw.Elapsed.TotalMilliseconds;
            long hFiles = Heap();
            sw.Restart();
            var w = new World(R, map.W, map.H, 7, "grass");
            double alloc = sw.Elapsed.TotalMilliseconds;
            long h1 = Heap();
            sw.Restart();
            for (int y = 0; y < map.H; y++)
                for (int x = 0; x < map.W; x++)
                {
                    char c = rows[y][x];
                    if (c == 'T') w.Paint(x, y, "tree");
                    else if (c == 'w') w.At(x, y).Water = 4;
                    else w.Paint(x, y, "grass");
                }
            double paint = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            w.SetLand(land);
            double setLand = sw.Elapsed.TotalMilliseconds;
            long h2 = Heap();
            sw.Restart();
            w.KeepStart();
            double keep = sw.Elapsed.TotalMilliseconds;
            long h3 = Heap();
            sw.Restart();
            w.CellHash();
            double hash = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            w.Step();
            double tick = sw.Elapsed.TotalMilliseconds;
            long h4 = Heap();
            int g0 = GC.CollectionCount(0) - gen0, g2 = GC.CollectionCount(2) - gen2;
            if (!tell) return;
            double chunks = w.W * w.H / (double)Chunks.Area;
            string Kb(long a, long b) => ((b - a) / 1024.0 / chunks).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            TestContext.Out.WriteLine($"PHASES {Path.GetFileName(dir)} read cells {readCells:0} ms, land {readLand:0} ms ({Kb(h0, hFiles)} KB a chunk held); " +
                $"new World {alloc:0} ms {Kb(hFiles, h1)} KB a chunk; paint {paint:0} ms; SetLand {setLand:0} ms {Kb(h1, h2)} KB; KeepStart {keep:0} ms {Kb(h2, h3)} KB; " +
                $"CellHash {hash:0} ms; first tick {tick:0} ms {Kb(h3, h4)} KB; world {(h4 - hFiles) / 1048576.0:0} MB; gen0 {g0}, gen2 {g2} (some ours)");
            GC.KeepAlive(w);
            GC.KeepAlive(land);
            GC.KeepAlive(rows);
        }

        [Test, Explicit("a benchmark: run it by name")]
        public void MapSizeCosts()
        {
            foreach (string dir in new[] { ChemistryTests.WorldDir(), BigMap })
            {
                string id = Path.GetFileName(dir);
                Load(dir, out _); // warm the JIT and the file cache
                long before = Heap();
                var world = Load(dir, out _);
                long after = Heap();
                double load = Median(() =>
                {
                    Load(dir, out double ms);
                    return ms;
                }, 5);
                int cells = world.W * world.H;
                double bytes = (after - before) / (double)cells;
                var field = new CellHeightfield(Layout(world.W)) { Threaded = false };
                field.Reset(world);
                double rebuildOne = Median(() =>
                {
                    var sw = Stopwatch.StartNew();
                    field.Reset(world);
                    return sw.Elapsed.TotalMilliseconds;
                }, 3);
                field.Threaded = true;
                double rebuildMany = Median(() =>
                {
                    var sw = Stopwatch.StartNew();
                    field.Reset(world);
                    return sw.Elapsed.TotalMilliseconds;
                }, 3);
                double hash = Median(() =>
                {
                    var sw = Stopwatch.StartNew();
                    world.CellHash();
                    return sw.Elapsed.TotalMilliseconds;
                }, 3);
                double gc = Median(FullCollect, 3);
                // The online desync check's cost a tick (RollingHash): a slice of Every, as before chunk-hash, and of the
                // world's own window.
                double SliceMs(int slices)
                {
                    var sum = Fnv1aSum.Start();
                    return Median(() =>
                    {
                        var sw = Stopwatch.StartNew();
                        world.HashSlice(ref sum, 0, slices);
                        return sw.Elapsed.TotalMilliseconds;
                    }, 9);
                }
                int window = RollingHash.WindowFor(world);
                TestContext.Out.WriteLine($"MAP SIZE {id} the desync check a tick: {SliceMs(RollingHash.Every):0.00} ms at {RollingHash.Every} ticks a window, " +
                    $"{SliceMs(window):0.00} ms at its own {window}");
                var sws = Stopwatch.StartNew();
                string save = world.Save(new FlatGrass());
                double saveMs = sws.Elapsed.TotalMilliseconds;
                TestContext.Out.WriteLine($"MAP SIZE {id} a full collection with it alive {gc:0} ms; CellHash {hash:0} ms; a save against flat grass {save.Length / 1024.0:0} KB in {saveMs:0} ms");
                TestContext.Out.WriteLine($"MAP SIZE {id} {world.W} x {world.H}: {cells:N0} cells, world {(after - before) / 1048576.0:0} MB ({bytes:0} B a cell), " +
                    $"load {load:0} ms, rebuild {rebuildOne:0} ms one thread, {rebuildMany:0} ms threaded ({Environment.ProcessorCount} cores), {cells / (Chunks.Size * Chunks.Size)} chunks");
                GC.KeepAlive(world);
                GC.KeepAlive(field);
            }
            Assert.Pass();
        }
    }
}
