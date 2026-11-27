// BakeTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in BakeTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class BakeWorldTests
    {

        [Test]
        public void ARowAtATimeIsTheSameBytesAsTheWholeMapAtOnce()
        {
            string root = Path.Combine(Path.GetTempPath(), "bake-lean-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var m = GeneratedMap.Load(BakeTests.TwoSquares(root));
                int chunkCount = m.Gen.ChunksW * m.Gen.ChunksH;
                string one = Path.Combine(root, "one"), four = Path.Combine(root, "four"), given = Path.Combine(root, "given"), whole = Path.Combine(root, "whole");
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                m.Bake(one, threads: 1);
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                TestContext.Out.WriteLine($"BAKE {chunkCount} chunks on one thread {watch.Elapsed.TotalMilliseconds:0} ms, making them allocated {allocated / 1024 / chunkCount} KB a chunk");
                Assert.That(allocated / chunkCount, Is.LessThan(256 * 1024), "a chunk's making allocates little more than the chunk");
                m.Bake(four, threads: 4);
                var chunks = m.MakeAll(3);
                m.Bake(given, chunks);

                var names = Directory.GetFiles(one).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
                Assert.That(names, Is.SupersetOf(GeneratedMap.Baked));
                foreach (string dir in new[] { four, given })
                {
                    Assert.That(Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal), Is.EqualTo(names), dir);
                    foreach (string name in names)
                        Assert.That(File.ReadAllBytes(Path.Combine(dir, name)), Is.EqualTo(File.ReadAllBytes(Path.Combine(one, name))), $"{name} in {Path.GetFileName(dir)}");
                }

                var (land, _) = WholeMap(m, chunks, whole);
                foreach (string name in new[] { MapFile.CellsName, "things.json" })
                    Assert.That(File.ReadAllBytes(Path.Combine(one, name)), Is.EqualTo(File.ReadAllBytes(Path.Combine(whole, name))), name);
                int w = m.Gen.CellsW, h = m.Gen.CellsH;
                Assert.That(LandFile.Read(File.ReadAllText(Path.Combine(one, MapFile.LandName)), w, h),
                    Is.EqualTo(LandFile.Read(File.ReadAllText(Path.Combine(whole, MapFile.LandName)), w, h)), MapFile.LandName);
                var settings = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(one, WorldPlaces.FileName)))["generate"]["water"];
                var filled = BakeTests.FloodFilled(land, m.Gen.CellsW, m.Gen.CellsH, (int)settings["below"], (int?)settings["least"] ?? 1);
                var baked = BakeTests.WaterPlaces(one);
                Assert.That(baked.Select(p => (string)p["id"]), Is.EqualTo(filled.Select(p => (string)p["id"])));
                Assert.That(baked.Select(p => (string)p["kind"]).Distinct(), Is.EquivalentTo(new[] { "sea", "lake" }), "the map has a sea and a lake");
                for (int i = 0; i < filled.Count; i++)
                    Assert.That(JToken.DeepEquals(baked[i], filled[i]), Is.True, $"{filled[i]["id"]}'s rows");

                string cells = Path.Combine(one, MapFile.CellsName);
                var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(cells, written);
                m.Bake(one, chunks);
                Assert.That(File.GetLastWriteTimeUtc(cells), Is.EqualTo(written), "the same cells aren't written again");
                Assert.That(Directory.GetFiles(one).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal), Is.EqualTo(names));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        static (int[] land, char[] cells) WholeMap(GeneratedMap m, GeneratedChunk[] chunks, string dir)
        {
            Directory.CreateDirectory(dir);
            int w = m.Gen.CellsW, h = m.Gen.CellsH;
            var (land, cells) = ChunkGenTests.WholeOf(m.Gen, chunks);
            var things = new JArray();
            foreach (var c in chunks)
                if (c.Things.Count > 0) things.Add(new JObject { ["chunk"] = new JArray(c.X, c.Y), ["things"] = c.Things.DeepClone() });
            var rows = new StringBuilder(w * h + h);
            for (int y = 0; y < h; y++)
            {
                if (y > 0) rows.Append('\n');
                rows.Append(cells, y * w, w);
            }
            File.WriteAllText(Path.Combine(dir, MapFile.CellsName), rows.ToString());
            File.WriteAllText(Path.Combine(dir, MapFile.LandName), WholeLand(land, w, h));
            var o = new JObject
            {
                ["about"] = $"Made by ChunkGen {ChunkGen.Version} from {MapFile.FileName}: the valley's things in its chunks, then each chunk's landmarks' and seeded things.",
                ["chunks"] = things,
            };
            File.WriteAllText(Path.Combine(dir, "things.json"), o.ToString(Formatting.Indented) + "\n");
            return (land, cells);
        }

        static string WholeLand(int[] land, int w, int h)
        {
            using var packed = new MemoryStream();
            using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, true))
            {
                for (int k = 0; k < land.Length; k++)
                {
                    int before = 0;
                    if (k > 0) before = k % w == 0 ? land[k - w] : land[k - 1];
                    uint v = (uint)(((land[k] - before) << 1) ^ ((land[k] - before) >> 31));
                    for (; v >= 0x80; v >>= 7) deflate.WriteByte((byte)(v | 0x80));
                    deflate.WriteByte((byte)v);
                }
            }
            var o = new JObject
            {
                ["version"] = LandFile.Version,
                ["w"] = w,
                ["h"] = h,
                ["unitMetres"] = 0.125,
                ["data"] = Convert.ToBase64String(packed.ToArray()),
            };
            return o.ToString(Formatting.Indented) + "\n";
        }
    }

    [Category("Unit")]
    public class WaterRunsTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void TheRunsFindTheBodiesAFloodFillDoes(int seed)
        {
            const int w = 61, h = 37;
            var rng = new Random(seed);
            var land = new int[w * h];
            for (int k = 0; k < land.Length; k++) land[k] = rng.Next(-6, 6);
            string dir = Path.Combine(Path.GetTempPath(), "water-runs-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, MapFile.LandName), LandFile.Write(land, w, h, 0.125));
                File.WriteAllText(Path.Combine(dir, WorldPlaces.FileName), "{\"generate\": {\"water\": {\"below\": 0, \"least\": 3}}, \"places\": []}");
                File.WriteAllText(Path.Combine(dir, WorldPlaces.FileName), GeneratedPlaces.Generate(dir));
                var found = BakeTests.WaterPlaces(dir);
                var filled = BakeTests.FloodFilled(land, w, h, 0, 3);
                Assert.That(found.Count, Is.GreaterThan(5), "bodies to compare");
                Assert.That(found.Select(p => (string)p["id"]), Is.EqualTo(filled.Select(p => (string)p["id"])));
                for (int i = 0; i < filled.Count; i++)
                    Assert.That(JToken.DeepEquals(found[i], filled[i]), Is.True, $"{filled[i]["id"]}: {found[i]["rows"]}");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
