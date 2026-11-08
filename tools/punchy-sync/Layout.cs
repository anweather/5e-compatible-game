// Rows written the way rules/fauna.json and rules/parts.json are laid out (CreatureRowExport's
// layout), and a file changed row by row: rows not touched keep their text exactly.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PunchySync
{
    public static class Layout
    {
        public static string Species(string id, JsonObject row)
        {
            var lines = new List<string>();
            var first = new List<string> { Key("name", row["name"]) };
            foreach (var k in new[] { "variantOf", "scale", "size" })
                if (row.ContainsKey(k)) first.Add(Key(k, row[k]));
            lines.Add(string.Join(", ", first));
            foreach (var (k, v) in row)
            {
                if (k == "name" || k == "variantOf" || k == "scale" || k == "size") continue;
                if (k == "parts" && v is JsonObject slots && slots.Count > 0)
                    lines.Add($"\"parts\": {{\n{string.Join(",\n", slots.Select(s => $"      {Key(s.Key, s.Value)}"))}\n    }}");
                else lines.Add(Key(k, v));
            }
            return Row(id, lines);
        }

        public static string Part(string id, JsonObject row)
        {
            var lines = new List<string>();
            foreach (var (k, v) in row)
            {
                if (k == "segments" && v is JsonArray segs)
                    lines.Add($"\"segments\": [\n{string.Join(",\n", segs.Select(s => $"      {Inline(s)}"))}\n    ]");
                else if (k == "poses" && v is JsonObject poses)
                    lines.Add($"\"poses\": {{\n{string.Join(",\n", poses.Select(p => $"      {Key(p.Key, p.Value)}"))}\n    }}");
                else lines.Add(Key(k, v));
            }
            return Row(id, lines);
        }

        static string Row(string id, List<string> lines) => $"  {Str(id)}: {{\n    {string.Join(",\n    ", lines)}\n  }}";

        static string Key(string k, JsonNode v) => $"{Str(k)}: {Inline(v)}";

        /// <summary>One line, with the spaces the hand-written rules have.</summary>
        public static string Inline(JsonNode t) => t switch
        {
            null => "null",
            JsonObject o => o.Count == 0 ? "{}" : "{ " + string.Join(", ", o.Select(p => Key(p.Key, p.Value))) + " }",
            JsonArray a => "[" + string.Join(", ", a.Select(Inline)) + "]",
            JsonValue v when v.TryGetValue<double>(out var d) => d.ToString("R", CultureInfo.InvariantCulture),
            JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
            JsonValue v when v.TryGetValue<string>(out var s) => Str(s),
            _ => t.ToJsonString(),
        };

        static string Str(string s) => JsonSerializer.Serialize(s);

        static readonly Regex RowStart = new("^  \"([^\"]+)\": \\{$");

        /// <summary>The file with these rows replaced (by id) and these added at the end, every other line as it was.</summary>
        public static string Rewrite(string text, IReadOnlyDictionary<string, string> replaced, IReadOnlyList<string> added)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            var output = new List<string>();
            var found = new HashSet<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var m = RowStart.Match(lines[i]);
                if (!m.Success || !replaced.TryGetValue(m.Groups[1].Value, out var row))
                {
                    output.Add(lines[i]);
                    continue;
                }
                int end = i;
                while (end < lines.Count && lines[end] != "  }" && lines[end] != "  },") end++;
                if (end == lines.Count) throw new FormatException($"row {m.Groups[1].Value} has no end");
                output.Add(row + (lines[end].EndsWith(',') ? "," : ""));
                found.Add(m.Groups[1].Value);
                i = end;
            }
            var missing = replaced.Keys.Where(k => !found.Contains(k)).ToList();
            if (missing.Count > 0) throw new FormatException($"couldn't find rows {string.Join(", ", missing)} laid out one key a line");
            if (added.Count > 0)
            {
                int close = output.FindLastIndex(l => l == "}");
                if (close < 1) throw new FormatException("the file doesn't end in }");
                output[close - 1] += ",";
                output.Insert(close, string.Join(",\n", added));
            }
            return string.Join("\n", output);
        }
    }
}
