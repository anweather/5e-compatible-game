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
    /// What fog weather costs a tick in the core: the valley's size, hills and water (world/land.json,
    /// open grass otherwise), 65 active chunks, the same seed, under clear weather and then fog. Not a
    /// check and not in CI (Explicit): run it by name in Release and read the last line, or the file
    /// FOG_BENCH_OUT names.
    /// <code>dotnet test core/Game.Core.sln -c Release --filter "FullyQualifiedName~FogTickBench" --logger "console;verbosity=detailed"</code>
    /// </summary>
    [Explicit("a benchmark: run it by name")]
    public class FogTickBench
    {
        const int Size = 576, ActiveChunks = 65, SettleTicks = 600, MeasuredTicks = 600;
        const uint Seed = 1337;

        static Rules Weather(string only)
        {
            var kinds = new[] { "clear", "cloudy", "windy", "fog", "rain", "storm", "snow" };
            string weights = string.Join(", ", kinds.Select(k => $"'{k}': {(k == only ? 1 : 0)}"));
            return ChemistryTests.R.WithMods(new[] { ModPatch.Parse(
                "{ 'sim': { 'weather': { 'periodMinutes': 60, 'rampMinutes': 0.02, 'calmPeriods': 0, 'meteors': { 'showerChance': 0 }, 'weights': { " + weights + " } } } }") });
        }

        /// <summary>The bench's map: water where land.json has none (a lake, the river, the sea), grass on its hills elsewhere.</summary>
        static World Valley(Rules rules)
        {
            string path = Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir()), "world", "land.json");
            var land = LandFile.Read(File.ReadAllText(path), Size, Size);
            var w = new World(rules, Size, Size, Seed, "grass");
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (land[w.Idx(x, y)] != World.NoLand) continue;
                    w.Paint(x, y, "pit");
                    w.AddWater(x, y, 8);
                }
            w.SetLand(land);
            w.SetAllActive(false);
            var near = w.ChunkCoords.OrderBy(c => Far(c.cx) + Far(c.cy)).ThenBy(c => c.cy).ThenBy(c => c.cx).Take(ActiveChunks).ToList();
            foreach (var (cx, cy) in near) w.SetActive(cx, cy, true);
            return w;
        }

        static int Far(int c) => (c - 4) * (c - 4);

        static (double mean, double p95) Time(World w)
        {
            var ms = new List<double>(MeasuredTicks);
            for (int i = 0; i < MeasuredTicks; i++)
            {
                long at = Stopwatch.GetTimestamp();
                w.Step();
                ms.Add((Stopwatch.GetTimestamp() - at) * 1000.0 / Stopwatch.Frequency);
            }
            ms.Sort();
            return (ms.Average(), ms[ms.Count * 95 / 100]);
        }

        static string Run(string weather)
        {
            var w = Valley(Weather(weather));
            int peak = 0;
            for (int i = 0; i < SettleTicks; i++)
            {
                w.Step();
                if (i % 30 == 0) peak = Math.Max(peak, Fog(w));
            }
            var (mean, p95) = Time(w);
            peak = Math.Max(peak, Fog(w));
            int open = 0;
            foreach (var (cx, cy) in w.ActiveChunks)
                for (int y = 0; y < Chunks.Size; y++)
                    for (int x = 0; x < Chunks.Size; x++)
                        if (!w.IsBlocking(w.At(cx * Chunks.Size + x, cy * Chunks.Size + y))) open++;
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.00} ms a tick (p95 {2:0.00}), {3} fog cells at peak of {4} open ({5:0.0}%), rng {6}",
                weather, mean, p95, peak, open, peak * 100.0 / open, w.RngState);
        }

        static int Fog(World w)
        {
            int n = 0;
            foreach (var (cx, cy) in w.ActiveChunks)
                for (int y = 0; y < Chunks.Size; y++)
                    for (int x = 0; x < Chunks.Size; x++)
                        if (w.At(cx * Chunks.Size + x, cy * Chunks.Size + y).Gas == "fog") n++;
            return n;
        }

        [Test]
        public void ClearThenFog()
        {
            // Twice, in case the first pass pays for the JIT: the second is the one to quote.
            var lines = new List<string>();
            for (int pass = 0; pass < 2; pass++)
            {
                lines.Add(Run("clear"));
                lines.Add(Run("fog"));
            }
            string text = string.Join("\n", lines);
            string file = Environment.GetEnvironmentVariable("FOG_BENCH_OUT");
            if (!string.IsNullOrEmpty(file)) File.WriteAllText(file, text + "\n");
            TestContext.Progress.WriteLine(text);
            Assert.That(lines, Has.Count.EqualTo(4));
        }
    }
}
