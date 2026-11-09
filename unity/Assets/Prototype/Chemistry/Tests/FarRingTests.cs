// The far ring's core half (docs/design/world-squares.md sections 3 and 4): the known chunks round a character (its
// dominant biome and land on a 9 × 9 grid, the same land a made chunk gets), stopping one chunk past the coast, handed
// over at a tick boundary in chunk order; and the plan's far landmarks (world/horizon.json), each listed from a
// character exactly when within its seenFrom radius. Both the same from the same seed on two loads.
// FAR_RING_TIMES=1 dotnet test core --filter "FullyQualifiedName~FarRingTests" prints the load cost.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class FarRingTests
    {
        static string NineDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "nine");

        static GeneratedMap nine;

        static GeneratedMap Nine => nine ??= GeneratedMap.Load(NineDir);

        static StreamingParams Streaming => ChemistryTests.R.Sim.Streaming;

        // The valley's middle chunk (the valley is chunks 0..8, 18..26 of the nine).
        static readonly (int x, int y) Valley = (4, 22);

        static KnownRing Filled(GeneratedMap m, params (int x, int y)[] characters)
        {
            var ring = m.KnownRing(Streaming);
            ring.Want(characters);
            ring.Fill();
            return ring;
        }

        static int Chebyshev((int x, int y) a, int bx, int by) => Math.Max(Math.Abs(a.x - bx), Math.Abs(a.y - by));

        [Test]
        public void SightAndKnowComeFromTheRulesAndAreChecked()
        {
            Assert.That((Streaming.Sight, Streaming.Know), Is.EqualTo((4, 7)), "sim.json streaming.sight and know");
            var errors = Assert.Throws<ModException>(() => ChemistryTests.R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": { \"know\": 3 } } }") })).Errors;
            Assert.That(errors, Does.Contain("sim streaming.know must be at least streaming.sight"));
            errors = Assert.Throws<ModException>(() => ChemistryTests.R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": { \"sight\": 3 } } }") })).Errors;
            Assert.That(errors, Does.Contain("sim streaming.sight must be more than streaming.keep"));
        }

        [Test]
        public void AKnownChunkIsTheMadeChunksLandAtEveryEighthCell()
        {
            var gen = Nine.Gen;
            // A valley chunk, one beside it, the far corner, and every chunk a landmark's stamps reach.
            var chunks = new List<(int x, int y)> { Valley, (9, 22), (26, 0), (13, 13) };
            foreach (var rows in gen.LandmarkRows.Where(r => r != null && r.Stamps.Count > 0))
                chunks.Add((rows.Centre.x / ChunkGen.Size, rows.Centre.y / ChunkGen.Size));
            Assert.That(chunks.Count, Is.GreaterThan(4), "the nine has a stamped landmark");
            foreach (var (cx, cy) in chunks)
            {
                var made = gen.Make(cx, cy);
                var known = gen.Know(cx, cy);
                for (int j = 0; j < KnownChunk.Side - 1; j++)
                    for (int i = 0; i < KnownChunk.Side - 1; i++)
                    {
                        int at = j * KnownChunk.Step * ChunkGen.Size + i * KnownChunk.Step;
                        Assert.That(known.Heights[j * KnownChunk.Side + i], Is.EqualTo(made.Land[at]), $"chunk ({cx}, {cy}) sample ({i}, {j})");
                        char cell = made.Cells[at];
                        bool wet = cell == 'w' || cell == 'r';
                        Assert.That(known.Water[j * KnownChunk.Side + i] != KnownChunk.Dry, Is.EqualTo(wet), $"chunk ({cx}, {cy}) sample ({i}, {j}) is {cell}");
                    }
                // The last row and column are the next chunk's first: two known chunks side by side meet.
                if (cx + 1 < gen.ChunksW)
                {
                    var east = gen.Know(cx + 1, cy);
                    for (int j = 0; j < KnownChunk.Side; j++)
                        Assert.That(known.Heights[j * KnownChunk.Side + KnownChunk.Side - 1], Is.EqualTo(east.Heights[j * KnownChunk.Side]), $"chunk ({cx}, {cy}) east edge");
                }
                if (gen.IsPinned(cx, cy)) Assert.That(known.Biome, Is.EqualTo(made.Biome));
            }
        }

        [Test]
        public void TheSameSeedGivesTheSameKnownRingAndLandmarksOnTwoLoads()
        {
            foreach (string seed in new[] { null, "seed-4" })
            {
                var a = GeneratedMap.Load(NineDir, seed);
                var b = GeneratedMap.Load(NineDir, seed);
                var (ra, rb) = (Filled(a, Valley, (20, 6)), Filled(b, Valley, (20, 6)));
                Assert.That(ra.Chunks.Count, Is.GreaterThan(100), seed);
                Assert.That(rb.Chunks.Select(Show), Is.EqualTo(ra.Chunks.Select(Show)), $"{seed ?? "valley-1"}: the known ring");
                var ids = ra.Chunks.Select(k => ra.Id(k.X, k.Y)).ToList();
                Assert.That(ids, Is.Ordered, "in chunk order");
                Assert.That(b.Far.All.Select(Show), Is.EqualTo(a.Far.All.Select(Show)), $"{seed ?? "valley-1"}: the far landmarks");
                Assert.That(a.Far.All, Is.Not.Empty);
            }
        }

        static string Show(KnownChunk k) => $"{k.X},{k.Y} {k.Biome} {string.Join(",", k.Heights)} {string.Join("", k.Water)}";

        static string Show(FarLandmark l) => $"{l.Type} {l.Name} {l.From}#{l.Index} sq{l.Square} ({l.X}, {l.Y}) seen {l.SeenFrom} {l.Height}x{l.Radius} {l.Shape}";

        // A 4 × 4 world, nearly half of it sea and no valley pinned, so open sea lies well inside the map (the nine's lies
        // past its edge, every sea chunk on it beside the land).
        static ChunkGen SeaWorld()
        {
            string world = LandmarkIntentTests.GameWorldDir();
            var inputs = PlanInputs.For("sea-1", 4, PlanWrap.None);
            inputs.Sea = 45;
            var plan = WorldPlan.Solve(inputs, SquareLibrary.Load(world));
            return new ChunkGen(plan, BiomeTable.Load(Path.Combine(world, BiomeTable.FileName)), null, 0, 0, 4, 4);
        }

        [Test]
        public void TheRingIsTheChunksPastSightWithinKnowAndStopsOneChunkPastTheCoast()
        {
            var gen = SeaWorld();
            var sea = new bool[gen.ChunksW, gen.ChunksH];
            for (int cy = 0; cy < gen.ChunksH; cy++)
                for (int cx = 0; cx < gen.ChunksW; cx++) sea[cx, cy] = gen.Know(cx, cy).IsSea;
            bool Touches(int cx, int cy)
            {
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx >= 0 && ny >= 0 && nx < gen.ChunksW && ny < gen.ChunksH && !sea[nx, ny]) return true;
                    }
                return false;
            }
            // Two characters on two coasts, and one inland.
            foreach (var at in new[] { new[] { (12, 12) }, new[] { (22, 22), (28, 12) }, new[] { (20, 30) } })
            {
                var ring = new KnownRing(gen, Streaming);
                ring.Want(at);
                ring.Fill();
                int dropped = 0, kept = 0;
                string where = string.Join(" ", at);
                for (int cy = 0; cy < gen.ChunksH; cy++)
                    for (int cx = 0; cx < gen.ChunksW; cx++)
                    {
                        int d = at.Min(c => Chebyshev(c, cx, cy));
                        bool inRing = d > Streaming.Sight && d <= Streaming.Know;
                        bool want = inRing && (!sea[cx, cy] || Touches(cx, cy));
                        Assert.That(ring.TryGet(cx, cy, out _), Is.EqualTo(want), $"chunk ({cx}, {cy}), {d} from {where}");
                        if (inRing && sea[cx, cy]) _ = Touches(cx, cy) ? kept++ : dropped++;
                    }
                TestContext.Out.WriteLine($"from {where}: {ring.Chunks.Count} known, {kept} sea chunks beside land kept, {dropped} of open sea left out");
                if (at[0] != (12, 12)) continue;
                Assert.That(dropped, Is.GreaterThan(0), "open sea past the coast is left out");
                Assert.That(kept, Is.GreaterThan(0), "the sea beside the land is known");
            }
        }

        [Test]
        public void TheWorkerHandsOverAtATickBoundaryWhatFillWorksOut()
        {
            var ring = Nine.KnownRing(Streaming);
            ring.Want(new[] { Valley });
            Assert.That(ring.Chunks, Is.Empty, "nothing shows before a handover");
            var watch = Stopwatch.StartNew();
            while ((ring.Busy || ring.Chunks.Count == 0) && watch.ElapsedMilliseconds < 60000) ring.Handover();
            ring.Handover();
            var filled = Filled(Nine, Valley);
            Assert.That(ring.Chunks.Select(Show), Is.EqualTo(filled.Chunks.Select(Show)));
            int version = ring.Version;
            ring.Want(new[] { Valley });
            Assert.That(ring.Handover(), Is.False, "standing still changes nothing");
            Assert.That(ring.Version, Is.EqualTo(version));
            // A step east: the chunks it no longer knows go at the next handover, the new ones when the worker has them.
            ring.Want(new[] { (Valley.x + 1, Valley.y) });
            while (ring.Busy && watch.ElapsedMilliseconds < 60000) ring.Handover();
            ring.Handover();
            Assert.That(ring.Chunks.Select(Show), Is.EqualTo(Filled(Nine, (Valley.x + 1, Valley.y)).Chunks.Select(Show)));
            Assert.That(ring.Version, Is.GreaterThan(version));
        }

        [Test]
        public void ALandmarkIsListedFromACharacterExactlyWithinItsSeenFromRadius()
        {
            var far = Nine.Far;
            var types = far.All.Select(l => l.Type).Distinct().ToList();
            TestContext.Out.WriteLine(string.Join("\n", far.All.Select(Show)));
            Assert.That(types, Does.Contain("castle"));
            Assert.That(types, Has.Some.Matches<string>(t => t == "volcano" || t == "great tree" || t == "lake"), "a village's landmark");
            foreach (var l in far.All)
            {
                int r = l.SeenFrom * ChunkGen.Size;
                // Straight out on each side, at its radius and a cell past it.
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                {
                    Assert.That(far.SeenAt(l.X + dx * r, l.Y + dy * r), Has.Member(l), $"{Show(l)} at {r} cells");
                    Assert.That(far.SeenAt(l.X + dx * (r + 1), l.Y + dy * (r + 1)), Has.No.Member(l), $"{Show(l)} at {r + 1} cells");
                }
                // A radius, not a square: on the diagonal at the square's corner it's out of sight.
                Assert.That(far.SeenAt(l.X + r, l.Y + r), Has.No.Member(l), $"{Show(l)} on the diagonal");
                int d = (int)(r / Math.Sqrt(2));
                Assert.That(far.SeenAt(l.X + d, l.Y - d), Has.Member(l), $"{Show(l)} on the diagonal within its radius");
            }
            // From the valley's middle: the list is exactly the landmarks within radius.
            var (vx, vy) = (Valley.x * ChunkGen.Size + 32, Valley.y * ChunkGen.Size + 32);
            var seen = far.SeenAt(vx, vy).ToList();
            Assert.That(seen, Is.EqualTo(far.All.Where(l => Math.Sqrt((double)(l.X - vx) * (l.X - vx) + (double)(l.Y - vy) * (l.Y - vy)) <= l.SeenFrom * ChunkGen.Size).ToList()));
        }

        [Test]
        public void EveryFarLandmarkShowsPastTheKnownRing()
        {
            var table = HorizonTable.Load(LandmarkIntentTests.GameWorldDir());
            foreach (var row in table.Rows.Values)
                Assert.That(row.SeenFrom, Is.GreaterThanOrEqualTo(Streaming.Know + 2), $"{row.Type}'s seenFrom");
            // Every type the plans place that the file names, and nothing it doesn't.
            foreach (string seed in new[] { null, "seed-4", "seed-9" })
                foreach (var l in GeneratedMap.Load(NineDir, seed).Far.All)
                {
                    Assert.That(table.Rows.ContainsKey(l.Type), l.Type);
                    Assert.That(l.SeenFrom, Is.EqualTo(table.Rows[l.Type].SeenFrom));
                }
            Assert.Throws<FormatException>(() => HorizonTable.Parse("{ \"landmarks\": { \"volcano\": { \"seenFrom\": 0, \"height\": 60, \"radius\": 105 } } }"));
        }

        [Test]
        public void TheLoadCostOfALoneCharactersRing()
        {
            var gen = Nine.Gen;
            // A character in the middle of the nine knows the whole 15 × 15 less the 9 × 9 it can see.
            var at = (13, 13);
            Filled(Nine, at);
            var times = new List<double>();
            KnownRing ring = null;
            for (int n = 0; n < 5; n++)
            {
                var watch = Stopwatch.StartNew();
                ring = Filled(Nine, at);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            Assert.That(ring.Chunks.Count, Is.EqualTo(15 * 15 - 9 * 9));
            times.Sort();
            var one = Stopwatch.StartNew();
            for (int cy = 0; cy < gen.ChunksH; cy++) gen.Know(5, cy);
            double each = one.Elapsed.TotalMilliseconds / gen.ChunksH;
            string line = $"known ring of {ring.Chunks.Count} chunks: median {times[2]:0.0} ms (best {times[0]:0.0}, worst {times[4]:0.0}); one known chunk {each:0.00} ms";
            TestContext.Out.WriteLine(line);
            if (Environment.GetEnvironmentVariable("FAR_RING_TIMES") != null) Console.WriteLine(line);
        }
    }
}
