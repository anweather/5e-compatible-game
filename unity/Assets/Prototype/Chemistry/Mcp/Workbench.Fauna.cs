// The tools for ambient life (#61, #72): add_species writes a new row of rules/fauna.json
// as a variant of a species there is, so it moves and behaves as that one does, and
// set_species changes a row's size and behaviour. The rows are the view's: they stay out
// of the fingerprint and off the cells (Fauna.cs), and Rules.CheckFauna checks them.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

#pragma warning disable S107 // a tool's parameters are its schema (RulesTools)

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        const string FaunaTable = FaunaTables.SpeciesKey;

        /// <summary>What a variant reads from its own row's behave; the rest it takes from the species it's built on.</summary>
        static readonly string[] VariantBehave = { "active", "graze", "thirst", "play", "laze", "hunger", "chase", "hunts" };

        // What a new species takes from the one it's based on, as a variant leaves them to the reader's defaults.
        static readonly string[] Inherited = { "coat", "coats", "hair", "vary", "behave" };

        /// <summary>A new species built on one there is: its body, gait, wandering and fleeing, with its own look, size and habits.</summary>
        public Draft AddSpecies(string draftId, string id, string name, string basedOn, double? scale, string color, string accent, double? glow, string behave, double? density)
        {
            id = Required(id, "id");
            basedOn = Required(basedOn, "basedOn");
            var draft = DraftOf(draftId);
            if (Current(draft, FaunaTable, id) != null)
                throw new RulesToolException($"There is already a species called {id}; set_species changes its size and behaviour.");
            if (!(Current(draft, FaunaTable, basedOn) is JObject under))
                throw new RulesToolException($"There is no species {basedOn}; list fauna shows them.");
            var row = new JObject { ["name"] = Required(name, "name"), ["variantOf"] = basedOn };
            foreach (var key in Inherited.Where(k => under[k] != null)) row[key] = under[key].DeepClone();
            row["scale"] = Rounded(Positive(scale ?? 1, "scale"));
            Colour(row, color, accent, glow);
            if (!string.IsNullOrWhiteSpace(behave)) row["behave"] = Rules.MergedEntry(row["behave"], Behave(basedOn, behave, true));
            double perHectare = density ?? Math.Max(0.2, (double?)under["spawn"]?["density"] / 2 ?? 0.5);
            if (perHectare < 0) throw new RulesToolException("density is animals per hectare in the wilds, 0 or more; 0 means it never comes out on its own.");
            if (perHectare > 0) row["spawn"] = new JObject { ["density"] = Rounded(perHectare) };
            Section(draft, FaunaTable)[id] = row;
            draft.Changed.Add($"{FaunaTable} {id}");
            return draft;
        }

        /// <summary>A species' size, against its normal size, and its behaviour, keeping the rest of its row.</summary>
        public Draft SetSpecies(string draftId, string id, double? scale, string behave)
        {
            id = Required(id, "id");
            var draft = DraftOf(draftId);
            if (!(Current(draft, FaunaTable, id) is JObject now)) throw new RulesToolException($"There is no species {id}; list fauna shows them.");
            if (!scale.HasValue && string.IsNullOrWhiteSpace(behave)) throw new RulesToolException("set_species changes scale, behave or both; give one.");
            var entry = Section(draft, FaunaTable)[id] as JObject ?? new JObject();
            if (scale.HasValue)
            {
                double times = Positive(scale.Value, "scale");
                // What "normal" is: the base rules' row, or the row as whoever added it wrote it.
                var normal = baseRaw[FaunaTable][id] as JObject ?? (EntryIn(Raw, FaunaTable, id) as JObject) ?? now;
                if (Str(now["variantOf"]) != null) entry["scale"] = Rounded(((double?)normal["scale"] ?? 1) * times);
                else entry["size"] = new JArray(((normal["size"] as JArray)?.Select(s => (double)s) ?? new[] { 0.9, 1.1 }).Select(s => Rounded(s * times)));
            }
            if (!string.IsNullOrWhiteSpace(behave)) entry["behave"] = Rules.MergedEntry(entry["behave"], Behave(id, behave, Str(now["variantOf"]) != null));
            Section(draft, FaunaTable)[id] = entry;
            draft.Changed.Add($"{FaunaTable} {id}");
            return draft;
        }

        // A behave object of keys the row's reader takes: a variant moves and keeps company as the species it's built on.
        JObject Behave(string species, string json, bool variant)
        {
            if (!(Value(json) is JObject given)) throw new RulesToolException("behave is a JSON object, such as {\"active\": \"always\", \"play\": 0.6}.");
            var takes = variant ? VariantBehave : new[] { "wander", "flee", "idle", "group" }.Concat(VariantBehave).ToArray();
            var unknown = given.Properties().FirstOrDefault(p => !takes.Contains(p.Name));
            if (unknown != null)
            {
                string why = variant ? $" (a species built on another wanders, flees and keeps company as {InForce.Fauna.BodyOf(species)?.Id ?? species} does)" : "";
                throw new RulesToolException($"behave takes {string.Join(", ", takes)}; {unknown.Name} isn't one of them{why}.");
            }
            return given;
        }

        // A colour over the coat it took: one coat in the colours given, the accent kept if none is.
        static void Colour(JObject row, string color, string accent, double? glow)
        {
            if (string.IsNullOrWhiteSpace(color) && string.IsNullOrWhiteSpace(accent) && !glow.HasValue) return;
            var coat = (row["coat"] as JObject)?.DeepClone() as JObject ?? (row["coats"] as JArray)?.OfType<JObject>().FirstOrDefault()?.DeepClone() as JObject ?? new JObject();
            coat.Remove("weight");
            coat.Remove("name");
            if (!string.IsNullOrWhiteSpace(color)) coat["base"] = Rgb(color, "color");
            if (!string.IsNullOrWhiteSpace(accent)) coat["accent"] = Rgb(accent, "accent");
            if (glow.HasValue) coat["glow"] = Positive(glow.Value, "glow", true);
            row["coat"] = coat;
            row.Remove("coats");
        }

        static JArray Rgb(string json, string what)
        {
            if (!(Value(json) is JArray rgb && rgb.Count == 3 && rgb.All(c => (c.Type == JTokenType.Integer || c.Type == JTokenType.Float) && (double)c >= 0 && (double)c <= 1)))
                throw new RulesToolException($"{what} is [r, g, b], each from 0 to 1, such as [0.9, 0.9, 0.95].");
            return rgb;
        }

        static double Positive(double value, string what, bool zero = false)
        {
            if (double.IsNaN(value) || value < 0 || (!zero && value == 0)) throw new RulesToolException($"{what} is a number above 0{(zero ? " or 0" : "")}, not {Num(value)}.");
            return value;
        }

        static double Rounded(double d) => Math.Round(d, 3);
    }
}
