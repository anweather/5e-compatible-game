// holdcurve: dotnet run --project core/Hold.Runner -c Release -- [--sets fire+water,air+air] [--peers 1,2] [--seeds 3] [--minutes 40]
//   [--moment name[,name]] prints, per hand set and peer count, the wave reached, the run's time and the keep's health at each wave.
//   --idle has the wizards stand on their posts and never cast (the keep left to itself), --no-upgrades plays with none offered, --points N as a player with that many points (0: a fresh one), and --journey as a new
//   player from nothing, run after run, until everything's unlocked.
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
        static int? points;
        static bool idle;

        // A new player: fire in the right hand, then whatever the unlocks give, spending each run's points on the next.
        static int Journey(Rules rules, string worldDir, int minutes)
        {
            var p = rules.Sim.Hold;
            int have = 0, total = p.Unlocks.Sum(u => u.Cost), runs = 0;
            double seconds = 0;
            Console.WriteLine($"all the unlocks cost {total} points");
            Console.WriteLine("run  wave  minutes  points  unlocked");
            while (have < total)
            {
                runs++;
                var run = new HoldRun(rules, worldDir, (uint)runs, 1, (Second(rules, have), "fire"), null, have);
                run.Play(minutes * 60);
                have = run.Points;
                seconds += run.Ticks / (double)rules.Sim.TicksPerSecond;
                int unlocked = run.World.UnlocksOf("bot0").Count();
                Console.WriteLine($"{runs,-4} {run.LastWave,-5} {run.Ticks / (rules.Sim.TicksPerSecond * 60.0),-8:0.0} {have,-7} {unlocked}/{p.Unlocks.Count}");
            }
            Console.WriteLine($"everything in {runs} runs, {seconds / 3600:0.0} hours of play");
            return 0;
        }

        // What a new player holds in the second hand: water once it has it, or fire.
        static string Second(Rules rules, int have)
        {
            foreach (var u in rules.Sim.Hold.Unlocks)
            {
                if ((have -= u.Cost) < 0) return "fire";
                if (u.Element == "water") return "water";
            }
            return "fire";
        }

        static int Main(string[] args)
        {
            var folders = GameFolders.InRepo(Environment.CurrentDirectory);
            var rules = Rules.Load(folders.Rules);
            string Arg(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
            var sets = Arg("--sets", "fire+water,fire+air,earth+air,water+air,fire+fire").Split(',').Select(s => s.Split('+')).ToList();
            var peers = Arg("--peers", "1,2").Split(',').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToList();
            int seeds = int.Parse(Arg("--seeds", "3"), CultureInfo.InvariantCulture), minutes = int.Parse(Arg("--minutes", "40"), CultureInfo.InvariantCulture);
            string moments = Arg("--moment", null);
            if (args.Contains("--no-upgrades")) rules.Sim.Hold.Offer = 0;
            idle = args.Contains("--idle");
            if (args.Contains("--journey")) return Journey(rules, folders.World, minutes);
            string given = Arg("--points", null);
            points = given == null ? (int?)null : int.Parse(given, CultureInfo.InvariantCulture);
            return moments != null ? Moments(rules, folders.World, moments.Split(','), seeds, minutes) : Curve(rules, folders.World, sets, peers, seeds, minutes);
        }

        static int Curve(Rules rules, string worldDir, List<string[]> sets, List<int> peers, int seeds, int minutes)
        {
            Console.WriteLine($"target {rules.Sim.Hold.TargetMinutes} min, {rules.Sim.Hold.Waves} waves (W: all beaten); {seeds} seeds each{(idle ? "; idle wizards" : "")}");
            Console.WriteLine("hands         peers  wave reached (each seed)  minutes (each seed)  mean min");
            var jobs = (from set in sets from p in peers select (set, p)).ToList();
            var lines = new string[jobs.Count];
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = 6 }, j =>
            {
                var (set, p) = jobs[j];
                var runs = Enumerable.Range(1, seeds).AsParallel().AsOrdered().Select(seed =>
                {
                    var run = new HoldRun(rules, worldDir, (uint)seed, p, (set[0], set[1]), null, points) { Idle = idle };
                    run.Play(minutes * 60);
                    if (run.World.HoldRunning) Console.Error.WriteLine($"still going: {set[0]}+{set[1]} seed {seed} wave {run.LastWave}, by the heart {string.Join(" ", Around(run.World))}; {string.Join(" ", run.World.Raiders.Take(6).Select(r => $"{r.Kind}@{r.X},{r.Y} hp {r.Hp}"))}");
                    return run;
                }).ToList();
                double tps = rules.Sim.TicksPerSecond * 60.0;
                lines[j] = $"{set[0]}+{set[1],-12} {p,-5}  {string.Join(" ", runs.Select(r => r.Outcome)),-24}  {string.Join(" ", runs.Select(r => (r.Ticks / tps).ToString("0.0", CultureInfo.InvariantCulture))),-19}  {runs.Average(r => r.Ticks / tps):0.0}";
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
                    var run = new HoldRun(rules, worldDir, (uint)seed, 1, (m.Holds[HandsDef.Left], m.Holds[HandsDef.Right]), name);
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
