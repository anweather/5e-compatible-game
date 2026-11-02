// The things mods add to the world (docs/design/world-tools.md G5): the rows of a mod's
// "world" section's "add", in the order the mods on bring them, so the things in force are a
// pure function of the base rules and the mods on and a revert is only composing again.
// WorldThings.With turns them into things with ids; World.Things.cs checks where they stand.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One row of a mod's "world" section's "add": a kind, a world cell, a turn and a chance.</summary>
    public sealed class ModThing
    {
        public string Kind;
        public int X, Y, Rotation;
        /// <summary>How likely it is to be in a given world, above 0 and up to 1 (ThingChance).</summary>
        public double Chance = 1;
    }

    public sealed partial class Rules
    {
        /// <summary>Where the composed document holds the mods' things, in the order they came.</summary>
        internal const string WorldThingsKey = "worldThings";

        static readonly string[] ThingRowFields = { "kind", "cell", "rot", "chance" };

        /// <summary>The things the mods on add, in the order they came.</summary>
        public IReadOnlyList<ModThing> ModThings { get; private set; } = Array.Empty<ModThing>();

        void ParseThings(JObject d)
        {
            if (!(d[WorldThingsKey] is JArray rows)) return;
            ModThings = rows.OfType<JObject>().Select(t => new ModThing
            {
                Kind = (string)t["kind"],
                X = (int)t["cell"][0],
                Y = (int)t["cell"][1],
                Rotation = (int)Math.Round(Num(t["rot"]) ?? 0),
                Chance = Num(t["chance"]) ?? 1,
            }).ToList();
        }

        static void ApplyThings(JObject r, JObject p)
        {
            if (!(p["world"]?["add"] is JArray given) || given.Count == 0) return;
            if (!(r[WorldThingsKey] is JArray rows)) r[WorldThingsKey] = rows = new JArray();
            foreach (var row in given) rows.Add(row.DeepClone());
        }

        sealed partial class Checker
        {
            public void Things()
            {
                foreach (var t in (r[WorldThingsKey] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    string kind = Show(t["kind"]);
                    string who = $"thing {kind}";
                    foreach (var f in t.Properties().Where(f => !ThingRowFields.Contains(f.Name))) Errors.Add($"{who}: {f.Name} isn't a field of a thing; it takes {string.Join(", ", ThingRowFields)}");
                    if (r["entities"]?[kind] == null && r["items"]?[kind] == null) Errors.Add($"{who}: unknown kind {kind} (an entity or an item)");
                    if (!(t["cell"] is JArray cell) || cell.Count != 2 || !cell.All(v => IsWholeAtLeast(v, 0))) Errors.Add($"{who} needs \"cell\": [x, y], the world cell it stands on");
                    if (t["rot"] != null && !IsNumber(t["rot"])) Errors.Add($"{who}: rot is a turn in degrees");
                    if (t["chance"] != null && !(IsNumber(t["chance"]) && (double)t["chance"] > 0 && (double)t["chance"] <= 1)) Errors.Add($"{who}: chance must be above 0 and at most 1");
                }
            }
        }
    }
}
