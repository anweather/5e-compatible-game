// ScaleIslandTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in ScaleIslandTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class ScaleIslandWorldTests
    {

        [Test]
        public void PlanPicture()
        {
            string folder = Environment.GetEnvironmentVariable("SCALE_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) Assert.Ignore("SCALE_PNG_OUT names the folder for the picture");
            Directory.CreateDirectory(folder);
            var cv = WorldPlanPicture.Draw(ScaleIslandTests.Island.Plan, 24);
            string path = Path.Combine(folder, "plan.png");
            File.WriteAllBytes(path, LandmarkPicture.Png(cv.Rgb, cv.W, cv.H));
            TestContext.Out.WriteLine($"PICTURE {path} ({cv.W} x {cv.H})");
            Assert.That(File.Exists(path));
        }

        [Test]
        public void ASmallCopyBakesAndMeetsTheSpawnsChecks()
        {
            if (Type.GetType("Mono.Runtime") != null)
                Assert.Ignore("The 6 x 6 copy's bake is proved in the .NET suite (dotnet test core): under Mono it's past the 180 s timeout (#187).");
            string root = Path.Combine(Path.GetTempPath(), "scale-copy-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                string dir = CopyWorld(root);
                var watch = Stopwatch.StartNew();
                var m = GeneratedMap.Load(dir);
                double loadMs = watch.Elapsed.TotalMilliseconds;
                Assert.That(m.Plan.Problems, Is.Empty);
                Assert.That(m.Plan.ForcedMisses, Is.Empty);
                // The nine baked first (its rows are read from its own bake), timed apart from the copy's own.
                watch.Restart();
                m.Inner.Bake(Path.Combine(root, "maps", "nine"));
                double nineMs = watch.Elapsed.TotalMilliseconds;
                long heapBefore = Heap(), allocBefore = Allocated();
                watch.Restart();
                var report = m.Bake(dir);
                long allocated = Allocated() - allocBefore;
                double bakeMs = watch.Elapsed.TotalMilliseconds;
                long baked = Heap() - heapBefore;
                long peak = Process.GetCurrentProcess().PeakWorkingSet64;
                Assert.That(MapCheck.Errors(dir), Is.Empty);

                var map = MapFile.Load(dir);
                var rows = MapFile.ReadCells(dir).Split('\n');
                Assert.That(rows[map.Spawn.y][map.Spawn.x], Is.EqualTo('@'), "the valley's spawn, moved with the nine");
                Assert.That(rows.Sum(r => r.Count(ch => ch == '@')), Is.EqualTo(1));
                int w = report.W, chunks = m.Gen.ChunksW * m.Gen.ChunksH;
                // The land as it was written: the bake holds none of it once it's done (bake-lean).
                var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), report.W, report.H);
                // A spawn place a square below the peaks, each named once; the nine's own nine among them, the valley's
                // the map's start.
                var spawns = WorldPlaces.Load(dir).All.Where(p => p.Kind == "spawn").ToList();
                Assert.That(spawns.Select(p => p.Middle), Is.EquivalentTo(m.Gen.Spawns.Select(s => (s.X, s.Y))));
                Assert.That(spawns.Select(p => p.Name).Distinct().Count(), Is.EqualTo(spawns.Count));
                Assert.That(spawns.Select(p => p.Id).Distinct().Count(), Is.EqualTo(spawns.Count));
                var start = JObject.Parse(File.ReadAllText(Path.Combine(dir, WorldPlaces.FileName)))["places"].Single(p => (bool?)p["start"] == true);
                Assert.That(spawns.Single(p => p.Id == (string)start["id"]).Middle, Is.EqualTo(map.Spawn));
                // Every spawn of the forced squares (the nine's are the nine's own checks) on open, level ground above the
                // sea, nothing of its chunk's in its clearing.
                var things = ((JArray)JObject.Parse(File.ReadAllText(Path.Combine(dir, "things.json")))["chunks"]).OfType<JObject>()
                    .ToDictionary(c => ((int)c["chunk"][0], (int)c["chunk"][1]), c => (JArray)c["things"]);
                var outer = m.Gen.Spawns.Where(s => !s.Held).ToList();
                Assert.That(outer.Count, Is.EqualTo(CopyRows.Sum(r => r.Count(ch => "~SN".IndexOf(ch) < 0))), "a spawn a forced land square");
                foreach (var s in outer)
                {
                    string name = $"the spawn at ({s.X}, {s.Y}) near the {s.Near}";
                    int lo = int.MaxValue, hi = int.MinValue;
                    for (int dy = -ChunkGen.ClearCells + 1; dy < ChunkGen.ClearCells; dy++)
                        for (int dx = -ChunkGen.ClearCells + 1; dx < ChunkGen.ClearCells; dx++)
                        {
                            if (dx * dx + dy * dy >= ChunkGen.ClearCells * ChunkGen.ClearCells) continue;
                            int x = s.X + dx, y = s.Y + dy;
                            Assert.That(rows[y][x] == ',' || rows[y][x] == '.', $"{name}: ({x}, {y}) is '{rows[y][x]}', not open ground");
                            lo = Math.Min(lo, land[y * w + x]);
                            hi = Math.Max(hi, land[y * w + x]);
                        }
                    Assert.That(hi - lo, Is.LessThanOrEqualTo(2), $"{name}: its clearing is level");
                    Assert.That(lo, Is.GreaterThan(ChunkGen.SeaLevel + 8), $"{name}: above the sea");
                    int cx = s.X / ChunkGen.Size, cy = s.Y / ChunkGen.Size;
                    if (things.TryGetValue((cx, cy), out var list))
                        foreach (var t in list.Select(t => (JArray)t["cell"]))
                            Assert.That(Math.Max(Math.Abs(cx * ChunkGen.Size + (int)t[0] - s.X), Math.Abs(cy * ChunkGen.Size + (int)t[1] - s.Y)),
                                Is.GreaterThanOrEqualTo(ChunkGen.ClearCells), $"{name}: a thing in its clearing");
                }
                // The land meets the nine at its rim: round all four sides the first cell out is within a height step
                // (0.5 m) of the nine's rim cell beside it.
                int x0 = m.Gen.PinnedX, y0 = m.Gen.PinnedY, pw = m.Gen.PinnedW, ph = m.Gen.PinnedH, worst = 0;
                for (int t = 0; t < pw; t++)
                {
                    worst = Math.Max(worst, Math.Abs(land[(y0 - 1) * w + x0 + t] - land[y0 * w + x0 + t]));
                    worst = Math.Max(worst, Math.Abs(land[(y0 + ph) * w + x0 + t] - land[(y0 + ph - 1) * w + x0 + t]));
                }
                for (int t = 0; t < ph; t++)
                {
                    worst = Math.Max(worst, Math.Abs(land[(y0 + t) * w + x0 - 1] - land[(y0 + t) * w + x0]));
                    worst = Math.Max(worst, Math.Abs(land[(y0 + t) * w + x0 + pw] - land[(y0 + t) * w + x0 + pw - 1]));
                }
                TestContext.Out.WriteLine($"MEET the biggest step onto the nine's rim {worst} land units; the copy's land {land.Min()}..{land.Max()}, " +
                    $"{land.Count(v => v >= ChunkGen.SeaLevel) * 100 / land.Length}% at or above the sea");
                Assert.That(worst, Is.LessThanOrEqualTo(4), "the biggest step onto the nine's rim");

                // What it cost, and the full map's at the same rates (its 9720 chunks; the World is cells, a Cell an object).
                var (worldKb, worldChunks) = WorldKbAChunk(dir, rows, map, m.Gen);
                double files = Directory.GetFiles(dir).Sum(f => new FileInfo(f).Length) / 1048576.0;
                TestContext.Out.WriteLine($"COSTS the copy, {chunks} chunks: load {loadMs:0} ms (the nine's plan and rim too), the nine's bake {nineMs:0} ms, " +
                    $"its own bake {bakeMs:0} ms ({bakeMs / chunks:0.0} ms a chunk on {Environment.ProcessorCount} threads), its files {files:0} MB, " +
                    $"it allocated {allocated / 1048576} MB and holds its land after {baked / 1048576} MB, the process's peak {peak / 1048576} MB; " +
                    $"the World {worldKb:0} KB a chunk (over {worldChunks} chunks of it)");
                TestContext.Out.WriteLine($"COSTS the full map at those rates, 9720 chunks: its bake {bakeMs / chunks * 9720 / 1000:0} s on {Environment.ProcessorCount} threads, " +
                    $"its files {files / chunks * 9720:0} MB, its bake allocating {allocated / 1048576.0 / chunks * 9720 / 1024:0.0} GB, its World {worldKb * 9720 / 1048576:0.0} GB");
                Assert.That(worldKb, Is.GreaterThan(0));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        // The World a chunk, held as the game holds it (a Cell an object, its land with it), over the nine's 27 x 27 chunks
        // of the copy's bake: the whole copy's World is more than this VM holds.
        static (double kb, int chunks) WorldKbAChunk(string dir, string[] rows, MapFile map, ChunkGen gen)
        {
            int x0 = gen.PinnedX, y0 = gen.PinnedY, w = gen.PinnedW, h = gen.PinnedH;
            var part = new int[w * h];
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H);
            for (int y = 0; y < h; y++) Array.Copy(land, (y0 + y) * map.W + x0, part, y * w, w);
            long before = Heap();
            var world = new World(ChemistryTests.R, w, h, 7, "grass");
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char c = rows[y0 + y][x0 + x];
                    if (c == 'T') world.Paint(x, y, "tree");
                    else if (c == 'w' || c == 'r') world.At(x, y).Water = 4;
                }
            world.SetLand(part);
            long after = Heap();
            GC.KeepAlive(world);
            int chunks = w / ChunkGen.Size * (h / ChunkGen.Size);
            return ((after - before) / 1024.0 / chunks, chunks);
        }

        // A world folder of its own for the copy: the files the plan and the chunks read, the valley and the nine's world
        // file, and the copy's, so its bakes (the nine's too) never touch the game's.
        static string CopyWorld(string root)
        {
            ScaleIslandTests.CopyInputs(root);
            string dir = Path.Combine(root, "maps", "scale-copy");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, MapFile.FileName), CopyFile().ToString());
            return dir;
        }

        // What's live on the heap, collected first: a measuring test's only use of the collector.
#pragma warning disable S1215
        static long Heap() => GC.GetTotalMemory(true);
#pragma warning restore S1215

        // Every byte the process has allocated: .NET has GC.GetTotalAllocatedBytes, Unity's Mono profile doesn't (0 there).
        static long Allocated() =>
            typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) })?.Invoke(null, new object[] { false }) is long n ? n : 0;

        static JObject CopyFile()
        {
            var o = JObject.Parse(File.ReadAllText(Path.Combine(ScaleIslandTests.Dir, MapFile.FileName)));
            o["id"] = "scale-copy";
            o["name"] = "The Scale ScaleIslandTests.Island, 6 x 6";
            o["size"] = new JArray(6 * ChunkGen.Square, 6 * ChunkGen.Square);
            var nine = MapFile.Load(ScaleIslandTests.NineDir);
            o["spawn"] = new JArray(nine.Spawn.x + ChunkGen.Square, nine.Spawn.y + 2 * ChunkGen.Square);
            o["expects"]["landShare"] = 40; // what its bake makes (its highest and lowest are the full map's)
            var g = (JObject)o["generated"];
            g["squares"] = new JArray(6, 6);
            g["pinned"][0]["square"] = new JArray(1, 2);
            g["forced"]["rows"] = new JArray(CopyRows.Cast<object>().ToArray());
            g["sketch"]["world"] = new JObject { ["w"] = 7, ["h"] = 7, ["wrap"] = "none" };
            g["sketch"]["shapes"][0]["pts"] = JArray.Parse("[[-0.5, -0.5], [7.5, -0.5], [7.5, 7.5], [-0.5, 7.5]]");
            return o;
        }

        // ---------- the small copy ----------

        // The same layout at 6 x 6: the nine at [1, 2], the sea west and south of it its own (the ring), grass north and
        // east with a meadow's village (its templates not the nine's, which stand within 3 squares), the ring of sea round
        // it all with a spire.
        static readonly string[] CopyRows = { "~~~~~~", "~hmhh~", "~NNNh~", "~NNNh~", "~NNNh~", "~~~~~S" };
    }
}
