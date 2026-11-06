// holdcurve: dotnet run --project core/Hold.Runner -c Release -- [--sets fire+water,air+air] [--peers 1,2] [--seeds 3] [--minutes 40]
//   [--moment name[,name]] prints, per hand set and peer count, the wave reached, the run's time and the keep's health at each wave.
//   With --moment it plays just that wave, from the moment's own hands unless --sets says otherwise, and prints how it went.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Chem.HoldCurve
{
    static class Program
    {
        static int Main(string[] args)
        {
            var folders = GameFolders.InRepo(Environment.CurrentDirectory);
            var rules = Rules.Load(folders.Rules);
            string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
            var sets = Arg("--sets", "fire+water,fire+air,earth+air,water+air,fire+fire").Split(',').Select(s => s.Split('+')).ToList();
            var peers = Arg("--peers", "1,2").Split(',').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToList();
            int seeds = int.Parse(Arg("--seeds", "3"), CultureInfo.InvariantCulture), minutes = int.Parse(Arg("--minutes", "40"), CultureInfo.InvariantCulture);
            string moments = Arg("--moment", null);
            return moments != null ? Moments(rules, folders.World, moments.Split(','), seeds, minutes) : Curve(rules, folders.World, sets, peers, seeds, minutes);
        }

        static int Curve(Rules rules, string worldDir, List<string[]> sets, List<int> peers, int seeds, int minutes)
        {
            Console.WriteLine($"target {rules.Sim.Hold.TargetMinutes} min; {seeds} seeds each");
            Console.WriteLine("hands         peers  wave reached (each seed)  minutes (each seed)  mean min");
            var jobs = (from set in sets from p in peers select (set, p)).ToList();
            var lines = new string[jobs.Count];
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = 6 }, j =>
            {
                var (set, p) = jobs[j];
                var runs = Enumerable.Range(1, seeds).AsParallel().AsOrdered().Select(seed =>
                {
                    var run = new HoldRun(rules, worldDir, (uint)seed, p, set[0], set[1]);
                    run.Play(minutes * 60);
                    if (run.World.HoldRunning) Console.Error.WriteLine($"still going: {set[0]}+{set[1]} seed {seed} wave {run.LastWave}, by the heart {string.Join(" ", Around(run.World))}; {string.Join(" ", run.World.Raiders.Take(6).Select(r => $"{r.Kind}@{r.X},{r.Y} hp {r.Hp}"))}");
                    return run;
                }).ToList();
                double tps = rules.Sim.TicksPerSecond * 60.0;
                lines[j] = $"{set[0]}+{set[1],-12} {p,-5}  {string.Join(" ", runs.Select(r => r.LastWave)),-24}  {string.Join(" ", runs.Select(r => (r.Ticks / tps).ToString("0.0", CultureInfo.InvariantCulture))),-19}  {runs.Average(r => r.Ticks / tps):0.0}";
                Console.WriteLine(lines[j]);
            });
            return 0;
        }

        // What stands round the heart: a run that stalls has usually lost its way to it.
        static IEnumerable<string> Around(World w)
        {
            var (x, y) = w.HoldSite.Heart;
            for (int dy = -2; dy <= 2; dy++) yield return string.Join(",", Enumerable.Range(-2, 5).Select(dx => w.At(x + dx, y + dy).T));
        }

        static int Moments(Rules rules, string worldDir, string[] names, int seeds, int minutes)
        {
            Console.WriteLine("moment                keep lost (each seed)  seconds (each seed)  passed");
            foreach (var name in names)
            {
                var m = rules.Sim.Hold.Moment(name);
                if (m == null)
                {
                    Console.Error.WriteLine("no moment " + name);
                    return 1;
                }
                var runs = Enumerable.Range(1, seeds).AsParallel().AsOrdered().Select(seed =>
                {
                    var run = new HoldRun(rules, worldDir, (uint)seed, 1, m.Holds[HandsDef.Left], m.Holds[HandsDef.Right], name);
                    run.Play(minutes * 60, m.Wave);
                    return run;
                }).ToList();
                int start = m.Health > 0 ? m.Health : rules.Sim.Hold.Health;
                int tps = rules.Sim.TicksPerSecond;
                Console.WriteLine($"{name,-21} {string.Join(" ", runs.Select(r => start - Math.Max(0, r.World.Hold.Health))),-22} {string.Join(" ", runs.Select(r => r.Ticks / tps)),-20} {runs.Count(r => r.LastWave > m.Wave)}/{seeds}");
            }
            return 0;
        }
    }
}
