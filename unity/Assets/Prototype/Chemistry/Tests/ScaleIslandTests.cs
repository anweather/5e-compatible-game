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
        internal static string Dir => Path.Combine(WorldDir, "maps", "scale-12x10");
        internal static string NineDir => Path.Combine(WorldDir, "maps", "nine");

        static GeneratedMap island;
        internal static GeneratedMap Island => island ??= GeneratedMap.Load(Dir);

        [OneTimeTearDown]
        public void DropTheMap()
        {
            island = null;
        }

        const uint PlanGolden = 0xacc38348u;

        const int BlockX = 4, BlockY = 3, BlockChunkX = BlockX * WorldPlan.ChunksPerSquare, BlockChunkY = BlockY * WorldPlan.ChunksPerSquare;

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
            Assert.That(p.Landmarks.Where(l => p.HeldSquare(l.Square) >= 0), Is.Empty);
            Assert.That(p.Spawns.Where(s => p.HeldSquare(s.Square) >= 0), Is.Empty);
            Assert.That(p.Roads.SelectMany(r => r.Path).Where(i => p.HeldSquare(i) >= 0), Is.Empty);
            Assert.That(Island.Gen.Spawns.Count(s => s.Held), Is.EqualTo(9), "the nine's nine spawns, moved");
            Assert.That(Island.Gen.Spawns.Count(s => s.Pinned), Is.EqualTo(1), "the valley's among them");
        }

        [Test]
        public void NoTemplateStandsWithinThreeSquaresOfACopy()
        {
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
            foreach (var (cx, cy) in new[] { (0, 0), (13, 13), (26, 26), (0, 20) })
            {
                var a = gen.Know(BlockChunkX + cx, BlockChunkY + cy);
                var b = nine.Know(cx, cy);
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
            var m = Island;
            var ids = PlaceIds(m);
            TestContext.Out.WriteLine("NAMES " + string.Join(", ", m.Gen.LandmarkRows.Where(r => r != null).Select(r => $"{r.Name} by {r.VillageName}")));
            Assert.That(ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key), Is.Empty, "place ids taken twice");
            Assert.That(m.Gen.PlaceIds, Is.EquivalentTo(ids), "the generator's set is the map's ids");
            var named = m.Far.All.Where(l => l.Name != null).ToList();
            foreach (var l in named.Where(l => l.From == "landmark" && !m.Gen.InPinnedSquare(l.Square)))
                Assert.That(l.Name, Is.EqualTo(m.Gen.LandmarkRows[l.Index].Name), $"landmark {l.Index}");
            var names = m.Gen.LandmarkRows.Concat(m.Inner.Gen.LandmarkRows).Where(r => r != null).Select(r => r.Name).ToList();
            Assert.That(named.Select(l => l.Name), Is.EquivalentTo(names), "a label for each landmark made, by its name");
            Assert.That(named.Select(l => l.Name).Distinct().Count(), Is.EqualTo(named.Count), "no label twice");
        }

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

        internal static void CopyInputs(string root)
        {
            foreach (string f in new[] { "bands.json", BiomeTable.FileName, HorizonTable.FileName }) File.Copy(Path.Combine(WorldDir, f), Path.Combine(root, f));
            foreach (string d in new[] { SquareLibrary.Folder, LandmarkIntent.Folder, Path.Combine("maps", "valley") }) CopyTree(Path.Combine(WorldDir, d), Path.Combine(root, d));
            Directory.CreateDirectory(Path.Combine(root, "maps", "nine"));
            File.Copy(Path.Combine(NineDir, MapFile.FileName), Path.Combine(root, "maps", "nine", MapFile.FileName));
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (string d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
