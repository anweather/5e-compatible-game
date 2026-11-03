// A graft (docs/design/bodies-and-surfaces.md section 2): a species row built from other
// species' slots by "from", flattened into an ordinary row when it's read, so the view, the
// checks and describe see plain slots. Each donor gives all its slots, or those it takes
// (with every slot hung on them), or all but those it drops (and theirs). The row's own parts
// come first; a donor's slot whose id is taken gets the donor's id as a prefix. A donor's
// root slots move together so the first sits at "at" (a mirrored pair keeps its spread
// across), turn by "dir" and, with every slot below them, grow by "scale". "coat": "own"
// stays on each slot with its donor, for the view. body, move and behave may say "from" a
// species and override its fields. View only, like every fauna row: no cell reads this.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

#pragma warning disable S1168 // a JToken is a collection to the analyser; null here means "no such row"

namespace Chem
{
    internal static class FaunaGraft
    {
        internal const string FromKey = "from";
        internal static readonly string[] DonorKeys = { "species", "take", "drop", "at", "dir", "scale", "coat" };
        internal static readonly string[] Sections = { "body", "move", "behave" };
        // What a graft that gives no colours of its own wears: its first donor's.
        static readonly string[] Colours = { "coat", "coats", "hair", "vary" };

        /// <summary>Whether a row is built from others: "from" on it, or on its body, move or behave.</summary>
        internal static bool IsGraft(JObject row) => row?[FromKey] != null || Sections.Any(k => row?[k]?[FromKey] != null);

        /// <summary>
        /// The species' row with its grafts flattened, or the row itself if it has none; null if there's no row.
        /// What can't be built (an unknown donor or slot, a loop) is left out and said in <paramref name="errors"/>.
        /// </summary>
        internal static JObject Flatten(JObject species, string id, List<string> errors = null) =>
            new Flattening(species, errors).Row(id, true);

        sealed class Flattening
        {
            readonly JObject species;
            readonly List<string> errors;
            readonly List<string> path = new List<string>();

            public Flattening(JObject species, List<string> errors)
            {
                this.species = species;
                this.errors = errors;
            }

            void Say(bool top, string message)
            {
                if (top) errors?.Add(message);
            }

            public JObject Row(string id, bool top)
            {
                if (id == null || !(species[id] is JObject row)) return null;
                if (!IsGraft(row)) return row;
                if (path.Contains(id))
                {
                    errors?.Add($"species {path[0]}: from goes round, through {string.Join(", ", path.Append(id))}");
                    return null;
                }
                path.Add(id);
                var flat = Build(id, row, top);
                path.RemoveAt(path.Count - 1);
                return flat;
            }

            JObject Build(string id, JObject row, bool top)
            {
                var flat = new JObject(row.Properties().Where(p => p.Name != FromKey).Select(p => new JProperty(p.Name, p.Value.DeepClone())));
                var slots = row["parts"] is JObject own ? (JObject)own.DeepClone() : new JObject();
                var donors = (row[FromKey] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                foreach (var donor in donors) AddDonor(id, slots, donor, top);
                if (row[FromKey] != null) flat["parts"] = slots;
                foreach (var key in Sections) Section(id, flat, key, top);
                var first = donors.Count > 0 ? Base((string)donors[0]["species"], false) : null;
                if (first != null && row["coat"] == null && row["coats"] == null)
                    foreach (var key in Colours.Where(k => first[k] != null && row[k] == null)) flat[key] = first[key].DeepClone();
                return flat;
            }

            // The row a donor's slots, body and gait come from: a variant's are the species it's built on.
            JObject Base(string id, bool top)
            {
                for (int i = 0; i <= FaunaTables.MaxVariantDepth && id != null; i++)
                {
                    if (!(species[id] is JObject row)) return null;
                    if (row["variantOf"] == null) return Row(id, top);
                    id = (string)row["variantOf"];
                }
                return null;
            }

            void Section(string id, JObject flat, string key, bool top)
            {
                if (!(flat[key] is JObject given) || given[FromKey] == null) return;
                string from = (string)given[FromKey];
                var donor = Base(from, false);
                if (donor == null) Say(top, $"species {id}: {key} from names unknown species {from}");
                var merged = donor?[key] is JObject under ? (JObject)under.DeepClone() : new JObject();
                foreach (var p in given.Properties().Where(p => p.Name != FromKey)) merged[p.Name] = p.Value.DeepClone();
                flat[key] = merged;
            }

            void AddDonor(string id, JObject slots, JObject donor, bool top)
            {
                string name = (string)donor["species"];
                var row = Base(name, false);
                if (row == null)
                {
                    Say(top, $"species {id}: from names unknown species {name ?? "(none)"}");
                    return;
                }
                var all = (row["parts"] as JObject)?.Properties().ToList() ?? new List<JProperty>();
                var picked = Pick(id, name, all, donor, top);
                var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
                var place = new Placing(donor, all.Where(p => picked.Contains(p.Name)).FirstOrDefault(p => IsRoot(p, picked)));
                foreach (var p in all.Where(p => picked.Contains(p.Name)))
                {
                    string slotId = Unclashed(slots, name, p.Name);
                    renamed[p.Name] = slotId;
                    slots[slotId] = Copy(p, picked, renamed, place, donor, name);
                }
            }

            // The donor's slots that come: those taken and every slot below them, or all but those dropped and theirs.
            HashSet<string> Pick(string id, string name, List<JProperty> all, JObject donor, bool top)
            {
                var take = Names(donor["take"]);
                var drop = Names(donor["drop"]);
                foreach (var n in take.Concat(drop).Where(n => all.All(p => p.Name != n)))
                    Say(top, $"species {id}: {name} has no part {n} to take or drop; describe fauna {name} lists its slots");
                var picked = new HashSet<string>(StringComparer.Ordinal);
                bool taking = donor["take"] != null;
                foreach (var p in all)
                {
                    string on = (string)p.Value["on"];
                    bool comes = taking ? take.Contains(p.Name) || (on != null && picked.Contains(on)) : !drop.Contains(p.Name);
                    if (!taking && on != null && !picked.Contains(on) && all.Any(q => q.Name == on)) comes = false;
                    if (comes) picked.Add(p.Name);
                }
                return picked;
            }

            static List<string> Names(JToken t) => (t as JArray)?.Select(x => x.Type == JTokenType.String ? (string)x : null).Where(x => x != null).ToList() ?? new List<string>();

            static bool IsRoot(JProperty p, HashSet<string> picked) => !(p.Value["on"] is JToken on) || on.Type != JTokenType.String || !picked.Contains((string)on);

            static string Unclashed(JObject slots, string donor, string id)
            {
                if (slots[id] == null) return id;
                string prefixed = $"{donor}-{id}";
                int n = 1;
                string unique = prefixed;
                while (slots[unique] != null) unique = $"{prefixed}-{++n}";
                return unique;
            }

            static JObject Copy(JProperty p, HashSet<string> picked, Dictionary<string, string> renamed, Placing place, JObject donor, string name)
            {
                var slot = (JObject)p.Value.DeepClone();
                if (IsRoot(p, picked))
                {
                    slot.Remove("on");
                    place.Root(slot);
                }
                else
                {
                    slot["on"] = renamed[(string)p.Value["on"]];
                    place.Below(slot);
                }
                if ((string)donor["coat"] == "own")
                {
                    slot["coat"] = "own";
                    slot["donor"] = name;
                }
                return slot;
            }
        }

        // Where a donor's slots go: its roots moved together so the first sits at "at", turned by "dir", all grown by "scale".
        sealed class Placing
        {
            readonly double[] shift, turn;
            readonly double scale;

            public Placing(JObject donor, JProperty firstRoot)
            {
                var at = Triple(donor["at"]);
                if (at != null)
                {
                    var from = Triple(firstRoot?.Value["at"]) ?? new double[3];
                    shift = new[] { at[0] - from[0], at[1] - from[1], at[2] - from[2] };
                }
                turn = Triple(donor["dir"]);
                scale = IsNumber(donor["scale"]) ? (double)donor["scale"] : 1;
            }

            public void Root(JObject slot)
            {
                bool mirror = (bool?)slot["mirror"] ?? false;
                if (shift != null)
                {
                    var at = Triple(slot["at"]) ?? new double[3];
                    slot["at"] = Rounded(new[] { mirror ? at[0] : at[0] + shift[0], at[1] + shift[1], at[2] + shift[2] });
                }
                if (turn != null)
                {
                    var dir = Triple(slot["dir"]) ?? new double[3];
                    slot["dir"] = Rounded(new[] { dir[0] + turn[0], dir[1] + turn[1], dir[2] + turn[2] });
                }
                Grow(slot, false);
            }

            public void Below(JObject slot) => Grow(slot, true);

            // A slot below another sits metres from its end, so it moves out as its parent grows.
            void Grow(JObject slot, bool below)
            {
                if (Math.Abs(scale - 1) < 1e-9) return;
                slot["scale"] = Math.Round((IsNumber(slot["scale"]) ? (double)slot["scale"] : 1) * scale, 6);
                if (below && Triple(slot["at"]) is double[] at) slot["at"] = Rounded(at.Select(v => v * scale).ToArray());
            }

            static JArray Rounded(double[] v) => new JArray(v.Select(x => Math.Round(x, 6)));

            static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);

            // A triple of numbers, or null where there's none to read.
            static double[] Triple(JToken t) => t is JArray a && a.Count == 3 && a.All(IsNumber) ? a.Select(x => (double)x).ToArray() : null;
        }
    }
}
