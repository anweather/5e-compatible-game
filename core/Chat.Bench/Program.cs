// chatbench (docs/design/chat-telemetry.md): five fixed prompts, each on a fresh world, through the
// arguments, stdin and folder the game's chat uses, against the rules MCP server on a copy of rules/
// and world/, so nothing it does reaches the repo. The one difference from the game: the server is
// over stdio with a world in memory and no view, so "in the world" can't be measured and the nearest
// mark is `applied`, when the room's answer came back. It spends real tokens: run it by hand.
//
//   dotnet run --project core/Chat.Bench -c Release -- [--model sonnet] [--runs 3] [--out <folder>]
//
// Each run's record goes to <folder>/chat-log-<date>.jsonl (default a new folder in the temp folder),
// the form tools/chat-report.mjs reads. Run it before and after, three times each: one run varies.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Chem.Mcp;
using Newtonsoft.Json.Linq;

namespace Chem.Bench
{
    public static class Program
    {
        /// <summary>A colour, a rule, a spell, a house, and one that must be refused.</summary>
        public static readonly (string kind, string prompt)[] Prompts =
        {
            ("colour", "make all fire blue"),
            ("rule", "make grass burn twice as fast"),
            ("spell", "add a cantrip that throws a small ball of frost"),
            ("house", "build a small house near hollowmere"),
            ("refused", "make the player twice as tall"),
        };

        /// <summary>As ClaudeChat's watchdog.</summary>
        const int MostSeconds = 240;

        const int StopWaitMs = 2000;

        public static int Main(string[] args)
        {
            string model = Option(args, "--model"), folder = Option(args, "--out") ?? Path.Combine(Path.GetTempPath(), "chatbench-" + Guid.NewGuid().ToString("N"));
            int runs = int.Parse(Option(args, "--runs") ?? "3", CultureInfo.InvariantCulture);
            string claude = ClaudeRun.Find(Environment.GetEnvironmentVariable("PATH"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists);
            if (claude == null)
            {
                Console.Error.WriteLine("claude isn't installed");
                return 2;
            }
            string rules = Workbench.FindRulesDir(Directory.GetCurrentDirectory());
            var records = new List<JObject>();
            for (int run = 0; run < runs; run++)
                foreach (var (_, prompt) in Prompts)
                {
                    var record = Run(claude, model, rules, prompt);
                    ChatLog.Append(folder, DateTime.UtcNow, record);
                    records.Add(record);
                    Console.WriteLine(Row(record));
                }
            Console.WriteLine();
            Console.WriteLine(Table(records));
            Console.WriteLine($"Records in {folder}");
            return 0;
        }

        static string Option(string[] args, string name)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        static JObject Run(string claude, string model, string rules, string prompt)
        {
            string dir = Path.Combine(Path.GetTempPath(), "chatbench-run-" + Guid.NewGuid().ToString("N"));
            string folder = Path.Combine(dir, "chat"), applied = Path.Combine(dir, "applied.jsonl");
            try
            {
                Directory.CreateDirectory(folder);
                Copy(rules, Path.Combine(dir, "rules"));
                Copy(Path.Combine(rules, "..", "world"), Path.Combine(dir, "world"));
                WriteFolder(folder, Path.Combine(dir, "rules"), applied);
                return Follow(claude, model, folder, prompt, applied);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        static void Copy(string from, string to)
        {
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string there = Path.Combine(to, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(there));
                File.Copy(file, there);
            }
        }

        // The folder as ClaudeTerminal.WriteFolder makes it, with the server over stdio in place of the game's url.
        static void WriteFolder(string folder, string rules, string applied)
        {
            string server = Path.Combine(AppContext.BaseDirectory, "Rules.Mcp.dll");
            var config = new JObject
            {
                ["mcpServers"] = new JObject
                {
                    [ClaudeRun.ServerName] = new JObject
                    {
                        ["type"] = "stdio",
                        ["command"] = "dotnet",
                        ["args"] = new JArray(server, "--rules", rules, "--applied", applied),
                    },
                },
            };
            File.WriteAllText(Path.Combine(folder, ".mcp.json"), config.ToString());
            File.WriteAllText(Path.Combine(folder, "CLAUDE.md"), ClaudeRun.Guide);
        }

        static ProcessStartInfo StartOf(string claude, string model, string folder)
        {
            string arguments = ClaudeRun.CommandLine(ClaudeRun.Arguments().Concat(model == null ? Array.Empty<string>() : new[] { "--model", model }));
            bool script = claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            var start = new ProcessStartInfo(script ? "cmd.exe" : claude, script ? $"/c \"\"{claude}\" {arguments}\"" : arguments)
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            start.Environment.Remove("CLAUDECODE"); // a bench run from inside Claude Code is still a player's top-level claude
            return start;
        }

        // As ClaudeChat.Follow: the message on stdin, each line out with its clock, a watchdog. Then the record.
        static JObject Follow(string claude, string model, string folder, string prompt, string applied)
        {
            var clock = Stopwatch.StartNew();
            var who = new ChatWho { Id = Guid.NewGuid().ToString("N"), GameVersion = "chatbench", Commit = Commit(), RoomMode = "open", Players = 1 };
            var recorder = new ChatRecorder(prompt, "bench", clock.ElapsedMilliseconds, who);
            using var process = Process.Start(StartOf(claude, model, folder));
            recorder.Started(clock.ElapsedMilliseconds);
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            bool timedOut = false;
            using var watchdog = new Timer(_ =>
            {
                timedOut = true;
                process.Kill(true);
            }, null, MostSeconds * 1000, Timeout.Infinite);
            var input = Encoding.UTF8.GetBytes(prompt);
            process.StandardInput.BaseStream.Write(input, 0, input.Length);
            process.StandardInput.Close();
            string line;
            while ((line = process.StandardOutput.ReadLine()) != null) recorder.Line(line, clock.ElapsedMilliseconds);
            process.WaitForExit(StopWaitMs);
            recorder.Ended(clock.ElapsedMilliseconds, timedOut ? ChatOutcome.TimedOut : null);
            if (File.Exists(applied))
                foreach (string sent in File.ReadAllLines(applied))
                {
                    var o = JObject.Parse(sent);
                    recorder.Patched((string)o["mod"], (string)o["patch"]);
                }
            return recorder.Record();
        }

        static string Commit()
        {
#pragma warning disable S4036 // git from the PATH, as a terminal finds it
            var start = new ProcessStartInfo("git", "rev-parse --short HEAD") { RedirectStandardOutput = true, UseShellExecute = false };
#pragma warning restore S4036
            using var git = Process.Start(start);
            string commit = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();
            return commit;
        }

        static string Seconds(JToken ms) => ms == null || ms.Type == JTokenType.Null ? "-" : ((double)ms / 1000).ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>One run in a line: the design's numbers, all from its record.</summary>
        public static string Row(JObject r)
        {
            var at = r["at"];
            var split = r["split"];
            var tokens = r["tokens"];
            var tools = (JArray)r["tools"];
            int refused = tools.Count(t => (bool?)t["game"] == true && (bool?)t["ok"] != true); // a Read of notes.md that isn't there yet is no refusal
            string cost = ((double?)tokens["cost"] ?? 0).ToString("0.0000", CultureInfo.InvariantCulture);
            return $"{r["prompt"],-48} {r["outcome"],-8} ready {Seconds(at["claudeReady"]),5} word {Seconds(at["firstWord"]),5} tool {Seconds(at["firstTool"]),5} apply {Seconds(at["firstApply"]),5} applied {Seconds(at["applied"]),5} done {Seconds(at["done"]),5}"
                + $" | start {Seconds(split["startingUp"]),4} model {Seconds(split["model"]),5} tools {Seconds(split["tools"]),5} left {Seconds(split["leftOver"]),5}"
                + $" | in {tokens["input"]} write {tokens["cacheWrite"]} read {tokens["cacheRead"]} out {tokens["output"]} ${cost} {tokens["turns"]} turns {tools.Count} tools {refused} refused";
        }

        /// <summary>Each prompt's median over its runs: done, to applied, tokens written and output, cost.</summary>
        public static string Table(IReadOnlyList<JObject> records)
        {
            var table = new StringBuilder("Prompt, runs, outcomes, then medians: done s, applied s, output tokens, cost\n");
            foreach (var group in records.GroupBy(r => (string)r["prompt"]))
            {
                string outcomes = string.Join("/", group.Select(r => (string)r["outcome"]).Distinct());
                string done = Median(group.Select(r => (double?)r["at"]["done"] / 1000)), applied = Median(group.Select(r => (double?)r["at"]["applied"] / 1000));
                string output = Median(group.Select(r => (double?)r["tokens"]["output"])), cost = Median(group.Select(r => (double?)r["tokens"]["cost"]));
                table.Append($"{group.Key}, {group.Count()}, {outcomes}, {done}, {applied}, {output}, {cost}\n");
            }
            return table.ToString().TrimEnd();
        }

        static string Median(IEnumerable<double?> values)
        {
            var sorted = values.Where(v => v != null).Select(v => v.Value).OrderBy(v => v).ToList();
            if (sorted.Count == 0) return "-";
            int mid = sorted.Count / 2;
            double median = sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
            return median.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
