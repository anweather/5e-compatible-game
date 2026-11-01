// The MCP protocol as the game speaks it (docs/RULES_MCP.md phase 3): one JSON-RPC
// message in, one out, for RulesTools. The game can't take the C# MCP SDK, so this
// is the small part it needs: initialize, ping, tools/list and tools/call. The tools
// and their schemas are read from RulesTools. RulesMcpServer carries it over HTTP.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public sealed class McpRpc
    {
        const string LatestProtocol = "2025-06-18";
        const int NoMethod = -32601, BadParams = -32602, BadRequest = -32600, BadJson = -32700;

        readonly RulesTools tools;
        readonly string name, version;
        readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        public McpRpc(RulesTools tools, string name, string version)
        {
            this.tools = tools;
            this.name = name;
            this.version = version;
            foreach (var m in typeof(RulesTools).GetMethods())
            {
                var tool = m.GetCustomAttribute<RulesToolAttribute>();
                if (tool != null) methods[tool.Name] = m;
            }
        }

        /// <summary>The answer to one message, as JSON; null for a notification, which has none.</summary>
        public string Handle(string body)
        {
            JObject message;
            try
            {
                message = JToken.Parse(body) as JObject;
            }
            catch (JsonReaderException)
            {
                return Failure(null, BadJson, "That isn't JSON.");
            }
            if (message == null) return Failure(null, BadRequest, "A message is one JSON object.");
            var id = message["id"];
            if (id == null) return null;
            string method = (string)message["method"];
            var given = message["params"] as JObject ?? new JObject();
            switch (method)
            {
                case "initialize": return Result(id, Initialize(given));
                case "ping": return Result(id, new JObject());
                case "tools/list": return Result(id, new JObject { ["tools"] = new JArray(methods.Select(Describe)) });
                case "tools/call": return Call(id, given);
                default: return Failure(id, NoMethod, $"There's no method {method}.");
            }
        }

        JObject Initialize(JObject given) => new JObject
        {
            ["protocolVersion"] = (string)given["protocolVersion"] ?? LatestProtocol,
            ["capabilities"] = new JObject { ["tools"] = new JObject() },
            ["serverInfo"] = new JObject { ["name"] = name, ["version"] = version },
            ["instructions"] = RulesTools.Instructions,
        };

        static JObject Describe(KeyValuePair<string, MethodInfo> tool)
        {
            var properties = new JObject();
            var required = new JArray();
            foreach (var p in tool.Value.GetParameters())
            {
                var schema = SchemaOf(p.ParameterType);
                schema["description"] = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                properties[p.Name] = schema;
                if (!p.HasDefaultValue) required.Add(p.Name);
            }
            var info = tool.Value.GetCustomAttribute<RulesToolAttribute>();
            return new JObject
            {
                ["name"] = tool.Key,
                ["description"] = tool.Value.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "",
                ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = required },
                ["annotations"] = new JObject { ["readOnlyHint"] = info.ReadOnly },
            };
        }

        static JObject SchemaOf(Type type)
        {
            var plain = Nullable.GetUnderlyingType(type) ?? type;
            if (plain == typeof(string[])) return new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } };
            if (plain == typeof(int)) return new JObject { ["type"] = "integer" };
            if (plain == typeof(double)) return new JObject { ["type"] = "number" };
            if (plain == typeof(bool)) return new JObject { ["type"] = "boolean" };
            return new JObject { ["type"] = "string" };
        }

        string Call(JToken id, JObject given)
        {
            string tool = (string)given["name"];
            if (tool == null || !methods.TryGetValue(tool, out var method)) return Failure(id, BadParams, $"There's no tool {tool}.");
            var arguments = given["arguments"] as JObject ?? new JObject();
            try
            {
                var values = method.GetParameters().Select(p => ValueOf(p, arguments)).ToArray();
                return Result(id, ToolResult((string)method.Invoke(tools, values), false));
            }
            catch (TargetInvocationException e) when (e.InnerException is RulesToolException)
            {
                return Result(id, ToolResult(e.InnerException.Message, true));
            }
            catch (RulesToolException e)
            {
                return Result(id, ToolResult(e.Message, true));
            }
        }

        // A value that's missing takes the parameter's default; a required one is a sentence, like any refusal.
        static object ValueOf(ParameterInfo p, JObject arguments)
        {
            var value = arguments[p.Name];
            if (value == null || value.Type == JTokenType.Null)
                return p.HasDefaultValue ? p.DefaultValue : throw new RulesToolException($"The tool needs {p.Name}.");
            try
            {
                return value.ToObject(p.ParameterType);
            }
            catch (Exception e) when (e is JsonException || e is ArgumentException || e is FormatException || e is InvalidCastException)
            {
                throw new RulesToolException($"{p.Name} can't be read: it should be a {SchemaOf(p.ParameterType)["type"]}.");
            }
        }

        static JObject ToolResult(string text, bool isError) => new JObject
        {
            ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        };

        static string Result(JToken id, JObject result) =>
            new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString(Formatting.None);

        static string Failure(JToken id, int code, string message) =>
            new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JObject { ["code"] = code, ["message"] = message } }.ToString(Formatting.None);
    }
}
