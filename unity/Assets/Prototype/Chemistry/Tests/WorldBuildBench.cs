using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What building the Scale Island's World allocates, phase by phase, on the path a player takes (ChemGrid.StartWorld,
    /// then the valley's land): cells.txt read whole, its BakedSource, MakeFrom, the lays, KeepStart, land.json read and
    /// set. Each phase's bytes allocated (garbage too: a player's Mono heap keeps what it grew to) and what's live after
    /// it. The island's bake is SparseWorldBench's, made once (~20 s). Not a check and not in CI (Explicit):
    /// <code>dotnet test core -c Release --filter "FullyQualifiedName~WorldBuildBench" --logger "console;verbosity=detailed"</code>
    /// </summary>
    [Explicit("a benchmark: run it by name")]
    [Category("Simulation")]
    public class WorldBuildBench
    {
        static Rules R => ChemistryTests.R;

        sealed class Phases
        {
            readonly List<string> lines = new List<string>();
            readonly long before = Live();
            long allocated = Allocated();
            readonly Stopwatch watch = Stopwatch.StartNew();

            public void Mark(string phase)
            {
                long now = Allocated(), made = now - allocated, live = Live() - before;
                allocated = now;
                lines.Add($"BUILD {phase,-28} {watch.Elapsed.TotalMilliseconds,7:0} ms  allocated {Mb(made),8:0.0} MB  live after {Mb(live),8:0.0} MB");
                watch.Restart();
            }

            // Bytes allocated so far. Unity's .NET profile has no GC.GetTotalAllocatedBytes (dotnet has), so it's found by
            // reflection, and is 0 in the Editor, where only the live column means anything.
            static readonly System.Reflection.MethodInfo TotalAllocated =
                typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) });

            static long Allocated() => TotalAllocated == null ? 0 : (long)TotalAllocated.Invoke(null, new object[] { true });

            // What's live on the heap, collected first: a measuring test's only use of the collector (as ScaleIslandTests).
#pragma warning disable S1215
            static long Live() => GC.GetTotalMemory(true);
#pragma warning restore S1215

            public void Print()
            {
                foreach (var line in lines) TestContext.Out.WriteLine(line);
            }
        }

        static double Mb(long bytes) => bytes / 1024.0 / 1024.0;

        // ChemGrid.CellMap and MapWorld: cells.txt read whole, then its source.
        static BakedSource FromWholeText(string dir, Phases at)
        {
            string text = MapFile.ReadCells(dir);
            at.Mark("cells.txt read whole");
            return BakedSource.FromText(text, R);
        }

        // ValleyChemistry.SetLand: land.json read whole and set (the view keeps its array, landFromFile).
        static void SetViewsLand(World w, string dir, MapFile map, Phases at)
        {
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H);
            at.Mark("land.json read (the view)");
            w.SetLand(land);
            at.Mark("SetLand");
        }

        [Test]
        public void TheIslandsBuildByPhase()
        {
            string dir = SparseWorldBench.IslandBaked();
            Assert.That(R.Fingerprint, Is.Not.Empty, "the rules loaded before the heap is read, so they aren't counted");
            var map = MapFile.Load(dir);
            var at = new Phases();
            var source = FromWholeText(dir, at);
            at.Mark("BakedSource.FromText");
            var w = new World(R, source.W, source.H, 7);
            at.Mark("new World");
            w.SetLandRange(map.LandRange);
            w.MakeFrom(source);
            at.Mark("MakeFrom");
            WorldPuzzles.Load(dir).LayInto(w);
            MapStamps.Load(dir).LayInto(w);
            at.Mark("puzzles and stamps");
            WorldBuildings.Load(dir).LayInto(w);
            at.Mark("rows (buildings)");
            if (GolfCommand.Load(dir) is { } hole) w.ApplyGolf(hole);
            at.Mark("golf");
            if (WorldHold.Load(dir) is { } hold && w.InBounds(hold.Heart.x, hold.Heart.y)) w.SetHold(hold);
            at.Mark("the Hold (places.json)");
            w.KeepStart();
            at.Mark("KeepStart");
            SetViewsLand(w, dir, map, at);
            at.Mark("the view's land let go");
            at.Print();
            TestContext.Out.WriteLine($"BUILD the island {w.W} x {w.H}, {w.ChunkCoords.Count} chunks, {w.UnpackedCount} unpacked; "
                + $"the source {Mb(source.Bytes):0.0} MB");
            GC.KeepAlive(w);
            Assert.Pass();
        }
    }
}
