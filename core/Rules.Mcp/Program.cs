// The rules MCP server (docs/RULES_MCP.md): stdio, local. Logs go to stderr,
// since stdout carries the protocol.
//   dotnet run --project core/Rules.Mcp [-- --rules <folder>] [--here x,y]
// --here puts the stand-in player (the host) on a cell for here() and "around me";
// with no game attached it's the middle of the valley's grid.
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

namespace Chem.Mcp
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            int at = Array.IndexOf(args, "--rules");
            string rulesDir = at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : Workbench.FindRulesDir(Directory.GetCurrentDirectory());

            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            var bench = new Workbench(rulesDir);
            int here = Array.IndexOf(args, "--here");
            if (here >= 0 && here + 1 < args.Length && args[here + 1].Split(',') is { Length: 2 } xy
                && int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) && int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                bench.Here = new StandInHere(new HereSpot(Workbench.HostName, x, y));
            var tools = new RulesTools(bench, message => new McpException(message));
            builder.Services.AddMcpServer(o => o.ServerInstructions = RulesTools.Instructions).WithStdioServerTransport().WithTools(Tools(tools));
            builder.Build().Run();
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
