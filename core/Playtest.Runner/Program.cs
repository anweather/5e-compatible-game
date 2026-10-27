using System;
using System.Collections.Generic;
using System.Globalization;

// The playtest runner's command line (playtest/DESIGN.md, J-013):
//
//   playtest check                      what the preflight says, and which player and build it would run
//   playtest run [--seed N] [--seconds N] [--replay run.json] [--until-frame N] [--force]
//   playtest build [--force]            builds the bot's player in .claude/worktrees/playtest-build
//
// Run it with `dotnet core/Playtest.Runner/bin/<config>/net8.0/playtest.dll <command>`
// from anywhere in the repository.
namespace Playtest.Runner
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: playtest check | run [--seed N] [--seconds N] [--replay run.json] [--until-frame N] [--force] | build [--force]");
                return 2;
            }
            var options = Options(args);
            var paths = Paths.Find(Environment.CurrentDirectory);
            switch (args[0])
            {
                case "check": return Check(paths);
                case "run": return PlaytestRun.Run(paths, options);
                case "build": return PlaytestBuild.Build(paths, options.ContainsKey("force"));
                default:
                    Console.Error.WriteLine($"unknown command {args[0]}");
                    return 2;
            }
        }

        static int Check(Paths paths)
        {
            Console.WriteLine($"repository:  {paths.Root}");
            Console.WriteLine($"player:      {paths.Player()}");
            Console.WriteLine($"build:       {Paths.BuildCommit(paths.Player())}");
            string why = Preflight.Why(Preflight.Now(), 8);
            Console.WriteLine($"preflight:   {why ?? "clear"}");
            return 0;
        }

        /// <summary>--name value pairs and --flag switches after the command.</summary>
        public static Dictionary<string, string> Options(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 1;
            while (i < args.Length)
            {
                string arg = args[i++];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
                bool hasValue = i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal);
                options[arg.Substring(2)] = hasValue ? args[i++] : "true";
            }
            return options;
        }

        public static int IntOption(Dictionary<string, string> options, string name, int fallback) =>
            options.TryGetValue(name, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
    }
}
