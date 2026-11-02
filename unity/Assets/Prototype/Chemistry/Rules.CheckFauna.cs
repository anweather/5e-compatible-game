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
            static readonly string[] SpeciesKeys = { "name", "variantOf", "scale", "size", "body", "coat", "coats", "hair", "vary", "parts", "move", "behave", "spawn" };
            static readonly string[] BodyKeys = { "length", "radius", "taper", "flatten" };
            static readonly string[] CoatKeys = { "name", "base", "accent", "glow", "weight" };
            static readonly string[] SlotKeys = { "part", "on", "at", "dir", "mirror", "phase", "mirrorPhase", "scale", "thickness", "style" };
            static readonly string[] MoveKeys = { "walk", "run", "turn", "rock", "bob", "bobs", "stride", "upright" };
            static readonly string[] BehaveKeys = { "wander", "flee", "idle", "group", "active", "graze", "thirst", "play", "laze", "hunger", "chase", "hunts" };
            static readonly string[] SpawnKeys = { "density", "nearWater", "among", "share" };
            static readonly string[] PartKeys = { "role", "segments", "spring", "ripple", "follows", "poses" };
            static readonly string[] VaryKeys = { "shade", "warm", "girth", "mix" };
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
                Number(row["scale"], $"{who}: scale", 0);
                Pair(who, "size", row["size"]);
                Colours(who, row);
                Spawn(who, row["spawn"], species);
                Vary(who, row["vary"]);
                Behave(who, row["behave"], species);
                Variant(id, row, species);
                if (row["variantOf"] != null && row["parts"] != null) Errors.Add($"{who} is a variant, so it takes its parts from {Show(row["variantOf"])}");
                if (row["parts"] != null) Slots(who, row["parts"], parts);
            }

            void Colours(string who, JObject row)
            {
                if (row["coat"] != null) Coat(who, row["coat"]);
                foreach (var c in row["coats"] as JArray ?? new JArray()) Coat(who, c);
                foreach (var c in row["hair"] as JArray ?? new JArray())
                    if (!IsColour(c)) Errors.Add($"{who}: a hair colour must be \"#rrggbb\" or [r, g, b]");
            }

            void Spawn(string who, JToken spawn, JObject species)
            {
                OnlyKeys($"{who}: spawn", spawn, SpawnKeys);
                Number(spawn?["density"], $"{who}: spawn density", 0);
                Number(spawn?["share"], $"{who}: spawn share", 0);
                if (spawn?["among"] != null && species[Show(spawn["among"])] == null) Errors.Add($"{who} comes out among unknown species {Show(spawn["among"])}");
            }

            void Vary(string who, JToken vary)
            {
                OnlyKeys($"{who}: vary", vary, VaryKeys);
                foreach (var key in new[] { "shade", "warm", "girth" })
                    if (vary?[key] != null && (!IsNumber(vary[key]) || (double)vary[key] < 0 || (double)vary[key] >= 1)) Errors.Add($"{who}: vary {key} must be a share, from 0 to under 1");
            }

            void Behave(string who, JToken behave, JObject species)
            {
                OnlyKeys($"{who}: behave", behave, BehaveKeys);
                if (behave?["active"] != null && !FaunaTables.Actives.Contains(Show(behave["active"]))) Errors.Add($"{who}: active must be one of {string.Join(", ", FaunaTables.Actives)}");
                foreach (var h in behave?["hunts"] as JArray ?? new JArray())
                    if (species[Show(h)] == null) Errors.Add($"{who} hunts unknown species {Show(h)}");
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
                    Slot($"{who}: part {slot.Name}", slot.Value, parts, earlier);
                    earlier.Add(slot.Name);
                }
            }

            void Slot(string where, JToken slot, JObject parts, HashSet<string> earlier)
            {
                OnlyKeys(where, slot, SlotKeys);
                if (parts[Show(slot["part"])] == null) Errors.Add($"{where} names unknown part {Show(slot["part"])}");
                if (slot["on"] != null && !earlier.Contains(Show(slot["on"]))) Errors.Add($"{where} hangs from {Show(slot["on"])}, which isn't a part before it");
                foreach (var key in new[] { "at", "dir" })
                    if (slot[key] != null && !(slot[key] is JArray a && a.Count == 3 && a.All(IsNumber))) Errors.Add($"{where}: {key} must be [x, y, z]");
                Number(slot["scale"], $"{where}: scale", 0);
                Number(slot["thickness"], $"{where}: thickness", 0);
                if (slot["style"] != null && !IsWholeAtLeast(slot["style"], 0)) Errors.Add($"{where}: style must be a whole number, 0 or more");
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
                foreach (var s in segments) Segment(who, s);
                Poses(who, row["poses"] as JObject);
            }

            void Segment(string who, JToken s)
            {
                OnlyKeys($"{who}: a segment", s, SegmentKeys);
                Number(s["length"], $"{who}: a segment's length", 0);
                if (s["radii"] != null) Pair(who, "a segment's radii", s["radii"]);
                if (s["colour"] != null && !IsColour(s["colour"])) Errors.Add($"{who}: a segment's colour must be \"#rrggbb\" or [r, g, b]");
            }

            void Poses(string who, JObject poses)
            {
                foreach (var pose in poses?.Properties() ?? Enumerable.Empty<JProperty>())
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
