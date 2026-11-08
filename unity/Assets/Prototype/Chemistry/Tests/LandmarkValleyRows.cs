// Throwaway (landmark-valley-look, never merged): the three forms' rows for the valley's chosen sites, written to
// LANDMARK_ROWS_OUT for a script to append to world/maps/valley. A seed is chosen so each form faces the right way, and
// a house goes only where the valley has open ground: no water, road, river, wall or place.
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class LandmarkValleyRows
    {
        // The lake's east shore: a circle of the lake stamp's shore through it, the village east of it.
        const int LakeX = 330, LakeY = 385;

        [Test, Explicit("writes the valley's rows for phase 1b")]
        public void WriteThem()
        {
            string folder = Environment.GetEnvironmentVariable("LANDMARK_ROWS_OUT");
            Assert.That(folder, Is.Not.Null.And.Not.Empty);
            Directory.CreateDirectory(folder);
            var open = Open();
            foreach (var (biome, x, y, name) in new[] { ("temperate", 470, 226, "forest"), ("mountains", 160, 170, "volcano"), ("foggy-marsh", LakeX + 48, LakeY, "fishing") })
            {
                for (int seed = 1; seed < 500; seed++)
                {
                    var rows = LandmarkIntentTests.Village.Make(biome, x, y, seed, open);
                    if (!Faces(biome, rows, x, y)) continue;
                    File.WriteAllText(Path.Combine(folder, name + ".json"), new JObject
                    {
                        ["seed"] = seed,
                        ["stamps"] = rows.Stamps,
                        ["village"] = rows.Village,
                        ["villagePlace"] = rows.VillagePlace,
                        ["buildings"] = rows.Buildings,
                        ["things"] = rows.Things,
                        ["place"] = rows.Place,
                    }.ToString());
                    TestContext.Out.WriteLine($"LANDMARK ROWS {name}: seed {seed}, {rows.Name} by {rows.VillageName}, {rows.Buildings.Count} rows");
                    break;
                }
            }
        }

        // Not turned: the village south of the landmark; the fishing village east of its lake.
        static bool Faces(string biome, LandmarkRows rows, int x, int y)
        {
            if (biome != "foggy-marsh") return rows.VillageAt.y > y && Math.Abs(rows.VillageAt.x - x) <= 8;
            var at = rows.Stamps[0]["at"];
            return (int)at[0] == LakeX && (int)at[1] == LakeY && rows.VillageAt.x > LakeX + 40;
        }

        // Open ground in the valley's files: grass or trees, clear of every place but the water's by three cells.
        static Func<int, int, bool> Open()
        {
            string dir = ChemistryTests.WorldDir();
            var cells = File.ReadAllText(Path.Combine(dir, "cells.txt")).Split('\n');
            var places = WorldPlaces.Load(dir).All.Where(p => p.Kind != "sea" && p.Kind != "lake" && p.Kind != "river").Select(p => p.Bounds.Grown(3)).ToList();
            var rows = WorldBuildings.Load(dir).FileRows.Select(r => ((int)r["at"][0], (int)r["at"][1])).ToList();
            return (x, y) =>
            {
                if (y < 0 || y >= cells.Length || x < 0 || x >= cells[y].Length) return false;
                char c = cells[y][x];
                if (c != ',' && c != 'T') return false;
                if (places.Any(p => p.Contains(x, y))) return false;
                return rows.All(r => Math.Abs(r.Item1 - x) > 12 || Math.Abs(r.Item2 - y) > 12);
            };
        }
    }
}
