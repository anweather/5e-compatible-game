// A generated map played unbaked (GeneratedSource, D17's generate switch; docs/design/sparse-world.md, "The generate
// switch"). On a small generated map (BakeTests' two squares, 162 chunks) every chunk made on demand is the bake's, cell
// for cell, land and things, and the World built on it is the World built on the bake, chunk for chunk; two peers that make
// and pack their chunks at different times agree tick by tick, and so do a late joiner replaying the history and one
// loading a save. The 12 x 10, too big to bake, builds its World and walks a character across the seam, the chunks ahead
// made on the worker; the lines it prints are the generation's cost (sparse-world.md, "measured").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class GeneratedSourceTests
    {
        static Rules R => ChemistryTests.R;

        string root, map, baked, rows;
        GeneratedMap made;

        [OneTimeSetUp]
        public void MakeTheMap()
        {
            root = Path.Combine(Path.GetTempPath(), "generate-switch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            map = BakeTests.TwoSquares(root);
            made = GeneratedMap.Load(map);
            baked = Path.Combine(root, "baked");
            made.Bake(baked);
            rows = Path.Combine(root, "rows", "two-squares"); // a map's folder is named for it (MapCheck)
            Assert.That(GeneratedMap.EnsureRows(map, rows), Is.True);
        }

        [OneTimeTearDown]
        public void DropTheMap()
        {
            made = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        GeneratedSource Source(Rules rules = null) => new GeneratedSource(GeneratedMap.Load(map), rules ?? R);

        // A world as ChemGrid.StartWorld builds one on the generated map, its reach round the spawn active.
        World Peer(GeneratedSource source, Rules rules = null)
        {
            var w = GeneratedSource.MapWorld(source, rows, rules ?? R, 7, out var start);
            w.KeepStart();
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { start }));
            return w;
        }

        [Test]
        public void ItsRowsAreTheBakesButTheCells()
        {
            var names = Directory.GetFiles(rows).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.That(names, Is.EquivalentTo(GeneratedMap.RowsBaked.Append(MapFile.FileName)), "its map.json too, away from the map's folder");
            foreach (string cells in new[] { MapFile.CellsName, MapFile.LandName, "things.json" }) Assert.That(names, Has.No.Member(cells));
            foreach (string name in names.Where(n => n != GeneratedMap.BakeName && n != WorldPlaces.FileName && n != MapFile.FileName))
                Assert.That(File.ReadAllText(Path.Combine(rows, name)), Is.EqualTo(File.ReadAllText(Path.Combine(baked, name))), name);
            // Its places are the bake's but the water bodies, which only the whole map's land finds.
            static List<string> Places(string dir, bool water) => WorldPlaces.Load(dir).All.Where(p => water || p.From != MapFile.LandName).Select(p => p.Id).ToList();
            Assert.That(Places(rows, true), Is.EqualTo(Places(baked, false)));
            Assert.That(GeneratedMap.IsRowsOnly(rows), Is.True);
            Assert.That(GeneratedMap.IsRowsOnly(baked), Is.False);
            Assert.That(MapCheck.Errors(rows), Is.Empty);
            Assert.That(GeneratedMap.EnsureRows(map, rows), Is.False, "written once for its inputs");
        }

        [Test]
        public void AChunkMadeOnDemandIsTheBakes()
        {
            var source = Source();
            var bake = BakedSource.Read(baked, R);
            Assert.That((source.W, source.H), Is.EqualTo((bake.W, bake.H)));
            Assert.That(source.Start, Is.EqualTo(bake.Start), "the spawn, the bake's '@'");
            int w = bake.W;
            var land = LandFile.Read(File.ReadAllText(Path.Combine(baked, MapFile.LandName)), bake.W, bake.H);
            var things = ((JArray)JObject.Parse(File.ReadAllText(Path.Combine(baked, "things.json")))["chunks"]).OfType<JObject>()
                .ToDictionary(c => ((int)c["chunk"][0], (int)c["chunk"][1]), c => (JArray)c["things"]);
            Cell a = new Cell(), b = new Cell();
            // Every chunk, in an order no bake makes them (from the south-east): a chunk is the same whatever came before.
            for (int cy = made.Gen.ChunksH - 1; cy >= 0; cy--)
                for (int cx = made.Gen.ChunksW - 1; cx >= 0; cx--)
                {
                    for (int i = 0; i < Chunks.Area; i++)
                    {
                        int x = cx * Chunks.Size + (i & Chunks.Mask), y = cy * Chunks.Size + (i >> Chunks.Shift);
                        a.Reset();
                        b.Reset();
                        source.Make(x, y, a);
                        bake.Make(x, y, b);
                        if (a.T != b.T || a.Water != b.Water || a.Frozen != b.Frozen || a.Cover != b.Cover)
                            Assert.Fail($"cell ({x}, {y}): made {a.T},{a.Water},{a.Frozen},{a.Cover}; baked {b.T},{b.Water},{b.Frozen},{b.Cover}");
                        if (source.LandAt(x, y) != land[y * w + x]) Assert.Fail($"cell ({x}, {y}): land made {source.LandAt(x, y)}, baked {land[y * w + x]}");
                    }
                    var mine = source.ThingsOf(cx, cy);
                    Assert.That(mine.ToString(), Is.EqualTo(things.TryGetValue((cx, cy), out var t) ? t.ToString() : "[]"), $"chunk ({cx}, {cy})'s things");
                }
            Assert.That(source.Made, Is.EqualTo(made.Gen.ChunksW * made.Gen.ChunksH), "each chunk made once");
        }

        // core-review-0: GeneratedSource makes chunks on its worker and on many threads at once (Ahead, Parallel.For), so
        // ChunkGen.Make must be a pure function of the chunk: no state it writes as it makes one. Every chunk made at once on
        // many threads, in no order, is the chunk made alone on one, its hash, things and biome alike.
        [Test]
        public void ChunksMadeAtOnceOnManyThreadsAreTheSameAsOneAtATime()
        {
            var gen = GeneratedMap.Load(map).Gen;
            int w = gen.ChunksW, n = gen.ChunksW * gen.ChunksH;
            var alone = Enumerable.Range(0, n).Select(k => Text(gen.Make(k % w, k / w))).ToArray();
            var fresh = GeneratedMap.Load(map).Gen;
            var together = new string[n];
            System.Threading.Tasks.Parallel.For(0, n, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 }, k =>
            {
                int at = (n - 1 - k) * 37 % n; // not in order
                together[at] = Text(fresh.Make(at % w, at / w));
            });
            Assert.That(together, Is.EqualTo(alone));

            static string Text(GeneratedChunk c) => $"{c.Hash} {c.Biome} {c.Pinned} {c.Things.ToString(Newtonsoft.Json.Formatting.None)}";
        }

        [Test]
        public void ItsWorldIsTheBakesWorld()
        {
            var bakedWorld = SparseWorldBench.Build(baked, R, out _);
            var source = Source();
            var w = GeneratedSource.MapWorld(source, rows, R, 7, out _);
            w.KeepStart();
            Assert.That(w.HashesChanges, Is.True);
            Assert.That(bakedWorld.HashesChanges, Is.False);
            int touched = w.ChunkCoords.Count(c => !w.IsPacked(c.cx, c.cy) || w.PackedOf(c.cx, c.cy).Count > 0);
            TestContext.Out.WriteLine($"GENERATE two squares: the build made {source.Made} of {w.ChunkCoords.Count} chunks, {touched} touched by its lays");
            Assert.That(source.Made, Is.LessThan(w.ChunkCoords.Count), "the build makes only the chunks it lays on");
            foreach (var (cx, cy) in w.ChunkCoords)
            {
                Assert.That(w.ChunkHash(cx, cy), Is.EqualTo(bakedWorld.ChunkHash(cx, cy)), $"chunk ({cx}, {cy})");
                for (int i = 0; i < Chunks.Area; i++)
                {
                    int x = cx * Chunks.Size + (i & Chunks.Mask), y = cy * Chunks.Size + (i >> Chunks.Shift);
                    if (w.LandUnitsAt(x, y) != bakedWorld.LandUnitsAt(x, y)) Assert.Fail($"cell ({x}, {y}): land {w.LandUnitsAt(x, y)}, the bake's world {bakedWorld.LandUnitsAt(x, y)}");
                }
            }
            Assert.That(w.FitText() + w.DragonText(), Is.EqualTo(bakedWorld.FitText() + bakedWorld.DragonText()));
        }

        [Test]
        public void TheChangesHashSeesEveryChangeAndNotHowAChunkIsHeld()
        {
            var a = Peer(Source());
            var b = Peer(Source());
            Assert.That(a.CellHash(), Is.EqualTo(b.CellHash()));
            long makes = ((GeneratedSource)a.Source).Made;
            // A cell far from the reach, in a chunk nothing has made: the hash sees it, and makes only that chunk.
            int x = a.W - 20, y = a.H - 20;
            var far = (Chunks.ChunkOf(x), Chunks.ChunkOf(y));
            Assert.That(a.IsPacked(far.Item1, far.Item2) && a.PackedOf(far.Item1, far.Item2).Count == 0, Is.True, "untouched");
            var was = new Cell();
            ((GeneratedSource)b.Source).Make(x, y, was);
            b.Paint(x, y, "stone");
            Assert.That(b.CellHash(), Is.Not.EqualTo(a.CellHash()));
            Assert.That(b.CellText(), Does.Contain("@" + (y * b.W + x) + ":stone,"));
            // Packed, it hashes the same; and back as it was made, the same as a world never changed.
            string changed = b.CellHash();
            b.Pack(far.Item1, far.Item2);
            Assert.That(b.CellHash(), Is.EqualTo(changed));
            var c = b.At(x, y);
            c.T = was.T;
            c.Water = was.Water;
            c.Frozen = was.Frozen;
            c.Cover = was.Cover;
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
            Assert.That(((GeneratedSource)a.Source).Made, Is.EqualTo(makes), "the hash made no chunk");
            // The running hash closes on the same fingerprint as the whole one.
            var rolling = new RollingHash();
            string closed = null;
            for (int t = 1; t <= RollingHash.WindowFor(a); t++) closed = rolling.Add(a, t) ?? closed;
            Assert.That(closed, Is.EqualTo(a.CellHash()));
        }

        // A walk east from the spawn and back, a fire bolt now and then: the host's plan and the casts, tick by tick.
        sealed class Walk
        {
            readonly (int x, int y) from;
            readonly int reach;

            public Walk((int x, int y) from, int reach)
            {
                this.from = from;
                this.reach = reach;
            }

            public (int x, int y) At(int t)
            {
                int along = t / 2;
                return (along <= reach ? from.x + along : from.x + 2 * reach - along, from.y);
            }
        }

        static Rules With(string streaming) => R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": " + streaming + " } }") });

        [Test]
        public void TwoPeersAndLateJoinersAgree()
        {
            // The host makes ahead and packs; the other packs nothing and has no view; they share the plan and the casts.
            var host = Peer(Source());
            var other = Peer(Source(), With("{ \"packsPerTick\": 0 }"));
            var walk = new Walk(((GeneratedSource)host.Source).Start, 4 * Chunks.Size);
            var planner = new ChunkPlanner();
            var history = new List<(List<ChunkSwitch> plan, (int x, int y)? cast)>();
            var peers = new List<(World world, RollingHash rolling, string name)> { (host, new RollingHash(), "the host"), (other, new RollingHash(), "the other peer") };
            const int ticks = 1200, joinAt = 700;
            string closedHash = null;
            int closedHashes = 0, loadUnpacks = 0;
            void Step(World w, List<ChunkSwitch> plan, (int x, int y)? cast)
            {
                ChunkStreaming.Apply(w, plan);
                if (cast is { } c) w.Cast("fire-bolt", c.x, c.y, 1);
                w.Step();
            }
            for (int t = 0; t < ticks; t++)
            {
                var me = new[] { walk.At(t) };
                var plan = planner.Plan(host, me);
                (int x, int y)? cast = t % 90 == 45 ? (me[0].x + 6, me[0].y) : ((int, int)?)null;
                history.Add((plan, cast));
                host.SetDrawn(SparseWorldDrawn(host, me[0], 3));
                if (t == joinAt)
                {
                    // A late joiner replaying the history from the start (NetChem), and one loading the host's save.
                    var replayed = Peer(Source());
                    foreach (var (p, c) in history.Take(t)) Step(replayed, p, c);
                    var loaded = World.Load(host.Save(host.Source), R, Source());
                    loaded.LandFromSource();
                    Assert.That(replayed.CellHash(), Is.EqualTo(host.CellHash()), "the joiner replaying the history");
                    Assert.That(loaded.CellHash(), Is.EqualTo(host.CellHash()), "the joiner loading the save");
                    peers.Add((replayed, null, "the joiner replaying"));
                    peers.Add((loaded, null, "the joiner loading"));
                    // A load reads the chunks its save changed as it lays them in (World.Load): not the tick's reads.
                    loadUnpacks = loaded.ReadUnpacks;
                }
                string first = null;
                foreach (var (w, rolling, name) in peers)
                {
                    Step(w, plan, cast);
                    string closed = rolling?.Add(w, w.Tick);
                    if (closed == null) continue;
                    if (first == null) first = closed;
                    else Assert.That(closed, Is.EqualTo(first), $"tick {w.Tick}: {name}'s running hash");
                    closedHash = closed;
                    closedHashes++;
                }
                if (t % 100 == 99 || t == joinAt + 1)
                {
                    string hash = host.CellHash();
                    foreach (var (w, _, name) in peers.Skip(1)) Assert.That(w.CellHash(), Is.EqualTo(hash), $"tick {host.Tick}: {name}");
                }
            }
            Assert.That(closedHash, Is.Not.Null);
            Assert.That(closedHashes, Is.GreaterThan(2));
            Assert.That(host.Packs, Is.GreaterThan(0), "the host packed chunks the walk left");
            Assert.That(other.UnpackedCount, Is.GreaterThan(host.UnpackedCount));
            foreach (var (w, _, name) in peers)
            {
                Assert.That(w.ReadUnpacks - (name == "the joiner loading" ? loadUnpacks : 0), Is.Zero, $"{name}: the tick never read a packed chunk");
                Assert.That(w.CellHash(), Is.EqualTo(host.CellHash()), name);
                // The cells themselves, round the walk, not only their fingerprint.
                foreach (var (cx, cy) in host.ActiveChunks) Assert.That(w.ChunkHash(cx, cy), Is.EqualTo(host.ChunkHash(cx, cy)), $"{name}: chunk ({cx}, {cy})");
            }
            var hostSource = (GeneratedSource)host.Source;
            TestContext.Out.WriteLine($"GENERATE peers: the host made {hostSource.Made} chunks ({hostSource.MadeAhead} ahead on the worker, {hostSource.MadeOnRead} on a read, "
                + $"{hostSource.Waits} waits), {hostSource.MakeMs / Math.Max(1, hostSource.Made):0.0} ms each");
        }

        static IEnumerable<(int cx, int cy)> SparseWorldDrawn(World w, (int x, int y) at, int radius)
        {
            int cx = Chunks.ChunkOf(at.x), cy = Chunks.ChunkOf(at.y);
            for (int j = cy - radius; j <= cy + radius; j++)
                for (int i = cx - radius; i <= cx + radius; i++)
                    if (w.HasChunk(i, j)) yield return (i, j);
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        [Test]
        public void TheTwelveByTenWalksAcrossTheSeam()
        {
            string twelve = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "world-12x10");
            string dir = Path.Combine(root, "world-12x10");
            var watch = Stopwatch.StartNew();
            var m = GeneratedMap.Load(twelve);
            double loadMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            m.WriteRowsOnly(dir);
            double rowsMs = watch.Elapsed.TotalMilliseconds;
            Assert.That(MapCheck.Errors(dir), Is.Empty);
            var source = new GeneratedSource(m, R);
            watch.Restart();
            var w = GeneratedSource.MapWorld(source, dir, R, 7, out var start);
            double buildMs = watch.Elapsed.TotalMilliseconds;
            long laid = source.Made;
            watch.Restart();
            w.KeepStart();
            double keepMs = watch.Elapsed.TotalMilliseconds;
            Assert.That((w.W, w.H, w.WrapX), Is.EqualTo((6912, 5760, true)));
            Assert.That(w.ChunkCoords.Count, Is.EqualTo(108 * 90));
            TestContext.Out.WriteLine($"GENERATE the 12 x 10: loaded in {loadMs:0} ms, rows written in {rowsMs:0} ms, the World built in {buildMs:0} ms "
                + $"making {laid} of its {w.ChunkCoords.Count} chunks (those its lays touch), its start kept in {keepMs:0} ms; {w.UnpackedCount} unpacked");
            Assert.That(laid, Is.LessThan(w.ChunkCoords.Count / 4), "the build makes only what it lays on");

            // The spawn's reach, then a walk from four chunks west of the seam (the far east) to four chunks east of it.
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { start }));
            int y = start.y, x0 = w.W - 4 * Chunks.Size + 10;
            var planner = new ChunkPlanner();
            var me = new (int x, int y)[] { (x0, y) };
            ChunkStreaming.Apply(w, planner.Plan(w, me));
            w.Step();
            long before = source.Made, onRead = source.MadeOnRead, waits = source.Waits, ahead = source.MadeAhead;
            var frames = new List<double>();
            var crossing = new List<double>();
            var plans = new List<double>();
            double arrival = 0;
            var seen = new HashSet<(int cx, int cy)>();
            const int cellsPerTick = 1, ticks = 8 * Chunks.Size / cellsPerTick;
            for (int t = 0; t < ticks; t++)
            {
                me[0] = (w.FoldX(x0 + t * cellsPerTick), y);
                long at = Stopwatch.GetTimestamp();
                var plan = planner.Plan(w, me);
                plans.Add(Ms(Stopwatch.GetTimestamp() - at));
                ChunkStreaming.Apply(w, plan);
                w.SetDrawn(SparseWorldDrawn(w, me[0], 2));
                w.Step();
                double ms = Ms(Stopwatch.GetTimestamp() - at);
                // The first is an arrival from the spawn: the reach at the seam made at once, on every core (MakeNow).
                if (t == 0)
                {
                    arrival = ms;
                    continue;
                }
                frames.Add(ms);
                if (plan.Any(s => s.Active)) crossing.Add(ms);
                foreach (var c in w.ActiveChunks) seen.Add(c);
            }
            int last = Chunks.ChunkOf(me[0].x);
            Assert.That(me[0].x, Is.LessThan(5 * Chunks.Size), "the walk crossed the seam");
            int row = Chunks.ChunkOf(y);
            Assert.That(w.IsActive(last, row), Is.True);
            foreach (int cx in new[] { 104, 107, 0, last + 2 }) Assert.That(seen.Contains((cx, row)), Is.True, $"chunk ({cx}, {row}) active on the way, either side of the seam");
            Assert.That(w.IsActive(104, row), Is.False, "the far east let go behind");
            Assert.That(seen.Select(c => c.cx).Distinct().Count(), Is.GreaterThanOrEqualTo(12));
            Assert.That(w.ReadUnpacks, Is.Zero, "the tick never read a packed chunk");
            frames.Sort();
            crossing.Sort();
            plans.Sort();
            long walked = source.Made - before;
            TestContext.Out.WriteLine($"GENERATE the walk of {ticks} ticks across the seam: {walked} chunks made, {source.MadeAhead - ahead} ahead on the worker, {source.MadeOnRead - onRead} on a read, "
                + $"{source.Waits - waits} waits for the worker; frames median {frames[frames.Count / 2]:0.00} ms, p99 {frames[(int)(frames.Count * 0.99)]:0.00}, "
                + $"worst {frames[frames.Count - 1]:0.00}; the {crossing.Count} that activated chunks median {crossing[crossing.Count / 2]:0.00} ms, worst {crossing[crossing.Count - 1]:0.00}; "
                + $"the planner's part median {plans[plans.Count / 2]:0.00} ms; the arrival at the seam {arrival:0} ms; "
                + $"a chunk made in {source.MakeMs / Math.Max(1, source.Made):0.0} ms; {w.UnpackedCount} unpacked, {source.HeldCount} held made");
            Assert.That(source.MadeOnRead - onRead, Is.LessThanOrEqualTo(walked / 4), "most of the walk's chunks were made ahead, not on a read");

            // Five chunks unpacking at a chunk edge, made ahead against made on the spot.
            int ey = Chunks.ChunkOf(y) + 20;
            var five = Enumerable.Range(0, 5).Select(k => (40 + k, ey)).ToList();
            long started = Stopwatch.GetTimestamp();
            foreach (var (cx, cy) in five) w.Unpack(cx, cy);
            double cold = Ms(Stopwatch.GetTimestamp() - started);
            var five2 = Enumerable.Range(0, 5).Select(k => (40 + k, ey + 4)).ToList();
            source.MakeNow(five2);
            started = Stopwatch.GetTimestamp();
            foreach (var (cx, cy) in five2) w.Unpack(cx, cy);
            double warm = Ms(Stopwatch.GetTimestamp() - started);
            TestContext.Out.WriteLine($"GENERATE five chunks unpacked at an edge: {cold:0.0} ms making them there, {warm:0.00} ms made ahead");
            Assert.That(warm, Is.LessThan(cold));
        }
    }
}
