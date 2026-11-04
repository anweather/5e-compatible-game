// The cheap playtester's text world (playtest/cheap/README.md).
//   Playtest.Text serve --seed N --minutes M --out <folder>    an MCP server on stdio
//   Playtest.Text replay <commands.jsonl> [--seed N]           the same commands again: does the world agree?
//                                                              a line with no "state" is printed with its answer instead
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Chem.PlaytestText
{
    [McpServerToolType]
    public sealed class Tools
    {
        readonly Desk desk;

        public Tools(Desk desk)
        {
            this.desk = desk;
        }

        [McpServerTool(Name = "places"), Description("The valley's named places and about where each is.")]
        public string Places() => desk.Run("places", new JObject());

        [McpServerTool(Name = "spells"), Description("Every spell: id, level, shape, element, what it says it does.")]
        public string Spells() => desk.Run("spells", new JObject());

        [McpServerTool(Name = "map"), Description("A text map round a cell, one character a cell, r up to 10. x and y default to where you stand.")]
        public string Map(int? x = null, int? y = null, int r = 6) => desk.Run("map", At(x, y, new JObject { ["r"] = r }));

        [McpServerTool(Name = "cell"), Description("Everything about one cell.")]
        public string Cell(int? x = null, int? y = null) => desk.Run("cell", At(x, y, new JObject()));

        [McpServerTool(Name = "census"), Description("Counts of terrain, covers, gases, liquids and burning cells in a square round a cell, r up to 32.")]
        public string Census(int? x = null, int? y = null, int r = 8) => desk.Run("census", At(x, y, new JObject { ["r"] = r }));

        [McpServerTool(Name = "rules"), Description("The rule rows naming an id (a terrain, cover, gas, liquid, spell or element): what should happen, to check against what did.")]
        public string Rules(string id) => desk.Run("rules", new JObject { ["id"] = id });

        [McpServerTool(Name = "go"), Description("Walk to a cell. Nothing else happens on the way; chunks near you stay active, and a far world stands still.")]
        public string Go(int x, int y) => desk.Run("go", new JObject { ["x"] = x, ["y"] = y });

        [McpServerTool(Name = "cast"), Description("Cast a spell at a cell. dir 0-7 for lines and cones; charge 1-3. Then wait to see what comes of it.")]
        public string Cast(string spell, int? x = null, int? y = null, int dir = 0, int charge = 1) =>
            desk.Run("cast", At(x, y, new JObject { ["spell"] = spell, ["dir"] = dir, ["charge"] = charge }));

        [McpServerTool(Name = "wait"), Description("Let the world run this many ticks, up to 300 (6 ticks a second). Says what the log said.")]
        public string Wait(int ticks = 30) => desk.Run("wait", new JObject { ["ticks"] = ticks });

        [McpServerTool(Name = "spawn"), Description("Puts a creature of an entity kind on a cell, to cast at.")]
        public string Spawn(string kind, int x, int y) => desk.Run("spawn", new JObject { ["kind"] = kind, ["x"] = x, ["y"] = y });

        [McpServerTool(Name = "creatures"), Description("The creatures in the world.")]
        public string Creatures() => desk.Run("creatures", new JObject());

        [McpServerTool(Name = "weather"), Description("Holds a weather: Clear, Cloudy, Windy, Fog, Rain, Storm or Snow, for some minutes.")]
        public string Weather(string kind, int minutes = 5) => desk.Run("weather", new JObject { ["kind"] = kind, ["minutes"] = minutes });

        [McpServerTool(Name = "fresh"), Description("Starts the same world again from its first tick: how to try something a second time.")]
        public string Fresh() => desk.Run("fresh", new JObject());

        [McpServerTool(Name = "note"), Description("Records a finding. Give the commands that show it from a fresh world, what happened, what should have, the evidence, and reproduced true only if a second try after fresh showed it again.")]
        public string Note(string title, string steps, string happened, string expected, string evidence, bool reproduced) =>
            desk.Note(new JObject { ["title"] = title, ["steps"] = steps, ["happened"] = happened, ["expected"] = expected, ["evidence"] = evidence, ["reproduced"] = reproduced });

        static JObject At(int? x, int? y, JObject args)
        {
            if (x.HasValue) args["x"] = x.Value;
            if (y.HasValue) args["y"] = y.Value;
            return args;
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0) return Usage();
            var folders = GameFolders.InRepo(Directory.GetCurrentDirectory());
            uint seed = uint.Parse(Option(args, "--seed") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
            if (args[0] == "replay" && args.Length > 1) return Replay(folders, seed, args[1]);
            if (args[0] != "serve") return Usage();
            double minutes = double.Parse(Option(args, "--minutes") ?? "20", System.Globalization.CultureInfo.InvariantCulture);
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(new Desk(folders, seed, minutes, Option(args, "--out")));
            builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<Tools>();
            builder.Build().Run();
            return 0;
        }

        static int Usage()
        {
            Console.Error.WriteLine("Playtest.Text serve --seed N --minutes M --out <folder> | replay <commands.jsonl> [--seed N]");
            return 2;
        }

        static string Option(string[] args, string name)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        // Runs the log's commands on a new desk: the first whose world differs fails it.
        static int Replay(GameFolders folders, uint seed, string path)
        {
            var desk = new Desk(folders, seed, double.MaxValue, null);
            int n = 0;
            foreach (var line in File.ReadLines(path).Select(JObject.Parse))
            {
                n++;
                string answer = desk.Run((string)line["name"], (JObject)line["args"]);
                if (line["state"] == null) Console.WriteLine($"> {line["name"]} {line["args"].ToString(Newtonsoft.Json.Formatting.None)}\n{answer}");
                if (line["state"] == null || desk.State == (string)line["state"]) continue;
                Console.WriteLine($"Command {n} ({line["name"]}) differs: {desk.State}, the log says {line["state"]}");
                return 1;
            }
            Console.WriteLine($"All {n} commands agree: {desk.State}");
            return 0;
        }
    }
}
