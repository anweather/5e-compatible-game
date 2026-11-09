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
