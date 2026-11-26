// The creature tables (docs/design/builders.md, section 2): rules/fauna.json, the
// species, and rules/parts.json, the shapes they're built from. Both are the
// view's: the core reads them only to check them and hand them on, they stay out of
// Rules.Fingerprint, and nothing here reaches the cells. A row replaces the one
// under its id, as every table's does (ModPatch), and is read when first asked for,
// so a rule switch costs only the rows that changed (the view's CreatureBodies).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class CoatDef
    {
        public string Name;
        public double[] Base, Accent;
        public double Glow;
        public double Weight = 1;
    }

    public sealed class VaryDef
    {
        public double Shade;
        public double Warm;
        public double Girth;
        public bool Mix;
    }

    public sealed class SlotDef
    {
        public string Id, Part;
        public string On;
        public double[] At;
        public double[] Dir;
        public bool Mirror;
        public double Phase;
        public double MirrorPhase = 0.5;
        public double Scale = 1, Thickness = 1;
        public int Style;
        public string OwnCoatOf;
    }

    public sealed class SpeciesDef
    {
        public string Id, Name, VariantOf;
        public double VariantScale = 1;
        public double[] Size = { 0.9, 1.1 };
        public double Length = 0.8, Radius = 0.15, Taper = 1, Flatten = 1.1;
        public CoatDef Coat = new CoatDef { Base = new[] { 0.55, 0.4, 0.25 }, Accent = new[] { 0.9, 0.85, 0.75 } };
        public IReadOnlyList<CoatDef> Coats = Array.Empty<CoatDef>();
        public IReadOnlyList<double[]> Hair = Array.Empty<double[]>();
        public VaryDef Vary;
        public IReadOnlyList<SlotDef> Slots = Array.Empty<SlotDef>();
        public IReadOnlyList<string> Grafted = Array.Empty<string>();
        public double Walk = 1.4, Run = 6, Turn = 140, Rock;
        public double[] Bob = { 0.02, 0.06 }, Bobs = { 2, 2 }, Stride = { 0.6, 0.9 };
        public IReadOnlyList<string> Habitats = Array.Empty<string>();
        public double Depth = 0.6, Leaps;
        public bool Upright;
        public double Wander = 25, Flee = 12;
        public double[] Idle = { 2, 8 }, Group = { 1, 1 };
        public string Active = "day";
        public double Graze, Thirst = 0.3, Play, Laze = 0.25, Hunger = 0.3, Chase = 10;
        public IReadOnlyList<string> Hunts = Array.Empty<string>();
        public double Density;
        public bool NearWater, Spawns;
        public string Among;
        public double Share;
    }

    public sealed class SegmentDef
    {
        public double Length = 0.2, RadiusStart = 0.04, RadiusEnd = 0.03, Flatten = 1;
        public double Accent;
        public double[] Colour;
        public double Glow;
        public bool Hair;
    }

    public sealed class PartDef
    {
        public static readonly string[] PoseKeys = { "idle", "walkReach", "walkPush", "walkLift", "runReach", "runPush", "runLift" };

        public string Id, Role;
        public IReadOnlyList<SegmentDef> Segments = Array.Empty<SegmentDef>();
        public double Spring, Ripple;
        public bool FollowsGround;
        public double[][][] Poses;
    }

    public sealed class FaunaTables
    {
        public const string SpeciesKey = "fauna", PartsKey = "parts";

        internal static bool Holds(string table) => table == SpeciesKey || table == PartsKey;

        internal static readonly string[] Roles = { "leg", "arm", "tail", "neck", "head", "ear", "horn", "eye", "feature" };
        internal static readonly string[] Actives = { "day", "night", "always" };
        internal static readonly string[] Habitats = { "walks", "swims", "floats", "leaps" };

        readonly JObject species, parts;
        readonly Dictionary<string, SpeciesDef> readSpecies = new Dictionary<string, SpeciesDef>(StringComparer.Ordinal);
        readonly Dictionary<string, PartDef> readParts = new Dictionary<string, PartDef>(StringComparer.Ordinal);

        internal FaunaTables(JObject doc)
        {
            species = doc[SpeciesKey] as JObject ?? new JObject();
            parts = doc[PartsKey] as JObject ?? new JObject();
        }

        public IEnumerable<string> SpeciesIds => species.Properties().Select(p => p.Name);

        public IEnumerable<string> PartIds => parts.Properties().Select(p => p.Name);

        public SpeciesDef Species(string id)
        {
            if (id == null) return null;
            if (readSpecies.TryGetValue(id, out var read)) return read;
            if (!(FaunaGraft.Flatten(species, id) is JObject row)) return null;
            read = ReadSpecies(id, row);
            if (species[id]?[FaunaGraft.FromKey] is JArray donors) read.Grafted = donors.Select(d => (string)d["species"]).Where(d => d != null).ToList();
            readSpecies[id] = read;
            return read;
        }

        public PartDef Part(string id)
        {
            if (id == null) return null;
            if (readParts.TryGetValue(id, out var read)) return read;
            if (!(parts[id] is JObject row)) return null;
            read = ReadPart(id, row);
            readParts[id] = read;
            return read;
        }

        public SpeciesDef BodyOf(string id)
        {
            var s = Species(id);
            for (int i = 0; i < MaxVariantDepth && s?.VariantOf != null; i++) s = Species(s.VariantOf);
            return s;
        }

        internal const int MaxVariantDepth = 8;

        public bool SameRows(FaunaTables other, string id, ISet<string> changedParts)
        {
            for (int i = 0; i <= MaxVariantDepth && id != null; i++)
            {
                var row = FaunaGraft.Flatten(species, id);
                if (!JToken.DeepEquals(row, FaunaGraft.Flatten(other.species, id))) return false;
                if (row == null) return true;
                if (row["parts"] is JObject slots && slots.PropertyValues().Any(s => changedParts.Contains((string)s["part"]))) return false;
                id = (string)row["variantOf"];
            }
            return true;
        }

        FaunaTables same;

        public bool SameAs(FaunaTables other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other) || ReferenceEquals(same, other) || ReferenceEquals(other.same, this)) return true;
            if (!JToken.DeepEquals(species, other.species) || !JToken.DeepEquals(parts, other.parts)) return false;
            same = other;
            return true;
        }

        public ISet<string> ChangedParts(FaunaTables other)
        {
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts.Properties())
                if (!JToken.DeepEquals(p.Value, other.parts[p.Name])) changed.Add(p.Name);
            foreach (var p in other.parts.Properties())
                if (parts[p.Name] == null) changed.Add(p.Name);
            return changed;
        }

        static SpeciesDef ReadSpecies(string id, JObject row)
        {
            var s = new SpeciesDef { Id = id, Name = (string)row["name"] ?? id, VariantOf = (string)row["variantOf"] };
            s.VariantScale = Num(row["scale"], s.VariantScale);
            s.Size = Nums(row["size"], s.Size);
            if (row["body"] is JObject body)
            {
                s.Length = Num(body["length"], s.Length);
                s.Radius = Num(body["radius"], s.Radius);
                s.Taper = Num(body["taper"], s.Taper);
                s.Flatten = Num(body["flatten"], s.Flatten);
            }
            if (row["coat"] is JObject coat) s.Coat = ReadCoat(coat, s.Coat);
            if (row["coats"] is JArray coats) s.Coats = coats.OfType<JObject>().Select(c => ReadCoat(c, s.Coat)).ToList();
            if (row["hair"] is JArray hair) s.Hair = hair.Select(c => Colour(c, Array.Empty<double>())).ToList();
            if (row["vary"] is JObject vary)
                s.Vary = new VaryDef { Shade = Num(vary["shade"], 0), Warm = Num(vary["warm"], 0), Girth = Num(vary["girth"], 0), Mix = (bool?)vary["mix"] ?? false };
            if (row["parts"] is JObject slots) s.Slots = slots.Properties().Select(ReadSlot).ToList();
            ReadMove(s, row["move"] as JObject);
            ReadBehave(s, row["behave"] as JObject);
            if (row["spawn"] is JObject spawn)
            {
                s.Spawns = true;
                s.Density = Num(spawn["density"], 0);
                s.NearWater = (bool?)spawn["nearWater"] ?? false;
                s.Among = (string)spawn["among"];
                s.Share = Num(spawn["share"], 0);
            }
            return s;
        }

        static void ReadMove(SpeciesDef s, JObject move)
        {
            if (move == null) return;
            s.Walk = Num(move["walk"], s.Walk);
            s.Run = Num(move["run"], s.Run);
            s.Turn = Num(move["turn"], s.Turn);
            s.Rock = Num(move["rock"], s.Rock);
            s.Bob = Nums(move["bob"], s.Bob);
            s.Bobs = Nums(move["bobs"], s.Bobs);
            s.Stride = Nums(move["stride"], s.Stride);
            s.Upright = (bool?)move["upright"] ?? false;
            if (move["habitats"] is JArray habitats) s.Habitats = habitats.Select(h => (string)h).ToList();
            s.Depth = Num(move["depth"], s.Depth);
            s.Leaps = Num(move["leaps"], s.Leaps);
        }

        static void ReadBehave(SpeciesDef s, JObject behave)
        {
            if (behave == null) return;
            s.Wander = Num(behave["wander"], s.Wander);
            s.Flee = Num(behave["flee"], s.Flee);
            s.Idle = Nums(behave["idle"], s.Idle);
            s.Group = Nums(behave["group"], s.Group);
            s.Active = (string)behave["active"] ?? s.Active;
            s.Graze = Num(behave["graze"], s.Graze);
            s.Thirst = Num(behave["thirst"], s.Thirst);
            s.Play = Num(behave["play"], s.Play);
            s.Laze = Num(behave["laze"], s.Laze);
            s.Hunger = Num(behave["hunger"], s.Hunger);
            s.Chase = Num(behave["chase"], s.Chase);
            if (behave["hunts"] is JArray hunts) s.Hunts = hunts.Select(h => (string)h).ToList();
        }

        static CoatDef ReadCoat(JObject c, CoatDef defaults) => new CoatDef
        {
            Name = (string)c["name"],
            Base = Colour(c["base"], defaults.Base),
            Accent = Colour(c["accent"], defaults.Accent),
            Glow = Num(c["glow"], 0),
            Weight = Num(c["weight"], 1),
        };

        static SlotDef ReadSlot(JProperty p)
        {
            var s = new SlotDef
            {
                Id = p.Name,
                Part = (string)p.Value["part"],
                On = (string)p.Value["on"],
                At = Nums(p.Value["at"], new double[3]),
                Dir = Nums(p.Value["dir"], new double[3]),
                Mirror = (bool?)p.Value["mirror"] ?? false,
            };
            s.Phase = Num(p.Value["phase"], s.Phase);
            s.MirrorPhase = Num(p.Value["mirrorPhase"], s.MirrorPhase);
            s.Scale = Num(p.Value["scale"], s.Scale);
            s.Thickness = Num(p.Value["thickness"], s.Thickness);
            s.Style = (int)Num(p.Value["style"], 0);
            if ((string)p.Value["coat"] == "own") s.OwnCoatOf = (string)p.Value["donor"];
            return s;
        }

        static PartDef ReadPart(string id, JObject row)
        {
            var part = new PartDef
            {
                Id = id,
                Role = (string)row["role"],
                Spring = Num(row["spring"], 0),
                Ripple = Num(row["ripple"], 0),
                FollowsGround = (string)row["follows"] == "ground",
                Poses = PartDef.PoseKeys.Select(k => (row["poses"]?[k] as JArray)?.Select(j => Nums(j, Array.Empty<double>())).ToArray() ?? Array.Empty<double[]>()).ToArray(),
            };
            part.Segments = (row["segments"] as JArray)?.OfType<JObject>().Select(ReadSegment).ToList() ?? part.Segments;
            return part;
        }

        static SegmentDef ReadSegment(JObject row)
        {
            var s = new SegmentDef();
            s.Length = Num(row["length"], s.Length);
            s.RadiusStart = Num(row["radii"]?[0], s.RadiusStart);
            s.RadiusEnd = Num(row["radii"]?[1], s.RadiusEnd);
            s.Flatten = Num(row["flatten"], s.Flatten);
            s.Accent = Num(row["accent"], s.Accent);
            s.Colour = Colour(row["colour"], s.Colour);
            s.Glow = Num(row["glow"], s.Glow);
            s.Hair = (bool?)row["hair"] ?? false;
            return s;
        }

        static double Num(JToken t, double otherwise) => t == null || t.Type == JTokenType.Null ? otherwise : (double)t;
        static double[] Nums(JToken t, double[] otherwise) => t is JArray a ? a.Select(x => (double)x).ToArray() : otherwise;

        static double[] Colour(JToken t, double[] otherwise)
        {
            if (!IsHex(t, out int rgb)) return Nums(t, otherwise);
            return new[] { (rgb >> 16 & 255) / 255.0, (rgb >> 8 & 255) / 255.0, (rgb & 255) / 255.0 };
        }

        internal static bool IsHex(JToken t, out int rgb)
        {
            rgb = 0;
            if (t == null || t.Type != JTokenType.String) return false;
            var hex = (string)t;
            return hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
        }
    }
}
