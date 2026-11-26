// ChunkGenTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in ChunkGenTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class ChunkGenWorldTests
    {
#pragma warning restore S1215

        // The nine's heights, a pixel for every three cells: sea blue by depth, land green to brown to white by height,
        // shaded from the north-west; rivers and lakes light blue, roads brown, the valley's edge drawn.
        static void PictureOf(string dir, BakeReport r)
        {
            string folder = Environment.GetEnvironmentVariable("CHUNK_GEN_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) return;
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), r.W, r.H);
            Directory.CreateDirectory(folder);
            const int scale = 3;
            int w = r.W / scale, h = r.H / scale;
            var rgb = new byte[w * h * 3];
            var cells = new char[r.W * r.H];
            foreach (var c in ChunkGenTests.All)
                for (int j = 0; j < ChunkGen.Size; j++)
                    Array.Copy(c.Cells, j * ChunkGen.Size, cells, (c.Y * ChunkGen.Size + j) * r.W + c.X * ChunkGen.Size, ChunkGen.Size);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cx = x * scale + 1, cy = y * scale + 1, v = land[cy * r.W + cx];
                    char cell = cells[cy * r.W + cx];
                    int shade = v - land[(cy - 1) * r.W + cx - 1];
                    var col = WorldPlanPicture.Height(v);
                    if (v >= ChunkGen.SeaLevel && (cell == 'r' || cell == 'w')) col = (90, 160, 230);
                    else if (v >= ChunkGen.SeaLevel && cell == 'T') col = (col.R * 3 / 4, col.G * 3 / 4, col.B * 3 / 4);
                    else if (v >= ChunkGen.SeaLevel && cell == '#') col = (130, 125, 120);
                    WorldPlanPicture.Put(rgb, y * w + x, col, Math.Max(-60, Math.Min(60, shade * 6)));
                }
            // The squares' borders, faintly.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if ((x * scale) % ChunkGen.Square < scale || (y * scale) % ChunkGen.Square < scale)
                        for (int k = 0; k < 3; k++) rgb[(y * w + x) * 3 + k] = (byte)(rgb[(y * w + x) * 3 + k] * 3 / 4);
            string path = Path.Combine(folder, $"nine-{ChunkGenTests.Nine.Seed}-heights.png");
            File.WriteAllBytes(path, LandmarkPicture.Png(rgb, w, h));
            TestContext.Out.WriteLine($"PICTURE {path} ({w} x {h}): land {land.Min()}..{land.Max()} land units");
            CellsOf(folder, cells, r.W, 1, 0, "north-middle");
            CellsOf(folder, cells, r.W, 1, 1, "middle");
        }

        [Test]
        public void TheBakeIsAMapThatPassesItsChecks()
        {
            string world = Path.Combine(Path.GetTempPath(), "chunk-gen-" + Guid.NewGuid().ToString("N"));
            string dir = Path.Combine(world, "nine");
            try
            {
                var report = ChunkGenTests.Nine.Bake(dir, ChunkGenTests.All);
                TestContext.Out.WriteLine($"BAKE writing the nine's folder {report.TotalMs - report.MakeMs:F0} ms");
                PictureOf(dir, report);
                Assert.That(MapCheck.Errors(dir), Is.Empty);
                var map = MapFile.Load(dir);
                // This folder is away from the map's own, as a build's bake is (the player's data), and the pinned map
                // can't be found from there: the bake says the full detail the pinned map gave it (#183: the nine's ground
                // was the valley's 257 samples a chunk on a layout of 65, and a build's world was never made).
                var own = MapFile.Load(ChunkGenTests.Nine.MapDir);
                Assert.That(own.FullDetail, Is.EqualTo((3, 2, 5, 4)), "the valley's full detail, where its square is (terrain rows count from the south)");
                Assert.That(map.FullDetail, Is.EqualTo(own.FullDetail), "a bake away from the map's folder draws the chunks its map does");
                foreach (var (i, j) in new[] { (0, 0), (3, 2), (4, 3), (5, 4), (6, 4) })
                    Assert.That(map.Layout().Resolution(i, j), Is.EqualTo(own.Layout().Resolution(i, j)), $"chunk ({i}, {j})'s samples");
                var rows = MapFile.ReadCells(dir).Split('\n');
                Assert.That(rows.Length, Is.EqualTo(map.H));
                Assert.That(rows[map.Spawn.y][map.Spawn.x], Is.EqualTo('@'), "the valley's spawn, moved with it");
                Assert.That(rows.Sum(r => r.Count(ch => ch == '@')), Is.EqualTo(1));
                foreach (string name in GeneratedMap.Baked) Assert.That(File.Exists(Path.Combine(dir, name)), name);
                // A spawn place a square, named for it, at its spawn; the valley's the map's start.
                var spawns = WorldPlaces.Load(dir).All.Where(p => p.Kind == "spawn").ToList();
                TestContext.Out.WriteLine("BAKE spawns: " + string.Join(", ", spawns.Select(p => $"{p.Name} {p.Middle}")));
                Assert.That(spawns.Select(p => p.Middle), Is.EquivalentTo(ChunkGenTests.Nine.Gen.Spawns.Select(s => (s.X, s.Y))));
                Assert.That(spawns.Select(p => p.Name).Distinct().Count(), Is.EqualTo(9));
                var start = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, WorldPlaces.FileName)))["places"].Single(p => (bool?)p["start"] == true);
                Assert.That(spawns.Single(p => p.Id == (string)start["id"]).Middle, Is.EqualTo(map.Spawn), "the valley's spawn is the map's start");
                // The rows the valley's buildings stand on are where the valley is now.
                var hold = WorldBuildings.Load(dir);
                Assert.That(hold, Is.Not.Null);
                // A build's bake is kept while it's of these inputs and this format; an older one (it has no "format",
                // and its map.json none of the pinned map's full detail) is made again.
                Assert.That(MapBake.EnsureBakedInto(ChunkGenTests.NineDir, dir), Is.False, "a bake of these inputs in this format is kept");
                File.Copy(Path.Combine(ChunkGenTests.NineDir, MapFile.FileName), Path.Combine(dir, MapFile.FileName), true);
                var note = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, GeneratedMap.BakeName)));
                note.Remove("format");
                File.WriteAllText(Path.Combine(dir, GeneratedMap.BakeName), note.ToString());
                Assert.That(MapFile.Load(dir).FullDetail, Is.EqualTo((0, 0, -1, -1)), "the old bake, as a build before #183's fix left it");
                Assert.That(MapBake.EnsureBakedInto(ChunkGenTests.NineDir, dir), Is.True, "a bake of an older format is made again");
                Assert.That(MapFile.Load(dir).FullDetail, Is.EqualTo(own.FullDetail));
                Assert.That(MapCheck.Errors(dir), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(world)) Directory.Delete(world, true);
            }
        }

        // One world square's cells at a pixel a cell, by their Legend character: roads and paths are the dirt lines.
        static void CellsOf(string folder, char[] cells, int width, int sx, int sy, string name)
        {
            int n = ChunkGen.Square;
            var rgb = new byte[n * n * 3];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    (byte, byte, byte) col = cells[(sy * n + y) * width + sx * n + x] switch
                    {
                        ',' => (110, 160, 70),
                        'T' => (40, 90, 40),
                        '"' => (140, 150, 60),
                        '.' => (150, 110, 70),
                        '#' => (120, 120, 120),
                        'w' => (50, 90, 170),
                        'r' => (90, 160, 230),
                        'L' or 'P' or '=' => (90, 60, 30),
                        _ => (255, 0, 255),
                    };
                    int k = (y * n + x) * 3;
                    (rgb[k], rgb[k + 1], rgb[k + 2]) = col;
                }
            string path = Path.Combine(folder, $"nine-{ChunkGenTests.Nine.Seed}-{name}-cells.png");
            File.WriteAllBytes(path, LandmarkPicture.Png(rgb, n, n));
            TestContext.Out.WriteLine($"PICTURE {path} ({n} x {n}): square ({sx}, {sy})'s cells");
        }
    }
}
