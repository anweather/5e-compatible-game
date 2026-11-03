// Temporary materials (docs/design/primitives.md §4, step 2; J-062): a terrain, cover
// or gas a mod adds lives while its mod is on. Its row may say `lapse` {into?, after?}:
// `after` ticks from the switch the mod goes off by itself (World.Lapse.cs), and either
// way, lapsed or reverted, every cell holding it turns `into` (a terrain's; else
// "dirt") or loses it (a cover's or gas's). The row is read here; which rows are
// temporary is the room's business (TempMaterials), since only it knows the base.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A material row's lapse: what a cell of it turns into (null: none, or "dirt" for a terrain) and the ticks from its switch (0: while its mod is on).</summary>
    public readonly struct MaterialLapse
    {
        public MaterialLapse(string into, int after)
        {
            Into = into;
            After = after;
        }

        public string Into { get; }
        public int After { get; }
    }

    public sealed partial class Rules
    {
        /// <summary>A material row's key for its lapse.</summary>
        public const string LapseKey = "lapse";

        /// <summary>What a lapse takes: into and after.</summary>
        public static readonly IReadOnlyList<string> LapseFields = new[] { "into", "after" };

        /// <summary>The material tables a temporary material may be in, in the order they're swept.</summary>
        public static readonly IReadOnlyList<string> MaterialTables = new[] { "terrain", "covers", "gases" };

        /// <summary>The lapse of a terrain, cover or gas row (table as in MaterialTables); none when the row gives none or isn't there.</summary>
        public MaterialLapse LapseOf(string table, string id)
        {
            var o = doc[table]?[id]?[LapseKey] as JObject;
            if (o == null) return default;
            return new MaterialLapse(o["into"]?.Type == JTokenType.String ? (string)o["into"] : null, o["after"]?.Type == JTokenType.Integer ? (int)o["after"] : 0);
        }

        /// <summary>Whether this table holds a row by this id.</summary>
        public bool HasMaterial(string table, string id) => table switch
        {
            "terrain" => terrain.ContainsKey(id),
            "covers" => covers.ContainsKey(id),
            "gases" => gases.ContainsKey(id),
            _ => false,
        };

        sealed partial class Checker
        {
            /// <summary>Each lapse: an object of into and after, after a whole number from 1, into a row of the same table but itself.</summary>
            public void Lapses()
            {
                foreach (var (table, rows) in new[] { ("terrain", terrain), ("covers", covers), ("gases", gases) })
                    foreach (var p in rows.Properties())
                        if (p.Value[LapseKey] != null) Lapse(Single(table) + " " + p.Name, p.Name, p.Value[LapseKey], rows);
            }

            void Lapse(string what, string id, JToken lapse, JObject rows)
            {
                if (!(lapse is JObject o))
                {
                    Errors.Add($"{what}: lapse must be an object of into and after");
                    return;
                }
                foreach (var k in o.Properties().Select(k => k.Name).Where(k => !LapseFields.Contains(k)))
                    Errors.Add($"{what}: lapse.{k} unknown; lapse takes into and after");
                if (o["after"] != null && !(IsWholeNumber(o["after"]) && (double)o["after"] >= 1))
                    Errors.Add($"{what}: lapse.after must be a whole number of ticks from 1");
                var into = o["into"];
                if (into == null) return;
                if (Text(into) == null || !IsIn(rows, into)) Errors.Add($"{what}: lapse.into {Show(into)} isn't a {what.Split(' ')[0]}");
                else if (Text(into) == id) Errors.Add($"{what}: lapse.into can't be itself");
            }
        }
    }
}
