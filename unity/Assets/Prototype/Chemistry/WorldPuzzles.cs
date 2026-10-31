// The valley's puzzles as data (world/puzzles.json): blocks of cells laid
// into the world when it is built, over the land's own cells: a pond, a coil
// and a gate on a path, say. The machines and gates then work through the
// rules like any others (machines.json, World.Machines.cs); a gate with an
// "opensBy" opens only for its own machine. Every peer lays
// the same cells from the same file, before the first tick.
//
// Not a port: the sandbox builds its boards from scenarios. Like things.json
// the file lives in world/: rules say what exists; this says where it stands.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One puzzle: its name, its top-left world cell, its rows and what each character lays (a terrain and water units).</summary>
    public sealed class Puzzle
    {
        public string Name;
        public int X, Y;
        public IReadOnlyList<string> Rows;
        public IReadOnlyDictionary<char, (string t, int water)> Legend;
        /// <summary>The gates' legend characters, each with the character of the machine that alone opens it ("opensBy").</summary>
        public IReadOnlyDictionary<char, char> OpensBy = new Dictionary<char, char>();

        public int Width => Rows.Count == 0 ? 0 : Rows.Max(r => r.Length);
        public int Height => Rows.Count;

        /// <summary>The world cells it lays, each with its terrain and water.</summary>
        public IEnumerable<(int x, int y, string t, int water)> Cells()
        {
            for (int j = 0; j < Rows.Count; j++)
                for (int i = 0; i < Rows[j].Length; i++)
                    if (Legend.TryGetValue(Rows[j][i], out var lay)) yield return (X + i, Y + j, lay.t, lay.water);
        }

        /// <summary>The world cells where a legend character stands, in row order.</summary>
        public IEnumerable<(int x, int y)> CellsOf(char ch)
        {
            for (int j = 0; j < Rows.Count; j++)
                for (int i = 0; i < Rows[j].Length; i++)
                    if (Rows[j][i] == ch) yield return (X + i, Y + j);
        }

        /// <summary>Each gate with an "opensBy" and the cell of its machine: the one cell its machine's character stands on.</summary>
        public IEnumerable<(int gx, int gy, int mx, int my)> Links() =>
            from link in OpensBy
            let machine = CellsOf(link.Value).ToList()
            where machine.Count == 1
            from gate in CellsOf(link.Key)
            select (gate.x, gate.y, machine[0].x, machine[0].y);
    }

    public sealed class WorldPuzzles
    {
        public const string FileName = "puzzles.json";

        public static readonly WorldPuzzles Empty = new WorldPuzzles(new List<Puzzle>());

        readonly List<Puzzle> puzzles;

        WorldPuzzles(List<Puzzle> puzzles) { this.puzzles = puzzles; }

        public IReadOnlyList<Puzzle> All => puzzles;

        /// <summary>Reads world/puzzles.json from <paramref name="worldDir"/>; no file means no puzzles.</summary>
        public static WorldPuzzles Load(string worldDir)
        {
            var path = Path.Combine(worldDir, FileName);
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : Empty;
        }

        /// <summary>The puzzles in the file's text. Throws <see cref="FormatException"/> for one that can't be read.</summary>
        public static WorldPuzzles FromJson(string json)
        {
            JObject doc;
            try { doc = JObject.Parse(json); }
            catch (JsonException e) { throw new FormatException($"{FileName}: {e.Message}", e); }
            var list = new List<Puzzle>();
            foreach (var p in (doc["puzzles"] as JArray ?? new JArray()).OfType<JObject>()) list.Add(Read(p));
            return new WorldPuzzles(list);
        }

        static Puzzle Read(JObject p)
        {
            var name = (string)p["name"] ?? "a puzzle";
            if (!(p["at"] is JArray at) || at.Count != 2) throw new FormatException($"{FileName}: {name} needs \"at\": [x, y]");
            if (!(p["rows"] is JArray rows)) throw new FormatException($"{FileName}: {name} needs its \"rows\"");
            var legend = new Dictionary<char, (string, int)>();
            var opensBy = new Dictionary<char, char>();
            foreach (var e in (p["legend"] as JObject ?? new JObject()).Properties())
            {
                if (e.Name.Length != 1 || e.Name == " ") throw new FormatException($"{FileName}: {name}'s legend key \"{e.Name}\" must be one character, not a space");
                legend[e.Name[0]] = ((string)e.Value["t"], (int?)e.Value["water"] ?? 0);
                var by = (string)e.Value["opensBy"];
                if (by == null) continue;
                if (by.Length != 1) throw new FormatException($"{FileName}: {name}'s \"{e.Name}\" opensBy \"{by}\" must be one legend character");
                opensBy[e.Name[0]] = by[0];
            }
            var lines = rows.Select(r => (string)r).ToList();
            char stray = lines.SelectMany(r => r).FirstOrDefault(ch => ch != ' ' && !legend.ContainsKey(ch));
            if (stray != default) throw new FormatException($"{FileName}: {name} uses '{stray}', which its legend lacks");
            return new Puzzle { Name = name, X = (int)at[0], Y = (int)at[1], Rows = lines, Legend = legend, OpensBy = opensBy };
        }

        /// <summary>
        /// What's wrong with the puzzles against these rules: terrain they lay that the rules don't know, and a gate
        /// whose "opensBy" isn't one machine standing on one cell.
        /// </summary>
        public IEnumerable<string> Check(Rules rules) =>
            (from p in puzzles
             from t in p.Legend.Values.Select(l => l.t).Distinct()
             where t == null || !rules.Terrain.ContainsKey(t)
             select $"{FileName}: {p.Name} lays unknown terrain {t ?? "(none)"}")
            .Concat(
             from p in puzzles
             from link in p.OpensBy
             where !p.Legend.TryGetValue(link.Value, out var m) || m.t == null || !rules.Machines.ContainsKey(m.t) || p.CellsOf(link.Value).Count() != 1
             select $"{FileName}: {p.Name}'s '{link.Key}' opensBy '{link.Value}', which must be one machine on one cell");

        /// <summary>
        /// Lays every puzzle that fits wholly inside the world into its cells (one that doesn't, a smaller world's, is
        /// left out), and links each gate with an "opensBy" to its own machine (World.LinkGate).
        /// </summary>
        public void LayInto(World world)
        {
            foreach (var p in puzzles)
            {
                if (p.X < 0 || p.Y < 0 || p.X + p.Width > world.W || p.Y + p.Height > world.H) continue;
                foreach (var (x, y, t, water) in p.Cells())
                {
                    world.Paint(x, y, t);
                    if (water > 0) world.AddWater(x, y, water);
                }
                foreach (var (gx, gy, mx, my) in p.Links()) world.LinkGate(gx, gy, mx, my);
            }
        }
    }
}
