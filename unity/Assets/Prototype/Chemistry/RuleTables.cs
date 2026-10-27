// Typed view of ../rules/*.json, the same tables the web sandbox runs on
// (sandbox/src/rules.ts). Only the cell-level parts, entities.json (for the
// Unity physics props and the defense field's raiders, with reactions.json's
// entity rules: Defense.cs) and items.json (things to carry, Items.cs) are interpreted; the other files (critters, machines)
// are kept as JSON so live mods (Rules.Mods.cs) can patch and check them, but
// nothing here reads them.
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
        /// <summary>Water a spring keeps the square filled to each tick (units; a river, a spring), or 0.</summary>
        public int Spring;
        public double Flammability;
        /// <summary>Null when the table leaves it out (code falls back to 5, like the TS).</summary>
        public int? Fuel;
        public string BurnsTo, BreaksTo, BurnLog;
        /// <summary>What earth shaping leaves of it (a tree topples into deadfall) before shaping the ground, or null; and the log line.</summary>
        public string UprootsTo, UprootLog;
        /// <summary>What digging leaves (grass is dug down to dirt), or null to keep the terrain.</summary>
        public string DugTo;
        /// <summary>
        /// What it grows into once its Cell.Life runs out (green shoots into grass, a sapling into a
        /// tree), or null; and the line logged when it does.
        /// </summary>
        public string GrowsTo, GrowLog;

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
        /// <summary>Ticks it lasts once laid (Cell.Life) before it withers away (thorns), or 0 for good; and the line logged then.</summary>
        public int Lasts;
        public string WitherLog;
        /// <summary>What it does to a creature walking through it: [dice, sides] damage a square (empty for none), and its speed (1 unhindered).</summary>
        public int[] Damage;
        public double Speed = 1;
        public bool Has(string tag) => Tags.Contains(tag);
    }

    public sealed class GasDef
    {
        public string Id, Name;
        public int Scald;
        /// <summary>Its own chance per tick a unit decays (fog lingers), or null for sim.gas.decayChance.</summary>
        public double? Decay;
        /// <summary>From how many units a square of it hides what's behind (World.LineOfSight), or 0 if it never does.</summary>
        public int BlocksSightAt;
    }

    /// <summary>
    /// A spell. Level/Power/Length/Size/Radius hold the value the sim actually uses,
    /// i.e. the TS defaults are applied (Power 1, Length 5, Size 2, Radius 1).
    /// </summary>
    public sealed class SpellDef
    {
        public string Id, Name, Shape, Origin, Element, Blurb;
        /// <summary>The spell radial's element it sits under (groups.json), or null.</summary>
        public string Group;
        /// <summary>False keeps a spell off the Unity game's spell radial (it stays in the rules for mods, tests and the sandbox).</summary>
        public bool Radial = true;
        public int Level, Power, Length, Size, Radius;
        /// <summary>Squares a burst's radius grows by for each level of charge past a tap (Plant Growth), 0 for none.</summary>
        public int RadiusPerCharge;
        // Extras: the cell sim uses Wind and (below) the terraform fields; Push and Duration drive the Unity props (SpellForces).
        public string Effect;
        public int Push, Wind, Range, Duration, MaxMass;
        /// <summary>Effect "haste": how many times as fast the day clock runs (World.Time.cs).</summary>
        public int Rate;
        /// <summary>[dice, sides], or empty.</summary>
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
        /// <summary>The ground the spell blows out where it lands (Fireball), or null.</summary>
        public CraterDef Crater;
    }

    /// <summary>
    /// Ground a blast digs out ("crater" on a spell or an entity's "explodes"): Depth steps in the
    /// middle, tapering to none past Radius squares.
    /// </summary>
    public sealed class CraterDef
    {
        public int Radius, Depth;
    }

    /// <summary>What an entity leaves behind when it blows up (entities.json "explodes").</summary>
    public sealed class ExplosionDef
    {
        public string Cover;
        public int Radius, Fire, Push;
        /// <summary>[dice, sides], or empty.</summary>
        public int[] Damage;
        /// <summary>The ground it blows out, or null.</summary>
        public CraterDef Crater;
    }

    /// <summary>A thing on the board (entities.json): a wizard, a crate, a barrel, a boulder.</summary>
    public sealed class EntityDef
    {
        public string Id, Name;
        public int Hp, Mass;
        public bool Flammable, Creature, Player;
        /// <summary>Null unless it blows up when destroyed.</summary>
        public ExplosionDef Explodes;
        /// <summary>Null unless it raids the defense field (Defense.cs).</summary>
        public RaiderDef Raider;
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

    /// <summary>sim.json's day: how long a day lasts in real minutes, and the hour the world starts at.</summary>
    public sealed class DayParams
    {
        public double LengthMinutes = 24, StartHour = 9;
    }

    /// <summary>
    /// sim.json's streaming (ChunkStreaming, docs/WORLD_STREAMING.md): a character's reach, the
    /// chunks within Reach of the one it stands in, is kept active; a chunk deactivates once no
    /// character is within Keep. C# only: the sandbox has no chunks.
    /// </summary>
    public sealed class StreamingParams
    {
        public int Reach = 2, Keep = 3;
        /// <summary>A chunk is settled once nothing in it has been live for this many ticks.</summary>
        public int SettleTicks = 12;
    }

    /// <summary>sim.json's weather (Sky.cs picks it, World.Weather.cs applies it).</summary>
    public sealed class WeatherParams
    {
        /// <summary>Real minutes per stretch of one weather, and of its ramp in and out.</summary>
        public double PeriodMinutes = 4, RampMinutes = 0.5;
        /// <summary>Stretches of clear sky the world starts with.</summary>
        public int CalmPeriods = 2;
        /// <summary>How often each WeatherKind comes up, in WeatherKind's order (sim.json's weights by name).</summary>
        public double[] Weights = { 4, 2, 1.5, 1, 2, 1, 1 };
        /// <summary>At full rain: the chance per tick a fire burns down a step, and how much less fire spreads.</summary>
        public double DouseChance = 0.05, Dampen = 0.6;
        /// <summary>Every fillEvery ticks of rain, each square's chance of a unit of water; every dryEvery ticks of no rain, a shallow square's chance to dry a unit.</summary>
        public int FillEvery = 12, DryEvery = 30;
        public double FillChance = 0.015, DryChance = 0.1;
        /// <summary>Every strikeEvery ticks of storm, the chance (at full strength) lightning strikes a square.</summary>
        public int StrikeEvery = 18;
        public double StrikeChance = 0.5;
        /// <summary>
        /// The chance a clear stretch is a meteor shower; in one, every MeteorEvery ticks the chance
        /// (at full strength) a meteor appears, which lands as the Meteor spell MeteorFallTicks later.
        /// </summary>
        public double ShowerChance = 0.15, MeteorChance = 0.6;
        public int MeteorEvery = 18, MeteorFallTicks = 11;
        /// <summary>
        /// Every FreezeEvery ticks of snow, a shallow square's chance (at full strength) its water freezes;
        /// every dryEvery ticks of neither rain nor snow, a shallow frozen square's chance to thaw.
        /// </summary>
        public int FreezeEvery = 24;
        public double FreezeChance = 0.05, ThawChance = 0.05;
        /// <summary>
        /// On the same beat, at full strength: open dry ground's chance of a cover of snow, and a snowy
        /// square's chance to drift into a bank where something blocking shelters it (upwind of it, in a wind).
        /// </summary>
        public double CoverChance = 0.08, BankChance = 0.05;
    }

    public sealed class SimParams
    {
        public int TicksPerSecond = 6;
        public DayParams Day = new DayParams();
        public StreamingParams Streaming = new StreamingParams();
        public WeatherParams Weather = new WeatherParams();
        public FireParams Fire = new FireParams();
        public GasParams Gas = new GasParams();
        public HeightParams Height = new HeightParams();
        public int WaterMaxLevel = 16, WaterUnitsPerStep = 4;
        public int ConductLimit = 80;
        // Not used by the cell port, carried so a view can read them.
        public int FallDamage = 4, ZapDamage = 3;
        /// <summary>A burning entity takes 1 damage on every tick divisible by this (sim.json status).</summary>
        public int BurnDamageEvery = 3;
        /// <summary>A creature in thick steam is scalded on every tick divisible by this (sim.json status).</summary>
        public int ScaldEvery = 6;
        /// <summary>The defense field's waves (sim.json defense).</summary>
        public DefenseParams Defense = new DefenseParams();
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
        /// <summary>The least power the element must arrive with (a charged cast's).</summary>
        public int? Power;
        /// <summary>The chance the rule applies, rolled last, once everything else holds.</summary>
        public double? Chance;
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
        /// <summary>
        /// grow: the terrain the cell becomes, with a Cell.Life of TicksMin..TicksMax (a roll of
        /// the world's generator between them) plus Wave ticks for each square from where the
        /// spell landed, so it ripens outward from the middle.
        /// </summary>
        public string Grow; public int TicksMin = 1, TicksMax = 1, Wave;
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
        static readonly string[] BaseElements = { "fire", "cold", "water", "air", "earth", "lightning", "grease", "oil", "thunder", "fog", "growth", "thorns" };

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

        /// <summary>
        /// Every table as JSON, in the shape of the sandbox's `Rules` object (elements, terrain,
        /// covers, gases, cellRules, entityRules, spells, critters, entities, items, machines, sim, groups).
        /// Mods patch this, never the typed tables, so merging works exactly like composeRules.
        /// </summary>
        readonly JObject doc;

        Rules(JObject doc) { this.doc = doc; }

        public SpellDef Spell(string id) => spells.FirstOrDefault(s => s.Id == id);

        /// <summary>The entity with this id, or null.</summary>
        public EntityDef Entity(string id) => id != null && entities.TryGetValue(id, out var e) ? e : null;

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
                ["items"] = Get("items.json", false) as JObject ?? new JObject(),
                ["machines"] = Get("machines.json", false) as JObject ?? new JObject(),
                ["sim"] = Get("sim.json", false) as JObject ?? new JObject(),
                ["groups"] = Get("groups.json", false) as JObject ?? new JObject(),
            });
        }

        /// <summary>The typed tables for a rule document (see <see cref="doc"/>), which the result keeps.</summary>
        static Rules FromDoc(JObject doc)
        {
            var r = new Rules(doc);
            foreach (var p in ((JObject)doc["terrain"]).Properties()) r.terrain[p.Name] = ParseTerrain(p.Name, (JObject)p.Value);
            foreach (var p in ((JObject)doc["covers"]).Properties()) r.covers[p.Name] = ParseCover(p.Name, (JObject)p.Value);
            foreach (var p in ((JObject)doc["gases"]).Properties())
                r.gases[p.Name] = new GasDef
                {
                    Id = p.Name,
                    Name = Str(p.Value["name"]) ?? p.Name,
                    Scald = Int(p.Value["scald"]) ?? 0,
                    Decay = Num(p.Value["decay"]),
                    BlocksSightAt = Int(p.Value["blocksSightAt"]) ?? 0,
                };

            foreach (var t in doc["cellRules"].OfType<JObject>())
            {
                var rule = ParseRule(t);
                r.cellRules.Add(rule);
                if (rule.Element == null) continue;
                if (!r.byElement.TryGetValue(rule.Element, out var list)) r.byElement[rule.Element] = list = new List<CellRule>();
                list.Add(rule);
            }

            ParseEntityRules(r, doc["entityRules"]);
            foreach (var t in doc["spells"].OfType<JObject>()) r.spells.Add(ParseSpell(t));
            foreach (var p in ((JObject)doc["entities"]).Properties())
                if (p.Value is JObject e) r.entities[p.Name] = ParseEntity(p.Name, e);
            foreach (var p in ((JObject)doc["items"]).Properties())
                if (p.Value is JObject i) r.items[p.Name] = ParseItem(p.Name, i);
            r.Sim = ParseSim((JObject)doc["sim"]);
            ParseGroups(r, (JObject)doc["groups"]);
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
            Spring = Int(o["spring"]) ?? 0,
            Flammability = Num(o["flammability"]) ?? 0,
            Fuel = Int(o["fuel"]),
            BurnsTo = Str(o["burnsTo"]),
            BreaksTo = Str(o["breaksTo"]),
            BurnLog = Str(o["burnLog"]),
            UprootsTo = Str(o["uprootsTo"]),
            UprootLog = Str(o["uprootLog"]),
            DugTo = Str(o["dugTo"]),
            GrowsTo = Str(o["growsTo"]),
            GrowLog = Str(o["growLog"]),
        };

        static CoverDef ParseCover(string id, JObject o) => new CoverDef
        {
            Id = id,
            Name = Str(o["name"]) ?? id,
            Tags = Strings(o["tags"]),
            Flammability = Num(o["flammability"]) ?? 0,
            Fuel = Int(o["fuel"]),
            BurnIntensity = Int(o["burnIntensity"]),
            Lasts = Int(o["lasts"]) ?? 0,
            WitherLog = Str(o["witherLog"]),
            Damage = Dice(o["damage"]),
            Speed = Num(o["speed"]) ?? 1,
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
                    Crater = ParseCrater(x["crater"]),
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
                Raider = ParseRaider(o["raider"]),
            };
        }

        static SpellDef ParseSpell(JObject o) => new SpellDef
        {
            Id = Str(o["id"]),
            Name = Str(o["name"]) ?? Str(o["id"]),
            Shape = Str(o["shape"]) ?? "point",
            Origin = Str(o["origin"]),
            Element = Str(o["element"]),
            Group = Str(o["group"]),
            Radial = o["radial"]?.Type != JTokenType.Boolean || (bool)o["radial"],
            Blurb = Str(o["blurb"]) ?? "",
            Level = Int(o["level"]) ?? 0,
            Power = Int(o["power"]) ?? 1,
            Length = Int(o["length"]) ?? 5,
            Size = Int(o["size"]) ?? 2,
            Radius = Int(o["radius"]) ?? 1,
            RadiusPerCharge = Int(o["radiusPerCharge"]) ?? 0,
            Effect = Str(o["effect"]),
            Push = Int(o["push"]) ?? 0,
            Wind = Int(o["wind"]) ?? 0,
            Range = Int(o["range"]) ?? 6,
            Duration = Int(o["duration"]) ?? 40,
            MaxMass = Int(o["maxMass"]) ?? 1,
            Rate = Int(o["rate"]) ?? 1,
            Damage = Dice(o["damage"]),
            Raise = Int(o["raise"]) ?? 0,
            Dig = Int(o["dig"]) ?? 0,
            Height = Int(o["height"]),
            Terrain = Str(o["terrain"]),
            Taper = Truthy(o["taper"]),
            Crater = ParseCrater(o["crater"]),
        };

        static CraterDef ParseCrater(JToken t) =>
            t is JObject c ? new CraterDef { Radius = Int(c["radius"]) ?? 1, Depth = Int(c["depth"]) ?? 1 } : null;

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
                    Power = Int(c["power"]),
                    Chance = Num(c["chance"]),
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
            e.Grow = Str(o["grow"]);
            if (o["ticks"] is JArray ticks && ticks.Count >= 2)
            {
                e.TicksMin = Int(ticks[0]) ?? 1;
                e.TicksMax = Int(ticks[1]) ?? e.TicksMin;
            }
            e.Wave = Int(o["wave"]) ?? 0;
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
            if (o["status"] is JObject st)
            {
                s.BurnDamageEvery = Int(st["burnDamageEvery"]) ?? s.BurnDamageEvery;
                s.ScaldEvery = Int(st["scaldEvery"]) ?? s.ScaldEvery;
            }
            s.Defense = ParseDefense(o["defense"]);
            if (o["day"] is JObject day)
            {
                s.Day.LengthMinutes = Num(day["lengthMinutes"]) ?? s.Day.LengthMinutes;
                s.Day.StartHour = Num(day["startHour"]) ?? s.Day.StartHour;
            }
            if (o["streaming"] is JObject so)
            {
                s.Streaming.Reach = Int(so["reach"]) ?? s.Streaming.Reach;
                s.Streaming.Keep = Int(so["keep"]) ?? s.Streaming.Keep;
                s.Streaming.SettleTicks = Int(so["settleTicks"]) ?? s.Streaming.SettleTicks;
            }
            if (o["weather"] is JObject wo) ParseWeather(wo, s.Weather);
            return s;
        }

        static void ParseWeather(JObject o, WeatherParams w)
        {
            w.PeriodMinutes = Num(o["periodMinutes"]) ?? w.PeriodMinutes;
            w.RampMinutes = Num(o["rampMinutes"]) ?? w.RampMinutes;
            w.CalmPeriods = Int(o["calmPeriods"]) ?? w.CalmPeriods;
            if (o["weights"] is JObject weights)
            {
                var names = Enum.GetNames(typeof(WeatherKind));
                for (int k = 0; k < names.Length; k++)
                    w.Weights[k] = Num(weights[names[k].ToLowerInvariant()]) ?? w.Weights[k];
            }
            if (o["rain"] is JObject rain)
            {
                w.DouseChance = Num(rain["douseChance"]) ?? w.DouseChance;
                w.Dampen = Num(rain["dampen"]) ?? w.Dampen;
                w.FillEvery = Int(rain["fillEvery"]) ?? w.FillEvery;
                w.FillChance = Num(rain["fillChance"]) ?? w.FillChance;
                w.DryEvery = Int(rain["dryEvery"]) ?? w.DryEvery;
                w.DryChance = Num(rain["dryChance"]) ?? w.DryChance;
            }
            if (o["storm"] is JObject storm)
            {
                w.StrikeEvery = Int(storm["strikeEvery"]) ?? w.StrikeEvery;
                w.StrikeChance = Num(storm["strikeChance"]) ?? w.StrikeChance;
            }
            if (o["snow"] is JObject snow)
            {
                w.FreezeEvery = Int(snow["freezeEvery"]) ?? w.FreezeEvery;
                w.FreezeChance = Num(snow["freezeChance"]) ?? w.FreezeChance;
                w.ThawChance = Num(snow["thawChance"]) ?? w.ThawChance;
                w.CoverChance = Num(snow["coverChance"]) ?? w.CoverChance;
                w.BankChance = Num(snow["bankChance"]) ?? w.BankChance;
            }
            if (o["meteors"] is JObject meteors)
            {
                w.ShowerChance = Num(meteors["showerChance"]) ?? w.ShowerChance;
                w.MeteorEvery = Int(meteors["every"]) ?? w.MeteorEvery;
                w.MeteorChance = Num(meteors["chance"]) ?? w.MeteorChance;
                w.MeteorFallTicks = Int(meteors["fallTicks"]) ?? w.MeteorFallTicks;
            }
        }
    }
}
