// The bake a row of chunks at a time (GeneratedMap.Bake.cs, bake-lean). A small generated map (the valley and one square
// east of it, 2 x 1 world squares, 162 chunks, with a sea and a lake) bakes to the same bytes in every file on one
// thread, on four and from chunks made first, and its cells, land, things and water bodies are the same bytes as the
// bake's before it, which gathered the whole map and wrote it at the end (kept here as it was, as the reference). The
// water bodies found a row of runs at a time are a flood fill's on made-up land too.
using System;
using System.Collections.Generic;
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
    public class BakeTests
    {
        // The valley at [0, 0] and a square east of it, in a world folder of the test's own.
        static string TwoSquares(string root)
        {
            ScaleIslandTests.CopyInputs(root);
            var o = JObject.Parse(File.ReadAllText(Path.Combine(root, "maps", "nine", MapFile.FileName)));
            o["id"] = "two-squares";
            o["name"] = "Two squares";
            o["size"] = new JArray(2 * ChunkGen.Square, ChunkGen.Square);
            o["spawn"] = new JArray(292, 328);
            var g = (JObject)o["generated"];
            g["squares"] = new JArray(2, 1);
            g.Remove("musts");
            g["pinned"][0]["square"] = new JArray(0, 0);
            string dir = Path.Combine(root, "maps", "two-squares");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, MapFile.FileName), o.ToString());
            return dir;
        }

        [Test]
        public void ARowAtATimeIsTheSameBytesAsTheWholeMapAtOnce()
        {
            string root = Path.Combine(Path.GetTempPath(), "bake-lean-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var m = GeneratedMap.Load(TwoSquares(root));
                int chunkCount = m.Gen.ChunksW * m.Gen.ChunksH;
                string one = Path.Combine(root, "one"), four = Path.Combine(root, "four"), given = Path.Combine(root, "given"), whole = Path.Combine(root, "whole");
                // On one thread its chunks are made on this one (Parallel.For runs a single worker inline), so this
                // thread's allocation is the making's: the bake once made 9 MB of garbage a chunk.
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

                // The files the bake once held the whole map for, as it wrote them then.
                var (land, _) = WholeMap(m, chunks, whole);
                foreach (string name in new[] { MapFile.CellsName, "things.json" })
                    Assert.That(File.ReadAllBytes(Path.Combine(one, name)), Is.EqualTo(File.ReadAllBytes(Path.Combine(whole, name))), name);
                // land.json is compared by its land, not its bytes: the deflater's output fed a row at a time differs by
                // runtime (J-088), and a generated map is known by its inputs, never its baked files.
                int w = m.Gen.CellsW, h = m.Gen.CellsH;
                Assert.That(LandFile.Read(File.ReadAllText(Path.Combine(one, MapFile.LandName)), w, h),
                    Is.EqualTo(LandFile.Read(File.ReadAllText(Path.Combine(whole, MapFile.LandName)), w, h)), MapFile.LandName);
                var settings = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(one, WorldPlaces.FileName)))["generate"]["water"];
                var filled = FloodFilled(land, m.Gen.CellsW, m.Gen.CellsH, (int)settings["below"], (int?)settings["least"] ?? 1);
                var baked = WaterPlaces(one);
                Assert.That(baked.Select(p => (string)p["id"]), Is.EqualTo(filled.Select(p => (string)p["id"])));
                Assert.That(baked.Select(p => (string)p["kind"]).Distinct(), Is.EquivalentTo(new[] { "sea", "lake" }), "the map has a sea and a lake");
                for (int i = 0; i < filled.Count; i++)
                    Assert.That(JToken.DeepEquals(baked[i], filled[i]), Is.True, $"{filled[i]["id"]}'s rows");

                // Baked again over its own files, its cells.txt is left as it is (MapFile.WriteCells), and nothing of the
                // bake's own is left beside it.
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

        // The places.json water bodies in a folder, in its order.
        internal static List<JObject> WaterPlaces(string dir) =>
            ((JArray)JObject.Parse(File.ReadAllText(Path.Combine(dir, WorldPlaces.FileName)))["places"]).OfType<JObject>()
                .Where(p => (string)p["from"] == "land.json").ToList();

        // ---------- the bake as it was: the whole map, then its files ----------

        // cells.txt, land.json and things.json as the bake wrote them before bake-lean: every chunk's cells, land and
        // things gathered for the whole map, then each file written whole. The land, for the water bodies.
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

        // LandFile.Write as it was: a byte at a time into the deflater, the base64 of it all in the JSON.
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

        // The water bodies as places.json held them before bake-lean: a flood fill from each cell below the level, a set
        // of its cells a body, the largest first, then by its north-west-most cell; a body touching the map's edge a sea.
        internal static List<JObject> FloodFilled(int[] land, int w, int h, int below, int least)
        {
            var seen = new bool[w * h];
            var bodies = new List<(SortedSet<(int y, int x)> cells, bool edge)>();
            var stack = new Stack<int>();
            for (int s = 0; s < land.Length; s++)
            {
                if (seen[s] || land[s] >= below) continue;
                var cells = new SortedSet<(int y, int x)>();
                bool edge = false;
                seen[s] = true;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int k = stack.Pop(), x = k % w, y = k / w;
                    cells.Add((y, x));
                    edge |= x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    {
                        int n = ny * w + nx;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[n] || land[n] >= below) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
                if (cells.Count >= least) bodies.Add((cells, edge));
            }
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var places = new List<JObject>();
            foreach (var (cells, edge) in bodies.OrderByDescending(b => b.cells.Count).ThenBy(b => b.cells.Min))
            {
                string kind = edge ? "sea" : "lake";
                counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
                string id = counts[kind] == 1 ? kind : $"{kind}-{counts[kind]}";
                var rows = new JArray();
                foreach (var row in cells.GroupBy(c => c.y))
                {
                    var spans = new List<int> { row.Key };
                    foreach (var (_, x) in row)
                    {
                        if (spans.Count > 1 && spans[spans.Count - 1] == x - 1) spans[spans.Count - 1] = x;
                        else spans.AddRange(new[] { x, x });
                    }
                    rows.Add(new JArray(spans.Cast<object>().ToArray()));
                }
                places.Add(new JObject { ["id"] = id, ["name"] = counts[kind] == 1 ? "the " + kind : $"{kind} {counts[kind]}", ["kind"] = kind, ["from"] = "land.json", ["generated"] = true, ["rows"] = rows });
            }
            return places;
        }
    }

    [Category("Unit")]
    public class WaterRunsTests
    {
        // Made-up land, a cell in two under the water (rough enough for bodies that fork and join again, U's and rings,
        // and cells that touch only at a corner): places.json's water bodies are the flood fill's.
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
                File.WriteAllText(Path.Combine(dir, WorldPlaces.FileName), WorldPlaces.Generate(dir));
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
