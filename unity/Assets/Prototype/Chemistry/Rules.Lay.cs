// The cells mods lay (docs/design/events-and-placing.md 2b): the rows of a mod's "world"
// section's "lay", each a terrain or a cover on a list of world cells, in the order the mods
// on bring them, so what's laid is a pure function of the base rules and the mods on and a
// revert is only composing again. The cells were picked once, when Claude asked (the MCP's
// scatter, Placement.Pick); the row's seed only says which pick they came from. World.Lay.cs
// lays them at the switch.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One row of a mod's "world" section's "lay": a terrain or a cover, the world cells it goes on, and the seed of the pick.</summary>
    public sealed class LayRow
    {
        public string Terrain, Cover;
        public int Seed;
        public IReadOnlyList<(int x, int y)> Cells = Array.Empty<(int, int)>();
    }

    public sealed partial class Rules
    {
        /// <summary>Where the composed document holds the mods' laid cells, in the order they came.</summary>
        internal const string WorldLayKey = "worldLay";

        static readonly string[] LayRowFields = { "terrain", "cover", "cells", "seed" };

        /// <summary>The rows the mods on lay, in the order they came.</summary>
        public IReadOnlyList<LayRow> ModLays { get; private set; } = Array.Empty<LayRow>();

        void ParseLays(JObject d)
        {
            if (!(d[WorldLayKey] is JArray rows)) return;
            ModLays = rows.OfType<JObject>().Select(t => new LayRow
            {
                Terrain = Str(t["terrain"]),
                Cover = Str(t["cover"]),
                Seed = Int(t["seed"]) ?? 0,
                Cells = (t["cells"] as JArray ?? new JArray()).OfType<JArray>().Select(c => ((int)c[0], (int)c[1])).ToList(),
            }).ToList();
        }

        static void ApplyLays(JObject r, JObject p)
        {
            if (!(p["world"]?["lay"] is JArray given) || given.Count == 0) return;
            if (!(r[WorldLayKey] is JArray rows)) r[WorldLayKey] = rows = new JArray();
            foreach (var row in given) rows.Add(row.DeepClone());
        }

        sealed partial class Checker
        {
            public void Lays()
            {
                foreach (var t in (r[WorldLayKey] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    string who = $"lay {Show(t["terrain"] ?? t["cover"])}";
                    foreach (var f in t.Properties().Where(f => !LayRowFields.Contains(f.Name))) Errors.Add($"{who}: {f.Name} isn't a field of a lay row; it takes {string.Join(", ", LayRowFields)}");
                    if ((t["terrain"] == null) == (t["cover"] == null)) Errors.Add($"{who} needs exactly one of terrain or cover");
                    if (t["terrain"] != null && !IsIn(terrain, t["terrain"])) Errors.Add($"{who}: unknown terrain {Show(t["terrain"])}");
                    if (t["cover"] != null && !IsIn(covers, t["cover"])) Errors.Add($"{who}: unknown cover {Show(t["cover"])}");
                    if (!(t["cells"] is JArray cells) || cells.Count == 0 || !cells.All(c => c is JArray a && a.Count == 2 && a.All(v => IsWholeAtLeast(v, 0))))
                        Errors.Add($"{who} needs \"cells\": [[x, y], ...], world cells from 0");
                    if (t["seed"] != null && !IsWholeNumber(t["seed"])) Errors.Add($"{who}: seed is a whole number");
                }
            }
        }
    }
}
