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

        // The Scale Island's bake (9,720 chunks, a row of chunks at a time: bake-lean), made once into a folder of the bench's
        // own, the nine baked first for its rows.
        static string islandBaked;

        internal static string IslandBaked()
        {
            if (islandBaked != null) return islandBaked;
            string root = Path.Combine(Path.GetTempPath(), "sparse-world-island");
            string dir = Path.Combine(root, "baked");
            if (!File.Exists(Path.Combine(dir, MapFile.CellsName)))
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.CreateDirectory(root);
                ScaleIslandTests.CopyInputs(root);
                string map = Path.Combine(root, "maps", "scale-12x10");
                Directory.CreateDirectory(map);
                File.Copy(Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "scale-12x10", MapFile.FileName), Path.Combine(map, MapFile.FileName));
                var watch = Stopwatch.StartNew();
                var island = GeneratedMap.Load(map);
                island.Inner.Bake(Path.Combine(root, "maps", "nine"));
                island.Bake(dir);
                TestContext.Out.WriteLine($"SPARSE baked the Scale Island in {watch.Elapsed.TotalSeconds:0} s");
            }
            return islandBaked = dir;
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

        // late-join-snapshot (docs/design/player-saves.md, step 6): what a snapshot costs on the valley and the nine, taken
        // after 60 ticks of play (casts at the spawn's middle, the reach active), and joined over the same map and land.
        [TestCase("valley")]
        [TestCase("nine")]
        public void ASnapshotsBytesAndTime(string map)
        {
            string dir = map == "nine" ? Baked() : Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
            var w = Build(dir, R, out var source);
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), w.W, w.H);
            int mx = w.W / 2, my = w.H / 2;
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { (mx, my) }));
            w.Cast("fireball", mx, my, 1);
            w.Cast("create-water", mx + 20, my, 1);
            for (int t = 0; t < 60; t++) w.Step();
            var rules = RuleSet.Plain(R);
            JoinSnapshot.Take(w, source, rules); // warm
            var take = Stopwatch.StartNew();
            string text = JoinSnapshot.Take(w, source, rules).Text();
            double takeMs = take.Elapsed.TotalMilliseconds;
            JoinSnapshot.Parse(text).Join(R, source, land); // warm
            var join = Stopwatch.StartNew();
            var (joined, _) = JoinSnapshot.Parse(text).Join(R, source, land);
            double joinMs = join.Elapsed.TotalMilliseconds;
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);
            long gz;
            using (var mem = new MemoryStream())
            {
                using (var zip = new System.IO.Compression.GZipStream(mem, System.IO.Compression.CompressionLevel.Optimal, true)) zip.Write(utf8, 0, utf8.Length);
                gz = mem.Length;
            }
            TestContext.Out.WriteLine($"SNAPSHOT {map} ({w.ChunkCoords.Count} chunks, {w.ActiveChunks.Count} active) at tick {w.Tick}: {utf8.Length / 1024.0:0.0} KB, "
                + $"{gz / 1024.0:0.0} KB gzipped; taken in {takeMs:0.0} ms, joined (parsed, loaded, checked) in {joinMs:0.0} ms");
            Assert.That(joined.CellHash(), Is.EqualTo(w.CellHash()));
        }

        // island-startup: two parts of the Scale Island's start the PC's player timed (STARTUP lines): the map check (4.0 s)
        // and setting its land (3.7 s), here under .NET on the baked island.
        [Test]
        public void TheIslandsStartUp()
        {
            // The bake's folder by the map's own name, as a player build has it (MapCheck checks the two agree).
            string bake = IslandBaked(), dir = Path.Combine(Path.GetDirectoryName(bake), "scale-12x10");
#if UNITY_5_3_OR_NEWER
            if (!Directory.Exists(dir)) Assert.Ignore("Unity's .NET has no Directory.CreateSymbolicLink; dotnet test core runs this");
#else
            if (!Directory.Exists(dir)) Directory.CreateSymbolicLink(dir, bake);
#endif
            var check = Stopwatch.StartNew();
            var errors = MapCheck.Errors(dir);
            double checkMs = check.Elapsed.TotalMilliseconds;
            var w = Build(dir, R, out _);
            var map = MapFile.Load(dir);
            var read = Stopwatch.StartNew();
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H);
            double readMs = read.Elapsed.TotalMilliseconds;
            var set = Stopwatch.StartNew();
            w.SetLand(land);
            double setMs = set.Elapsed.TotalMilliseconds;
            var built = Build(dir, R, out _);
            var byChunk = Stopwatch.StartNew();
            built.SetLandByChunk(land);
            double byChunkMs = byChunk.Elapsed.TotalMilliseconds;
            string record = Path.Combine(Path.GetTempPath(), "island-startup-mapcheck-" + Guid.NewGuid().ToString("N"));
            var first = Stopwatch.StartNew();
            MapCheck.Errors(dir, record);
            double firstMs = first.Elapsed.TotalMilliseconds;
            var again = Stopwatch.StartNew();
            var none = MapCheck.Errors(dir, record);
            double againMs = again.Elapsed.TotalMilliseconds;
            File.Delete(record);
            TestContext.Out.WriteLine($"ISLAND {w.W} x {w.H}: MapCheck.Errors {checkMs:0} ms ({errors.Count} errors); with a record, first {firstMs:0} ms, again {againMs:0} ms ({none.Count}); "
                + $"land.json read {readMs:0} ms; SetLand {setMs:0} ms; SetLandByChunk {byChunkMs:0} ms ({built.LandUnmadeChunks} of {built.ChunkCoords.Count} chunks left for later)");
            Assert.That(errors, Is.Empty);
        }

        // core-mem-scale: what the core holds, part by part, that grows with the map, on the nine and the Scale Island, as the
        // game starts one: the source, the World (its chunks packed), the land rows, the land set a chunk at a time, the sea
        // mask, the start kept for the dragon; then in play round the spawn.
        [TestCase("nine")]
        [TestCase("island")]
        public void WhatTheCoreHoldsByMapSize(string which)
        {
            string dir = which == "nine" ? Baked() : IslandBaked();
            Assert.That(R.Fingerprint, Is.Not.Empty);
            var map = MapFile.Load(dir);
            long at = Heap();
            long Step()
            {
                long now = Heap();
                long d = now - at;
                at = now;
                return d;
            }
            var source = BakedSource.Read(dir, R);
            long sourceBytes = Step();
            var w = new World(R, source.W, source.H, 7);
            w.SetLandRange(map.LandRange);
            w.MakeFrom(source);
            long worldBytes = Step();
            WorldPuzzles.Load(dir).LayInto(w);
            MapStamps.Load(dir).LayInto(w);
            WorldBuildings.Load(dir).LayInto(w);
            if (GolfCommand.Load(dir) is { } hole) w.ApplyGolf(hole);
            if (WorldHold.Load(dir) is { } hold && w.InBounds(hold.Heart.x, hold.Heart.y)) w.SetHold(hold);
            long laidBytes = Step();
            w.KeepStart();
            long startBytes = Step();
            string landJson = File.ReadAllText(Path.Combine(dir, MapFile.LandName));
            long jsonBytes = Step();
            var readWhole = Stopwatch.StartNew();
            var land = LandFile.Read(landJson, map.W, map.H);
            double wholeMs = readWhole.Elapsed.TotalMilliseconds;
            long rowsBytes = Step();
            var readChunks = Stopwatch.StartNew();
            var landChunks = LandChunks.Read(landJson, map.W, map.H);
            double chunksMs = readChunks.Elapsed.TotalMilliseconds;
            long chunksBytes = Step();
            w.SetLandByChunk(land);
            long setBytes = Step();
            var sea = SeaMask.Of(land, map.W, map.H, map.SeaLevel);
            long seaBytes = Step();
            var spawn = map.Spawn;
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { spawn }));
            var drawn = Drawn(w, spawn.x, spawn.y, 4).ToList();
            w.SetDrawn(drawn);
            for (int t = 0; t < 120; t++)
            {
                w.Step();
                foreach (var (cx, cy) in drawn) _ = w.ChunkCells(cx, cy).Length;
            }
            long playBytes = Step();
            TestContext.Out.WriteLine($"HOLDS {which} land: land.json text {Mb(jsonBytes):0.0} MB; as an int a cell {Mb(rowsBytes):0.0} MB, read in {wholeMs:0} ms; "
                + $"a chunk at a time {Mb(chunksBytes):0.0} MB (held {Mb(landChunks.Bytes):0.0} MB), read in {chunksMs:0} ms");
            TestContext.Out.WriteLine($"HOLDS {which} {map.W} x {map.H} ({w.ChunkCoords.Count} chunks, {(long)map.W * map.H / 1e6:0.0} M cells): "
                + $"source {Mb(sourceBytes):0.0} MB, World made {Mb(worldBytes):0.0} MB, lays {Mb(laidBytes):0.0} MB, start {Mb(startBytes):0.0} MB, "
                + $"land rows {Mb(rowsBytes):0.0} MB, land set by chunk {Mb(setBytes):0.0} MB, sea mask {Mb(seaBytes):0.0} MB (held {Mb(sea.Bytes):0.00} MB), 120 ticks of play {Mb(playBytes):0.0} MB "
                + $"({w.UnpackedCount} unpacked); the World's map-sized arrays {Mb(w.MapArrayBytes):0.0} MB");
            GC.KeepAlive(source);
            GC.KeepAlive(w);
            GC.KeepAlive(land);
            GC.KeepAlive(landChunks);
            GC.KeepAlive(sea);
        }

        // planner-reach: ChunkPlanner.Plan visiting every chunk against only those it can touch, on the nine and the Scale
        // Island, a character flying east from the spawn at ~10 cells a tick: the same plan every tick, its time and garbage.
        [TestCase("nine")]
        [TestCase("island")]
        public void APlansCostByMapSize(string which)
        {
            string dir = which == "nine" ? Baked() : IslandBaked();
            var w = Build(dir, R, out _);
            var spawn = MapFile.Load(dir).Spawn;
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { spawn }));
            var every = new ChunkPlanner();
            var near = new ChunkPlanner();
            double everyMs = 0, nearMs = 0;
            long everyBytes = 0, nearBytes = 0;
            const int ticks = 300;
            for (int t = 0; t < ticks; t++)
            {
                var me = new List<(int x, int y)> { (Math.Min(w.W - 1, spawn.x + t * 10), spawn.y) };
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                var a = every.Plan(w, w.IsActive, me, null);
                everyMs += watch.Elapsed.TotalMilliseconds;
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                watch.Restart();
                var b = near.Plan(w, me);
                nearMs += watch.Elapsed.TotalMilliseconds;
                long b2 = GC.GetAllocatedBytesForCurrentThread();
                everyBytes += b1 - b0;
                nearBytes += b2 - b1;
                Assert.That(string.Join(" ", b.Select(x => $"{x.CX},{x.CY},{x.Active}")), Is.EqualTo(string.Join(" ", a.Select(x => $"{x.CX},{x.CY},{x.Active}"))), $"tick {t}");
                ChunkStreaming.Apply(w, b);
                w.Step();
            }
            TestContext.Out.WriteLine($"PLAN {which} ({w.ChunkCoords.Count} chunks), {ticks} ticks: every chunk {everyMs / ticks:0.00} ms and {everyBytes / ticks / 1024.0:0.0} KB a plan; "
                + $"what it can touch {nearMs / ticks:0.000} ms and {nearBytes / ticks / 1024.0:0.0} KB; the same plan every tick");
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
        public void TheIslandsWorldMemory()
        {
            string dir = IslandBaked();
            Assert.That(R.Fingerprint, Is.Not.Empty, "the rules loaded before the heap is read, so they aren't counted");
            long before = Heap();
            var built = Stopwatch.StartNew();
            var w = Build(dir, R, out var source);
            double buildMs = built.Elapsed.TotalMilliseconds;
            long asBuilt = Heap() - before;
            int chunks = w.ChunkCoords.Count;
            TestContext.Out.WriteLine($"SPARSE the Scale Island, {w.W} x {w.H}, {chunks} chunks: built in {buildMs:0} ms; the source holds {Mb(source.Bytes):0.0} MB "
                + $"({source.UniformChunks} chunks all one character); {w.UnpackedCount} chunks unpacked by the build, the world {Mb(asBuilt):0.0} MB, "
                + $"the map-sized arrays {Mb(w.MapArrayBytes):0.0} MB");

            var spawn = MapFile.Load(dir).Spawn;
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { spawn }));
            w.SetDrawn(Drawn(w, spawn.x, spawn.y, 4));
            int settled = R.Sim.Streaming.PackTicks + 60;
            var drawn = Drawn(w, spawn.x, spawn.y, 4).ToList();
            for (int t = 0; t < settled; t++)
            {
                w.Step();
                foreach (var (cx, cy) in drawn) _ = w.ChunkCells(cx, cy).Length;
            }
            w.TrimPackPool(0);
            long playing = Heap() - before;
            long changes = w.ChunkCoords.Where(c => w.IsPacked(c.cx, c.cy)).Sum(c => w.PackedOf(c.cx, c.cy).Bytes);
            TestContext.Out.WriteLine($"SPARSE the island in play after {settled} ticks: {w.ActiveChunks.Count} active, {w.UnpackedCount} unpacked, "
                + $"{chunks - w.UnpackedCount} packed holding {Mb(changes):0.00} MB of changes: the world {Mb(playing):0.0} MB, of which the blocks ~{Mb(w.UnpackedCount * (long)BlockBytes):0.0} MB, "
                + $"the source {Mb(source.Bytes):0.0} MB and the map-sized arrays {Mb(w.MapArrayBytes):0.0} MB ({Mb(w.PackedLandBytes):0.0} MB the packed chunks' land, "
                + $"{w.StartShapes} cells of the start held apart from the source)");
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
