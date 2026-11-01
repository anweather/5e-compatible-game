// The rules MCP server (docs/RULES_MCP.md): stdio, local. Logs go to stderr,
// since stdout carries the protocol.
//   dotnet run --project core/Rules.Mcp [-- --rules <folder>]
using System;
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
            var tools = new RulesTools(new Workbench(rulesDir), message => new McpException(message));
            builder.Services.AddMcpServer().WithStdioServerTransport().WithTools(Tools(tools));
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
