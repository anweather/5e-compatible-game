// The biome field (BiomeField, J-079 phase 2): its integer maths, world/biomes.json, the anchors, and goldens so the PC
// and the Mac can confirm they agree bit for bit. BiomeFieldTiming (explicit) measures one chunk on demand.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class BiomeFieldTests
    {
        const long One = BiomeMath.One;

        static string TablePath => Path.Combine(ChemistryTests.Folders.Repo, "world", BiomeTable.FileName);
        static BiomeTable table;
        static BiomeTable Table => table ??= BiomeTable.Load(TablePath);

        static string Id(int row) => Table.Rows[row].Id;

        [Test]
        public void ExpNegFollowsTheRealOne()
        {
            for (double x = 0; x < 22; x += 0.37)
            {
                double got = BiomeMath.ExpNeg(BiomeMath.FromDouble(x)) / (double)BiomeMath.One30;
                Assert.That(got, Is.EqualTo(Math.Exp(-x)).Within(Math.Exp(-x) * 1e-3 + 2e-9), $"e^-{x}");
            }
            Assert.That(BiomeMath.ExpNeg(0), Is.EqualTo(BiomeMath.One30));
            Assert.That(BiomeMath.ExpNeg(40 * One), Is.EqualTo(0));
        }

        [Test]
        public void Log2AndSqrtAreRight()
        {
            foreach (ulong m in new ulong[] { 1, 2, 3, 1000, 65537, 33554431 })
                Assert.That(BiomeMath.Log2(m) / 4294967296.0, Is.EqualTo(Math.Log(m, 2)).Within(1e-8), $"log2 {m}");
            foreach (long v in new long[] { 0, 1, 2, 15, 16, 17, 1L << 40, (1L << 40) + 12345, long.MaxValue / 3 })
            {
                long r = BiomeMath.Sqrt(v);
                Assert.That(r * r <= v && (r + 1) * (r + 1) > v, Is.True, $"√{v} = {r}");
            }
        }

        [Test]
        public void NoiseStaysInRangeAndIsSmooth()
        {
            long last = BiomeMath.Noise(7, 1, 0, 0, 100);
            for (long x = 0; x < 3000 * One; x += One / 3)
            {
                long v = BiomeMath.Noise(7, 1, x, -x / 2, 100);
                Assert.That(v, Is.InRange(0, One));
                Assert.That(Math.Abs(v - last), Is.LessThan(One / 50), "a third of a cell apart, near the same");
                last = v;
            }
        }

        [Test]
        public void TheTableHoldsPunchysRowsWithTheCuratedSevenOn()
        {
            Assert.That(Table.Rows.Count, Is.EqualTo(19), "Punchy's biomes at 4601262");
            var on = Table.Rows.Where(r => r.On).Select(r => r.Id).ToArray();
            Assert.That(on, Is.EquivalentTo(new[] { "temperate", "grassland", "mountains", "tundra", "salt-flat", "foggy-marsh", "ashlands" }));
            Assert.That(Table.Rows.Single(r => r.Ocean).Id, Is.EqualTo("ocean"));
            Assert.That(Id(Table.Layout[0, 0]), Is.EqualTo("temperate"), "the valley is the south-west square");
            Assert.That(Table.RegionCells, Is.EqualTo(576), "a region about one valley square");
            foreach (var r in Table.Rows)
                for (int c = 0; c < 4; c++)
                    Assert.That(r.ClimateMin[c], Is.LessThanOrEqualTo(r.ClimateMax[c]), $"{r.Id} {BiomeTable.Channels[c]}");
        }

        [Test]
        public void ALayoutMustNameBiomesThatAreOn()
        {
            var json = File.ReadAllText(TablePath).Replace("[\"temperate\", \"grassland\", \"salt-flat\"]", "[\"temperate\", \"grassland\", \"sonoran\"]");
            Assert.That(() => BiomeTable.Parse(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains("sonoran"));
        }

        [Test]
        public void WeightsAreAtMostFourAndSumToOne()
        {
            var f = new BiomeField(Table);
            for (int y = -300; y < 2000; y += 37)
                for (int x = -300; x < 2000; x += 41)
                {
                    var w = f.At(x, y);
                    long sum = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        Assert.That(w.Weight(i), Is.GreaterThanOrEqualTo(0));
                        if (w.Weight(i) > 0) Assert.That(Table.Rows[w.Id(i)].On, Is.True);
                        sum += w.Weight(i);
                    }
                    Assert.That(sum, Is.EqualTo(One), $"({x}, {y})");
                }
        }

        [Test]
        public void TheBlendIsGradualAcrossABorder()
        {
            var f = new BiomeField(Table);
            // Walking east along a row, a biome's weight never jumps by more than a slice of the blend per cell.
            for (int y = 100; y < 1700; y += 160)
            {
                var last = f.At(0, y);
                for (int x = 1; x < 1728; x++)
                {
                    var w = f.At(x, y);
                    foreach (int row in Enumerable.Range(0, Table.Rows.Count))
                        Assert.That(Math.Abs(WeightOf(w, row) - WeightOf(last, row)), Is.LessThan(One / 8), $"{Id(row)} at ({x}, {y})");
                    last = w;
                }
            }
        }

        static long WeightOf(BiomeBlend w, int row)
        {
            long s = 0;
            for (int i = 0; i < 4; i++)
                if (w.Weight(i) > 0 && w.Id(i) == row) s += w.Weight(i);
            return s;
        }

        // How much of each square each biome holds, by the dominant biome on a grid.
        static Dictionary<int, double> Shares(BiomeField f, int sx, int sy)
        {
            var count = new Dictionary<int, int>();
            int n = 0;
            for (int y = 8; y < 576; y += 16)
                for (int x = 8; x < 576; x += 16)
                {
                    int b = f.At(sx * 576 + x, sy * 576 + y).Dominant;
                    count[b] = (count.TryGetValue(b, out int c) ? c : 0) + 1;
                    n++;
                }
            return count.ToDictionary(p => p.Key, p => p.Value / (double)n);
        }

        [TestCase(7), TestCase(1), TestCase(1337)]
        public void EachSquaresAnchorHoldsTheMostOfIt(int seed)
        {
            var f = new BiomeField(Table, seed);
            for (int sx = 0; sx < 3; sx++)
                for (int sy = 0; sy < 3; sy++)
                {
                    var shares = Shares(f, sx, sy);
                    int anchor = Table.Layout[sx, sy];
                    // Two squares can share an anchor (grassland, mountains): what counts is that it leads in each.
                    var top = shares.OrderByDescending(p => p.Value).First();
                    Assert.That(Id(top.Key), Is.EqualTo(Id(anchor)), $"seed {seed}, square ({sx}, {sy}): " +
                        string.Join(", ", shares.OrderByDescending(p => p.Value).Select(p => $"{Id(p.Key)} {p.Value:P0}")));
                }
        }

        [Test, Explicit]
        public void PrintShares()
        {
            foreach (int seed in new[] { 7, 1, 1337 })
            {
                var f = new BiomeField(Table, seed);
                for (int sy = 2; sy >= 0; sy--)
                    for (int sx = 0; sx < 3; sx++)
                        TestContext.Out.WriteLine($"seed {seed} ({sx},{sy}) {Id(Table.Layout[sx, sy])}: " + string.Join(", ", Shares(f, sx, sy).OrderByDescending(p => p.Value).Select(p => $"{Id(p.Key)} {p.Value:P0}")));
            }
            Assert.Pass();
        }

        [Test]
        public void TheValleySquareIsMostlyTemperate()
        {
            var shares = Shares(new BiomeField(Table), 0, 0);
            Assert.That(shares[Table.IndexOf("temperate")], Is.GreaterThan(0.5));
        }

        [Test]
        public void TurningABiomeOnOnlyTakesTheRegionsItWins()
        {
            var json = File.ReadAllText(TablePath);
            var before = new BiomeField(BiomeTable.Parse(json));
            // Sonoran on: every region keeps its biome or becomes Sonoran, never a third one.
            var more = BiomeTable.Parse(json);
            more.Rows[more.IndexOf("sonoran")].On = true;
            var after = new BiomeField(more);
            int sonoran = more.IndexOf("sonoran"), changed = 0;
            for (int ry = -6; ry < 10; ry++)
                for (int rx = -6; rx < 10; rx++)
                {
                    int a = before.RegionBiome(rx, ry), b = after.RegionBiome(rx, ry);
                    if (a != b)
                    {
                        changed++;
                        Assert.That(b, Is.EqualTo(sonoran), $"region ({rx}, {ry})");
                    }
                }
            Assert.That(changed, Is.GreaterThan(0), "sonoran wins somewhere");
        }

        [Test]
        public void AChunkIsItsCellsWhoeverAsks()
        {
            var f = new BiomeField(Table);
            var chunk = new BiomeBlend[64 * 64];
            f.Chunk(8, 3, chunk);
            for (int j = 0; j < 64; j += 7)
                for (int i = 0; i < 64; i += 5)
                    Assert.That(chunk[j * 64 + i], Is.EqualTo(f.At(8 * 64 + i, 3 * 64 + j)));
            var again = new BiomeBlend[64 * 64];
            new BiomeField(Table).Chunk(8, 3, again);
            Assert.That(again, Is.EqualTo(chunk));
        }

        // FNV-1a over the weights on a grid across the nine squares and a little beyond.
        static uint GridHash(BiomeField f, int step, int blend = 0)
        {
            uint h = 2166136261u;
            void Mix(long v)
            {
                unchecked
                {
                    for (int b = 0; b < 4; b++)
                    {
                        h ^= (uint)(v >> (8 * b)) & 0xFF;
                        h *= 16777619u;
                    }
                }
            }
            for (int y = -64; y < 1792; y += step)
                for (int x = -64; x < 1792; x += step)
                {
                    var c = f.At(x, y, blend);
                    for (int i = 0; i < 4; i++)
                    {
                        Mix(c.Weight(i) > 0 ? c.Id(i) : -1);
                        Mix(c.Weight(i));
                    }
                }
            return h;
        }

        static string RegionMap(BiomeField f)
        {
            var s = new System.Text.StringBuilder();
            for (int ry = 4; ry >= -2; ry--)
            {
                for (int rx = -2; rx <= 4; rx++)
                    s.Append((char)('a' + f.RegionBiome(rx, ry)));
                s.Append('/');
            }
            return s.ToString();
        }

        // The goldens: run on the PC and the Mac, the same numbers, or the field isn't lockstep-safe. The regions'
        // biomes as letters (a temperate, b grassland, c mountains, d tundra, e salt-flat, f foggy-marsh, g ashlands),
        // then the surface blend on a 9-cell grid and the shape blend on a 27-cell grid over the nine squares.
        [TestCase(1337, "ceegacd/beeadaa/bedcgea/befbcdf/fbabedb/fbaffdc/afaabbd/", 0x2a5244fbu, 0x2e80f00du)]
        [TestCase(7, "caacbbe/dccafbe/cddcgee/dcfbcbe/dcabeca/dccccad/accgcab/", 0x5002e0a0u, 0xf2965e36u)]
        [TestCase(1, "edceddg/aaaabca/cbdcgae/bbfbcbe/acabecb/eccaacc/gecacgg/", 0x0623647eu, 0x8895c7d6u)]
        public void Golden(int seed, string regions, uint surface, uint shape)
        {
            var f = new BiomeField(Table, seed);
            var got = (RegionMap(f), GridHash(f, 9), GridHash(f, 27, Table.ShapeBlendCells));
            TestContext.Out.WriteLine($"[TestCase({seed}, \"{got.Item1}\", 0x{got.Item2:x8}u, 0x{got.Item3:x8}u)]");
            Assert.That(got, Is.EqualTo((regions, surface, shape)));
        }

        // Andrew's "How quickly can we generate on demand?": one chunk's weights, and roughly what land from biomes adds
        // (four biomes' five-octave relief per cell). Explicit: a timing, not a check. Run in Release:
        //   dotnet test core -c Release --filter "FullyQualifiedName~BiomeFieldTiming"
        [Test, Explicit]
        public void BiomeFieldTiming()
        {
            var f = new BiomeField(Table);
            var w = new BiomeBlend[64 * 64];
            for (int i = 0; i < 20; i++) f.Chunk(i, i, w); // warm up
            var chunk = new List<double>();
            var land = new List<double>();
            var sw = new Stopwatch();
            for (int n = 0; n < 200; n++)
            {
                int cx = n % 27, cy = n / 27;
                sw.Restart();
                f.Chunk(cx, cy, w);
                chunk.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart();
                long sink = 0;
                for (int c = 0; c < 4096; c++)
                    for (int b = 0; b < 4; b++)
                    {
                        if (w[c].Weight(b) == 0) continue;
                        for (int o = 0; o < 5; o++)
                            sink += BiomeMath.Noise(7, b * 16 + o, (cx * 64 + c % 64) * One, (cy * 64 + c / 64) * One, 300 >> o);
                    }
                land.Add(sw.Elapsed.TotalMilliseconds);
                if (sink == 42) TestContext.Out.Write("");
            }
            double Median(List<double> v) => v.OrderBy(x => x).ElementAt(v.Count / 2);
            double P95(List<double> v) => v.OrderBy(x => x).ElementAt(v.Count * 95 / 100);
            TestContext.Out.WriteLine($"BIOME CHUNK: weights median {Median(chunk):F2} ms, p95 {P95(chunk):F2} ms; " +
                $"land (4 × 5-octave relief) median {Median(land):F2} ms, p95 {P95(land):F2} ms, per 64×64 chunk, one thread");
            Assert.That(Median(chunk), Is.GreaterThan(0));
        }
    }
}
