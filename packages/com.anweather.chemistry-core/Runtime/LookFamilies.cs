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
        /// <summary>Family creature: the row of rules/fauna.json whose model dresses a thing.</summary>
        public string Species;
        /// <summary>Family creature: how big against that species' own size.</summary>
        public float Scale = 1;
        /// <summary>Several colours with weights in place of the one, a pick per cell (look "mix"); null for one colour.</summary>
        public LookMix Mix;
        /// <summary>Looks that hold only round a place, a cell or a player (look "scoped"), earliest first (LookScopes).</summary>
        public IReadOnlyList<ScopedLook> Scoped = Array.Empty<ScopedLook>();
        /// <summary>The mix and scoped looks as written, so Same can tell two apart.</summary>
        public string Extra;

        /// <summary>Whether two looks have one colour, and the same mix and scoped looks (either may be null).</summary>
        public static bool SameColor(LookDef a, LookDef b) =>
            ReferenceEquals(a, b) || (a != null && b != null && Near(a.R, b.R) && Near(a.G, b.G) && Near(a.B, b.B) && a.Extra == b.Extra);

        /// <summary>Whether two looks draw alike (either may be null).</summary>
        public static bool Same(LookDef a, LookDef b) =>
            ReferenceEquals(a, b) || (SameColor(a, b) && a.Family == b.Family && a.Form == b.Form
                && Near(a.Height, b.Height) && Near(a.Glow, b.Glow)
                && Near(a.Roughness, b.Roughness) && Near(a.Flow, b.Flow) && a.Baked == b.Baked && a.Species == b.Species && Near(a.Scale, b.Scale));

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
        /// <summary>A colour this pale says nothing about its hue (saturation, 0 to 1), so what can only be turned can't take it.</summary>
        public const float PaleSaturation = 0.22f;
        /// <summary>Hues this close count as the same (a share of the wheel): nothing is turned.</summary>
        public const float SameHue = 12f / 360f;

        /// <summary>The tables of rules/looks.json that a mod's entries name a family from.</summary>
        public static readonly string[] Tables = { "terrain", "covers", "gases", "spells" };

        /// <summary>The look keys that aren't parameters: which family draws it, and its variant.</summary>
        public static readonly string[] Naming = { "family", "form" };

        readonly Dictionary<string, Dictionary<string, LookFamily>> tables = new Dictionary<string, Dictionary<string, LookFamily>>(StringComparer.Ordinal);

        // looks.json "bare": what the view draws from a bare look's colour, by table, and which of those only turn their art.
        readonly Dictionary<string, HashSet<string>> shows = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> turnsOnly = new HashSet<string>(StringComparer.Ordinal);

        LookFamilies() { }

        /// <summary>The ids a table's colours are drawn for, for a check that they're all real.</summary>
        public IReadOnlyDictionary<string, HashSet<string>> Shows => shows;

        /// <summary>
        /// Why this colour won't show on this entry, in a sentence, or null if it will: nothing draws it,
        /// or what draws it can only turn its art (<paramref name="was"/>: the colour it's drawn in now,
        /// or null). The view's rules (ChemGrid.WorldLook.cs, LookTint), in one place.
        /// </summary>
        public static string WontShow(LookFamilies looks, string table, string id, LookFamily family, bool baked, float[] was, float[] now)
        {
            if (looks.shows.TryGetValue(table, out var drawn) && !drawn.Contains(id))
                return $"This won't show: nothing in the world is drawn from {id}'s colour yet.";
            if (baked) return $"This won't show where the land draws {id} itself, only where it grows in this world.";
            if (table == "terrain" && family?.Name == "ground" && !CellPaint.LayerLooks.Any(l => l.table == table && l.id == id))
                return $"This won't show: the land has no layer of {id} to colour.";
            if (!looks.turnsOnly.Contains(id) && !(table == "spells" && family?.Own != null)) return null;
            if (Saturation(now) < PaleSaturation) return $"This won't show: {id} can only change hue, so a pale or grey colour leaves it as it is.";
            if (was != null && Saturation(was) >= PaleSaturation && Apart(Hue(was), Hue(now)) <= SameHue)
                return $"This won't show: it's too close to the hue {id} already has.";
            return null;
        }

        /// <summary>Whether what draws this entry can only turn its art's hue (flames, smoke, steam; a spell's show).</summary>
        public bool OnlyTurns(string table, string id, LookFamily family) => turnsOnly.Contains(id) || (table == "spells" && family?.Own != null);

        /// <summary>Whether a colour is too pale or grey to turn a hue to.</summary>
        public static bool IsPale(LookColour c) => Saturation(new[] { c.R, c.G, c.B }) < PaleSaturation;

        static float Apart(float a, float b)
        {
            float d = Math.Abs(a - b);
            return Math.Min(d, 1f - d);
        }

        static float Saturation(float[] c)
        {
            float top = Math.Max(c[0], Math.Max(c[1], c[2]));
            return top <= 0f ? 0f : (top - Math.Min(c[0], Math.Min(c[1], c[2]))) / top;
        }

        static float Hue(float[] c)
        {
            float top = Math.Max(c[0], Math.Max(c[1], c[2])), spread = top - Math.Min(c[0], Math.Min(c[1], c[2]));
            if (spread <= 0f) return 0f;
            float sixth = 4f + (c[0] - c[1]) / spread;
            if (c[0] >= c[1] && c[0] >= c[2]) sixth = (c[1] - c[2]) / spread;
            else if (c[1] >= c[2]) sixth = 2f + (c[2] - c[0]) / spread;
            return (sixth / 6f + 1f) % 1f;
        }

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
                foreach (var table in (bare["shows"] as JObject ?? new JObject()).Properties())
                    looks.shows[table.Name] = new HashSet<string>(table.Value.Select(x => (string)x), StringComparer.Ordinal);
                looks.turnsOnly = new HashSet<string>(bare["turnsOnly"]?.Select(x => (string)x) ?? Array.Empty<string>(), StringComparer.Ordinal);
                foreach (var table in (bare["also"] as JObject ?? new JObject()).Properties())
                    looks.bareAlso[table.Name] = looks.Bare.Params.Concat(table.Value.Select(x => (string)x)).Distinct().ToList();
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

        // looks.json "bare".also: a table whose bare looks take more than the colour (a cover's glow), with the colour.
        readonly Dictionary<string, IReadOnlyList<string>> bareAlso = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        /// <summary>The parameters a bare look on this table takes: the colour, and what "also" adds for it (a cover's glow).</summary>
        public IReadOnlyList<string> BareParams(string table) => bareAlso.TryGetValue(table, out var takes) ? takes : Bare.Params;

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
            if (family == null) return BareTables.Contains(table) ? BareParams(table) : Array.Empty<string>();
            return Find(table, family)?.Params;
        }

        /// <summary>A table's families, in name order.</summary>
        public IEnumerable<LookFamily> In(string table) =>
            tables.TryGetValue(OfThings(table), out var families) ? families.Values.OrderBy(f => f.Name, StringComparer.Ordinal) : Enumerable.Empty<LookFamily>();

        /// <summary>A family of a table, or null.</summary>
        public LookFamily Find(string table, string name) =>
            name != null && tables.TryGetValue(OfThings(table), out var families) && families.TryGetValue(name, out var f) ? f : null;

        /// <summary>Where the entities and items tables take their families: looks.json "things".</summary>
        public const string ThingsTable = "things";

        static string OfThings(string table) => table == "entities" || table == "items" ? ThingsTable : table;

        /// <summary>The names of the families the view draws for a table, comma-separated, for a sentence.</summary>
        public string DrawnNames(string table)
        {
            var names = In(table).Where(f => f.Drawn).Select(f => f.Name).ToList();
            return names.Count == 0 ? "none yet" : string.Join(", ", names);
        }
    }
}
