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

        /// <summary>What each of sim.json's dragon fields does, in DragonParams' words.</summary>
        static readonly (string field, string what)[] DragonFields =
        {
            ("budget", "the cells (ground, height or cover) that may differ from how the world started before it comes by itself; 0, or tune it to never, and it never comes by itself"),
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

        const string DragonAbout = "The dragon resets the world: when too much of it has changed (budget), it comes by itself, warns, then flies passes over the valley, and every chunk its breath reaches goes back to how it started, things and all. Whoever the breath catches is downed unless sheltered by deep water or a stone wall. Its fields are sim parameters: tune dragon.<field>, such as dragon.budget never so it only comes when summoned.";

        // The dragon's fields as list sim gives them, each with what it does.
        IEnumerable<string> DragonLines()
        {
            var lines = new List<string>();
            Flatten((Newtonsoft.Json.Linq.JObject)Raw["sim"][DragonTable], DragonTable + ".", lines);
            return new[] { DragonAbout }.Concat(lines);
        }

        /// <summary>What a sim parameter does, after its line in list sim; empty for one with no words here.</summary>
        static string SimNote(string path)
        {
            if (!path.StartsWith(DragonTable + ".", StringComparison.Ordinal)) return "";
            string field = path.Substring(DragonTable.Length + 1);
            var what = DragonFields.FirstOrDefault(f => f.field == field).what;
            return what == null ? "" : $": {what}";
        }

        /// <summary>describe dragon, or describe dragon budget, is describe sim dragon or sim dragon.budget.</summary>
        static (string table, string id) DragonPath(string table, string id)
        {
            if (!string.Equals(table?.Trim(), DragonTable, StringComparison.OrdinalIgnoreCase)) return (table, id);
            string field = (id ?? "").Trim();
            if (field.StartsWith(DragonTable, StringComparison.OrdinalIgnoreCase)) field = field.Substring(DragonTable.Length).TrimStart('.');
            return ("sim", field.Length == 0 ? DragonTable : $"{DragonTable}.{field}");
        }

        // describe's words for the dragon or one of its fields.
        static string DragonNote(string table, string id)
        {
            if (table != "sim" || !(id == DragonTable || id.StartsWith(DragonTable + ".", StringComparison.Ordinal))) return "";
            return id == DragonTable
                ? $"\n{DragonAbout}\n" + string.Join("\n", DragonFields.Select(f => $"- {f.field}: {f.what}."))
                : $"\n{id}{SimNote(id)}.";
        }

        /// <summary>tune's value: "never" (or "off") for the dragon's budget is 0, its own word for never coming by itself.</summary>
        static string DragonValue(string param, string value)
        {
            if (param != DragonTable + ".budget") return value;
            string word = (value ?? "").Trim().Trim('"').ToLowerInvariant();
            return word == "never" || word == "off" || word == "none" ? "0" : value;
        }
    }
}
