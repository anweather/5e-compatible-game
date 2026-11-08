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

        /// <summary>
        /// A new species built on one there is (its body, gait, wandering and fleeing, with its own look, size and habits),
        /// or grafted from others' slots by <paramref name="from"/> (FaunaGraft), moving and behaving as its first donor.
        /// </summary>
        public Draft AddSpecies(string draftId, string id, string name, string basedOn, double? scale, string color, string accent, double? glow, string behave, double? density, string from = null)
        {
            id = Required(id, "id");
            var draft = DraftOf(draftId);
            if (Current(draft, FaunaTable, id) != null)
                throw new RulesToolException($"There is already a species called {id}; set_species changes its size and behaviour.");
            if (!string.IsNullOrWhiteSpace(from))
            {
                if (!string.IsNullOrWhiteSpace(basedOn)) throw new RulesToolException("A species is built on one (basedOn) or grafted from donors (from), not both.");
                return AddGraft(draft, id, Required(name, "name"), Donors(draft, from), scale, (color, accent, glow), behave, density);
            }
            basedOn = Required(basedOn, "basedOn");
            if (!(Current(draft, FaunaTable, basedOn) is JObject under))
                throw new RulesToolException($"There is no species {basedOn}; list fauna shows them.");
            var row = new JObject { ["name"] = Required(name, "name"), ["variantOf"] = basedOn };
            foreach (var key in Inherited.Where(k => under[k] != null)) row[key] = under[key].DeepClone();
            // A variant's reader takes only VariantBehave from its own behave; wander, idle, flee and group stay
            // the base's (CreatureBody), so copies of them would be dead and go stale. describe says them as the base's.
            if (row["behave"] is JObject copied)
            {
                foreach (var p in copied.Properties().Where(p => !VariantBehave.Contains(p.Name)).ToList()) p.Remove();
                if (!copied.HasValues) row.Remove("behave");
            }
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
        public Draft SetSpecies(string draftId, string id, double? scale, string behave, string from = null, string color = null, string accent = null, double? glow = null, double? density = null, double? speed = null)
        {
            id = Required(id, "id");
            var draft = DraftOf(draftId);
            if (!(Current(draft, FaunaTable, id) is JObject now)) throw new RulesToolException($"There is no species {id}; list fauna shows them.");
            bool grafting = !string.IsNullOrWhiteSpace(from);
            bool recoloured = !string.IsNullOrWhiteSpace(color) || !string.IsNullOrWhiteSpace(accent) || glow.HasValue;
            if (!scale.HasValue && string.IsNullOrWhiteSpace(behave) && !grafting && !recoloured && !density.HasValue && !speed.HasValue) throw new RulesToolException("set_species changes scale, speed, behave, from, color, accent, glow, density or some of them; give one.");
            var entry = Section(draft, FaunaTable)[id] as JObject ?? new JObject();
            if (grafting)
            {
                if (Str(now["variantOf"]) != null) throw new RulesToolException($"{id} is built on {Str(now["variantOf"])}, so its slots are that one's; add_species with from makes a graft of its own.");
                entry[FaunaGraft.FromKey] = Donors(draft, from);
            }
            if (scale.HasValue)
            {
                double times = Positive(scale.Value, "scale");
                // What "normal" is: the base rules' row, or the row as whoever added it wrote it.
                var normal = baseRaw[FaunaTable][id] as JObject ?? (EntryIn(Raw, FaunaTable, id) as JObject) ?? now;
                if (Str(now["variantOf"]) != null) entry["scale"] = Rounded(((double?)normal["scale"] ?? 1) * times);
                else entry["size"] = new JArray(((normal["size"] as JArray)?.Select(s => (double)s) ?? new[] { 0.9, 1.1 }).Select(s => Rounded(s * times)));
            }
            if (!string.IsNullOrWhiteSpace(behave)) entry["behave"] = Rules.MergedEntry(entry["behave"], Behave(id, behave, Str(now["variantOf"]) != null));
            if (speed.HasValue) entry["move"] = Rules.MergedEntry(entry["move"], Gait(id, now, speed.Value));
            if (recoloured) Recolour(entry, now, color, accent, glow);
            if (density.HasValue)
            {
                if (density < 0) throw new RulesToolException("density is animals per hectare in the wilds, 0 or more; 0 means it never comes out on its own.");
                entry["spawn"] = new JObject { ["density"] = Rounded(density.Value) };
            }
            Section(draft, FaunaTable)[id] = entry;
            draft.Changed.Add($"{FaunaTable} {id}");
            return draft;
        }

        // A species' walk and run at speed times its normal gait (the base rules' row, or the row as whoever added it wrote it), within the caps.
        JObject Gait(string id, JObject now, double speed)
        {
            if (Str(now["variantOf"]) != null)
                throw new RulesToolException($"{id} is built on {Str(now["variantOf"])} and moves at its gait, so its speed is that one's: set_species {Str(now["variantOf"])} speed changes both, or add_species with from makes one that moves on its own.");
            double times = Math.Max(Budgets.Number("fauna.minSpeed", 0.2), Math.Min(Budgets.Number("fauna.maxSpeed", 5), Positive(speed, "speed")));
            var gait = (FaunaGraft.Flatten(baseRaw[FaunaTable] as JObject, id) ?? FaunaGraft.Flatten(Raw[FaunaTable] as JObject, id) ?? now)["move"];
            double walk = (double?)gait?["walk"] ?? 1.4, run = (double?)gait?["run"] ?? 6;
            return new JObject { ["walk"] = Rounded(walk * times), ["run"] = Rounded(run * times) };
        }

        /// <summary>What set_species says of a speed: cut to the caps, how fast that is, and that the player should hear it.</summary>
        public string SpeedNote(double speed)
        {
            double least = Budgets.Number("fauna.minSpeed", 0.2), most = Budgets.Number("fauna.maxSpeed", 5);
            double times = Math.Max(least, Math.Min(most, speed));
            string cut = Math.Abs(times - speed) > 1e-9 ? $" {Num(speed)} times is past what an animal may have, so it's {Num(times)} times: {Num(least)} to {Num(most)} times its normal speed. Tell the player." : "";
            return $"\nIt walks and runs at {Num(times)} times its normal speed (its normal: the base rules' row), once it's applied.{cut}";
        }

        // A graft's row: its donors' slots, and its first donor's body, gait, habits, colours and a share of its number.
        Draft AddGraft(Draft draft, string id, string name, JArray donors, double? scale, (string color, string accent, double? glow) look, string behave, double? density)
        {
            string first = Str(donors[0]["species"]);
            var under = (JObject)Current(draft, FaunaTable, first);
            var basis = InForce.Fauna.BodyOf(first) ?? InForce.Fauna.Species(first);
            var row = new JObject { ["name"] = name, ["size"] = new JArray((basis?.Size ?? new[] { 0.9, 1.1 }).Select(v => Rounded(v * Positive(scale ?? 1, "scale")))) };
            foreach (var key in FaunaGraft.Sections) row[key] = new JObject { [FaunaGraft.FromKey] = first };
            row[FaunaGraft.FromKey] = donors;
            foreach (var key in Inherited.Where(k => k != "behave" && under[k] != null)) row[key] = under[key].DeepClone();
            Colour(row, look.color, look.accent, look.glow);
            if (!string.IsNullOrWhiteSpace(behave)) row["behave"] = Rules.MergedEntry(row["behave"], Behave(id, behave, false));
            double perHectare = density ?? Math.Max(0.2, (double?)under["spawn"]?["density"] / 2 ?? 0.5);
            if (perHectare < 0) throw new RulesToolException("density is animals per hectare in the wilds, 0 or more; 0 means it never comes out on its own.");
            if (perHectare > 0) row["spawn"] = new JObject { ["density"] = Rounded(perHectare) };
            Section(draft, FaunaTable)[id] = row;
            draft.Changed.Add($"{FaunaTable} {id}");
            return draft;
        }

        // A from list: donors as JSON, each naming a species there is; the rest Rules.CheckFauna checks when it's validated.
        JArray Donors(Draft draft, string json)
        {
            const string example = "[{\"species\": \"deer\", \"drop\": [\"neck\", \"head\"]}, {\"species\": \"human\", \"take\": [\"human-torso\"], \"at\": [0, 0.45, 0.8]}]";
            if (!(Value(json) is JArray donors) || donors.Count == 0 || !donors.All(d => d is JObject))
                throw new RulesToolException($"from is a JSON list of donors, such as {example}.");
            foreach (var donor in donors)
            {
                string species = Str(donor["species"]) ?? throw new RulesToolException($"Every donor names a species, such as {example}.");
                if (Current(draft, FaunaTable, species) == null) throw new RulesToolException($"There is no species {species}; list fauna shows them.");
            }
            return donors;
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
        // An existing species' coat: one coat in place of whatever it wore, its own colours where none is given.
        static void Recolour(JObject entry, JObject now, string color, string accent, double? glow)
        {
            var row = new JObject();
            foreach (var key in new[] { "coat", "coats" })
                if ((entry[key] ?? now[key]) is JToken worn && worn.Type != JTokenType.Null) row[key] = worn.DeepClone();
            Colour(row, color, accent, glow);
            entry["coat"] = row["coat"];
            if (now["coats"] != null) entry["coats"] = JValue.CreateNull();
        }

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
