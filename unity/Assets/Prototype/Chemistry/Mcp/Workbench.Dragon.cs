// The dragon in the tools (#77): sim.json's "dragon" (DragonParams, World.Dragon.cs) is
// tuned like any sim parameter, and list dragon and describe say what each field does, so
// a model asked about the dragon finds it. Nothing here changes how it flies.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        const string DragonTable = "dragon";

        static readonly (string field, string what)[] DragonFields =
        {
            ("budget", "the cells (ground, height or cover) that may differ from how the world started before it comes by itself; 0, the default, or never, and it comes only when summoned"),
            ("warningTicks", "ticks of warning before it dives out of the clouds"),
            ("turnTicks", "ticks it wheels round between passes"),
            ("leavingTicks", "ticks it takes to leave after its last pass"),
            ("speed", "cells its breath goes a tick"),
            ("depth", "how deep its breath is along a pass, in cells"),
            ("breathChunks", "chunks across one pass"),
            ("overlapChunks", "chunks of a pass the next one sweeps again"),
            ("chunksPerTick", "chunks its breath puts back as they started, at most, each tick"),
            ("shelterWater", "water units (not frozen) from which a cell's water hides whoever is in it from the breath"),
            ("wallReach", "cells from a creature, toward the dragon, in which a wall of wallTerrains shelters it"),
            ("wallTerrains", "the terrains whose walls stop the breath"),
            ("scale", "how big it's drawn, against a 70 m wingspan; drawn only, so its reach doesn't follow it"),
        };

        const string DragonAbout = "The dragon resets the world: it comes when summoned, or by itself past a budget if a mod sets one (0, the default, is never), warns, then flies passes over the valley, and every chunk its breath reaches goes back to how it started, things and all. Whoever the breath catches is downed unless sheltered by deep water or a stone wall. Its fields are sim parameters: tune dragon.<field>, such as dragon.budget never so it only comes when summoned.";

        IEnumerable<string> DragonLines()
        {
            var lines = new List<string>();
            Flatten((Newtonsoft.Json.Linq.JObject)Raw["sim"][DragonTable], DragonTable + ".", lines);
            return new[] { DragonAbout }.Concat(lines);
        }

        static string SimNote(string path)
        {
            if (!path.StartsWith(DragonTable + ".", StringComparison.Ordinal)) return "";
            string field = path.Substring(DragonTable.Length + 1);
            var what = DragonFields.FirstOrDefault(f => f.field == field).what;
            return what == null ? "" : $": {what}";
        }

        static (string table, string id) DragonPath(string table, string id)
        {
            if (!string.Equals(table?.Trim(), DragonTable, StringComparison.OrdinalIgnoreCase)) return (table, id);
            string field = (id ?? "").Trim();
            if (field.StartsWith(DragonTable, StringComparison.OrdinalIgnoreCase)) field = field.Substring(DragonTable.Length).TrimStart('.');
            return ("sim", field.Length == 0 ? DragonTable : $"{DragonTable}.{field}");
        }

        static string DragonNote(string table, string id)
        {
            if (table != "sim" || !(id == DragonTable || id.StartsWith(DragonTable + ".", StringComparison.Ordinal))) return "";
            return id == DragonTable
                ? $"\n{DragonAbout}\n" + string.Join("\n", DragonFields.Select(f => $"- {f.field}: {f.what}."))
                : $"\n{id}{SimNote(id)}.";
        }

        static string DragonValue(string param, string value)
        {
            if (param != DragonTable + ".budget") return value;
            string word = (value ?? "").Trim().Trim('"').ToLowerInvariant();
            return word == "never" || word == "off" || word == "none" ? "0" : value;
        }
    }
}
