// The rules MCP server (docs/RULES_MCP.md): stdio, local. Logs go to stderr,
// since stdout carries the protocol.
//   dotnet run --project core/Rules.Mcp [-- --rules <folder>]
using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            builder.Services.AddSingleton(new Workbench(rulesDir));
            builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<RulesTools>();
            builder.Build().Run();
        }
    }
}
