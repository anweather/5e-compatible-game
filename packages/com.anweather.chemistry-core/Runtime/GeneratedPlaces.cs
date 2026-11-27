// places.json's generated places (docs/design/world-tools.md, G1): the villages (villages.json), the courses
// (gauntlet.json, golf.json), the puzzles (puzzles.json) and the water bodies (land.json's cells below the water level
// its "generate" section gives), made from what the world data already says; the reading of places.json (Place,
// WorldPlaces) is the land's (Runtime/Land/Places.cs). This half reads Play's files, so it lives above the land
// (docs/design/chem-land-split.md, way 1).
//
// Not a port: the sandbox has no places.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public static class GeneratedPlaces
    {
        public static string Generate(string worldDir) => Generate(worldDir, null);

        internal static string Generate(string worldDir, WaterRuns water)
        {
            var current = WorldPlaces.Load(worldDir);
            var made = new List<Place>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Add(Place p)
            {
                if (ids.Add(p.Id)) made.Add(p);
            }
            foreach (var spec in VillageLayout.Load(worldDir))
            {
                var plan = VillageLayout.Lay(spec, (x, y) => true);
                Add(FromRect(WorldPlaces.Slug(spec.Name), spec.Name, "village", VillageLayout.FileName, plan.Bounds));
            }
            if (CourseLayout.Load(worldDir) is CourseLayout course)
                Add(FromRect(WorldPlaces.Slug(course.Name), course.Name, "course", CourseLayout.FileName, course.Area));
            if (GolfCommand.Load(worldDir) is GolfCommand golf)
                Add(Shaped(Path.GetFileNameWithoutExtension(GolfCommand.FileName), "course", GolfCommand.FileName,
                    new JObject { ["line"] = new JArray(new JArray(golf.TeeX, golf.TeeY), new JArray(golf.HoleX, golf.HoleY)), ["width"] = 3 }));
            foreach (var puzzle in WorldPuzzles.Load(worldDir).All)
                Add(FromRect(WorldPlaces.Slug(puzzle.Name), puzzle.Name, "puzzle", WorldPuzzles.FileName, new CellRect(puzzle.X, puzzle.Y, puzzle.Width, puzzle.Height)));
            water ??= WaterRuns.Read(worldDir, current.Doc);
            if (water != null) foreach (var body in water.Bodies()) Add(body);
            foreach (var hand in current.All.Where(p => !p.Generated)) Add(hand);
            return WorldPlaces.Write(current.Doc, made);
        }

        static Place FromRect(string id, string name, string kind, string from, CellRect r) =>
            Shaped(id, kind, from, new JObject { ["area"] = new JArray(r.X, r.Y, r.Right - 1, r.Bottom - 1) }, name);

        static Place Shaped(string id, string kind, string from, JObject shape, string name = null)
        {
            var o = new JObject { ["id"] = id, ["name"] = name ?? id, ["kind"] = kind, ["from"] = from, ["generated"] = true };
            foreach (var p in shape.Properties()) o[p.Name] = p.Value;
            return Place.Parse(o);
        }
    }

    internal sealed class WaterRuns
    {
        readonly int w, h, below, least;
        readonly List<int> runY = new List<int>(), runX0 = new List<int>(), runX1 = new List<int>(), parent = new List<int>();
        int above, aboveEnd, y;

        WaterRuns(int w, int h, JObject settings)
        {
            this.w = w;
            this.h = h;
            below = (int)settings["below"];
            least = (int?)settings["least"] ?? 1;
        }

        internal static WaterRuns For(string worldDir, int w, int h)
        {
            var path = Path.Combine(worldDir, WorldPlaces.FileName);
            if (!File.Exists(path)) return null;
            return JObject.Parse(File.ReadAllText(path))["generate"]?["water"] is JObject settings ? new WaterRuns(w, h, settings) : null;
        }

        internal static WaterRuns Read(string worldDir, JObject doc)
        {
            var path = Path.Combine(worldDir, "land.json");
            if (!(doc["generate"]?["water"] is JObject settings) || !File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            var o = JObject.Parse(text);
            int w = (int)o["w"], h = (int)o["h"];
            var runs = new WaterRuns(w, h, settings);
            var land = LandFile.Read(text, w, h);
            for (int y = 0; y < h && land.Length > 0; y++) runs.Row(new ReadOnlySpan<int>(land, y * w, w));
            return runs;
        }

        internal void Row(ReadOnlySpan<int> land)
        {
            int start = runY.Count, p = above, x = 0;
            while (x < w)
            {
                if (land[x] >= below)
                {
                    x++;
                    continue;
                }
                int x0 = x;
                while (x < w && land[x] < below) x++;
                int k = runY.Count, x1 = x - 1;
                runY.Add(y);
                runX0.Add(x0);
                runX1.Add(x1);
                parent.Add(k);
                while (p < aboveEnd && runX1[p] < x0) p++;
                for (int q = p; q < aboveEnd && runX0[q] <= x1; q++) Join(q, k);
            }
            above = start;
            aboveEnd = runY.Count;
            y++;
        }

        int Root(int k)
        {
            while (parent[k] != k)
            {
                parent[k] = parent[parent[k]];
                k = parent[k];
            }
            return k;
        }

        void Join(int a, int b)
        {
            int ra = Root(a), rb = Root(b);
            if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }

        sealed class Body
        {
            public readonly List<int> Runs = new List<int>();
            public long Cells;
            public bool Edge;
        }

        internal IEnumerable<Place> Bodies()
        {
            var of = new Dictionary<int, Body>();
            var bodies = new List<Body>();
            for (int k = 0; k < runY.Count; k++)
            {
                int r = Root(k);
                if (!of.TryGetValue(r, out var b))
                {
                    of[r] = b = new Body();
                    bodies.Add(b);
                }
                b.Runs.Add(k);
                b.Cells += runX1[k] - runX0[k] + 1;
                b.Edge |= runY[k] == 0 || runY[k] == h - 1 || runX0[k] == 0 || runX1[k] == w - 1;
            }
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var b in bodies.Where(b => b.Cells >= least).OrderByDescending(b => b.Cells).ThenBy(b => (runY[b.Runs[0]], runX0[b.Runs[0]])))
            {
                string kind = b.Edge ? "sea" : "lake";
                counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
                string id = counts[kind] == 1 ? kind : $"{kind}-{counts[kind]}";
                var o = new JObject { ["id"] = id, ["name"] = counts[kind] == 1 ? "the " + kind : $"{kind} {counts[kind]}", ["kind"] = kind, ["from"] = "land.json", ["generated"] = true };
                var place = Place.FromSpans(runY[b.Runs[0]], Spans(b), o);
                o["rows"] = place.Rows();
                yield return place;
            }
        }

        int[][] Spans(Body b)
        {
            int top = runY[b.Runs[0]], bottom = runY[b.Runs[b.Runs.Count - 1]];
            var spans = new int[bottom - top + 1][];
            var row = new List<int>();
            int i = 0;
            for (int at = top; at <= bottom; at++)
            {
                row.Clear();
                for (; i < b.Runs.Count && runY[b.Runs[i]] == at; i++)
                {
                    row.Add(runX0[b.Runs[i]]);
                    row.Add(runX1[b.Runs[i]]);
                }
                spans[at - top] = row.Count == 0 ? Array.Empty<int>() : row.ToArray();
            }
            return spans;
        }
    }
}
