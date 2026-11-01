// Times NetChem's Checksum on the full valley: the same calls in the same order, since
// NetChem itself needs Unity (keep this expression as NetChem.Checksum has it). One call
// every HashEvery ticks through the forest fire, as a host makes them, then at rest; and
// in fog, which puts gas on most cells. Not part of the server:
//   dotnet run -c Release --project core/Rules.Mcp -- --checksum-bench [--calls N]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Chem.Mcp
{
    public static class ChecksumBench
    {
        const int HashEvery = 30;
        const string FogOnly = "{ \"sim\": { \"weather\": { \"periodMinutes\": 60, \"rampMinutes\": 0.02, \"calmPeriods\": 0, \"meteors\": { \"showerChance\": 0 }, \"weights\": { \"clear\": 0, \"cloudy\": 0, \"windy\": 0, \"fog\": 1, \"rain\": 0, \"storm\": 0, \"snow\": 0 } } } }";

        static string Checksum(World w, WorldThings things) =>
            w.CellHash() + "/" + w.Rules.Fingerprint + "/" + things.Fingerprint + "/" + w.SkyHash() + "/" + w.DefenseHash() + "/" + w.CastHash() + "/" + w.DragonHash();

        public static int Run(string[] args)
        {
            int at = Array.IndexOf(args, "--calls");
            int calls = at >= 0 ? int.Parse(args[at + 1], CultureInfo.InvariantCulture) : 100;
            string rulesDir = Workbench.FindRulesDir(Directory.GetCurrentDirectory());
            string root = Path.GetDirectoryName(rulesDir);
            var rules = Rules.Load(rulesDir);
            var things = WorldThings.Load(Path.Combine(root, "world"));
            var fire = new ForestFire(rules, root);
            Console.WriteLine($"world {fire.World.W} x {fire.World.H}, {fire.World.Cells.Length} cells");
            Series("warm-up (not for quoting)", fire, things, 10);
            Series("through the fire and after, one call each 30 ticks", fire, things, calls);
            Back2Back("at rest, back to back", fire, things, calls);
            var fog = new ForestFire(rules.WithMods(new[] { ModPatch.Parse(FogOnly) }), root);
            Series("fog settling, with the fire", fog, things, calls);
            return 0;
        }

        static void Series(string name, ForestFire fire, WorldThings things, int calls)
        {
            var ms = new List<double>();
            var bytes = new List<long>();
            int length = 0, gas = 0, burning = 0, collections = GC.CollectionCount(0);
            for (int k = 0; k < calls; k++)
            {
                for (int t = 0; t < HashEvery; t++)
                {
                    fire.PlanChunks();
                    fire.World.Step();
                }
                burning = Math.Max(burning, fire.World.BurningCells);
                length = Call(fire.World, things, ms, bytes);
                gas = Math.Max(gas, fire.World.Cells.Count(c => c.Gas != null));
            }
            Report(name, ms, bytes, length, $"tick {fire.World.Tick}, peak burning at a call {burning}, peak gas cells at a call {gas}, collections {GC.CollectionCount(0) - collections}");
        }

        static void Back2Back(string name, ForestFire fire, WorldThings things, int calls)
        {
            var ms = new List<double>();
            var bytes = new List<long>();
            int length = 0;
            for (int k = 0; k < calls; k++) length = Call(fire.World, things, ms, bytes);
            Report(name, ms, bytes, length, $"tick {fire.World.Tick}");
            var parts = new List<double>();
            int chars = 0;
            for (int k = 0; k < calls; k++)
            {
                long start = Stopwatch.GetTimestamp();
                chars += Parts(fire.World).Length;
                parts.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "everything but CellHash, cached fingerprints aside: mean {0:0.000} ms, worst {1:0.000} ({2} chars)", parts.Average(), parts.Max(), chars));
        }

        static string Parts(World w) => w.SkyHash() + w.DefenseHash() + w.CastHash() + w.DragonHash();

        static int Call(World w, WorldThings things, List<double> ms, List<long> bytes)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            string hash = Checksum(w, things);
            long end = Stopwatch.GetTimestamp();
            bytes.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            ms.Add((end - start) * 1000.0 / Stopwatch.Frequency);
            return hash.Length;
        }

        static void Report(string name, List<double> ms, List<long> bytes, int length, string note)
        {
            var v = ms.OrderBy(x => x).ToArray();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1} calls, mean {2:0.0} ms, p95 {3:0.0}, worst {4:0.0}, best {5:0.0}, {6:0.0} MB a call, checksum {7} chars; {8}",
                name, v.Length, v.Average(), v[Math.Min(v.Length - 1, v.Length * 95 / 100)], v[v.Length - 1], v[0], bytes.Average() / 1e6, length, note));
        }
    }
}
