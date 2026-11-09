using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What each stage of a tick costs in the core, on FogTickBench's valley (65 active chunks, the
    /// same seed) under clear weather, storm and fog: the tick is run a slice at a time (StepSome) and
    /// each slice's time goes to the stage it ran. Not a check and not in CI (Explicit): run it by
    /// name in Release and read the table, or the file STAGE_BENCH_OUT names.
    /// <code>dotnet test core/Game.Core.sln -c Release --filter "FullyQualifiedName~TickStageBench" --logger "console;verbosity=detailed"</code>
    /// </summary>
    [Explicit("a benchmark: run it by name")]
    [Category("Simulation")]
    public class TickStageBench
    {
        const int SettleTicks = 600, MeasuredTicks = 300;
        // World.TickStageNumber: 1 burn (with the tick's start), 2 water check, 3 springs, 4 flow, 5 gas,
        // 6 the rest (weather, raiders, defense ...), 7 timers, 8 dragon, 9 end.
        static readonly string[] Stages = { "", "burn", "check", "springs", "flow", "gas", "rest", "timers", "dragon", "end" };
        static readonly Func<bool> OneSlice = () => true;

        static string Run(string weather)
        {
            var w = FogTickBench.Valley(FogTickBench.Weather(weather));
            for (int i = 0; i < SettleTicks; i++) w.Step();
            var ticks = new long[Stages.Length];
            long total = 0;
            for (int i = 0; i < MeasuredTicks; i++)
            {
                bool done = false;
                while (!done)
                {
                    int stage = Math.Max(1, w.TickStageNumber);
                    long at = Stopwatch.GetTimestamp();
                    done = w.StepSome(OneSlice);
                    long spent = Stopwatch.GetTimestamp() - at;
                    ticks[stage] += spent;
                    total += spent;
                }
            }
            double Ms(long t) => t * 1000.0 / Stopwatch.Frequency / MeasuredTicks;
            var parts = Enumerable.Range(1, Stages.Length - 1).Select(s => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.00}", Stages[s], Ms(ticks[s])));
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.00} ms a tick; {2}; hash {3}, rng {4}",
                weather, Ms(total), string.Join(", ", parts), w.CellHash(), w.RngState);
        }

        [Test]
        public void EachStageUnderClearStormAndFog()
        {
            // Twice, in case the first pass pays for the JIT: the second is the one to quote.
            var lines = new List<string>();
            for (int pass = 0; pass < 2; pass++)
                foreach (var weather in new[] { "clear", "storm", "fog" })
                    lines.Add(Run(weather));
            string text = string.Join("\n", lines);
            string file = Environment.GetEnvironmentVariable("STAGE_BENCH_OUT");
            if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, text + "\n");
            TestContext.Progress.WriteLine(text);
            Assert.That(lines, Has.Count.EqualTo(6));
        }
    }
}
