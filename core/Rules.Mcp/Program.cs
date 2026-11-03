// The rules MCP server (docs/RULES_MCP.md): stdio, local. Logs go to stderr,
// since stdout carries the protocol.
//   dotnet run --project core/Rules.Mcp [-- --rules <folder>] [--here x,y] [--applied <file>]
// --here puts the stand-in player (the host) on a cell for here() and "around me";
// with no game attached it's the middle of the valley's grid. --applied appends each patch that
// goes to the room to a file, for chatbench's record, which the stream can't give.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            int at = Array.IndexOf(args, "--rules");
            var folders = at >= 0 && at + 1 < args.Length ? new GameFolders(Path.GetFullPath(args[at + 1])) : InRepo(Directory.GetCurrentDirectory());

            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            var bench = new Workbench(folders);
            int here = Array.IndexOf(args, "--here");
            if (here >= 0 && here + 1 < args.Length && args[here + 1].Split(',') is { Length: 2 } xy
                && int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) && int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                bench.Here = new StandInHere(new HereSpot(Workbench.HostName, x, y));
            int applied = Array.IndexOf(args, "--applied");
            if (applied >= 0 && applied + 1 < args.Length)
                bench.Applied = (key, patch) => File.AppendAllText(args[applied + 1], new JObject { ["mod"] = key, ["patch"] = patch }.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            var tools = new RulesTools(bench, message => new McpException(message));
            builder.Services.AddMcpServer(o => o.ServerInstructions = RulesTools.Instructions).WithStdioServerTransport().WithTools(Tools(tools));
            builder.Build().Run();
        }

        // The repo's folders above a server started inside it, with no player, so a save goes to rules/mods.
        static GameFolders InRepo(string start)
        {
            try
            {
                return GameFolders.InRepo(start);
            }
            catch (DirectoryNotFoundException)
            {
                throw new DirectoryNotFoundException("No rules/ folder above " + start + "; pass --rules <folder>.");
            }
        }

        // Each RulesTool method as the SDK's tool: its name, its Description and its parameters' are read from there.
        static McpServerTool[] Tools(RulesTools tools) =>
            typeof(RulesTools).GetMethods()
                .Select(m => (method: m, tool: m.GetCustomAttribute<RulesToolAttribute>()))
                .Where(t => t.tool != null)
                .Select(t => McpServerTool.Create(t.method, tools, new McpServerToolCreateOptions { Name = t.tool.Name, ReadOnly = t.tool.ReadOnly }))
                .ToArray();
    }
}
