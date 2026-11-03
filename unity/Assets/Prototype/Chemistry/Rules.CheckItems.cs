// Checks items.json (Items.cs), with the same messages as validateRules in
// sandbox/src/rules.ts. Unlike the older tables, the TS checks an item's
// numbers too, so both sides refuse the same mods.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class Rules
    {
        sealed partial class Checker
        {
            public void Items()
            {
                var entities = (JObject)r["entities"];
                foreach (var p in ((JObject)r["items"]).Properties()) Item($"item {p.Name}", p.Value, entities[p.Name] != null);
            }

            void Item(string what, JToken i, bool takenByEntity)
            {
                var schema = RuleSchema.Items;
                if (!Truthy(i["name"])) Errors.Add($"{what}: needs a name");
                if (takenByEntity) Errors.Add($"{what}: id is taken by an entity");
                MaterialRef(what, i["material"]);
                Values(what, "", i, schema);
                ComponentList(what, i["components"]);
                bool lit = Section(i, "light", what, out var light);
                bool breakable = Section(i, "breaks", what, out var breaks);
                if (lit) Values(what, "light.", light, schema.SectionOf("light"));
                if (breakable) Breaks(what, breaks, schema.SectionOf("breaks"));
                Use(what, i, lit);
            }

            void Use(string what, JToken i, bool lit)
            {
                var use = i["use"];
                if (use == null) return;
                switch (Text(use))
                {
                    case ItemUses.Light:
                        if (!lit) Errors.Add($"{what}: use light needs a light");
                        break;
                    case ItemUses.Zoom:
                        if (!(IsNumber(i["zoom"]) && (double)i["zoom"] > 1)) Errors.Add($"{what}: use zoom needs a zoom above 1");
                        break;
                    case ItemUses.Send:
                        if (!Truthy(i["message"])) Errors.Add($"{what}: use send needs a message");
                        break;
                    case ItemUses.Dig:
                        Dig(what, i);
                        break;
                    case ItemUses.Point:
                        if (!lit) Errors.Add($"{what}: use point needs a light");
                        if (!(IsNumber(i["reach"]) && (double)i["reach"] > 0)) Errors.Add($"{what}: use point needs a reach above 0");
                        break;
                    default:
                        Errors.Add($"{what}: unknown use {Show(use)}");
                        break;
                }
            }

            void Dig(string what, JToken i)
            {
                if (!(IsWholeNumber(i["strength"]) && (double)i["strength"] >= 1)) Errors.Add($"{what}: use dig needs a whole strength from 1");
            }

            void Breaks(string what, JObject b, RuleSchema schema)
            {
                Values(what, "breaks.", b, schema);
                if (!Section(b, "spill", $"{what}: breaks", out var spill)) return;
                if (!IsIn(covers, spill["cover"])) Errors.Add($"{what}: breaks.spill.cover {Show(spill["cover"])} unknown");
                Values(what, "breaks.spill.", spill, schema.SectionOf("spill"));
            }

            /// <summary>A section's numbers and colours, each held to its field in the schema, in the schema's order.</summary>
            void Values(string what, string path, JToken section, RuleSchema schema)
            {
                foreach (var f in schema.Fields)
                {
                    var v = section[f.Name];
                    if (f.Type == RuleFieldType.Colour && v != null && !IsHexColor(v)) Errors.Add($"{what}: {path}{f.Name} must be #rrggbb");
                    if (!f.Ranged) continue;
                    bool missing = v == null || v.Type == JTokenType.Null;
                    if (missing && f.Required) Errors.Add($"{what}: {path}{f.Name} must be a number {RangeWords(f.Min, f.Above, f.Max)}");
                    else Range(v, $"{what}: {path}{f.Name}", f.Min, f.Above, f.Max);
                }
            }

            /// <summary>An optional sub-object: true if it is there; anything else there is an error.</summary>
            bool Section(JToken parent, string key, string what, out JObject section)
            {
                var v = parent[key];
                section = v as JObject;
                if (section == null && v != null && v.Type != JTokenType.Null) Errors.Add($"{what}: {key} must be an object");
                return section != null;
            }

            /// <summary>"#rrggbb".</summary>
            static bool IsHexColor(JToken v) =>
                v.Type == JTokenType.String && ((string)v).Length == 7 && ((string)v)[0] == '#' && ((string)v).Skip(1).All(Uri.IsHexDigit);

            /// <summary>A number from <paramref name="min"/> (or above it), up to <paramref name="max"/>, when present.</summary>
            void Range(JToken v, string what, double min, bool above = false, double max = double.MaxValue)
            {
                if (v == null || v.Type == JTokenType.Null) return;
                double n = IsNumber(v) ? (double)v : double.NaN;
                if (n >= min && (!above || n > min) && n <= max) return;
                Errors.Add($"{what} must be a number {RangeWords(min, above, max)}");
            }

            static string RangeWords(double min, bool above, double max)
            {
                if (above) return $"above {min}";
                return max < double.MaxValue ? $"from {min} to {max}" : $"from {min}";
            }
        }
    }
}
