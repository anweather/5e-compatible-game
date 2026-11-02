// Checks the creature tables (Fauna.cs): every name a row gives is a row or a word
// the view knows, and every key is one the view reads, so a core field can't hide
// in a view table. Refused here, a bad row never reaches a body.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class Rules
    {
        sealed partial class Checker
        {
            static readonly string[] SpeciesKeys = { "name", "variantOf", "scale", "size", "body", "coat", "coats", "hair", "parts", "move", "behave", "spawn" };
            static readonly string[] BodyKeys = { "length", "radius", "taper", "flatten" };
            static readonly string[] CoatKeys = { "name", "base", "accent", "glow", "weight" };
            static readonly string[] SlotKeys = { "part", "on", "at", "dir", "mirror", "phase", "mirrorPhase", "scale", "thickness", "style" };
            static readonly string[] MoveKeys = { "walk", "run", "turn", "rock", "bob", "bobs", "stride", "upright" };
            static readonly string[] BehaveKeys = { "wander", "flee", "idle", "group", "active", "graze", "thirst", "play", "laze", "hunger", "chase", "hunts" };
            static readonly string[] SpawnKeys = { "density", "nearWater", "among", "share" };
            static readonly string[] PartKeys = { "role", "segments", "spring", "ripple", "follows", "poses" };
            static readonly string[] SegmentKeys = { "length", "radii", "flatten", "accent", "colour", "glow", "hair" };

            public void Fauna()
            {
                var parts = r[FaunaTables.PartsKey] as JObject ?? new JObject();
                var species = r[FaunaTables.SpeciesKey] as JObject ?? new JObject();
                foreach (var p in parts.Properties()) PartRow(p.Name, p.Value as JObject);
                foreach (var p in species.Properties()) SpeciesRow(p.Name, p.Value as JObject, species, parts);
            }

            void SpeciesRow(string id, JObject row, JObject species, JObject parts)
            {
                string who = $"species {id}";
                if (row == null)
                {
                    Errors.Add($"{who} must be an object");
                    return;
                }
                OnlyKeys(who, row, SpeciesKeys);
                OnlyKeys($"{who}: body", row["body"], BodyKeys);
                OnlyKeys($"{who}: move", row["move"], MoveKeys);
                OnlyKeys($"{who}: behave", row["behave"], BehaveKeys);
                OnlyKeys($"{who}: spawn", row["spawn"], SpawnKeys);
                Number(row["scale"], $"{who}: scale", 0);
                Number(row["spawn"]?["density"], $"{who}: spawn density", 0);
                Pair(who, "size", row["size"]);
                if (row["coat"] != null) Coat(who, row["coat"]);
                foreach (var c in row["coats"] as JArray ?? new JArray()) Coat(who, c);
                foreach (var c in row["hair"] as JArray ?? new JArray())
                    if (!IsColour(c)) Errors.Add($"{who}: a hair colour must be \"#rrggbb\" or [r, g, b]");
                if (row["spawn"]?["among"] != null && species[Show(row["spawn"]["among"])] == null) Errors.Add($"{who} comes out among unknown species {Show(row["spawn"]["among"])}");
                Number(row["spawn"]?["share"], $"{who}: spawn share", 0);
                string active = Show(row["behave"]?["active"]);
                if (row["behave"]?["active"] != null && !FaunaTables.Actives.Contains(active)) Errors.Add($"{who}: active must be one of {string.Join(", ", FaunaTables.Actives)}");
                foreach (var h in row["behave"]?["hunts"] as JArray ?? new JArray())
                    if (species[Show(h)] == null) Errors.Add($"{who} hunts unknown species {Show(h)}");
                Variant(id, row, species);
                if (row["variantOf"] != null && row["parts"] != null) Errors.Add($"{who} is a variant, so it takes its parts from {Show(row["variantOf"])}");
                if (row["parts"] != null) Slots(who, row["parts"], parts);
            }

            void Variant(string id, JObject row, JObject species)
            {
                var at = row;
                int steps = 0;
                while (at?["variantOf"] != null)
                {
                    string next = Show(at["variantOf"]);
                    if (species[next] == null)
                    {
                        Errors.Add($"species {id}: variantOf names unknown species {next}");
                        return;
                    }
                    if (++steps > FaunaTables.MaxVariantDepth)
                    {
                        Errors.Add($"species {id}: variantOf goes round, or further than {FaunaTables.MaxVariantDepth} steps");
                        return;
                    }
                    at = species[next] as JObject;
                }
            }

            void Slots(string who, JToken slots, JObject parts)
            {
                if (!(slots is JObject table))
                {
                    Errors.Add($"{who}: parts must be an object of slots by id");
                    return;
                }
                var earlier = new HashSet<string>();
                foreach (var slot in table.Properties())
                {
                    string where = $"{who}: part {slot.Name}";
                    OnlyKeys(where, slot.Value, SlotKeys);
                    if (parts[Show(slot.Value["part"])] == null) Errors.Add($"{where} names unknown part {Show(slot.Value["part"])}");
                    if (slot.Value["on"] != null && !earlier.Contains(Show(slot.Value["on"]))) Errors.Add($"{where} hangs from {Show(slot.Value["on"])}, which isn't a part before it");
                    foreach (var key in new[] { "at", "dir" })
                        if (slot.Value[key] != null && !(slot.Value[key] is JArray a && a.Count == 3 && a.All(IsNumber))) Errors.Add($"{where}: {key} must be [x, y, z]");
                    Number(slot.Value["scale"], $"{where}: scale", 0);
                    Number(slot.Value["thickness"], $"{where}: thickness", 0);
                    if (slot.Value["style"] != null && !IsWholeAtLeast(slot.Value["style"], 0)) Errors.Add($"{where}: style must be a whole number, 0 or more");
                    earlier.Add(slot.Name);
                }
            }

            void Coat(string who, JToken coat)
            {
                OnlyKeys($"{who}: coat", coat, CoatKeys);
                foreach (var key in new[] { "base", "accent" })
                    if (coat[key] != null && !IsColour(coat[key])) Errors.Add($"{who}: a coat's {key} must be \"#rrggbb\" or [r, g, b]");
                Number(coat["weight"], $"{who}: a coat's weight", 0);
            }

            void PartRow(string id, JObject row)
            {
                string who = $"part {id}";
                if (row == null)
                {
                    Errors.Add($"{who} must be an object");
                    return;
                }
                OnlyKeys(who, row, PartKeys);
                if (!FaunaTables.Roles.Contains(Show(row["role"]))) Errors.Add($"{who}: role must be one of {string.Join(", ", FaunaTables.Roles)}");
                if (row["follows"] != null && Show(row["follows"]) != "ground") Errors.Add($"{who}: follows can only be ground");
                if (!(row["segments"] is JArray segments) || segments.Count == 0)
                {
                    Errors.Add($"{who} needs segments");
                    return;
                }
                foreach (var s in segments)
                {
                    OnlyKeys($"{who}: a segment", s, SegmentKeys);
                    Number(s["length"], $"{who}: a segment's length", 0);
                    if (s["radii"] != null) Pair(who, "a segment's radii", s["radii"]);
                    if (s["colour"] != null && !IsColour(s["colour"])) Errors.Add($"{who}: a segment's colour must be \"#rrggbb\" or [r, g, b]");
                }
                foreach (var pose in (row["poses"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    if (!PartDef.PoseKeys.Contains(pose.Name)) Errors.Add($"{who}: {pose.Name} isn't a pose; the poses are {string.Join(", ", PartDef.PoseKeys)}");
                    if (!(pose.Value is JArray joints) || !joints.All(j => j is JArray a && a.Count == 3 && a.All(IsNumber))) Errors.Add($"{who}: pose {pose.Name} must be a list of [x, y, z]");
                }
            }

            void OnlyKeys(string who, JToken group, string[] keys)
            {
                if (group == null) return;
                if (!(group is JObject o))
                {
                    Errors.Add($"{who} must be an object");
                    return;
                }
                foreach (var p in o.Properties().Where(p => !keys.Contains(p.Name))) Errors.Add($"{who}: {p.Name} isn't a key the view reads; it reads {string.Join(", ", keys)}");
            }

            void Pair(string who, string what, JToken t)
            {
                if (t != null && !(t is JArray a && a.Count == 2 && a.All(IsNumber))) Errors.Add($"{who}: {what} must be [low, high]");
            }

            static bool IsColour(JToken t) => t is JArray a ? a.Count == 3 && a.All(IsNumber) : FaunaTables.IsHex(t, out _);
        }
    }
}
