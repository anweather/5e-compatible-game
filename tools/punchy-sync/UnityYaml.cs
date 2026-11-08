// The little of Unity's YAML that an asset uses: block maps, block lists (Unity writes a
// list at its key's indent), and one-line {k: v} maps and [] lists. Values stay strings.
using System.Collections.Generic;
using System.Globalization;

namespace PunchySync
{
    public static class UnityYaml
    {
        /// <summary>The asset's one document's object (the MonoBehaviour's fields), or an empty map.</summary>
        public static Dictionary<string, object> Fields(string text)
        {
            var root = Parse(text);
            foreach (var v in root.Values)
                if (v is Dictionary<string, object> body) return body;
            return new Dictionary<string, object>();
        }

        public static Dictionary<string, object> Parse(string text)
        {
            var lines = new List<(int indent, string text)>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0 || raw.StartsWith('%') || raw.StartsWith("---")) continue;
                int indent = raw.Length - raw.TrimStart(' ').Length;
                lines.Add((indent, raw.Substring(indent).TrimEnd()));
            }
            int i = 0;
            return lines.Count == 0 ? new Dictionary<string, object>() : Map(lines, ref i, lines[0].indent);
        }

        static bool IsItem(string t) => t == "-" || t.StartsWith("- ");

        static object Block(List<(int indent, string text)> lines, ref int i, int indent) =>
            IsItem(lines[i].text) ? List(lines, ref i, indent) : Map(lines, ref i, indent);

        static Dictionary<string, object> Map(List<(int indent, string text)> lines, ref int i, int indent)
        {
            var map = new Dictionary<string, object>();
            while (i < lines.Count && lines[i].indent == indent && !IsItem(lines[i].text))
            {
                var (key, rest) = KeyValue(lines[i].text);
                i++;
                if (rest.Length > 0) map[key] = Scalar(rest);
                else if (i < lines.Count && (lines[i].indent > indent || (lines[i].indent == indent && IsItem(lines[i].text))))
                    map[key] = Block(lines, ref i, lines[i].indent);
                else map[key] = "";
            }
            return map;
        }

        static List<object> List(List<(int indent, string text)> lines, ref int i, int indent)
        {
            var list = new List<object>();
            while (i < lines.Count && lines[i].indent == indent && IsItem(lines[i].text))
            {
                string item = lines[i].text.Length > 1 ? lines[i].text.Substring(2).TrimStart() : "";
                if (item.StartsWith('{') || item.StartsWith('[') || !item.Contains(':'))
                {
                    list.Add(Scalar(item));
                    i++;
                    continue;
                }
                // "- key: value" opens a map whose keys sit two further in.
                lines[i] = (indent + 2, item);
                list.Add(Map(lines, ref i, indent + 2));
            }
            return list;
        }

        static (string key, string rest) KeyValue(string t)
        {
            int colon = t.IndexOf(':');
            return colon < 0 ? (t, "") : (t.Substring(0, colon).Trim(), t.Substring(colon + 1).Trim());
        }

        static object Scalar(string t)
        {
            if (t.StartsWith('{') && t.EndsWith('}'))
            {
                var map = new Dictionary<string, object>();
                foreach (var part in SplitTop(t.Substring(1, t.Length - 2)))
                {
                    var (k, v) = KeyValue(part);
                    if (k.Length > 0) map[k] = Scalar(v);
                }
                return map;
            }
            if (t.StartsWith('[') && t.EndsWith(']'))
            {
                var list = new List<object>();
                foreach (var part in SplitTop(t.Substring(1, t.Length - 2)))
                    if (part.Trim().Length > 0) list.Add(Scalar(part.Trim()));
                return list;
            }
            if (t.Length >= 2 && (t[0] == '\'' || t[0] == '"') && t[^1] == t[0]) return t.Substring(1, t.Length - 2);
            return t;
        }

        static IEnumerable<string> SplitTop(string t)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '{' || t[i] == '[') depth++;
                else if (t[i] == '}' || t[i] == ']') depth--;
                else if (t[i] == ',' && depth == 0)
                {
                    yield return t.Substring(start, i - start).Trim();
                    start = i + 1;
                }
            }
            yield return t.Substring(start).Trim();
        }

        // ---------- reading fields, with the class's default when an old asset leaves one out ----------

        /// <summary>A float field by the digits CreatureRowExport wrote under Unity's Mono, whose float "R" is seven
        /// digits if they read back the same float, else nine (0.46750003 is written 0.467500031).</summary>
        public static double Num(this Dictionary<string, object> m, string key, double otherwise)
        {
            if (!(m.TryGetValue(key, out var v) && v is string s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))) return otherwise;
            string digits = f.ToString("G7", CultureInfo.InvariantCulture);
            if (float.Parse(digits, CultureInfo.InvariantCulture) != f) digits = f.ToString("G9", CultureInfo.InvariantCulture);
            return double.Parse(digits, CultureInfo.InvariantCulture);
        }

        public static bool Flag(this Dictionary<string, object> m, string key) => m.Num(key, 0) != 0;

        public static string Text(this Dictionary<string, object> m, string key) => m.TryGetValue(key, out var v) ? v as string : null;

        public static Dictionary<string, object> Sub(this Dictionary<string, object> m, string key) =>
            m.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> Items(this Dictionary<string, object> m, string key) =>
            m.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();

        /// <summary>A vector's x, y(, z), or the default.</summary>
        public static double[] Vec(this Dictionary<string, object> m, string key, params double[] otherwise)
        {
            var v = m.Sub(key);
            if (v == null) return otherwise;
            var names = otherwise.Length == 3 ? new[] { "x", "y", "z" } : new[] { "x", "y" };
            var r = new double[names.Length];
            for (int i = 0; i < names.Length; i++) r[i] = v.Num(names[i], otherwise[i]);
            return r;
        }

        public static double[] Rgb(Dictionary<string, object> c, double[] otherwise) =>
            c == null ? otherwise : new[] { c.Num("r", otherwise[0]), c.Num("g", otherwise[1]), c.Num("b", otherwise[2]) };

        /// <summary>A reference's GUID, or null for none (fileID 0).</summary>
        public static string Guid(object reference) =>
            reference is Dictionary<string, object> r && r.Text("fileID") != "0" ? r.Text("guid") : null;
    }
}
