// Times the forest-fire scenario (ForestFire) tick by tick, from the Fireball to
// burn-out, stage by stage through World.StepSome's stop callback, which runs
// between slices and changes nothing. Not part of the server:
//   dotnet run -c Release --project core/Rules.Mcp -- --forest-bench [--passes N] [--csv <file>] [--max-ticks N] [--mod <rules/mods name>] [--seed N] [--walk]
// Each pass builds the world afresh; only the last is reported (the first pays for the JIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Chem.Mcp
{
    public static class ForestBench
    {
        // World.TickStage by number, then what's timed around a tick.
        static readonly string[] Stages = { "plan", "burn", "watercheck", "springs", "flow", "gas", "rest", "timers", "dragon", "end", "apply", "begin" };
        const int Plan = 0, Apply = 10, Begin = 11;
        const int GasEvery = 6;
        static readonly int[] Checkpoints = { 60, 120, 240 };

        sealed class Sample
        {
            public int At, Burning, Active, Gas;
            public double Total;
            public readonly double[] Times = new double[Stages.Length];
        }

        public static int Run(string[] args)
        {
            int passes = Option(args, "--passes", 2), maxTicks = Option(args, "--max-ticks", 20000);
            int at = Array.IndexOf(args, "--csv");
            string csv = at >= 0 ? args[at + 1] : null;
            string rulesDir = Workbench.FindRulesDir(Directory.GetCurrentDirectory());
            var rules = Rules.Load(rulesDir);
            at = Array.IndexOf(args, "--mod");
            if (at >= 0) rules = rules.WithMods(new[] { ModPatch.Parse(File.ReadAllText(Path.Combine(rulesDir, "mods", args[at + 1] + ".json"))) });
            uint seed = (uint)Option(args, "--seed", (int)ForestFire.Seed);
            string root = Path.GetDirectoryName(rulesDir);
            List<Sample> samples = null;
            List<string> prints = null;
            for (int p = 0; p < passes; p++) (samples, prints) = Pass(rules, root, maxTicks, seed, Array.IndexOf(args, "--walk") >= 0);
            foreach (string line in prints) Console.WriteLine(line);
            Report(samples);
            if (csv != null) WriteCsv(samples, csv);
            return 0;
        }

        static int Option(string[] args, string name, int otherwise)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 ? int.Parse(args[at + 1], CultureInfo.InvariantCulture) : otherwise;
        }

        static (List<Sample>, List<string>) Pass(Rules rules, string root, int maxTicks, uint seed, bool walks)
        {
            var fire = new ForestFire(rules, root, seed, walks);
            var w = fire.World;
            var samples = new List<Sample>(maxTicks);
            var prints = new List<string> { fire.Fingerprint() };
            for (int t = 1; t <= maxTicks; t++)
            {
                var s = new Sample();
                Tick(fire, s);
                s.At = t;
                s.Burning = w.BurningCells;
                s.Active = w.ActiveChunks.Count;
                samples.Add(s);
                if (t % GasEvery == 0) s.Gas = GasCells(w);
                if (Array.IndexOf(Checkpoints, t) >= 0) prints.Add(fire.Fingerprint());
                if (t % GasEvery == 0 && s.Burning == 0 && s.Gas == 0 && Quiet(w)) break;
            }
            prints.Add("final " + fire.Fingerprint());
            return (samples, prints);
        }

        // One tick timed: the plan, its switches, beginning, then each stage as a slice ends.
        static void Tick(ForestFire fire, Sample s)
        {
            var w = fire.World;
            long start = Stopwatch.GetTimestamp();
            var plan = fire.Plan();
            long last = Stopwatch.GetTimestamp();
            s.Times[Plan] = Ms(last - start);
            ChunkStreaming.Apply(w, plan);
            long now = Stopwatch.GetTimestamp();
            s.Times[Apply] = Ms(now - last);
            last = now;
            w.BeginStep();
            now = Stopwatch.GetTimestamp();
            s.Times[Begin] = Ms(now - last);
            last = now;
            int stage = w.TickStageNumber;
            w.StepSome(() =>
            {
                long t = Stopwatch.GetTimestamp();
                s.Times[stage] += Ms(t - last);
                last = t;
                stage = w.TickStageNumber;
                return false;
            });
            now = Stopwatch.GetTimestamp();
            s.Times[stage] += Ms(now - last);
            s.Total = Ms(now - start);
        }

        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        static int GasCells(World w) => w.Cells.Count(c => c.Gas != null && c.GasAmt > 0 && c.Gas != "fog");

        // No fire anywhere, even one lit during the last tick.
        static bool Quiet(World w) => !w.Cells.Any(c => c.Fire > 0);

        static double P(double[] sorted, double q) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * q))];

        static string Row(string name, IEnumerable<double> values)
        {
            var v = values.OrderBy(x => x).ToArray();
            return string.Format(CultureInfo.InvariantCulture, "{0,-12} mean {1,8:0.000}  p95 {2,8:0.000}  worst {3,8:0.000}  sum {4,10:0.0}", name, v.Average(), P(v, 0.95), v[v.Length - 1], v.Sum());
        }

        static void Report(List<Sample> samples)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "ticks {0}  peak burning {1} at {2}  peak active {3}  peak gas {4}",
                samples.Count, samples.Max(s => s.Burning), samples.OrderByDescending(s => s.Burning).First().At, samples.Max(s => s.Active), samples.Max(s => s.Gas)));
            Console.WriteLine(Row("tick", samples.Select(s => s.Total)));
            for (int k = 0; k < Stages.Length; k++)
            {
                int stage = k;
                Console.WriteLine(Row(Stages[k], samples.Select(s => s.Times[stage])));
            }
            var busy = samples.Where(s => s.Burning > 0).ToList();
            if (busy.Count > 0) Console.WriteLine(Row("tick (fire)", busy.Select(s => s.Total)) + "  ticks " + busy.Count);
        }

        static void WriteCsv(List<Sample> samples, string path)
        {
            var sb = new StringBuilder("tick,burning,active,gas,ms," + string.Join(",", Stages) + "\n");
            foreach (var s in samples)
                sb.Append(string.Join(",", new[] { s.At, s.Burning, s.Active, s.Gas }.Select(n => n.ToString(CultureInfo.InvariantCulture))))
                  .Append(',').Append(string.Join(",", new[] { s.Total }.Concat(s.Times).Select(n => n.ToString("0.000", CultureInfo.InvariantCulture)))).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }
    }
}
