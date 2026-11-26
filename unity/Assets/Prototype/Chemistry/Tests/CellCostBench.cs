// What a cell costs a tick and a byte on the nine and the hills (#168): a walk east with a fire-bolt a second, timed tick by tick, the heap in play and the lockstep check's cost beside it; run by name in Release: dotnet test core -c Release --filter "FullyQualifiedName~CellCostBench" --logger "console;verbosity=detailed".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Explicit("a benchmark: run it by name")]
    [Category("Simulation")]
    public class CellCostBench
    {
        static Rules R => ChemistryTests.R;

#pragma warning disable S1215
        static long Heap() => GC.GetTotalMemory(true);
#pragma warning restore S1215

        static World Hills(out (int x, int y) start)
        {
            string map = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "hills-12x10");
            string rows = Path.Combine(Path.GetTempPath(), "cell-cost-bench", "hills-12x10");
            var made = GeneratedMap.Load(map);
            if (!File.Exists(Path.Combine(rows, MapFile.CellsName))) made.WriteRowsOnly(rows);
            var w = GeneratedSource.MapWorld(new GeneratedSource(made, R), rows, R, 7, out start);
            w.KeepStart();
            return w;
        }

        static World Nine(out (int x, int y) start)
        {
            string dir = SparseWorldBench.Baked();
            start = MapFile.Load(dir).Spawn;
            return SparseWorldBench.Build(dir, R, out _);
        }

        static double P95(List<double> times) => times.OrderBy(t => t).ElementAt((int)(times.Count * 0.95));

        [TestCase("nine")]
        [TestCase("hills")]
        public void AWalksTicksAndBytes(string which)
        {
            Assert.That(R.Fingerprint, Is.Not.Empty);
            long before = Heap();
            (int x, int y) start;
            var w = which == "nine" ? Nine(out start) : Hills(out start);
            var planner = new ChunkPlanner();
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { start }));
            var baseline = Stopwatch.StartNew();
            w.RefreshChunkHashes();
            TestContext.Out.WriteLine($"CELLCOST {which}: the hashes' baseline, every chunk once, {baseline.Elapsed.TotalMilliseconds:0.0} ms");
            const int warm = 120, ticks = 1800;
            var sim = new List<double>();
            var check = new List<double>();
            var rolling = new RollingHash();
            for (int t = 0; t < warm + ticks; t++)
            {
                var me = new[] { (Math.Min(start.x + t / 2, w.W - 1), start.y) };
                if (t % 60 == 30) w.Cast("fire-bolt", me[0].Item1 + 6, me[0].y, 1);
                long at = Stopwatch.GetTimestamp();
                ChunkStreaming.Apply(w, planner.Plan(w, me));
                w.Step();
                long stepped = Stopwatch.GetTimestamp();
                Check(w, rolling, t + 1);
                long checkedAt = Stopwatch.GetTimestamp();
                if (t < warm) continue;
                sim.Add((stepped - at) * 1000.0 / Stopwatch.Frequency);
                check.Add((checkedAt - stepped) * 1000.0 / Stopwatch.Frequency);
            }
            long bytes = Heap() - before;
            TestContext.Out.WriteLine($"CELLCOST {which} {w.W} x {w.H}: tick mean {sim.Average():0.000} ms p95 {P95(sim):0.000} ms; "
                + $"check mean {check.Average():0.000} ms p95 {P95(check):0.000} ms max {check.Max():0.000} ms; "
                + $"{w.ActiveChunks.Count} active, {w.UnpackedCount} unpacked; heap {bytes} bytes ({bytes / 1024.0 / 1024.0:0.0} MB)");
            GC.KeepAlive(w);
            Assert.Pass();
        }

        static void Check(World w, RollingHash rolling, int tick)
        {
            if (tick % RollingHash.Every != 0) return;
            w.RefreshChunkHashes();
            Assert.That(w.ChunkDigestHash(), Is.Not.Empty);
            GC.KeepAlive(rolling);
        }
    }
}
