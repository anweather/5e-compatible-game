// The look families and effect families (rules/looks.json): what the view can
// draw, by name, with the parameters each takes. A material or spell names one
// in its own "look" group (LookDef), so a new thing draws with no code and no
// asset, and ModValidator checks a mod against this list. View only: nothing
// here is read by a tick, and "look" groups stay out of the rules fingerprint
// (Rules.WriteCanonical). The sandbox has no counterpart: it's frozen (J-042).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>
    /// A material's or spell's "look" group: the family that draws it and the parameters the
    /// family takes. Numbers are for the view (floats are fine here: no tick reads them).
    /// </summary>
    public sealed class LookDef
    {
        public string Family, Form;
        /// <summary>Colour 0 to 1 (a spell's: the glow in the hand and any tint).</summary>
        public float R = 1, G = 1, B = 1;
        /// <summary>How high it stands over its ground, in metres (block, plant, debris, plate).</summary>
        public float Height;
        /// <summary>How strongly it shines in its own colour; 0 not at all.</summary>
        public float Glow;
        /// <summary>0 glossy to 1 matt; below 0 the family's own.</summary>
        public float Roughness = -1;
        /// <summary>How fast a liquid family's surface moves (no drawn family reads it yet).</summary>
        public float Flow;
        /// <summary>The land itself draws it until it grows in this world (the valley's trees).</summary>
        public bool Baked;

        /// <summary>Whether two looks have one colour (either may be null).</summary>
        public static bool SameColor(LookDef a, LookDef b) =>
            ReferenceEquals(a, b) || (a != null && b != null && Near(a.R, b.R) && Near(a.G, b.G) && Near(a.B, b.B));

        /// <summary>Whether two looks draw alike (either may be null).</summary>
        public static bool Same(LookDef a, LookDef b) =>
            ReferenceEquals(a, b) || (SameColor(a, b) && a.Family == b.Family && a.Form == b.Form
                && Near(a.Height, b.Height) && Near(a.Glow, b.Glow)
                && Near(a.Roughness, b.Roughness) && Near(a.Flow, b.Flow) && a.Baked == b.Baked);

        static bool Near(float a, float b) => Math.Abs(a - b) < 1e-5f;
    }

    /// <summary>One family of rules/looks.json.</summary>
    public sealed class LookFamily
    {
        public string Name, About, Needs, Notes;
        /// <summary>Which table it draws: terrain, covers, gases, spells or fire.</summary>
        public string Table;
        public bool Drawn = true;
        public IReadOnlyList<string> Forms = Array.Empty<string>();
        public IReadOnlyList<string> Params = Array.Empty<string>();
        /// <summary>Spells: the Particle Pack effects, as "Fire/BigExplosion" (ChemYardBuilder resolves the folder).</summary>
        public string Projectile, Impact, AfterImpact;
        /// <summary>Spells: how long the impact plays.</summary>
        public float Seconds = 2f;
        public bool FromSky, Storm, Beam, Tinted;
        /// <summary>Spells: the number the show scales with (radius, size or length; null never), and that number at scale 1.</summary>
        public string ScaleBy;
        public float Base = 1f;
        /// <summary>Spells: the colour the family's effects are drawn in, 0 to 1 (three numbers), or null if it can't be turned from it yet.</summary>
        public float[] Own;
    }

    public static class EffectScale
    {
        /// <summary>The largest and smallest a show is drawn, against its family's size.</summary>
        public const float Min = 0.25f, Max = 4f;

        /// <summary>
        /// How much bigger than its family's own size a spell's show is drawn, from the rules in
        /// force and how long it was held: its radius, size or length over the family's base.
        /// </summary>
        public static float Of(string scaleBy, float familyBase, SpellDef spell, int charge)
        {
            if (spell == null || scaleBy == null || familyBase <= 0f) return 1f;
            int number;
            switch (scaleBy)
            {
                case "radius": number = spell.Radius; break;
                case "size": number = spell.Size; break;
                case "length": number = spell.Length; break;
                default: return 1f;
            }
            float scale = (number + World.ChargedGrowth(spell, charge)) / familyBase;
            return Math.Max(Min, Math.Min(Max, scale));
        }
    }

    public sealed class LookFamilies
    {
        /// <summary>The tables of rules/looks.json that a mod's entries name a family from.</summary>
        public static readonly string[] Tables = { "terrain", "covers", "gases", "spells" };

        /// <summary>The look keys that aren't parameters: which family draws it, and its variant.</summary>
        public static readonly string[] Naming = { "family", "form" };

        readonly Dictionary<string, Dictionary<string, LookFamily>> tables = new Dictionary<string, Dictionary<string, LookFamily>>(StringComparer.Ordinal);

        LookFamilies() { }

        /// <summary>
        /// looks.json "bare": a look with no family, only a colour. The tables whose entries may give
        /// one (covers, gases, elements; an element never names a family) and the parameters it takes.
        /// </summary>
        public LookFamily Bare { get; private set; } = new LookFamily { Name = "bare" };

        public static LookFamilies Load(string rulesDir) => Parse(File.ReadAllText(Path.Combine(rulesDir, "looks.json")));

        public static LookFamilies Parse(string json)
        {
            var doc = JObject.Parse(json);
            var looks = new LookFamilies();
            if (doc["bare"] is JObject bare)
            {
                looks.Bare = ParseFamily("bare", "bare", bare);
                looks.BareTables = bare["tables"]?.Select(x => (string)x).ToList() ?? new List<string>();
            }
            foreach (var table in doc.Properties().Where(p => p.Value is JObject && p.Name != "bare"))
            {
                var families = new Dictionary<string, LookFamily>(StringComparer.Ordinal);
                foreach (var f in ((JObject)table.Value).Properties()) families[f.Name] = ParseFamily(table.Name, f.Name, (JObject)f.Value);
                looks.tables[table.Name] = families;
            }
            return looks;
        }

        static LookFamily ParseFamily(string table, string name, JObject o) => new LookFamily
        {
            Name = name,
            Table = table,
            About = (string)o["about"],
            Needs = (string)o["needs"],
            Notes = (string)o["notes"],
            Drawn = o["drawn"]?.Type != JTokenType.Boolean || (bool)o["drawn"],
            Forms = o["forms"]?.Select(x => (string)x).ToList() ?? new List<string>(),
            Params = o["params"]?.Select(x => (string)x).ToList() ?? new List<string>(),
            Projectile = (string)o["projectile"],
            Impact = (string)o["impact"],
            AfterImpact = (string)o["afterImpact"],
            Seconds = (float?)o["seconds"] ?? 2f,
            FromSky = (bool?)o["fromSky"] ?? false,
            Storm = (bool?)o["storm"] ?? false,
            Beam = (bool?)o["beam"] ?? false,
            Tinted = (bool?)o["tinted"] ?? false,
            ScaleBy = (string)o["scaleBy"],
            Base = (float?)o["base"] ?? 1f,
            Own = o["own"]?.Select(x => (float)x).ToArray(),
        };

        /// <summary>The tables whose entries may have a bare look (looks.json "bare".tables).</summary>
        public IReadOnlyList<string> BareTables { get; private set; } = Array.Empty<string>();

        /// <summary>The tables of looks.json with families, in file order.</summary>
        public IEnumerable<string> TableNames => tables.Keys;

        /// <summary>
        /// The parameters a look takes: its family's, or the bare look's when it names none and its
        /// table may have one; null when a family is named that this table doesn't have.
        /// </summary>
        public IReadOnlyList<string> ParamsOf(string table, string family)
        {
            if (family == null) return BareTables.Contains(table) ? Bare.Params : Array.Empty<string>();
            return Find(table, family)?.Params;
        }

        /// <summary>A table's families, in name order.</summary>
        public IEnumerable<LookFamily> In(string table) =>
            tables.TryGetValue(table, out var families) ? families.Values.OrderBy(f => f.Name, StringComparer.Ordinal) : Enumerable.Empty<LookFamily>();

        /// <summary>A family of a table, or null.</summary>
        public LookFamily Find(string table, string name) =>
            name != null && tables.TryGetValue(table, out var families) && families.TryGetValue(name, out var f) ? f : null;

        /// <summary>The names of the families the view draws for a table, comma-separated, for a sentence.</summary>
        public string DrawnNames(string table)
        {
            var names = In(table).Where(f => f.Drawn).Select(f => f.Name).ToList();
            return names.Count == 0 ? "none yet" : string.Join(", ", names);
        }
    }
}
