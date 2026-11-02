// The rules in force: the base with every mod on in the room over it, in order, the way
// the room composes them (World.Room.cs). Everything the tools read comes from here,
// so a draft builds on what's showing and apply's diff is against it. Each mod is a layer,
// which is how describe and whats_on say which mod changed what.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    /// <summary>A running game's world as the tools read it: a copy made on the game's own thread, safe to read off it.</summary>
    public sealed class WorldNow
    {
        WorldNow(World w)
        {
            Tick = w.Tick;
            Seed = w.Seed;
            Minutes = w.Minutes;
            Weather = w.Weather;
            Held = w.WeatherSet;
            Haste = w.Haste;
            Room = new RoomState { Mode = w.Room?.Mode ?? ChangeMode.Open, Host = w.Room?.Host ?? "" };
            if (w.Room != null) Room.Mods.AddRange(w.Room.Mods);
        }

        public int Tick { get; }
        public uint Seed { get; }
        /// <summary>World.Minutes: game minutes since midnight of day 1.</summary>
        public long Minutes { get; }
        public WeatherNow Weather { get; }
        public (WeatherKind kind, int from, int until)? Held { get; }
        public (int rate, int ticks)? Haste { get; }
        /// <summary>The room's mode, host and mods on, in order.</summary>
        public RoomState Room { get; }

        public static WorldNow Of(World world) => new WorldNow(world);
    }

    public sealed partial class Workbench
    {
        // The written rules after each mod on: [0] the base, the last the rules in force.
        sealed class Layer
        {
            public RoomMod Mod;
            public JObject Written;
            public Dictionary<string, JToken> Raw;
        }

        readonly Dictionary<string, JToken> baseRaw;
        List<Layer> layers;
        string layersFor = "";
        JObject inForceDoc;

        /// <summary>The rules in force: the base rules with the room's mods on, in order.</summary>
        public Rules InForce { get; private set; }

        /// <summary>The rules in force as the engine reads them (materials ungrouped into flat keys).</summary>
        internal JObject Doc => inForceDoc;

        /// <summary>The rules in force as written (materials in verb groups), by file name without .json.</summary>
        internal Dictionary<string, JToken> Raw => layers[layers.Count - 1].Raw;

        // A game too old to say what's on: the tools can only read the base.
        bool Blind => Desk != null && !(Desk is ILiveDesk);

        /// <summary>The world now: the game's, or the one in memory. Null if the game is between scenes or can't say.</summary>
        WorldNow Now()
        {
            if (Desk != null) return (Desk as ILiveDesk)?.Now();
            return WorldNow.Of(Live);
        }

        /// <summary>Reads what's on in the room and, if that changed, works out the rules in force again. Every tool call starts with this.</summary>
        public void Refresh()
        {
            if (Desk == null && live == null) return;
            var mods = Now()?.Room.Mods;
            if (mods == null) return;
            try
            {
                PutLayers(mods);
            }
            catch (ModException)
            {
                // The room composed them over its own base; ours differs (rules/ edited on disk). The last that fit stands.
            }
        }

        void PutLayers(IReadOnlyList<RoomMod> mods)
        {
            string signature = string.Join("|", mods.Select(m => m.Id + ":" + m.By + ":" + Fnv1a.Hash(m.Json)));
            if (layers != null && signature == layersFor) return;
            var next = new List<Layer> { new Layer { Written = WrittenDoc(baseRaw), Raw = baseRaw } };
            foreach (var mod in mods)
            {
                var written = (JObject)next[next.Count - 1].Written.DeepClone();
                Rules.ApplyPatch(written, ModPatch.Parse(mod.Json).Json);
                next.Add(new Layer { Mod = mod, Written = written, Raw = RawOf(written) });
            }
            InForce = mods.Count == 0 ? Base : Base.WithMods(mods.Select(m => ModPatch.Parse(m.Json)));
            inForceDoc = JObject.Parse(InForce.ToJson());
            layers = next;
            layersFor = signature;
        }

        // The rule files in the shape of Rules' document, so a patch goes on them as the room does it, but not ungrouped.
        static JObject WrittenDoc(Dictionary<string, JToken> raw) =>
            Rules.Doc((file, _) => raw.TryGetValue(file.Substring(0, file.Length - ".json".Length), out var t) ? t.DeepClone() : JValue.CreateNull());

        // Back from that shape to the files. looks and budgets aren't patched: a mod can't raise its own budget.
        Dictionary<string, JToken> RawOf(JObject written)
        {
            var raw = new Dictionary<string, JToken>(baseRaw, StringComparer.Ordinal);
            var materials = (JObject)baseRaw["materials"].DeepClone();
            foreach (var kind in MaterialKinds) materials[kind] = written[kind].DeepClone();
            raw["materials"] = materials;
            var reactions = (JObject)baseRaw["reactions"].DeepClone();
            reactions["cell"] = written["cellRules"].DeepClone();
            reactions["entity"] = written["entityRules"].DeepClone();
            raw["reactions"] = reactions;
            raw["elements"] = written[Rules.ElementLooksKey].DeepClone();
            foreach (var file in new[] { "spells", "critters", "entities", "items", "machines", "sim", "groups", "components", "combos", BuildingTables.Key })
                raw[file] = written[file].DeepClone();
            if (written[Rules.WorldRowsKey] != null) raw[Rules.WorldRowsKey] = written[Rules.WorldRowsKey].DeepClone();
            if (written[Rules.WorldThingsKey] != null) raw[Rules.WorldThingsKey] = written[Rules.WorldThingsKey].DeepClone();
            return raw;
        }

        // ---------- which mods changed what ----------

        /// <summary>describe's last lines: each mod on that changed this entry, in order, and what it changed.</summary>
        string ChangedBy(string table, string id)
        {
            if (Blind) return "\nThis game can't tell the tools what's on yet, so this is the base rules, and mods may have changed it.";
            if (layers.Count == 1) return "\nNo mods are on: this is the base rules.";
            var lines = new List<string>();
            for (int i = 1; i < layers.Count; i++)
            {
                var (was, now) = (EntryIn(layers[i - 1].Raw, table, id), EntryIn(layers[i].Raw, table, id));
                if (JToken.DeepEquals(was, now)) continue;
                var what = EntryWhat(was, now);
                lines.Add($"- {layers[i].Mod.Title} ({layers[i].Mod.Id}), by {layers[i].Mod.By}: {what}");
            }
            return lines.Count == 0 ? "\nNo mod on changes it: it is as the base rules have it." : "\nAs the rules are now; mods on that changed it, in order, the later winning:\n" + string.Join("\n", lines);
        }

        static string EntryWhat(JToken was, JToken now)
        {
            if (was == null) return "added it";
            return now == null ? "took it away" : string.Join(", ", Differences("", was, now));
        }

        // Everything a layer changed from the one under it, in the words apply uses.
        IEnumerable<string> LayerChanges(int i)
        {
            var (under, over) = (layers[i - 1].Raw, layers[i].Raw);
            foreach (var table in new[] { "terrain", "covers", "gases", "spells", "elements", "reactions", "critters", "entities", "items", "machines", "groups" })
                foreach (var id in EntryIds(over, table).Union(EntryIds(under, table)))
                {
                    var (was, now) = (EntryIn(under, table, id), EntryIn(over, table, id));
                    if (!JToken.DeepEquals(was, now)) yield return EntryChange(table, id, was, now);
                }
            foreach (var change in Differences("", under["sim"], over["sim"])) yield return change;
            foreach (var id in EntryIds(over, Rules.WorldRowsKey))
                if (!JToken.DeepEquals(EntryIn(under, Rules.WorldRowsKey, id), EntryIn(over, Rules.WorldRowsKey, id))) yield return $"the building {id}";
            foreach (var t in ThingsIn(over).Skip(ThingsIn(under).Count())) yield return $"a {Str(t["kind"])} at {Compact(t["cell"])}";
        }

        static IEnumerable<string> EntryIds(Dictionary<string, JToken> raw, string table)
        {
            switch (table)
            {
                case "terrain":
                case "covers":
                case "gases":
                    return (raw["materials"][table] as JObject)?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();
                case "reactions":
                    return ((raw["reactions"]["cell"] as JArray) ?? new JArray()).Concat((raw["reactions"]["entity"] as JArray) ?? new JArray()).Select(r => Str(r["id"]));
                case "spells":
                case "critters":
                    return raw.TryGetValue(table, out var list) ? list.Select(s => Str(s["id"])) : Enumerable.Empty<string>();
                default:
                    return raw.TryGetValue(table, out var named) && named is JObject o ? o.Properties().Select(p => p.Name) : Enumerable.Empty<string>();
            }
        }

        // One entry's change, as apply and whats_on say it.
        static string EntryChange(string table, string id, JToken was, JToken now)
        {
            string name = Str(now?["name"]) ?? Str(was?["name"]) ?? id;
            if (now == null) return $"takes away the {Single(table)} {name}";
            if (was == null) return $"a new {Single(table)}, {name}";
            return $"{name}: {string.Join(", ", Differences("", was, now))}";
        }

        // ---------- whats_on ----------

        /// <summary>What's on now in plain words: the room, each mod on and what it changes, the clock and the weather.</summary>
        public string WhatsOn()
        {
            if (Blind)
                return Desk.Room(null) + "\nThis game can't tell the tools what those mods change, or its clock and weather, yet: what the tools read is the base rules.";
            var now = Now();
            if (now == null) return NoWorld;
            var sb = new StringBuilder(Describe(now.Room, now.Tick));
            sb.Append(now.Room.Mods.Count == 0 ? "" : "\nThey stack, and where two change the same value the later wins. What each changes from what was under it:");
            for (int i = 1; i < layers.Count; i++)
            {
                var changes = LayerChanges(i).ToList();
                sb.Append($"\n{i}. {layers[i].Mod.Title}: {(changes.Count == 0 ? "nothing that differs from what was under it" : string.Join("; ", changes))}.");
            }
            return sb.Append('\n').Append(TimeWords(now)).Append('\n').Append(WeatherWords(now)).ToString();
        }

        const string NoWorld = "There's no world running: the game is between scenes. Try again in a moment.";

        // Mods on after this one that name a field it does: their values stand once it's gone.
        string StillHeld(string mod)
        {
            var mods = Now()?.Room.Mods;
            int at = mods == null ? -1 : mods.FindIndex(m => m.Id == mod);
            if (at < 0) return "";
            var mine = FieldsOf(mods[at].Json);
            var later = mods.Skip(at + 1).Where(m => FieldsOf(m.Json).Overlaps(mine)).Select(m => m.Title).ToList();
            return later.Count == 0 ? "" : $"\n{string.Join(" and ", later)} sets some of the same values, so what it set stands.";
        }

        // Every value a patch sets, as "table id field.path"; a row a mod replaces whole counts as one.
        static HashSet<string> FieldsOf(string json)
        {
            var patch = ModPatch.Parse(json).Json;
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var table in ModPatch.Tables.Append("elements"))
                if (patch[table] is JObject named)
                    foreach (var p in named.Properties()) Leaves(table + " " + p.Name, p.Value, fields);
            foreach (var table in new[] { "spells", "critters" })
                if (patch[table] is JArray list)
                    foreach (var s in list) Leaves(table + " " + Str(s["id"]), s, fields);
            foreach (var list in new[] { "add", "addEntity" })
                if (patch["reactions"]?[list] is JArray rules)
                    foreach (var r in rules) Leaves("reactions " + Str(r["id"]), r, fields);
            foreach (var p in (patch["world"]?["buildings"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) fields.Add("building " + p.Name);
            return fields;
        }

        static void Leaves(string path, JToken t, HashSet<string> into)
        {
            if (!(t is JObject o))
            {
                into.Add(path);
                return;
            }
            // A scoped look adds to what's under it, so nothing holds it.
            foreach (var p in o.Properties().Where(p => p.Name != "id" && p.Name != "scoped")) Leaves(path + "." + p.Name, p.Value, into);
        }
    }
}
