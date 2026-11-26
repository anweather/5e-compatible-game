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
    public sealed class LookDef
    {
        public string Family, Form;
        public float R = 1, G = 1, B = 1;
        public float Height;
        public float Glow;
        public float Roughness = -1;
        public float Flow;
        public bool Baked;
        public string Species;
        public float Scale = 1;
        public LookMix Mix;
        public IReadOnlyList<ScopedLook> Scoped = Array.Empty<ScopedLook>();
        public string Extra;

        public static bool SameColor(LookDef a, LookDef b) =>
            ReferenceEquals(a, b) || (a != null && b != null && Near(a.R, b.R) && Near(a.G, b.G) && Near(a.B, b.B) && a.Extra == b.Extra);

        public static bool Same(LookDef a, LookDef b) =>
            ReferenceEquals(a, b) || (SameColor(a, b) && a.Family == b.Family && a.Form == b.Form
                && Near(a.Height, b.Height) && Near(a.Glow, b.Glow)
                && Near(a.Roughness, b.Roughness) && Near(a.Flow, b.Flow) && a.Baked == b.Baked && a.Species == b.Species && Near(a.Scale, b.Scale));

        static bool Near(float a, float b) => Math.Abs(a - b) < 1e-5f;
    }

    public sealed class LookFamily
    {
        public string Name, About, Needs, Notes;
        public string Table;
        public bool Drawn = true;
        public IReadOnlyList<string> Forms = Array.Empty<string>();
        public IReadOnlyList<string> Params = Array.Empty<string>();
        public string Projectile, Impact, AfterImpact;
        public float Seconds = 2f;
        public bool FromSky, Storm, Beam, Tinted;
        public string ScaleBy;
        public float Base = 1f;
        public float[] Own;
    }

    public static class EffectScale
    {
        public const float Min = 0.25f, Max = 4f;

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
        public const float PaleSaturation = 0.22f;
        public const float SameHue = 12f / 360f;

        public static readonly string[] Tables = { "terrain", "covers", "gases", "spells" };

        public static readonly string[] Naming = { "family", "form" };

        readonly Dictionary<string, Dictionary<string, LookFamily>> tables = new Dictionary<string, Dictionary<string, LookFamily>>(StringComparer.Ordinal);

        readonly Dictionary<string, HashSet<string>> shows = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> turnsOnly = new HashSet<string>(StringComparer.Ordinal);

        LookFamilies() { }

        public IReadOnlyDictionary<string, HashSet<string>> Shows => shows;

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

        public bool OnlyTurns(string table, string id, LookFamily family) => turnsOnly.Contains(id) || (table == "spells" && family?.Own != null);

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

        readonly Dictionary<string, IReadOnlyList<string>> bareAlso = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        public IReadOnlyList<string> BareParams(string table) => bareAlso.TryGetValue(table, out var takes) ? takes : Bare.Params;

        public IReadOnlyList<string> BareTables { get; private set; } = Array.Empty<string>();

        public IEnumerable<string> TableNames => tables.Keys;

        public IReadOnlyList<string> ParamsOf(string table, string family)
        {
            if (family == null) return BareTables.Contains(table) ? BareParams(table) : Array.Empty<string>();
            return Find(table, family)?.Params;
        }

        public IEnumerable<LookFamily> In(string table) =>
            tables.TryGetValue(OfThings(table), out var families) ? families.Values.OrderBy(f => f.Name, StringComparer.Ordinal) : Enumerable.Empty<LookFamily>();

        public LookFamily Find(string table, string name) =>
            name != null && tables.TryGetValue(OfThings(table), out var families) && families.TryGetValue(name, out var f) ? f : null;

        public const string ThingsTable = "things";

        static string OfThings(string table) => table == "entities" || table == "items" ? ThingsTable : table;

        public string DrawnNames(string table)
        {
            var names = In(table).Where(f => f.Drawn).Select(f => f.Name).ToList();
            return names.Count == 0 ? "none yet" : string.Join(", ", names);
        }
    }
}
