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
    }

    public static class Simulator
    {
        public const int Side = 64;
        const uint Seed = 20261031;

        /// <summary>The named scenarios: one chunk (64 × 64 cells) walled with bedrock, but for forest-fire, the whole valley.</summary>
        public static readonly IReadOnlyDictionary<string, string> Scenarios = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["fire-on-grass"] = "A meadow with trees, brambles, an oil slick and a pond; a Fire Bolt lands in the middle.",
            ["fireball"] = "Tufts of grass on bare dirt, too far apart for fire to spread; a Fireball bursts in the middle, so its radius, crater and scatter show in the cells burnt.",
            [ForestFire.Name] = "A Fireball into the valley's forest: the real map, land and scene tuning, a wizard standing by and the chunks following it.",
            ["flood"] = "A pond overflows: a block of deep water on grass and dirt runs out over the field, past a trench, an oil slick and a small fire.",
            ["storm"] = "A thunderstorm is held over a meadow with trees, a pond and a metal post: rain fills the hollows and lightning strikes.",
        };

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

        /// <summary>Runs a scenario; repoRoot is the folder holding world/ and core/, for the one that reads them.</summary>
        public static SimRun Run(Rules rules, string scenario, int ticks, string repoRoot = null)
        {
            var fire = scenario == ForestFire.Name ? new ForestFire(rules, repoRoot) : null;
            var w = fire?.World ?? Build(rules, scenario);
            var run = new SimRun { Ticks = ticks };
            var start = w.Cells.Select(Snapshot).ToArray();
            Census(w, run.Before);
            var clock = Stopwatch.StartNew();
            double stepping = 0;
            var burnt = new bool[w.Cells.Length];
            for (int i = 0; i < ticks; i++)
            {
                fire?.PlanChunks();
                var t0 = clock.Elapsed.TotalSeconds;
                w.Step();
                stepping += clock.Elapsed.TotalSeconds - t0;
                for (int c = 0; c < burnt.Length; c++) burnt[c] |= w.Cells[c].Fire > 0;
                Peak(run, "fire", w.Cells.Count(c => c.Fire > 0), w.Tick);
                Peak(run, "water", w.Cells.Count(c => c.Water > 0), w.Tick);
                Peak(run, "gas", w.Cells.Count(c => c.Gas != null && c.GasAmt > 0), w.Tick);
            }
            run.TicksPerSecond = ticks / Math.Max(stepping, 1e-6);
            Census(w, run.After);
            run.CellsBurnt = burnt.Count(b => b);
            run.CellsChanged = w.Cells.Select(Snapshot).Zip(start, (a, b) => a == b ? 0 : 1).Sum();
            run.Hash = w.CellHash();
            run.Rng = w.RngState;
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
            foreach (var c in w.Cells)
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
                $"{run.TicksPerSecond:0} ticks per second ({speed} the budget of {minTicksPerSecond:0}). Cell hash {run.Hash}.";
        }
    }
}
