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
                if (!Truthy(i["name"])) Errors.Add($"{what}: needs a name");
                if (takenByEntity) Errors.Add($"{what}: id is taken by an entity");
                MaterialRef(what, i["material"]);
                Range(i["mass"], $"{what}: mass", 0, above: true);
                Range(i["zoom"], $"{what}: zoom", 1);
                Range(i["reach"], $"{what}: reach", 0);
                ComponentList(what, i["components"]);
                bool lit = Section(i, "light", what, out var light);
                bool breakable = Section(i, "breaks", what, out var breaks);
                if (lit) Light(what, light);
                if (breakable) Breaks(what, breaks);
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
                    case ItemUses.Point:
                        if (!lit) Errors.Add($"{what}: use point needs a light");
                        if (!(IsNumber(i["reach"]) && (double)i["reach"] > 0)) Errors.Add($"{what}: use point needs a reach above 0");
                        break;
                    default:
                        Errors.Add($"{what}: unknown use {Show(use)}");
                        break;
                }
            }

            void Light(string what, JObject l)
            {
                Range(l["bright"], $"{what}: light.bright", 0);
                Range(l["dim"], $"{what}: light.dim", 0);
                Range(l["cone"], $"{what}: light.cone", 0, max: 179);
                var color = l["color"];
                if (color != null && !IsHexColor(color)) Errors.Add($"{what}: light.color must be #rrggbb");
            }

            void Breaks(string what, JObject b)
            {
                var speed = b["speed"];
                if (speed == null || speed.Type == JTokenType.Null) Errors.Add($"{what}: breaks.speed must be a number above 0");
                else Range(speed, $"{what}: breaks.speed", 0, above: true);
                if (!Section(b, "spill", $"{what}: breaks", out var spill)) return;
                if (!IsIn(covers, spill["cover"])) Errors.Add($"{what}: breaks.spill.cover {Show(spill["cover"])} unknown");
                Range(spill["radius"], $"{what}: breaks.spill.radius", 0);
                Range(spill["fire"], $"{what}: breaks.spill.fire", 0);
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
                string range = $"from {min}";
                if (above) range = $"above {min}";
                else if (max < double.MaxValue) range = $"from {min} to {max}";
                Errors.Add($"{what} must be a number {range}");
            }
        }
    }
}
