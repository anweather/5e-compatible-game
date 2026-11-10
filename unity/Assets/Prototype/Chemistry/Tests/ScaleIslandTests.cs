// The scale island (world/maps/scale-12x10; docs/design/world-12x8-scope.md, "A test map for scale"): a 12 x 10 map
// for finding what doesn't scale, the nine pinned whole in its middle as a pinned block and every other world square a
// forced square. Its plan is a golden, holds the nine's squares as the nine's own plan has them and breaks no spacing
// rule; its middle chunks are the nine's; it has landmarks spread over the grass for the far ring. The whole map is too
// big for this VM, so a 6 x 6 copy of the same layout (the nine, a ring of grass with two landmark squares, the sea round
// it) bakes in a world folder of its own: it passes MapCheck, meets the nine at its rim, every square's spawn is open,
// flat and dry, and the bake's time and the World's memory a chunk are measured on it and carried to 9720 chunks.
// SCALE_PNG_OUT=<folder> dotnet test core --filter "FullyQualifiedName~ScaleIslandTests" draws the plan.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class ScaleIslandTests
    {
        static string WorldDir => LandmarkIntentTests.GameWorldDir();
        static string Dir => Path.Combine(WorldDir, "maps", "scale-12x10");
        static string NineDir => Path.Combine(WorldDir, "maps", "nine");

        static GeneratedMap island;
        static GeneratedMap Island => island ??= GeneratedMap.Load(Dir);

        // The plan's golden: a change here is a change to the world file, the files the plan reads or the nine's plan.
        const uint PlanGolden = 0xacc38348u;

        // The nine's north-west square in the map, and the chunk it starts at.
        const int BlockX = 4, BlockY = 3, BlockChunkX = BlockX * WorldPlan.ChunksPerSquare, BlockChunkY = BlockY * WorldPlan.ChunksPerSquare;

        // How near (in world squares) two copies of one template may not stand (PlanSolver.RepeatGap, J-080).
        const int RepeatGap = 3;

        [Test]
        public void TheWorldFileSaysItsShape()
        {
            var m = Island;
            var map = MapFile.Load(Dir);
            Assert.That((m.SquaresW, m.SquaresH, m.Wrap, m.Bakes), Is.EqualTo((12, 10, PlanWrap.None, true)), "12 x 10, no wrap, baked whole");
            Assert.That((map.W, map.H), Is.EqualTo((6912, 5760)));
            Assert.That(m.Gen.ChunksW * m.Gen.ChunksH, Is.EqualTo(9720));
            Assert.That(m.Inner?.Id, Is.EqualTo("nine"), "the nine is pinned whole");
            Assert.That((m.Gen.PinnedX, m.Gen.PinnedY), Is.EqualTo((BlockX * ChunkGen.Square, BlockY * ChunkGen.Square)));
            var nine = MapFile.Load(NineDir);
            Assert.That(map.Spawn, Is.EqualTo((nine.Spawn.x + m.Gen.PinnedX, nine.Spawn.y + m.Gen.PinnedY)), "the nine's spawn, moved with it");
            var valley = MapFile.Load(Path.Combine(WorldDir, "maps", "valley"));
            var (di, dj) = MapFile.ChunkShift(map.H, valley.H, m.Gen.PinnedX, m.Gen.PinnedY + 2 * ChunkGen.Square);
            var f = valley.FullDetail;
            Assert.That(map.FullDetail, Is.EqualTo((f.i0 + di, f.j0 + dj, f.i1 + di, f.j1 + dj)), "the valley's full detail, two pins down");
            Assert.That(GeneratedMap.PinnedSquares(Dir).Select(p => (Path.GetFileName(p.dir), p.x, p.y)),
                Is.EqualTo(new[] { ("nine", 2304, 1728), ("valley", 2304, 2880) }), "the pinned maps, the valley's ground where it lies");
        }

        [Test]
        public void ThePlanIsItsGoldenWithNothingWrong()
        {
            var p = Island.Plan;
            TestContext.Out.WriteLine($"PLAN {p.Hash:x8}, {p.Steps} steps, {p.Backtracks} backtracks, {p.Landmarks.Count} landmarks, {p.Roads.Count} roads");
            Assert.That(p.Problems, Is.Empty);
            Assert.That(p.ForcedMisses, Is.Empty, "every forced square took its template");
            Assert.That(p.Hash, Is.EqualTo(PlanGolden));
            Assert.That(GeneratedMap.Load(Dir).Plan.Hash, Is.EqualTo(p.Hash), "the same inputs, the same plan");
            // Each forced square is what the world file says: the sea's open water, the grass its band's filler, a
            // landmark square its template.
            var forced = JObject.Parse(File.ReadAllText(Path.Combine(Dir, MapFile.FileName)))["generated"]["forced"];
            var rows = ((JArray)forced["rows"]).Select(r => (string)r).ToList();
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 12; x++)
                {
                    var t = p.TemplateAt(p.Grid.Index(x + 1, y));
                    string want = ((string)forced["legend"][rows[y][x].ToString()]).Split('@')[0];
                    if (want == GeneratedMap.ForcedPinned) continue;
                    if (want == ForcedSquare.Filler) Assert.That(t.Filler && t.Bands[0] > 0, $"[{x}, {y}] is {t.Id}, not a land filler");
                    else Assert.That(t.Id, Is.EqualTo(want), $"[{x}, {y}]");
                }
        }

        [Test]
        public void ItHoldsTheNinesSquaresAsTheNinesPlanHasThem()
        {
            var p = Island.Plan;
            var nine = GeneratedMap.Load(NineDir).Plan;
            int held = 0;
            for (int j = 0; j < nine.Count; j++)
            {
                if (!nine.InMap(j)) continue;
                int i = p.Grid.Index(BlockX + nine.Grid.X(j), BlockY + nine.Grid.Y(j));
                Assert.That(p.HeldSquare(i), Is.EqualTo(j));
                Assert.That((p.Options[i].Index, p.Band[i]), Is.EqualTo((nine.Options[j].Index, nine.Band[j])), $"{nine.TemplateAt(j).Id} at the nine's ({nine.Grid.X(j)}, {nine.Grid.Y(j)})");
                held++;
            }
            Assert.That(held, Is.EqualTo(9));
            Assert.That((p.Grid.X(p.Valley), p.Grid.Y(p.Valley)), Is.EqualTo((BlockX + 1, BlockY + 2)), "the nine's valley is the plan's");
            // Its landmarks, spawns and roads are the nine's own: the island's plan makes none in the held squares.
            Assert.That(p.Landmarks.Where(l => p.HeldSquare(l.Square) >= 0), Is.Empty);
            Assert.That(p.Spawns.Where(s => p.HeldSquare(s.Square) >= 0), Is.Empty);
            Assert.That(p.Roads.SelectMany(r => r.Path).Where(i => p.HeldSquare(i) >= 0), Is.Empty);
            Assert.That(Island.Gen.Spawns.Count(s => s.Held), Is.EqualTo(9), "the nine's nine spawns, moved");
            Assert.That(Island.Gen.Spawns.Count(s => s.Pinned), Is.EqualTo(1), "the valley's among them");
        }

        [Test]
        public void NoTemplateStandsWithinThreeSquaresOfACopy()
        {
            // J-080's spacing over the whole plan, the nine's squares and the forced ones with the rest: the forced squares
            // are laid out to keep it, so none is exempt.
            var p = Island.Plan;
            var close = new List<string>();
            for (int i = 0; i < p.Count; i++)
                for (int j = i + 1; j < p.Count; j++)
                {
                    var t = p.TemplateAt(i);
                    if (t.Repeats || t != p.TemplateAt(j) || p.Grid.Distance(i, j) >= RepeatGap) continue;
                    close.Add($"{t.Id} at ({p.Grid.X(i)}, {p.Grid.Y(i)}) and ({p.Grid.X(j)}, {p.Grid.Y(j)})");
                }
            Assert.That(close, Is.Empty);
        }

        [Test]
        public void TheMiddleChunksAreTheNines()
        {
            // Every chunk of the middle 3 x 3 world squares is the nine's own chunk, its land, cells, things and biome, so
            // its hash at the nine's coordinates is the nine's and the nine's golden holds over them.
            var gen = Island.Gen;
            var nine = GeneratedMap.Load(NineDir).Gen;
            int side = 3 * WorldPlan.ChunksPerSquare;
            var ours = new GeneratedChunk[side * side];
            var its = new GeneratedChunk[side * side];
            Parallel.For(0, side * side, k =>
            {
                ours[k] = gen.Make(BlockChunkX + k % side, BlockChunkY + k / side);
                its[k] = nine.Make(k % side, k / side);
            });
            var differ = new List<string>();
            for (int k = 0; k < ours.Length; k++)
            {
                var (a, b) = (ours[k], its[k]);
                if (!a.Pinned || !a.Land.SequenceEqual(b.Land) || !a.Cells.SequenceEqual(b.Cells) || a.Biome != b.Biome
                    || a.Things.ToString(Newtonsoft.Json.Formatting.None) != b.Things.ToString(Newtonsoft.Json.Formatting.None))
                    differ.Add($"({b.X}, {b.Y})");
            }
            Assert.That(differ, Is.Empty, "the nine's chunks that differ in the island");
            Assert.That(ChunkGenTests.HashOfAll(its), Is.EqualTo(ChunkGenTests.NineGolden), "the nine's golden");
            Assert.That(gen.IsPinned(BlockChunkX - 1, BlockChunkY), Is.False);
            Assert.That(gen.IsPinned(BlockChunkX + side, BlockChunkY + side - 1), Is.False);
            // Known as the nine knows them, and the rim's samples of the chunks beside it are the nine's too.
            foreach (var (cx, cy) in new[] { (0, 0), (13, 13), (26, 26), (0, 20) })
            {
                var a = gen.Know(BlockChunkX + cx, BlockChunkY + cy);
                var b = nine.Know(cx, cy);
                // Each apart: Unity's older NUnit compares a tuple's arrays by reference.
                Assert.That(a.Heights, Is.EqualTo(b.Heights), $"known ({cx}, {cy}) heights");
                Assert.That(a.Water, Is.EqualTo(b.Water), $"known ({cx}, {cy}) water");
                Assert.That(a.Biomes, Is.EqualTo(b.Biomes), $"known ({cx}, {cy}) biomes");
                Assert.That(a.Biome, Is.EqualTo(b.Biome), $"known ({cx}, {cy}) biome");
            }
            var west = gen.Know(BlockChunkX - 1, BlockChunkY + 4);
            var edge = nine.Know(0, 4);
            for (int j = 0; j < KnownChunk.Side; j++)
                Assert.That(west.Heights[j * KnownChunk.Side + KnownChunk.Side - 1], Is.EqualTo(edge.Heights[j * KnownChunk.Side]), "the shared edge sample");
        }

        [Test]
        public void TheFarRingHasOldGrumblerAndLandmarksOverTheGrass()
        {
            var m = Island;
            var far = m.Far.All;
            var nineFar = GeneratedMap.Load(NineDir).Far.All;
            var grumbler = nineFar.Single(l => l.Name == "Old Grumbler");
            var ours = far.Single(l => l.Name == "Old Grumbler");
            Assert.That((ours.Type, ours.X, ours.Y), Is.EqualTo(("volcano", grumbler.X + m.Gen.PinnedX, grumbler.Y + m.Gen.PinnedY)), "the nine's volcano, where the nine is");
            var nine = GeneratedMap.Load(NineDir).Gen;
            Assert.That(far.Count(l => l.From == "landmark" && m.Gen.InPinnedSquare(l.Square)),
                Is.EqualTo(nineFar.Count(l => l.From == "landmark" && l.X >= 0 && l.Y >= 0 && l.X < nine.CellsW && l.Y < nine.CellsH)), "the nine's landmarks on its map, all of them");
            // The village landmarks and sea spires over the rest of the map, in the map, each in a square of its own and
            // spread: none side by side, some in every quarter.
            var others = far.Where(l => l.From == "landmark" && !m.Gen.InPinnedSquare(l.Square) && l.X >= 0 && l.Y >= 0 && l.X < m.Gen.CellsW && l.Y < m.Gen.CellsH).ToList();
            TestContext.Out.WriteLine("FAR " + string.Join(", ", others.Select(l => $"{l.Name ?? l.Type} ({l.Type}) at ({l.X / ChunkGen.Square}, {l.Y / ChunkGen.Square})")));
            Assert.That(others.Count, Is.InRange(6, 10));
            Assert.That(others.Select(l => l.Type).Distinct(), Is.EquivalentTo(new[] { "great tree", "volcano", "lake", "spire" }));
            Assert.That(others.Select(l => l.Square).Distinct().Count(), Is.EqualTo(others.Count));
            for (int a = 0; a < others.Count; a++)
                for (int b = a + 1; b < others.Count; b++)
                    Assert.That(m.Plan.Grid.Distance(others[a].Square, others[b].Square), Is.GreaterThanOrEqualTo(2), $"{others[a].Name} and {others[b].Name}");
            int Quarter(FarLandmark l) => (l.X * 2 / m.Gen.CellsW) + 2 * (l.Y * 2 / m.Gen.CellsH);
            Assert.That(others.Select(Quarter).Distinct().Count(), Is.EqualTo(4), "some in every quarter");
        }

        [Test]
        public void NoPlaceIdIsTakenTwiceAndTheFarRingFollowsTheNames()
        {
            // #203: two lake villages both rolled Dunmere (and two great trees the Elder Oak), and the bake's places.json
            // was refused. Every place id on the map once: the pinned maps', each landmark's and each village's.
            var m = Island;
            var ids = PlaceIds(m);
            TestContext.Out.WriteLine("NAMES " + string.Join(", ", m.Gen.LandmarkRows.Where(r => r != null).Select(r => $"{r.Name} by {r.VillageName}")));
            Assert.That(ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key), Is.Empty, "place ids taken twice");
            Assert.That(m.Gen.PlaceIds, Is.EquivalentTo(ids), "the generator's set is the map's ids");
            // The far ring's labels are the landmarks' names as they were made, so none shows twice either.
            var named = m.Far.All.Where(l => l.Name != null).ToList();
            foreach (var l in named.Where(l => l.From == "landmark" && !m.Gen.InPinnedSquare(l.Square)))
                Assert.That(l.Name, Is.EqualTo(m.Gen.LandmarkRows[l.Index].Name), $"landmark {l.Index}");
            var names = m.Gen.LandmarkRows.Concat(m.Inner.Gen.LandmarkRows).Where(r => r != null).Select(r => r.Name).ToList();
            Assert.That(named.Select(l => l.Name), Is.EquivalentTo(names), "a label for each landmark made, by its name");
            Assert.That(named.Select(l => l.Name).Distinct().Count(), Is.EqualTo(named.Count), "no label twice");
        }

        /// <summary>
        /// The place ids a generated map's places.json would hold but its spawns' (which are named apart): its pinned map's
        /// (a block's, the same way; a map folder's, its places.json's), then each landmark's place and its village's.
        /// </summary>
        internal static List<string> PlaceIds(GeneratedMap m)
        {
            var ids = m.Inner != null ? PlaceIds(m.Inner) : WorldPlaces.Load(m.PinnedDir).All.Select(p => p.Id).ToList();
            foreach (var r in m.Gen.LandmarkRows.Where(r => r != null))
            {
                ids.Add((string)r.Place["id"]);
                ids.Add(r.VillagePlace != null ? (string)r.VillagePlace["id"] : Slug(r.VillageName));
            }
            return ids;
        }

        static string Slug(string name) =>
            string.Join("-", new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));

        [Test, Explicit("makes all 9720 chunks (minutes on two threads): run it by name")]
        public void ItsExpectsAreWhatItsChunksMake()
        {
            // map.json's expects, from every chunk as the bake makes it, one at a time and let go (the whole map's cells are
            // more than this VM holds): the share at or above the sea, the highest and the lowest land.
            var gen = Island.Gen;
            int w = gen.ChunksW, n = w * gen.ChunksH;
            long above = 0;
            int hi = int.MinValue, lo = int.MaxValue;
            var sync = new object();
            var watch = Stopwatch.StartNew();
            Parallel.For(0, n, k =>
            {
                var c = gen.Make(k % w, k / w);
                int a = 0, h = int.MinValue, l = int.MaxValue;
                foreach (int v in c.Land)
                {
                    if (v >= ChunkGen.SeaLevel) a++;
                    h = Math.Max(h, v);
                    l = Math.Min(l, v);
                }
                lock (sync)
                {
                    above += a;
                    hi = Math.Max(hi, h);
                    lo = Math.Min(lo, l);
                }
            });
            long cells = (long)n * ChunkGen.Size * ChunkGen.Size;
            int share = (int)Math.Round(100.0 * above / cells);
            var map = MapFile.Load(Dir);
            TestContext.Out.WriteLine($"EXPECTS made {n} chunks in {watch.Elapsed.TotalSeconds:0} s on {Environment.ProcessorCount} threads: " +
                $"land share {share}%, highest {hi}, lowest {lo}; map.json says {map.LandShare}%, {map.Highest}, {map.Lowest}");
            Assert.That((map.LandShare, map.Highest, map.Lowest), Is.EqualTo((share, hi, lo)));
        }

        [Test]
        public void PlanPicture()
        {
            string folder = Environment.GetEnvironmentVariable("SCALE_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) Assert.Ignore("SCALE_PNG_OUT names the folder for the picture");
            Directory.CreateDirectory(folder);
            var cv = WorldPlanPicture.Draw(Island.Plan, 24);
            string path = Path.Combine(folder, "plan.png");
            File.WriteAllBytes(path, LandmarkPicture.Png(cv.Rgb, cv.W, cv.H));
            TestContext.Out.WriteLine($"PICTURE {path} ({cv.W} x {cv.H})");
            Assert.That(File.Exists(path));
        }

        // ---------- the small copy ----------

        // The same layout at 6 x 6: the nine at [1, 2], the sea west and south of it its own (the ring), grass north and
        // east with a meadow's village (its templates not the nine's, which stand within 3 squares), the ring of sea round
        // it all with a spire.
        static readonly string[] CopyRows = { "~~~~~~", "~hmhh~", "~NNNh~", "~NNNh~", "~NNNh~", "~~~~~S" };

        static JObject CopyFile()
        {
            var o = JObject.Parse(File.ReadAllText(Path.Combine(Dir, MapFile.FileName)));
            o["id"] = "scale-copy";
            o["name"] = "The Scale Island, 6 x 6";
            o["size"] = new JArray(6 * ChunkGen.Square, 6 * ChunkGen.Square);
            var nine = MapFile.Load(NineDir);
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

        // A world folder of its own for the copy: the files the plan and the chunks read, the valley and the nine's world
        // file, and the copy's, so its bakes (the nine's too) never touch the game's.
        static string CopyWorld(string root)
        {
            foreach (string f in new[] { "bands.json", BiomeTable.FileName, HorizonTable.FileName }) File.Copy(Path.Combine(WorldDir, f), Path.Combine(root, f));
            foreach (string d in new[] { SquareLibrary.Folder, LandmarkIntent.Folder, Path.Combine("maps", "valley") }) CopyTree(Path.Combine(WorldDir, d), Path.Combine(root, d));
            Directory.CreateDirectory(Path.Combine(root, "maps", "nine"));
            File.Copy(Path.Combine(NineDir, MapFile.FileName), Path.Combine(root, "maps", "nine", MapFile.FileName));
            string dir = Path.Combine(root, "maps", "scale-copy");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, MapFile.FileName), CopyFile().ToString());
            return dir;
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (string d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
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
                            lo = Math.Min(lo, report.Land[y * w + x]);
                            hi = Math.Max(hi, report.Land[y * w + x]);
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
                    worst = Math.Max(worst, Math.Abs(report.Land[(y0 - 1) * w + x0 + t] - report.Land[y0 * w + x0 + t]));
                    worst = Math.Max(worst, Math.Abs(report.Land[(y0 + ph) * w + x0 + t] - report.Land[(y0 + ph - 1) * w + x0 + t]));
                }
                for (int t = 0; t < ph; t++)
                {
                    worst = Math.Max(worst, Math.Abs(report.Land[(y0 + t) * w + x0 - 1] - report.Land[(y0 + t) * w + x0]));
                    worst = Math.Max(worst, Math.Abs(report.Land[(y0 + t) * w + x0 + pw] - report.Land[(y0 + t) * w + x0 + pw - 1]));
                }
                TestContext.Out.WriteLine($"MEET the biggest step onto the nine's rim {worst} land units; the copy's land {report.Land.Min()}..{report.Land.Max()}, " +
                    $"{report.Land.Count(v => v >= ChunkGen.SeaLevel) * 100 / report.Land.Length}% at or above the sea");
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

        // What's live on the heap, collected first: a measuring test's only use of the collector.
#pragma warning disable S1215
        static long Heap() => GC.GetTotalMemory(true);
#pragma warning restore S1215

        // Every byte the process has allocated: .NET has GC.GetTotalAllocatedBytes, Unity's Mono profile doesn't (0 there).
        static long Allocated() =>
            typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) })?.Invoke(null, new object[] { false }) is long n ? n : 0;
    }
}
