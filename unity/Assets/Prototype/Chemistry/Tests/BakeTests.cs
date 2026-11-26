// The bake a row of chunks at a time (GeneratedMap.Bake.cs, bake-lean). A small generated map (the valley and one square
// east of it, 2 x 1 world squares, 162 chunks, with a sea and a lake) bakes to the same bytes in every file on one
// thread, on four and from chunks made first, and its cells, land, things and water bodies are the same bytes as the
// bake's before it, which gathered the whole map and wrote it at the end (kept here as it was, as the reference). The
// water bodies found a row of runs at a time are a flood fill's on made-up land too.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem.Tests
{
    /// <summary>The bake's fixtures' helpers (its tests are BakeWorldTests: a bake writes Play's rows, so it is the core's).</summary>
    internal static class BakeTests
    {
        // The valley at [0, 0] and a square east of it, in a world folder of the test's own.
        internal static string TwoSquares(string root)
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

        // The places.json water bodies in a folder, in its order.
        internal static List<JObject> WaterPlaces(string dir) =>
            ((JArray)JObject.Parse(File.ReadAllText(Path.Combine(dir, WorldPlaces.FileName)))["places"]).OfType<JObject>()
                .Where(p => (string)p["from"] == "land.json").ToList();

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
}
