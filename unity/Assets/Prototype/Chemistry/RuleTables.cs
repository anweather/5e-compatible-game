// Typed view of ../rules/*.json, the same tables the web sandbox runs on
// (sandbox/src/rules.ts). Only the cell-level parts are interpreted; the
// other files (entities, critters, machines) are kept as JSON so live mods
// (Mods.cs) can patch and check them, but nothing here reads them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class TerrainDef
    {
        public string Id, Name;
        public IReadOnlyList<string> Tags;
        /// <summary>In steps (pit = -1). World.ElevationUnits converts to water units.</summary>
        public int Elevation;
        public double Flammability;
        /// <summary>Null when the table leaves it out (code falls back to 5, like the TS).</summary>
        public int? Fuel;
        public string BurnsTo, BreaksTo, BurnLog;
        /// <summary>What digging leaves (grass is dug down to dirt), or null to keep the terrain.</summary>
        public string DugTo;

        // Tags don't change once the rules are loaded; a set makes the sim's many tag checks O(1).
        HashSet<string> tagSet;
        public bool Has(string tag) => (tagSet ??= new HashSet<string>(Tags)).Contains(tag);
    }

    public sealed class CoverDef
    {
        public string Id, Name;
        public IReadOnlyList<string> Tags;
        public double Flammability;
        public int? Fuel;
        public int? BurnIntensity;
        public bool Has(string tag) => Tags.Contains(tag);
    }

    public sealed class GasDef
    {
        public string Id, Name;
        public int Scald;
    }

    /// <summary>
    /// A spell. Level/Power/Length/Size/Radius hold the value the sim actually uses,
    /// i.e. the TS defaults are applied (Power 1, Length 5, Size 2, Radius 1).
    /// </summary>
    public sealed class SpellDef
    {
        public string Id, Name, Shape, Origin, Element, Blurb;
        public int Level, Power, Length, Size, Radius;
        // Extras, carried for completeness; the cell sim uses Wind and (below) the terraform fields.
        public string Effect;
        public int Push, Wind, Range, Duration, MaxMass;
        /// <summary>[dice, sides], or null.</summary>
        public int[] Damage;
        /// <summary>
        /// Effect "terraform": steps to raise or dig (one is 0), or a Height to raise the cells
        /// to (null unless set; then Raise and Dig are 0), the terrain the cells become (null:
        /// keep it, or its DugTo when digging), and whether the steps taper off with distance
        /// from the target over Radius (a hill or a crater).
        /// </summary>
        public int Raise, Dig;
        public int? Height;
        public string Terrain;
        public bool Taper;
    }

    public sealed class FireParams
    {
        public double SpreadChance = 0.5, HeatChance = 0.04, BoilChance = 0.35, DownwindBonus = 0.35, SmokeChance = 0.25;

        /// <summary>
        /// When above 0, fire weakens as it spreads: a cell this many spreads from where the fire
        /// started can no longer pass it on. The chance stays near full for most of the way and
        /// falls off toward the end (1 - (generation / FadeCells)^3). 0 (the
        /// default, and the web sandbox's behaviour) means fire spreads as far as there is fuel.
        /// sim.json's fire.fadeCells (the sandbox ignores it), so a live mod can set it.
        /// </summary>
        public int FadeCells;
    }

    public sealed class GasParams
    {
        public double DecayChance = 0.07;
        public int MaxAmount = 6;
    }

    /// <summary>The range of Cell.Height, in steps, and the height from which a cell is a wall.</summary>
    public sealed class HeightParams
    {
        public int Min = -3, Max = 4, BlockAt = 3;
    }

    public sealed class SimParams
    {
        public int TicksPerSecond = 6;
        public FireParams Fire = new FireParams();
        public GasParams Gas = new GasParams();
        public HeightParams Height = new HeightParams();
        public int WaterMaxLevel = 16, WaterUnitsPerStep = 4;
        public int ConductLimit = 80;
        // Not used by the cell port, carried so a view can read them.
        public int FallDamage = 4, ZapDamage = 3;
    }

    /// <summary>The "if" of a cell rule. Null members mean "not checked".</summary>
    public sealed class CellCond
    {
        public IReadOnlyList<string> Cover;
        /// <summary>fire: true / false.</summary>
        public bool? FireFlag;
        /// <summary>fire: a number (minimum intensity).</summary>
        public double? FireMin;
        public double? FireBelow;
        public bool? Water, Frozen, Conductive;
        public string Gas, TerrainTag, NotTerrainTag;
    }

    /// <summary>One effect object; the first verb present (in TS runEffect order) wins.</summary>
    public sealed class Effect
    {
        public bool HasIgnite, IgniteCover; public int IgniteN;
        public int? Boil;
        public bool Melt, Freeze, Extinguish, Condense;
        public bool HasAddWater, AddWaterPower; public int AddWaterN;
        public bool HasSetCover; public string SetCover;
        public int? SpreadCover;
        public bool Break;
        public string SetTerrain, Raise;
        public int? Conduct;
        public bool Fan, PushGas, ClearGas;
        public string Mark;
        public string Gas; public int? N;
    }

    public sealed class CellRule
    {
        public string Id, Element, Log;
        public CellCond If;
        public IReadOnlyList<Effect> Do;
        public bool Notable, Stop;
    }

    public sealed partial class Rules
    {
        /// <summary>BASE_ELEMENTS in rules.ts: the elements before any mod adds more.</summary>
        static readonly string[] BaseElements = { "fire", "cold", "water", "air", "earth", "lightning", "grease", "oil", "thunder" };

        readonly Dictionary<string, TerrainDef> terrain = new Dictionary<string, TerrainDef>();
        readonly Dictionary<string, CoverDef> covers = new Dictionary<string, CoverDef>();
        readonly Dictionary<string, GasDef> gases = new Dictionary<string, GasDef>();
        readonly List<SpellDef> spells = new List<SpellDef>();
        readonly List<CellRule> cellRules = new List<CellRule>();
        readonly Dictionary<string, List<CellRule>> byElement = new Dictionary<string, List<CellRule>>();
        static readonly IReadOnlyList<CellRule> NoRules = Array.Empty<CellRule>();

        public IReadOnlyDictionary<string, TerrainDef> Terrain => terrain;
        public IReadOnlyDictionary<string, CoverDef> Covers => covers;
        public IReadOnlyDictionary<string, GasDef> Gases => gases;
        public IReadOnlyList<SpellDef> Spells => spells;
        /// <summary>reactions.json "cell", in file order.</summary>
        public IReadOnlyList<CellRule> CellRules => cellRules;
        public SimParams Sim { get; private set; } = new SimParams();

        /// <summary>
        /// Every table as JSON, in the shape of the sandbox's `Rules` object (elements, terrain,
        /// covers, gases, cellRules, entityRules, spells, critters, entities, machines, sim).
        /// Mods patch this, never the typed tables, so merging works exactly like composeRules.
        /// </summary>
        readonly JObject doc;

        Rules(JObject doc) { this.doc = doc; }

        public SpellDef Spell(string id) => spells.FirstOrDefault(s => s.Id == id);

        /// <summary>Cell rules for one element, top to bottom.</summary>
        public IReadOnlyList<CellRule> CellRulesFor(string element) =>
            element != null && byElement.TryGetValue(element, out var list) ? list : NoRules;

        /// <summary>Reads every *.json in the folder (see <see cref="FromJson"/>). Subfolders such as mods/ are skipped.</summary>
        public static Rules Load(string rulesDir)
        {
            var files = new Dictionary<string, string>();
            foreach (var path in Directory.GetFiles(rulesDir, "*.json"))
                files[Path.GetFileName(path)] = File.ReadAllText(path);
            return FromJson(files);
        }

        /// <summary>
        /// Builds the rules from the files' text by name. materials.json and reactions.json are
        /// required; the rest are optional.
        /// </summary>
        public static Rules FromJson(IDictionary<string, string> filesByName)
        {
            JToken Get(string name, bool required)
            {
                if (filesByName.TryGetValue(name, out var text)) return JToken.Parse(text);
                if (required) throw new ArgumentException($"Rules: {name} is missing");
                return JValue.CreateNull();
            }

            var materials = (JObject)Get("materials.json", true);
            var reactions = (JObject)Get("reactions.json", true);
            return FromDoc(new JObject
            {
                ["elements"] = new JArray(BaseElements),
                ["terrain"] = materials["terrain"] as JObject ?? new JObject(),
                ["covers"] = materials["covers"] as JObject ?? new JObject(),
                ["gases"] = materials["gases"] as JObject ?? new JObject(),
                ["cellRules"] = reactions["cell"] as JArray ?? new JArray(),
                ["entityRules"] = reactions["entity"] as JArray ?? new JArray(),
                ["spells"] = Get("spells.json", false) as JArray ?? new JArray(),
                ["critters"] = Get("critters.json", false) as JArray ?? new JArray(),
                ["entities"] = Get("entities.json", false) as JObject ?? new JObject(),
                ["machines"] = Get("machines.json", false) as JObject ?? new JObject(),
                ["sim"] = Get("sim.json", false) as JObject ?? new JObject(),
            });
        }

        /// <summary>The typed tables for a rule document (see <see cref="doc"/>), which the result keeps.</summary>
        static Rules FromDoc(JObject doc)
        {
            var r = new Rules(doc);
            foreach (var p in ((JObject)doc["terrain"]).Properties()) r.terrain[p.Name] = ParseTerrain(p.Name, (JObject)p.Value);
            foreach (var p in ((JObject)doc["covers"]).Properties()) r.covers[p.Name] = ParseCover(p.Name, (JObject)p.Value);
            foreach (var p in ((JObject)doc["gases"]).Properties())
                r.gases[p.Name] = new GasDef { Id = p.Name, Name = Str(p.Value["name"]) ?? p.Name, Scald = Int(p.Value["scald"]) ?? 0 };

            foreach (var t in doc["cellRules"].OfType<JObject>())
            {
                var rule = ParseRule(t);
                r.cellRules.Add(rule);
                if (rule.Element == null) continue;
                if (!r.byElement.TryGetValue(rule.Element, out var list)) r.byElement[rule.Element] = list = new List<CellRule>();
                list.Add(rule);
            }

            foreach (var t in doc["spells"].OfType<JObject>()) r.spells.Add(ParseSpell(t));
            r.Sim = ParseSim((JObject)doc["sim"]);
            return r;
        }

        // ---------- parsing helpers ----------

        static string Str(JToken t) => t == null || t.Type == JTokenType.Null ? null : (string)t;
        static int? Int(JToken t) => t == null || t.Type == JTokenType.Null ? (int?)null : (int)Math.Floor((double)t);
        static double? Num(JToken t) => t == null || t.Type == JTokenType.Null ? (double?)null : (double)t;
        static bool? Bool(JToken t) => t == null || t.Type == JTokenType.Null ? (bool?)null : Truthy(t);

        /// <summary>JavaScript truthiness, since the TS tests effects with `if (eff.melt)`.</summary>
        static bool Truthy(JToken t)
        {
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined: return false;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.Integer:
                case JTokenType.Float: return (double)t != 0;
                case JTokenType.String: return ((string)t).Length > 0;
                default: return true;
            }
        }

        /// <summary>The strings in a list (anything else in it is skipped), or none.</summary>
        static IReadOnlyList<string> Strings(JToken t) =>
            t is JArray a ? a.Where(x => x.Type == JTokenType.String).Select(x => (string)x).ToList() : new List<string>();

        static TerrainDef ParseTerrain(string id, JObject o) => new TerrainDef
        {
            Id = id,
            Name = Str(o["name"]) ?? id,
            Tags = Strings(o["tags"]),
            Elevation = Int(o["elevation"]) ?? 0,
            Flammability = Num(o["flammability"]) ?? 0,
            Fuel = Int(o["fuel"]),
            BurnsTo = Str(o["burnsTo"]),
            BreaksTo = Str(o["breaksTo"]),
            BurnLog = Str(o["burnLog"]),
            DugTo = Str(o["dugTo"]),
        };

        static CoverDef ParseCover(string id, JObject o) => new CoverDef
        {
            Id = id,
            Name = Str(o["name"]) ?? id,
            Tags = Strings(o["tags"]),
            Flammability = Num(o["flammability"]) ?? 0,
            Fuel = Int(o["fuel"]),
            BurnIntensity = Int(o["burnIntensity"]),
        };

        static SpellDef ParseSpell(JObject o)
        {
            int[] dmg = null;
            if (o["damage"] is JArray d && d.Count >= 2) dmg = new[] { (int)d[0], (int)d[1] };
            return new SpellDef
            {
                Id = Str(o["id"]),
                Name = Str(o["name"]) ?? Str(o["id"]),
                Shape = Str(o["shape"]) ?? "point",
                Origin = Str(o["origin"]),
                Element = Str(o["element"]),
                Blurb = Str(o["blurb"]) ?? "",
                Level = Int(o["level"]) ?? 0,
                Power = Int(o["power"]) ?? 1,
                Length = Int(o["length"]) ?? 5,
                Size = Int(o["size"]) ?? 2,
                Radius = Int(o["radius"]) ?? 1,
                Effect = Str(o["effect"]),
                Push = Int(o["push"]) ?? 0,
                Wind = Int(o["wind"]) ?? 0,
                Range = Int(o["range"]) ?? 6,
                Duration = Int(o["duration"]) ?? 40,
                MaxMass = Int(o["maxMass"]) ?? 1,
                Damage = dmg,
                Raise = Int(o["raise"]) ?? 0,
                Dig = Int(o["dig"]) ?? 0,
                Height = Int(o["height"]),
                Terrain = Str(o["terrain"]),
                Taper = Truthy(o["taper"]),
            };
        }

        static CellRule ParseRule(JObject o)
        {
            CellCond cond = null;
            if (o["if"] is JObject c)
            {
                cond = new CellCond
                {
                    Cover = c["cover"] is JArray ? Strings(c["cover"]) : null,
                    FireBelow = Num(c["fireBelow"]),
                    Water = Bool(c["water"]),
                    Frozen = Bool(c["frozen"]),
                    Conductive = Bool(c["conductive"]),
                    Gas = Str(c["gas"]),
                    TerrainTag = Str(c["terrainTag"]),
                    NotTerrainTag = Str(c["notTerrainTag"]),
                };
                var fire = c["fire"];
                if (fire != null && fire.Type == JTokenType.Boolean) cond.FireFlag = (bool)fire;
                else if (fire != null && (fire.Type == JTokenType.Integer || fire.Type == JTokenType.Float)) cond.FireMin = (double)fire;
            }
            var effects = new List<Effect>();
            if (o["do"] is JArray d)
                foreach (var e in d.OfType<JObject>()) effects.Add(ParseEffect(e));
            return new CellRule
            {
                Id = Str(o["id"]),
                Element = Str(o["element"]),
                If = cond,
                Do = effects,
                Log = Str(o["log"]),
                Notable = Truthy(o["notable"]),
                Stop = Truthy(o["stop"]),
            };
        }

        static Effect ParseEffect(JObject o)
        {
            var e = new Effect();
            var ig = o["ignite"];
            if (ig != null && ig.Type != JTokenType.Null)
            {
                e.HasIgnite = true;
                if (ig.Type == JTokenType.String) e.IgniteCover = (string)ig == "cover";
                else e.IgniteN = Int(ig) ?? 0;
            }
            e.Boil = Int(o["boil"]);
            e.Melt = Truthy(o["melt"]);
            e.Freeze = Truthy(o["freeze"]);
            e.Extinguish = Truthy(o["extinguish"]);
            e.Condense = Truthy(o["condense"]);
            var aw = o["addWater"];
            if (aw != null && aw.Type != JTokenType.Null)
            {
                e.HasAddWater = true;
                if (aw.Type == JTokenType.String) e.AddWaterPower = (string)aw == "power";
                else e.AddWaterN = Int(aw) ?? 0;
            }
            // setCover: null is meaningful (clear the cover), so presence is what counts.
            if (o.TryGetValue("setCover", out var sc))
            {
                e.HasSetCover = true;
                e.SetCover = Str(sc);
            }
            e.SpreadCover = Int(o["spreadCover"]);
            e.Break = Truthy(o["break"]);
            e.SetTerrain = Str(o["setTerrain"]);
            e.Raise = Str(o["raise"]);
            e.Conduct = Int(o["conduct"]);
            e.Fan = Truthy(o["fan"]);
            e.PushGas = Truthy(o["pushGas"]);
            e.ClearGas = Truthy(o["clearGas"]);
            e.Mark = Str(o["mark"]);
            e.Gas = Str(o["gas"]);
            e.N = Int(o["n"]);
            return e;
        }

        static SimParams ParseSim(JObject o)
        {
            var s = new SimParams();
            s.TicksPerSecond = Int(o["ticksPerSecond"]) ?? s.TicksPerSecond;
            if (o["fire"] is JObject f)
            {
                s.Fire.SpreadChance = Num(f["spreadChance"]) ?? s.Fire.SpreadChance;
                s.Fire.HeatChance = Num(f["heatChance"]) ?? s.Fire.HeatChance;
                s.Fire.BoilChance = Num(f["boilChance"]) ?? s.Fire.BoilChance;
                s.Fire.DownwindBonus = Num(f["downwindBonus"]) ?? s.Fire.DownwindBonus;
                s.Fire.SmokeChance = Num(f["smokeChance"]) ?? s.Fire.SmokeChance;
                s.Fire.FadeCells = Int(f["fadeCells"]) ?? s.Fire.FadeCells;
            }
            if (o["water"] is JObject w)
            {
                s.WaterMaxLevel = Int(w["maxLevel"]) ?? s.WaterMaxLevel;
                s.WaterUnitsPerStep = Int(w["unitsPerStep"]) ?? s.WaterUnitsPerStep;
            }
            if (o["height"] is JObject ht)
            {
                s.Height.Min = Int(ht["min"]) ?? s.Height.Min;
                s.Height.Max = Int(ht["max"]) ?? s.Height.Max;
                s.Height.BlockAt = Int(ht["blockAt"]) ?? s.Height.BlockAt;
            }
            if (o["gas"] is JObject g)
            {
                s.Gas.DecayChance = Num(g["decayChance"]) ?? s.Gas.DecayChance;
                s.Gas.MaxAmount = Int(g["maxAmount"]) ?? s.Gas.MaxAmount;
            }
            s.ConductLimit = Int(o["conductLimit"]) ?? s.ConductLimit;
            s.FallDamage = Int(o["fallDamage"]) ?? s.FallDamage;
            s.ZapDamage = Int(o["zapDamage"]) ?? s.ZapDamage;
            return s;
        }
    }
}
