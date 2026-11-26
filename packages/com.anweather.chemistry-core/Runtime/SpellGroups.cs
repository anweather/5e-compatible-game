// The spell radial's elements (rules/groups.json): which ring-1 wedge each
// spell sits under, and the wedges' order, name and colour. UI only: the sim
// never reads them, but they are in the rule document, so mods can regroup or
// add elements and the fingerprint covers them. The sandbox carries and checks
// the same table (sandbox/src/rules.ts) but has no radial.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class GroupDef
    {
        public string Id, Name;
        public double Order;
        public string Color;
    }

    public sealed partial class Rules
    {
        public const string DefaultGroupColor = "#888888";

        readonly List<GroupDef> groups = new List<GroupDef>();

        public IReadOnlyList<GroupDef> Groups => groups;

        public GroupDef Group(string id) => groups.FirstOrDefault(g => g.Id == id);

        static void ParseGroups(Rules r, JObject table)
        {
            r.groups.AddRange(table.Properties()
                .Where(p => p.Value is JObject)
                .Select(p => new GroupDef
                {
                    Id = p.Name,
                    Name = Str(p.Value["name"]) ?? p.Name,
                    Order = Num(p.Value["order"]) ?? 0,
                    Color = Str(p.Value["color"]) ?? DefaultGroupColor,
                })
                .OrderBy(g => g.Order)
                .ThenBy(g => g.Id, StringComparer.Ordinal));
        }

        sealed partial class Checker
        {
            public void Groups()
            {
                var table = (JObject)r["groups"];
                foreach (var p in table.Properties())
                {
                    if (!(p.Value is JObject))
                    {
                        Errors.Add($"group {p.Name}: must be an object");
                        continue;
                    }
                    if (!Truthy(p.Value["name"])) Errors.Add($"group {p.Name}: needs a name");
                    var order = p.Value["order"];
                    if (order != null && order.Type != JTokenType.Null && !IsNumber(order)) Errors.Add($"group {p.Name}: order must be a number");
                    var color = p.Value["color"];
                    if (color != null && !IsHexColor(color)) Errors.Add($"group {p.Name}: color must be #rrggbb");
                }
                foreach (var s in r["spells"])
                {
                    var g = s["group"];
                    if (g != null && !IsIn(table, g)) Errors.Add($"spell {Show(s["id"])}: unknown group {Show(g)}");
                }
            }
        }
    }
}
