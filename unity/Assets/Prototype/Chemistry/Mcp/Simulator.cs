// simulate: runs the core headless on a named scenario, once with a patch on and
// once without, and says what happened in words. The worlds are seeded and the
// tick is deterministic, so the cell hash is the same every run; only the
// ticks per second (wall clock) differ.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Chem.Mcp
{
    /// <summary>What one run did: the census before and after, the peaks, the speed and the cell hash.</summary>
    public sealed class SimRun
    {
        public Dictionary<string, int> Before { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> After { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The most cells burning, holding water and holding gas at once, and the tick of each.</summary>
        public Dictionary<string, (int count, int tick)> Peaks { get; } = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        public int CellsChanged { get; set; }
        /// <summary>The cells that were on fire at some tick of the run.</summary>
        public int CellsBurnt { get; set; }
        public int Ticks { get; set; }
        public double TicksPerSecond { get; set; }
        public string Hash { get; set; }
        public uint Rng { get; set; }
        /// <summary>Who walked the loop (Simulator.Walk), or null for a run with no walk.</summary>
        public string Walker { get; set; }
        /// <summary>The walker's stands (the first, then a step each), the times triggers fired and the casts they made.</summary>
        public int Steps { get; set; }
        public int Firings { get; set; }
        public int Casts { get; set; }
    }

    public static class Simulator
    {
        public const int Side = 64;
        const uint Seed = 20261031;

        /// <summary>The named scenarios, each on one chunk (64 × 64 cells) walled with bedrock.</summary>
        public static readonly IReadOnlyDictionary<string, string> Scenarios = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["fire-on-grass"] = "A meadow with trees, brambles, an oil slick and a pond; a Fire Bolt lands in the middle.",
            ["fireball"] = "Tufts of grass on bare dirt, too far apart for fire to spread; a Fireball bursts in the middle, so its radius, crater and scatter show in the cells burnt.",
            ["flood"] = "A pond overflows: a block of deep water on grass and dirt runs out over the field, past a trench, an oil slick and a small fire.",
            ["storm"] = "A thunderstorm is held over a meadow with trees, a pond and a metal post: rain fills the hollows and lightning strikes.",
            [WalkScenario] = "A meadow with trees, and a wizard running a loop round it, a cell every 2 ticks: for a trigger on steps.",
        };

        /// <summary>The scenario that's only the walk; any scenario walks too when asked (Run's walker).</summary>
        public const string WalkScenario = "walk";

        /// <summary>Ticks between a walker's steps: a run, three cells a second.</summary>
        public const int StepTicks = 2;

        const int LoopFrom = 16, LoopTo = Side - 1 - 16;

        /// <summary>The walker's cell after <paramref name="steps"/> steps round the loop, a square from (16, 16) to (47, 47), clockwise.</summary>
        public static (int x, int y) Walk(int steps)
        {
            int side = LoopTo - LoopFrom, i = steps % (side * 4), leg = i / side, along = i % side;
            return leg switch
            {
                0 => (LoopFrom + along, LoopFrom),
                1 => (LoopTo, LoopFrom + along),
                2 => (LoopTo - along, LoopTo),
                _ => (LoopFrom, LoopTo - along),
            };
        }

        public static World Build(Rules rules, string scenario)
        {
            var w = Meadow(rules);
            switch (scenario)
            {
                case "fire-on-grass":
                    FireOnGrass(w);
                    break;
                case "fireball":
                    Fireball(w);
                    break;
                case "flood":
                    Flood(w, rules);
                    break;
                case "storm":
                    Pond(w, 40, 40, 10, 6, 4);
                    w.At(20, 20).T = "metal";
                    w.WeatherSet = (WeatherKind.Storm, 0, int.MaxValue);
                    break;
                case WalkScenario:
                    break;
                default:
                    throw new RulesToolException($"There is no scenario {scenario}; the scenarios are {string.Join(", ", Scenarios.Keys)}.");
            }
            return w;
        }

        /// <summary>One chunk of grass with a tree here and there, walled with bedrock.</summary>
        static World Meadow(Rules rules)
        {
            var w = new World(rules, Side, Side, Seed, "grass");
            for (int y = 0; y < Side; y++)
                for (int x = 0; x < Side; x++)
                {
                    var c = w.At(x, y);
                    if (x == 0 || y == 0 || x == Side - 1 || y == Side - 1) c.T = "bedrock";
                    else if ((x * 7 + y * 3) % 29 == 0) c.T = "tree";
                }
            return w;
        }

        static void FireOnGrass(World w)
        {
            Pond(w, 44, 20, 8, 8, 4);
            for (int x = 10; x < 20; x++) w.At(x, 40).Cover = "oil";
            for (int y = 10; y < 16; y++) w.At(24, y).T = "brush";
            w.Cast("fire-bolt", 32, 32, 1);
        }

        // Tufts of grass with bare dirt between them, so fire can't run from tuft to tuft and the
        // cells burnt are the ones the blast (and its scatter) reached.
        static void Fireball(World w)
        {
            for (int y = 1; y < Side - 1; y++)
                for (int x = 1; x < Side - 1; x++)
                    if ((x + y) % 2 == 1 && w.At(x, y).T == "grass") w.At(x, y).T = "dirt";
            w.Cast("fireball", 32, 32, 1);
        }

        static void Flood(World w, Rules rules)
        {
            Pond(w, 4, 4, 12, 12, rules.Sim.WaterMaxLevel);
            for (int x = 20; x < 60; x++) w.At(x, 30).T = "pit";
            for (int y = 20; y < 50; y++)
                for (int x = 20; x < 40; x++)
                    if (w.At(x, y).T == "grass" && (x + y) % 3 == 0) w.At(x, y).T = "dirt";
            for (int x = 30; x < 40; x++) w.At(x, 44).Cover = "oil";
            w.Cast("fire-bolt", 50, 50, 1);
        }

        static void Pond(World w, int x0, int y0, int width, int height, int water)
        {
            for (int y = y0; y < y0 + height; y++)
                for (int x = x0; x < x0 + width; x++)
                {
                    w.At(x, y).T = "pit";
                    w.At(x, y).Water = water;
                }
        }

        /// <summary>
        /// Runs a scenario for some ticks. With a <paramref name="walker"/>, that player stands at the loop's start and
        /// steps round it every StepTicks ticks, as Stand commands, so step triggers fire as they would in play.
        /// </summary>
        public static SimRun Run(Rules rules, string scenario, int ticks, string walker = null)
        {
            var w = Build(rules, scenario);
            var run = new SimRun { Ticks = ticks, Walker = walker };
            var start = w.AllCells().Select(Snapshot).ToArray();
            Census(w, run.Before);
            var clock = Stopwatch.StartNew();
            double stepping = 0;
            var burnt = new bool[w.W * w.H];
            for (int i = 0; i < ticks; i++)
            {
                var t0 = clock.Elapsed.TotalSeconds;
                if (walker != null && i % StepTicks == 0)
                {
                    var (x, y) = Walk(run.Steps++);
                    w.ApplyStrike(StrikeCommand.Stand(walker, x, y, 12));
                }
                w.Step();
                stepping += clock.Elapsed.TotalSeconds - t0;
                for (int c = 0; c < burnt.Length; c++) burnt[c] |= w.CellAt(c).Fire > 0;
                Peak(run, "fire", w.AllCells().Count(c => c.Fire > 0), w.Tick);
                Peak(run, "water", w.AllCells().Count(c => c.Water > 0), w.Tick);
                Peak(run, "gas", w.AllCells().Count(c => c.Gas != null && c.GasAmt > 0), w.Tick);
            }
            run.TicksPerSecond = ticks / Math.Max(stepping, 1e-6);
            Census(w, run.After);
            run.CellsBurnt = burnt.Count(b => b);
            run.CellsChanged = w.AllCells().Select(Snapshot).Zip(start, (a, b) => a == b ? 0 : 1).Sum();
            run.Hash = w.CellHash();
            run.Rng = w.RngState;
            run.Firings = w.TriggerFirings;
            run.Casts = w.TriggeredCasts;
            return run;
        }

        static string Snapshot(Cell c) => $"{c.T}|{c.Cover}|{c.Gas}|{WaterOf(c)}|{c.Fire > 0}|{c.Height}";

        static string WaterOf(Cell c)
        {
            if (c.Water <= 0) return "";
            return c.Frozen ? "ice" : "water";
        }

        static void Peak(SimRun run, string what, int count, int tick)
        {
            if (!run.Peaks.TryGetValue(what, out var p) || count > p.count) run.Peaks[what] = (count, tick);
        }

        static void Census(World w, Dictionary<string, int> into)
        {
            void Count(string key)
            {
                into.TryGetValue(key, out int n);
                into[key] = n + 1;
            }
            foreach (var c in w.AllCells())
            {
                Count(c.T);
                if (c.Cover != null) Count(c.Cover + " (cover)");
                if (c.Gas != null && c.GasAmt > 0) Count(c.Gas + " (gas)");
                if (c.Water > 0) Count(c.Frozen ? "ice" : "water");
                if (c.Fire > 0) Count("fire");
            }
        }

        /// <summary>One run in words.</summary>
        public static string Describe(SimRun run, double minTicksPerSecond)
        {
            var changes = run.Before.Keys.Union(run.After.Keys)
                .Select(k => (k, d: (run.After.TryGetValue(k, out int a) ? a : 0) - (run.Before.TryGetValue(k, out int b) ? b : 0)))
                .Where(x => x.d != 0).OrderByDescending(x => Math.Abs(x.d)).ThenBy(x => x.k, StringComparer.Ordinal).Take(10)
                .Select(x => $"{x.k} {(x.d > 0 ? "+" : "−")}{Math.Abs(x.d)}").ToList();
            var peaks = new[] { "fire", "water", "gas" }.Select(k => run.Peaks.TryGetValue(k, out var p) ? $"{k} {p.count} (tick {p.tick})" : $"{k} 0");
            string speed = run.TicksPerSecond >= minTicksPerSecond ? "within" : "OVER";
            return $"{run.CellsChanged} cells changed, {run.CellsBurnt} burnt. By material: {(changes.Count == 0 ? "no change" : string.Join(", ", changes))}.\n" +
                $"Peak cells at once: {string.Join(", ", peaks)}.\n" +
                $"{run.TicksPerSecond:0} ticks per second on one chunk ({speed} the budget of {minTicksPerSecond:0}). Cell hash {run.Hash}." +
                (run.Walker == null ? "" : $"\n{run.Walker} ran {Math.Max(0, run.Steps - 1)} steps round a loop; triggers fired {run.Firings} times, casting {run.Casts} spells.");
        }
    }
}
