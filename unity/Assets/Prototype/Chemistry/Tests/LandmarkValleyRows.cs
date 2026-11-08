// Throwaway (landmark-valley-look, never merged): the three forms' rows for the valley's chosen sites, written to
// LANDMARK_ROWS_OUT for a script to append to world/maps/valley. A seed is chosen so the form isn't turned.
using System;
using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    public class LandmarkValleyRows
    {
        // The cove: the fishing village's hollow on the coast, its village inland to the north-west.
        const int CoveX = 356, CoveY = 506;

        static LandmarkRows Fits(string biome, int x, int y, int seed)
        {
            if (biome != "foggy-marsh")
            {
                var rows = LandmarkIntentTests.Village.Make(biome, x, y, seed);
                return rows.VillageAt.y <= y || Math.Abs(rows.VillageAt.x - x) > 8 ? null : rows;
            }
            foreach (var (dx, dy) in new[] { (0, 48), (-48, 0), (0, -48), (48, 0) })
            {
                var rows = LandmarkIntentTests.Village.Make(biome, x + dx, y + dy, seed);
                var at = rows.Stamps[0]["at"];
                int vx = rows.VillageAt.x - x, vy = rows.VillageAt.y - y;
                if ((int)at[0] == x && (int)at[1] == y && vy < -40 && Math.Abs(vx) < 10) return rows;
            }
            return null;
        }

        // The lake's water, level: the hollow's cells below the lowest point of its rim, on the valley's land.
        static void Water(LandmarkRows rows, string folder)
        {
            var map = MapFile.Load(ChemistryTests.WorldDir());
            var land = LandFile.Read(File.ReadAllText(Path.Combine(ChemistryTests.WorldDir(), MapFile.LandName)), map.W, map.H);
            var stamp = MapStamps.FromJson(rows.StampsJson).All[0].Stamp;
            int Ground(int x, int y) => land[y * map.W + x] + stamp.At(x - stamp.X, y - stamp.Y);
            int r = stamp.Radius, level = int.MaxValue;
            for (int y = stamp.Y - r - 1; y <= stamp.Y + r + 1; y++)
                for (int x = stamp.X - r - 1; x <= stamp.X + r + 1; x++)
                {
                    long d2 = (long)(x - stamp.X) * (x - stamp.X) + (long)(y - stamp.Y) * (y - stamp.Y);
                    if (d2 >= (long)(r - 1) * (r - 1) && d2 <= (long)(r + 1) * (r + 1)) level = Math.Min(level, Ground(x, y));
                }
            var cells = new Newtonsoft.Json.Linq.JArray();
            for (int y = stamp.Y - r; y <= stamp.Y + r; y++)
                for (int x = stamp.X - r; x <= stamp.X + r; x++)
                    if (Ground(x, y) < map.SeaLevel) cells.Add(new Newtonsoft.Json.Linq.JArray(x, y));
            // The hollow baked into the land (a map stamp keeps off water), for the cove that crosses the sea.
            var baked = (int[])land.Clone();
            for (int y = stamp.Y - r; y <= stamp.Y + r; y++)
                for (int x = stamp.X - r; x <= stamp.X + r; x++)
                    if (x >= 0 && y >= 0 && x < map.W && y < map.H) baked[y * map.W + x] = Ground(x, y);
            File.WriteAllText(Path.Combine(folder, "land.json"), LandFile.Write(baked, map.W, map.H, 0.125));
            File.WriteAllText(Path.Combine(folder, "water.json"), new Newtonsoft.Json.Linq.JObject { ["level"] = level, ["cells"] = cells }.ToString());
            TestContext.Out.WriteLine($"LANDMARK WATER level {level}, {cells.Count} cells of {(int)(Math.PI * r * r)} in the hollow");
        }

        [Test, Explicit("writes the valley's rows for phase 1b")]
        public void WriteThem()
        {
            string folder = Environment.GetEnvironmentVariable("LANDMARK_ROWS_OUT");
            Assert.That(folder, Is.Not.Null.And.Not.Empty);
            Directory.CreateDirectory(folder);
            foreach (var (biome, x, y, name) in new[] { ("temperate", 470, 226, "forest"), ("mountains", 160, 170, "volcano"), ("foggy-marsh", CoveX, CoveY, "fishing") })
            {
                for (int seed = 1; seed < 500; seed++)
                {
                    var rows = Fits(biome, x, y, seed);
                    if (rows == null) continue;
                    File.WriteAllText(Path.Combine(folder, name + ".json"), new Newtonsoft.Json.Linq.JObject
                    {
                        ["seed"] = seed,
                        ["stamps"] = rows.Stamps,
                        ["village"] = rows.Village, ["villagePlace"] = rows.VillagePlace,
                        ["buildings"] = rows.Buildings,
                        ["things"] = rows.Things,
                        ["place"] = rows.Place,
                    }.ToString());
                    TestContext.Out.WriteLine($"LANDMARK ROWS {name}: seed {seed}, {rows.Name} by {rows.VillageName}");
                    if (name == "fishing") Water(rows, folder);
                    break;
                }
            }
        }
    }
}
