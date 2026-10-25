// Typed view of ../rules/*.json, the same tables the web sandbox runs on
// (sandbox/src/rules.ts). Only the cell-level parts are interpreted, plus
// entities.json for the Unity physics props; the other files (critters,
// machines) are accepted and ignored.
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
        // Extras: only Wind is used by the cell sim; Push and Duration drive the Unity props (SpellForces).
        public string Effect;
        public int Push, Wind, Range, Duration, MaxMass;
        /// <summary>[dice, sides], or empty.</summary>
        public int[] Damage;
    }

    /// <summary>What an entity leaves behind when it blows up (entities.json "explodes").</summary>
    public sealed class ExplosionDef
    {
        public string Cover;
        public int Radius, Fire, Push;
        /// <summary>[dice, sides], or empty.</summary>
        public int[] Damage;
    }

    /// <summary>A thing on the board (entities.json): a wizard, a crate, a barrel, a boulder.</summary>
    public sealed class EntityDef
    {
        public string Id, Name;
        public int Hp, Mass;
        public bool Flammable, Creature, Player;
        /// <summary>Null unless it blows up when destroyed.</summary>
        public ExplosionDef Explodes;
    }

    public sealed class FireParams
    {
        public double SpreadChance = 0.5, HeatChance = 0.04, BoilChance = 0.35, DownwindBonus = 0.35, SmokeChance = 0.25;

        /// <summary>
        /// When above 0, fire weakens as it spreads: a cell this many spreads from where the fire
        /// started can no longer pass it on. The chance stays near full for most of the way and
        /// falls off toward the end (1 - (generation / FadeCells)^3). 0 (the
        /// default, and the web sandbox's behaviour) means fire spreads as far as there is fuel.
        /// </summary>
        public int FadeCells;
    }

    public sealed class GasParams
    {
        public double DecayChance = 0.07;
        public int MaxAmount = 6;
    }

    public sealed class SimParams
    {
        public int TicksPerSecond = 6;
        public FireParams Fire = new FireParams();
        public GasParams Gas = new GasParams();
        public int WaterMaxLevel = 16, WaterUnitsPerStep = 4;
        public int ConductLimit = 80;
        // Not used by the cell port, carried so a view can read them.
        public int FallDamage = 4, ZapDamage = 3;
        /// <summary>A burning entity takes 1 damage on every tick divisible by this (sim.json status).</summary>
        public int BurnDamageEvery = 3;
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

    public sealed class Rules
    {
        readonly Dictionary<string, TerrainDef> terrain = new Dictionary<string, TerrainDef>();
        readonly Dictionary<string, CoverDef> covers = new Dictionary<string, CoverDef>();
        readonly Dictionary<string, GasDef> gases = new Dictionary<string, GasDef>();
        readonly List<SpellDef> spells = new List<SpellDef>();
        readonly Dictionary<string, EntityDef> entities = new Dictionary<string, EntityDef>();
        readonly List<CellRule> cellRules = new List<CellRule>();
        readonly Dictionary<string, List<CellRule>> byElement = new Dictionary<string, List<CellRule>>();
        static readonly IReadOnlyList<CellRule> NoRules = Array.Empty<CellRule>();

        public IReadOnlyDictionary<string, TerrainDef> Terrain => terrain;
        public IReadOnlyDictionary<string, CoverDef> Covers => covers;
        public IReadOnlyDictionary<string, GasDef> Gases => gases;
        public IReadOnlyList<SpellDef> Spells => spells;
        public IReadOnlyDictionary<string, EntityDef> Entities => entities;
        /// <summary>reactions.json "cell", in file order.</summary>
        public IReadOnlyList<CellRule> CellRules => cellRules;
        public SimParams Sim { get; private set; } = new SimParams();

        Rules() { }

        public SpellDef Spell(string id) => spells.FirstOrDefault(s => s.Id == id);

        /// <summary>The entity with this id, or null.</summary>
        public EntityDef Entity(string id) => id != null && entities.TryGetValue(id, out var e) ? e : null;

        /// <summary>Cell rules for one element, top to bottom.</summary>
        public IReadOnlyList<CellRule> CellRulesFor(string element) =>
            element != null && byElement.TryGetValue(element, out var list) ? list : NoRules;

        /// <summary>Reads every *.json in the folder (materials, reactions, sim required; spells and entities optional).</summary>
        public static Rules Load(string rulesDir)
        {
            var files = new Dictionary<string, string>();
            foreach (var path in Directory.GetFiles(rulesDir, "*.json"))
                files[Path.GetFileName(path)] = File.ReadAllText(path);
            return FromJson(files);
        }

        public static Rules FromJson(IDictionary<string, string> filesByName)
        {
            string Get(string name, bool required)
            {
                if (filesByName.TryGetValue(name, out var text)) return text;
                if (required) throw new ArgumentException($"Rules: {name} is missing");
                return null;
            }

            var r = new Rules();
            var materials = JObject.Parse(Get("materials.json", true));
            var reactions = JObject.Parse(Get("reactions.json", true));
            var simText = Get("sim.json", false);
            var spellsText = Get("spells.json", false);
            var entitiesText = Get("entities.json", false);

            if (materials["terrain"] is JObject tj)
                foreach (var p in tj.Properties()) r.terrain[p.Name] = ParseTerrain(p.Name, (JObject)p.Value);
            if (materials["covers"] is JObject cj)
                foreach (var p in cj.Properties()) r.covers[p.Name] = ParseCover(p.Name, (JObject)p.Value);
            if (materials["gases"] is JObject gj)
                foreach (var p in gj.Properties())
                    r.gases[p.Name] = new GasDef { Id = p.Name, Name = Str(p.Value["name"]) ?? p.Name, Scald = Int(p.Value["scald"]) ?? 0 };

            if (reactions["cell"] is JArray cells)
                foreach (var t in cells.OfType<JObject>())
                {
                    var rule = ParseRule(t);
                    r.cellRules.Add(rule);
                    if (rule.Element == null) continue;
                    if (!r.byElement.TryGetValue(rule.Element, out var list)) r.byElement[rule.Element] = list = new List<CellRule>();
                    list.Add(rule);
                }

            if (spellsText != null && JToken.Parse(spellsText) is JArray sj)
                foreach (var t in sj.OfType<JObject>()) r.spells.Add(ParseSpell(t));

            r.LoadEntities(entitiesText);
            if (simText != null) r.Sim = ParseSim(JObject.Parse(simText));
            return r;
        }

        void LoadEntities(string json)
        {
            if (json == null || JToken.Parse(json) is not JObject o) return;
            foreach (var p in o.Properties()) entities[p.Name] = ParseEntity(p.Name, (JObject)p.Value);
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

        static IReadOnlyList<string> Strings(JToken t) =>
            t is JArray a ? a.Select(x => (string)x).ToList() : new List<string>();

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

        static int[] Dice(JToken t) => t is JArray d && d.Count >= 2 ? new[] { (int)d[0], (int)d[1] } : Array.Empty<int>();

        static EntityDef ParseEntity(string id, JObject o)
        {
            ExplosionDef boom = null;
            if (o["explodes"] is JObject x)
                boom = new ExplosionDef
                {
                    Cover = Str(x["cover"]),
                    Radius = Int(x["radius"]) ?? 1,
                    Fire = Int(x["fire"]) ?? 0,
                    Push = Int(x["push"]) ?? 0,
                    Damage = Dice(x["damage"]),
                };
            return new EntityDef
            {
                Id = id,
                Name = Str(o["name"]) ?? id,
                Hp = Int(o["hp"]) ?? 1,
                Mass = Int(o["mass"]) ?? 1,
                Flammable = Truthy(o["flammable"]),
                Creature = Truthy(o["creature"]),
                Player = Truthy(o["player"]),
                Explodes = boom,
            };
        }

        static SpellDef ParseSpell(JObject o) => new SpellDef
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
            Damage = Dice(o["damage"]),
        };

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
            }
            if (o["water"] is JObject w)
            {
                s.WaterMaxLevel = Int(w["maxLevel"]) ?? s.WaterMaxLevel;
                s.WaterUnitsPerStep = Int(w["unitsPerStep"]) ?? s.WaterUnitsPerStep;
            }
            if (o["gas"] is JObject g)
            {
                s.Gas.DecayChance = Num(g["decayChance"]) ?? s.Gas.DecayChance;
                s.Gas.MaxAmount = Int(g["maxAmount"]) ?? s.Gas.MaxAmount;
            }
            s.ConductLimit = Int(o["conductLimit"]) ?? s.ConductLimit;
            s.FallDamage = Int(o["fallDamage"]) ?? s.FallDamage;
            s.ZapDamage = Int(o["zapDamage"]) ?? s.ZapDamage;
            if (o["status"] is JObject st) s.BurnDamageEvery = Int(st["burnDamageEvery"]) ?? s.BurnDamageEvery;
            return s;
        }
    }
}
