// The heightfield Reset run side by side across chunks (J-079, rebuild-threading): the same bits as one chunk after
// another, on the valley and on seeded worlds with dug and raised cells, walls, snow banks and land stamps, both ways
// under walls. HeightfieldThreadingBench (explicit) measures it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class HeightfieldThreadingTests
    {
        static readonly Rules R = ChemistryTests.R;
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        // Draws from WorldHash, not System.Random, whose numbers differ between .NET and Unity's Mono.
        sealed class Draws
        {
            readonly int seed;
            int n;

            public Draws(int seed) => this.seed = seed;

            public int Next(int hi) => Next(0, hi);

            public int Next(int lo, int hi) => lo + (int)(WorldHash.Hash4(seed, 0x7e5d, n++, 0) % (uint)(hi - lo));
        }

        // A valley-sized world that tries every rule the samples follow, from a seed.
        static World Seeded(int seed)
        {
            var grid = TerrainGrid.Valley();
            var w = new World(R, grid.GridW, grid.GridH, (uint)seed, "grass");
            var rng = new Draws(seed);
            for (int n = 0; n < 40; n++)
            {
                int cx = rng.Next(w.W), cy = rng.Next(w.H), r = rng.Next(2, 14), kind = rng.Next(5);
                for (int y = Math.Max(0, cy - r); y < Math.Min(w.H, cy + r); y++)
                    for (int x = Math.Max(0, cx - r); x < Math.Min(w.W, cx + r); x++)
                    {
                        if (kind == 0) w.Paint(x, y, "pit");
                        else if (kind == 1) w.At(x, y).Height = rng.Next(-4, 5);
                        else if (kind == 2 && (x == cx || y == cy)) w.At(x, y).Height = R.Sim.Height.BlockAt + rng.Next(0, 3);
                        else if (kind == 3) w.SetCover(x, y, World.BankCover);
                        else if (kind == 4) w.Paint(x, y, "bedrock");
                    }
            }
            w.SetLand(new int[w.W * w.H]);
            w.OpenRoom(ChangeMode.Open, "Host", R, Budgets);
            for (int n = 0; n < 3; n++)
            {
                var stamp = RoomCommand.Act("Host", new JObject
                {
                    ["kind"] = World.ActLand,
                    ["what"] = World.ActLand,
                    ["stamp"] = LandStamp.Make(rng.Next(40, w.W - 40), rng.Next(40, w.H - 40), 30, rng.Next(60, 260), LandStamp.Dome, 0, 0, 20, 3).Row,
                }.ToString());
                var done = w.ApplyRoom(stamp);
                Assert.That(done.Done, $"land stamp {n} on seed {seed}: {done.Reason}");
            }
            Assert.That(w.LandHasMoved);
            return w;
        }

        // Every chunk's target and drawn heights, as bytes, in chunk order.
        static byte[] Heights(CellHeightfield f)
        {
            var bytes = new List<byte>();
            foreach (var (i, j) in f.Chunks.OrderBy(c => c.j).ThenBy(c => c.i))
                foreach (var a in new[] { f.Target(i, j), f.Current(i, j) })
                    foreach (float v in a) bytes.AddRange(BitConverter.GetBytes(v));
            return bytes.ToArray();
        }

        static byte[] ResetWith(World w, bool threaded, bool flatUnderWalls)
        {
            var f = new CellHeightfield(TerrainGrid.Valley()) { Threaded = threaded, FlatUnderWalls = flatUnderWalls };
            f.Reset(w);
            return Heights(f);
        }

        static void SameBothWays(World w, string what)
        {
            string cells = w.CellHash();
            foreach (bool flat in new[] { false, true })
            {
                var one = ResetWith(w, false, flat);
                var many = ResetWith(w, true, flat);
                Assert.That(many.Length, Is.EqualTo(one.Length));
                Assert.That(many.SequenceEqual(one), Is.True, $"{what}, flat under walls {flat}: threaded heights differ");
                // Twice threaded, in whatever order the chunks ran this time.
                Assert.That(ResetWith(w, true, flat).SequenceEqual(one), Is.True, $"{what}: a second threaded Reset differs");
            }
            Assert.That(w.CellHash(), Is.EqualTo(cells), $"{what}: a Reset only reads the cells");
        }

        [TestCase(1), TestCase(7), TestCase(42), TestCase(1337)]
        public void ThreadedResetIsTheSameOnASeededWorld(int seed) => SameBothWays(Seeded(seed), $"seed {seed}");

        [Test]
        public void ThreadedResetIsTheSameOnTheValley() => SameBothWays(FogTickBench.Valley(R), "the valley");

        [Test]
        public void ItIsOnUnlessSwitchedOff()
        {
            Assert.That(new CellHeightfield(TerrainGrid.Valley()).Threaded, Is.True);
        }

        // FNV-1a over the bytes, so the PC and the Mac can compare one number.
        static uint Fnv(byte[] bytes)
        {
            uint h = 2166136261u;
            unchecked
            {
                foreach (byte b in bytes)
                {
                    h ^= b;
                    h *= 16777619u;
                }
            }
            return h;
        }

        // The goldens, to compare between machines. The valley's heights are the same under .NET 8 and Unity's Mono.
        // A seeded world's are not: its cells and land agree, but the land-stamp and snow-bank sums round differently
        // under Mono, threaded or not. The heights are view-only floats, never shared state, so the seeded golden
        // compares .NET with .NET (the PC's and the Mac's core runs), and the valley's compares everything.
        [Test]
        public void GoldenValley()
        {
            uint valley = Fnv(ResetWith(FogTickBench.Valley(R), true, false));
            TestContext.Out.WriteLine($"HEIGHTFIELD GOLDEN valley 0x{valley:x8}");
            Assert.That(valley, Is.EqualTo(0xe8327365u));
        }

#if !UNITY_5_3_OR_NEWER
        [Test]
        public void GoldenSeeded()
        {
            uint walls = Fnv(ResetWith(Seeded(7), true, false)), flat = Fnv(ResetWith(Seeded(7), true, true));
            TestContext.Out.WriteLine($"HEIGHTFIELD GOLDEN seed 7 0x{walls:x8}, flat under walls 0x{flat:x8}");
            Assert.That((walls, flat), Is.EqualTo((0xc0a9b4f1u, 0xc1eaca71u)));
        }
#endif

        // Before and after, in core Release: dotnet test core -c Release --filter "FullyQualifiedName~HeightfieldThreadingBench"
        // --logger "console;verbosity=detailed"
        [Test, Explicit("a benchmark: run it by name")]
        public void HeightfieldThreadingBench()
        {
            var world = FogTickBench.Valley(R);
            double Median(bool threaded)
            {
                var f = new CellHeightfield(TerrainGrid.Valley()) { Threaded = threaded };
                f.Reset(world);
                var times = new List<double>();
                for (int n = 0; n < 15; n++)
                {
                    var sw = Stopwatch.StartNew();
                    f.Reset(world);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                return times.OrderBy(t => t).ElementAt(times.Count / 2);
            }
            double one = Median(false), many = Median(true);
            TestContext.Out.WriteLine($"HEIGHTFIELD RESET, 81 chunks, median of 15: one thread {one:0.0} ms, threaded {many:0.0} ms " +
                $"({Environment.ProcessorCount} logical cores)");
            Assert.That(many, Is.GreaterThan(0));
        }
    }
}
