using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What the sparse World costs and saves (World.Pack.cs; docs/design/sparse-world.md, "Step 2, measured"), on the nine
    /// baked (27 x 27 chunks) and built as ChemGrid.MapWorld builds it: its cells.txt the World's source (BakedSource),
    /// then its land and whatever the bake lays (puzzles, stamps, rows, golf, the Hold). The World's memory with every
    /// chunk unpacked (what the World held before step 2, less World.Cells) and as play leaves it packed; then a walk
    /// across it, a character's reach activating ahead and deactivating behind with the view's drawn hint round it, timed
    /// tick by tick with packing on and off, and the two worlds' hashes compared. Not a check and not in CI (Explicit):
    /// run it by name in Release and read the lines it prints.
    /// <code>dotnet test core -c Release --filter "FullyQualifiedName~SparseWorldBench" --logger "console;verbosity=detailed"</code>
    /// </summary>
    [Explicit("a benchmark: run it by name")]
    [Category("Simulation")]
    public class SparseWorldBench
    {
        static Rules R => ChemistryTests.R;

        // The nine's bake, made once into a folder of the bench's own (the inputs copied as ScaleIslandTests copies them).
        static string baked;

        static string Baked()
        {
            if (baked != null) return baked;
            string root = Path.Combine(Path.GetTempPath(), "sparse-world-nine");
            string dir = Path.Combine(root, "baked");
            if (!File.Exists(Path.Combine(dir, MapFile.CellsName)))
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.CreateDirectory(root);
                ScaleIslandTests.CopyInputs(root);
                var watch = Stopwatch.StartNew();
                GeneratedMap.Load(Path.Combine(root, "maps", "nine")).Bake(dir);
                TestContext.Out.WriteLine($"SPARSE baked the nine in {watch.Elapsed.TotalSeconds:0.0} s");
            }
            return baked = dir;
        }

        /// <summary>A world as ChemGrid.MapWorld builds one from a map folder, and its starting state kept (StartWorld).</summary>
        internal static World Build(string dir, Rules rules, out BakedSource source)
        {
            var map = MapFile.Load(dir);
            source = BakedSource.Read(dir, rules);
            var w = new World(rules, source.W, source.H, 7);
            w.SetLandRange(map.LandRange);
            w.MakeFrom(source);
            w.SetLand(LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H));
            WorldPuzzles.Load(dir).LayInto(w);
            MapStamps.Load(dir).LayInto(w);
            WorldBuildings.Load(dir).LayInto(w);
            if (GolfCommand.Load(dir) is { } hole) w.ApplyGolf(hole);
            if (WorldHold.Load(dir) is { } hold && w.InBounds(hold.Heart.x, hold.Heart.y)) w.SetHold(hold);
            w.KeepStart();
            return w;
        }

        // What's live on the heap, collected first: a measuring test's only use of the collector (as ScaleIslandTests).
#pragma warning disable S1215
        static long Heap() => GC.GetTotalMemory(true);
#pragma warning restore S1215

        static double Mb(long bytes) => bytes / 1024.0 / 1024.0;

        // A block's bytes: 4,096 Cell objects of 40 bytes (64-bit .NET, no Rare) and its arrays (cells, fire generations,
        // marks, the still-water snapshot), headers left out.
        const int BlockBytes = Chunks.Area * (40 + 8 + 4 + 1 + 4 + 4 + 1 + 1 + 8);

        static Rules With(string streaming) => R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": " + streaming + " } }") });

        // The drawn tier round a cell: the chunks within radius (FarRing.SetView's drawn 4).
        static IEnumerable<(int cx, int cy)> Drawn(World w, int x, int y, int radius)
        {
            int cx = Chunks.ChunkOf(x), cy = Chunks.ChunkOf(y);
            for (int j = cy - radius; j <= cy + radius; j++)
                for (int i = cx - radius; i <= cx + radius; i++)
                    if (w.HasChunk(i, j)) yield return (i, j);
        }

        [Test]
        public void TheNinesWorldMemory()
        {
            string dir = Baked();
            Assert.That(R.Fingerprint, Is.Not.Empty, "the rules loaded before the heap is read, so they aren't counted");
            long before = Heap();
            var built = Stopwatch.StartNew();
            var w = Build(dir, R, out var source);
            double buildMs = built.Elapsed.TotalMilliseconds;
            long asBuilt = Heap() - before;
            int chunks = w.ChunkCoords.Count;
            TestContext.Out.WriteLine($"SPARSE the nine, {w.W} x {w.H}, {chunks} chunks: built in {buildMs:0} ms; the source holds {Mb(source.Bytes):0.0} MB "
                + $"({source.UniformChunks} chunks all one character); {w.UnpackedCount} chunks unpacked by the build (its lays), the world {Mb(asBuilt):0.0} MB");

            // As play starts: the reach round the spawn active, the rest let go, the drawn tier hinted; then the hold runs out.
            var spawn = MapFile.Load(dir).Spawn;
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { spawn }));
            w.SetDrawn(Drawn(w, spawn.x, spawn.y, 4));
            int settled = R.Sim.Streaming.PackTicks + 60;
            var drawn = Drawn(w, spawn.x, spawn.y, 4).ToList();
            for (int t = 0; t < settled; t++)
            {
                w.Step();
                // The view reads what it draws (ChunkCells, At), which unpacks the drawn tier.
                foreach (var (cx, cy) in drawn) _ = w.ChunkCells(cx, cy).Length;
            }
            w.TrimPackPool(0);
            long playing = Heap() - before;
            long changes = w.ChunkCoords.Where(c => w.IsPacked(c.cx, c.cy)).Sum(c => w.PackedOf(c.cx, c.cy).Bytes);
            long blocks = w.UnpackedCount * (long)BlockBytes;
            TestContext.Out.WriteLine($"SPARSE in play after {settled} ticks: {w.ActiveChunks.Count} active, {w.DrawnCount} drawn, {w.UnpackedCount} unpacked, "
                + $"{chunks - w.UnpackedCount} packed holding {Mb(changes):0.00} MB of changes: the world {Mb(playing):0.0} MB, of which the blocks ~{Mb(blocks):0.0} MB, "
                + $"the source {Mb(source.Bytes):0.0} MB and the map-sized arrays step 3 moves {Mb(w.MapArrayBytes):0.0} MB");

            // Every chunk unpacked: what the World held before step 2 (with World.Cells, 8 bytes a cell more).
            foreach (var (cx, cy) in w.ChunkCoords) w.Unpack(cx, cy);
            long all = Heap() - before;
            double cellsArray = 8.0 * w.W * w.H / 1024 / 1024;
            TestContext.Out.WriteLine($"SPARSE every chunk unpacked: the world {Mb(all):0.0} MB ({all / chunks / 1024} KB a chunk); "
                + $"before step 2 add World.Cells' {cellsArray:0.0} MB: {Mb(all) + cellsArray:0.0} MB");
            GC.KeepAlive(w);
            Assert.Pass();
        }

        [Test]
        public void AWalkAcrossTheNine()
        {
            string dir = Baked();
            var on = Build(dir, R, out _);
            var off = Build(dir, With("{ \"packsPerTick\": 0 }"), out _);
            var spawn = MapFile.Load(dir).Spawn;
            // East along the spawn's row at walking pace, 4.5 m/s: 3 cells a second, half a cell a tick; then back west.
            const int ticks = 3600;
            int x0 = spawn.x, y = spawn.y, reach = 10 * Chunks.Size;
            (int x, int y) At(int t)
            {
                int along = t / 2;
                int x = along <= reach ? x0 + along : x0 + 2 * reach - along;
                return (Math.Min(Math.Max(x, 0), on.W - 1), y);
            }
            var plannerOn = new ChunkPlanner();
            var plannerOff = new ChunkPlanner();
            ChunkStreaming.Apply(on, ChunkStreaming.StartPlan(on, new[] { spawn }));
            ChunkStreaming.Apply(off, ChunkStreaming.StartPlan(off, new[] { spawn }));
            // Off is the World before step 2: every chunk's Cell objects made, none packed.
            foreach (var (cx, cy) in off.ChunkCoords) off.Unpack(cx, cy);
            double packMs = on.PackMs, unpackMs = on.UnpackMs;
            int packs = on.Packs, unpacks = on.Unpacks;
            var times = new List<double>[] { new List<double>(), new List<double>() };
            // The ticks a chunk activated on: their switches (unpacking, making fit, catching up) and the whole frame.
            var switching = new List<double>[] { new List<double>(), new List<double>() };
            var crossing = new List<double>[] { new List<double>(), new List<double>() };
            int mostUnpacked = 0;
            for (int t = 0; t < ticks; t++)
            {
                var me = new[] { At(t) };
                if (t % 60 == 30) on.Cast("fire-bolt", me[0].x + 6, me[0].y, 1);
                if (t % 60 == 30) off.Cast("fire-bolt", me[0].x + 6, me[0].y, 1);
                for (int k = 0; k < 2; k++)
                {
                    var w = k == 0 ? on : off;
                    var planner = k == 0 ? plannerOn : plannerOff;
                    long at = Stopwatch.GetTimestamp();
                    // What a frame does: the plan's switches (unpacking what activates), the hint, the tick (packing what's due).
                    var plan = planner.Plan(w, me);
                    ChunkStreaming.Apply(w, plan);
                    long switched = Stopwatch.GetTimestamp();
                    w.SetDrawn(Drawn(w, me[0].x, me[0].y, 4));
                    w.Step();
                    double ms = (Stopwatch.GetTimestamp() - at) * 1000.0 / Stopwatch.Frequency;
                    times[k].Add(ms);
                    if (plan.Any(s => s.Active))
                    {
                        switching[k].Add((switched - at) * 1000.0 / Stopwatch.Frequency);
                        crossing[k].Add(ms);
                    }
                }
                mostUnpacked = Math.Max(mostUnpacked, on.UnpackedCount);
                if (t % 300 == 299) Assert.That(on.CellHash(), Is.EqualTo(off.CellHash()), $"tick {on.Tick}: packing changes nothing");
            }
            static string Line(List<double> ms)
            {
                var s = ms.Skip(60).OrderBy(m => m).ToList();
                return $"mean {s.Average():0.00} ms, p99 {s[(int)(s.Count * 0.99)]:0.00} ms, worst {s[s.Count - 1]:0.00} ms";
            }
            TestContext.Out.WriteLine($"SPARSE a walk of {ticks} ticks, 10 chunks east and back: packing on {Line(times[0])}; off {Line(times[1])}");
            static string Median(List<double> ms) => $"{ms.OrderBy(m => m).ElementAt(ms.Count / 2):0.00}";
            packs = on.Packs - packs;
            unpacks = on.Unpacks - unpacks;
            packMs = on.PackMs - packMs;
            unpackMs = on.UnpackMs - unpackMs;
            TestContext.Out.WriteLine($"SPARSE packing on: {packs} packs ({packMs / Math.Max(1, packs):0.000} ms each, {packMs:0} ms in all), {unpacks} unpacks "
                + $"({unpackMs / Math.Max(1, unpacks):0.000} ms each, {unpackMs:0} ms in all), {on.ReadUnpacks} read unpacks; at most {mostUnpacked} chunks unpacked (off: all {off.UnpackedCount})");
            TestContext.Out.WriteLine($"SPARSE the {crossing[0].Count} ticks chunks activated on: the switches median {Median(switching[0])} ms on, {Median(switching[1])} ms off "
                + $"(worst {switching[0].Max():0.00} / {switching[1].Max():0.00}, a lingering chunk catching up); the frame median {Median(crossing[0])} / {Median(crossing[1])} ms");
            Assert.That(on.CellHash(), Is.EqualTo(off.CellHash()));
            Assert.That(on.ReadUnpacks, Is.Zero, "the tick never read a packed chunk");
        }
    }
}
