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
        public int Elevation;
        public int Spring;
        public double Flammability;
        public double Heat = 1;
        public string FireLook;
        public LookDef Look;
        public int? Fuel;
        public string BurnsTo, BreaksTo, BurnLog;
        public string UprootsTo, UprootLog;
        public string DugTo;
        public int Hardness = 1;
        public string GrowsTo, GrowLog;
        public int SoakEvery, SoakEveryWet;
        public double Dampens;
        public int RustEvery;
        public string RustsTo;
        public string ShattersTo;
        public bool Cracks;
        public int HotFor, HotQuench;
        public string SoaksTo, DriesTo;
        public int BurnStatus, FreezeStatus, SoakStatus;
        public bool ChainsWet;
        public int StatusTicks(string verb) => verb switch
        {
            "burn" => BurnStatus,
            "freeze" => FreezeStatus,
            "soak" => SoakStatus,
            _ => 0,
        };
        public static string StatusOfVerb(string verb) => verb switch
        {
            "burn" => "burning",
            "freeze" => "frozen",
            "soak" => "wet",
            _ => null,
        };
        public double Speed = 1;
        public double WetSpeed = 1;
        public string DissolvedBy, DissolvesTo;
        public string ScoursTo;
        public bool ScourWears;
        public int RunsEvery;
        public string QuenchesTo, QuenchLog;
        public string BakesTo;
        public string SwingsTo;
        public string WaterTo;
        public bool FreezesWater, BoilsWater;
        public string BurnAsh, BurnSmoke;
        public string CrustsTo;
        public int CrustFor;
        public string ThawsTo;
        public int ThawWater;
        public int Lift;

        HashSet<string> tagSet;
        bool blocking, vessel;
        HashSet<string> TagSet
        {
            get
            {
                if (tagSet != null) return tagSet;
                var set = new HashSet<string>(Tags);
                blocking = set.Contains("blocking");
                vessel = set.Contains("vessel");
                return tagSet = set;
            }
        }
        public bool Has(string tag) => TagSet.Contains(tag);
        public bool Blocking => TagSet != null && blocking;
        public bool Vessel => TagSet != null && vessel;

        public bool Burns => Has("flammable");
        public bool Breaks => Has("breakable");
        public bool Conducts => Has("conductive");
    }

    public sealed class CoverDef
    {
        public string Id, Name;
        public IReadOnlyList<string> Tags;
        public double Flammability;
        public int? Fuel;
        public int? BurnIntensity;
        public string BurnAsh, BurnSmoke;
        public string FireLook;
        public LookDef Look;
        public string MeltsTo;
        public int MeltWater;
        public int Lasts;
        public string WitherLog;
        public int[] Damage;
        public double Speed = 1;
        public string DissolvedBy, DissolvesTo;
        public int FloatEvery;
        public int FloatReach;
        public bool RotBreaks;
        public int SpreadEvery, SpreadReach;
        public double SpreadChance = 1;
        public IReadOnlyList<string> SpreadOnto, SpreadNotOnto;
        public string Mix;
        public bool Has(string tag) => Tags.Contains(tag);
        public bool Burns => Has("flammable");
    }

    public sealed class GasDef
    {
        public string Id, Name;
        public int Scald;
        public double? Decay;
        public int BlocksSightAt;
        public int HidesAfter;
        public int CondenseEvery;
        public int CondenseAway;
        public int RainEvery;
        public int Rusts;
        public FlashDef Flash;
        public string Sheds;
        public int ShedEvery;
        public double ShedChance = 1;
        public LookDef Look;
    }

    public sealed class MachineDef
    {
        public string Id, Name, Needs;
        public int Build = 4, Max = 10, DecayEvery = 8, Mass = 3, Hold = 10;
    }

    public sealed class FlashDef
    {
        public int Fire, Push;
        public string Log;
    }

    public sealed class CarryDef
    {
        public string Id, Name, Sheds, Log;
        public CellCond If;
    }

    public sealed class SpellDef
    {
        public string Id, Name, Shape, Origin, Element, Blurb;
        public LookDef Look;
        public string Group;
        public bool Radial = true;
        public int Level, Power, Length, Size, Radius;
        public int RadiusPerCharge;
        public string Effect;
        public int Push, Wind, Range, Duration, MaxMass;
        public int Rate;
        public int[] Damage;
        public int Raise, Dig;
        public int Strength = 1;
        public int? Height;
        public string Terrain;
        public bool Taper;
        public bool Inward;
        public int PullEvery;
        public int Delay;
        public ScatterDef Scatter;
        public string Core;
        public string Then;
        public int Jagged;
        public int TravelEvery, DropPower = 1;
        public int Eye = 1;
        public int LiftTicks = 12, LiftFling = 6, LiftHurt = 2;
        public CellCond Needs;
        public string Fizzle;
        public string Leaves;
        public bool Rolls;
        public string Trail;
        public int ForceThrow, ForcePush;
        public int SeekRange;
        public CellCond SeekIf;
        public int Stepping;
        public bool Ramp;
        public IReadOnlyList<CarryDef> Carries = Array.Empty<CarryDef>();
        public int LightBright, LightDim;
        public CraterDef Crater;
        public PlaceDef Place;
    }

    public sealed class PlaceDef
    {
        public string Thing, Animal, Terrain, Cover;
        public int Count = 1, Spacing, Lasts;
        public int Within = -1;
        public IReadOnlyList<string> Onto = Array.Empty<string>();
    }

    public sealed class CraterDef
    {
        public int Radius, Depth;
        public int Strength = 1;
        public string Leaves;
    }

    public sealed class ScatterDef
    {
        public string Cover;
        public int Count, Reach;
    }

    public sealed class ExplosionDef
    {
        public string Cover;
        public int Radius, Fire, Push;
        public int[] Damage;
        public CraterDef Crater;
    }

    public sealed class EntityDef
    {
        public string Id, Name;
        public int Hp, Mass;

        internal EntityDef Copy() => (EntityDef)MemberwiseClone();
        public const int DefaultScore = 10;
        public int Str = DefaultScore, Ac = DefaultScore;
        public bool Flammable, Creature, Player;
        public string Material;
        public TerrainDef MaterialDef;
        public ComponentSet Components;
        public ExplosionDef Explodes;
        public RaiderDef Raider;
        public LookDef Look;
    }

    public sealed class FireParams
    {
        public double SpreadChance = 0.5, HeatChance = 0.04, BoilChance = 0.35, SlickBoilChance = 0.35, DownwindBonus = 0.35, SmokeChance = 0.25;

        public string Ash, Smoke;

        public int FadeCells;
    }

    public sealed class GasParams
    {
        public double DecayChance = 0.07;
        public int MaxAmount = 6;
    }

    public sealed class HeightParams
    {
        public int Min = -3, Max = 4, BlockAt = 3;
    }

    public sealed class DayParams
    {
        public double LengthMinutes = 24, StartHour = 9;
        public double DawnFrom = 5, DawnTo = 7, DuskFrom = 18, DuskTo = 20;
        public double NightFreeze = 2;
        public double FireBright = 20, FireDim = 20;
    }

    public sealed class StreamingParams
    {
        public int Reach = 2, Keep = 3;
        public int Sight = 4, Know = 7;
        public int SettleTicks = 12;
        public int LingerChunks = 16;
        public int LingerTicks = 1800;
        public int CatchUpTicks = 720;
        public int PackTicks = 300, PacksPerTick = 2;
    }

    public sealed class WeatherParams
    {
        public double PeriodMinutes = 4, RampMinutes = 0.5;
        public int CalmPeriods = 2;
        public double[] Weights = { 4, 2, 1.5, 1, 2, 1, 1 };
        public double DouseChance = 0.05, Dampen = 0.6;
        public int FillEvery = 12, DryEvery = 30;
        public double FillChance = 0.015, DryChance = 0.1;
        public int StrikeEvery = 18;
        public double StrikeChance = 0.5;
        public double ShowerChance = 0.15, MeteorChance = 0.6;
        public int MeteorEvery = 18, MeteorFallTicks = 11;
        public string MeteorSpell;
        public int FreezeEvery = 24;
        public double FreezeChance = 0.05, ThawChance = 0.05;
        public double CoverChance = 0.08, BankChance = 0.05;
        public double DeepenChance = 0.004;
        public string SnowCover, BankCover;
        public int FogEvery = 6;
        public double FogChance = 0.3;
        public string FogGas = "fog";
        public int FogRegionChunks = 2, FogPatches = 4, FogRadiusMin = 10, FogRadiusMax = 15;
        public int FogTries = 8, FogWetReach = 8;
        public double FogLow = 0.02, FogWet = 2;
        public int FogDriftTicks = 90;
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
        public string WaterBoils, WaterFreezes;
        public int ThinWater = 2, SoakPass = 8, DampTicks = 600;
        public int Seep = 2;
        public int ConductLimit = 80;
        public int FallDamage = 4, ZapDamage = 3;
        public int CliffSteps = 4, FallStepsPerHurt = 2;
        public int MostShoves = 2, ShoveStaggerTicks = 6;
        public int BurnDamageEvery = 3;
        public int ScaldEvery = 6;
        public string ShatterWhen, ShatterNotBy;
        public DefenseParams Defense = new DefenseParams();
        public HoldParams Hold = new HoldParams();
        public GolfParams Golf = new GolfParams();
        public BreathParams Breath = new BreathParams();
        public DownedParams Downed = new DownedParams();
        public DragonParams Dragon = new DragonParams();
        public StrikeParams Strike = new StrikeParams();
        public PlayerParams Player = new PlayerParams();
    }

    public sealed class CellCond
    {
        public IReadOnlyList<string> Cover;
        public bool? FireFlag;
        public double? FireMin;
        public double? FireBelow;
        public bool? Water, Frozen, Conductive;
        public bool? Melts;
        public bool? Hot;
        public bool? Freezes;
        public string Gas, TerrainTag, NotTerrainTag;
        public string Liquid;
        public int? Power;
        public double? Chance;
    }

    public sealed class Effect
    {
        public bool HasIgnite, IgniteCover; public int IgniteN;
        public int? Boil;
        public bool Melt, Freeze, Extinguish, Condense;
        public bool HasAddWater, AddWaterPower; public int AddWaterN;
        public string Liquid;
        public bool HasSetCover; public string SetCover;
        public int? SpreadCover;
        public bool Break;
        public bool Heat;
        public bool Bake;
        public bool MeltCover;
        public bool Crust;
        public bool Thaw;
        public bool Shatter;
        public string Dissolve;
        public string SetTerrain, Raise;
        public int? Conduct;
        public bool Fan, PushGas, ClearGas;
        public bool Scour;
        public bool PushCover;
        public string Mark;
        public string Gas; public int? N;
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
        static readonly string[] BaseElements = { "fire", "cold", "water", "air", "earth", "lightning", "grease", "oil", "thunder", "fog", "growth", "thorns", "acid", "steam", "lava", "mud", "dust", "rain", "sand", "heat", "smoke", "snow" };

        readonly Dictionary<string, TerrainDef> terrain = new Dictionary<string, TerrainDef>();
        readonly Dictionary<string, CoverDef> covers = new Dictionary<string, CoverDef>();
        readonly Dictionary<string, GasDef> gases = new Dictionary<string, GasDef>();
        readonly List<SpellDef> spells = new List<SpellDef>();
        readonly Dictionary<string, EntityDef> entities = new Dictionary<string, EntityDef>();
        readonly List<CellRule> cellRules = new List<CellRule>();
        readonly Dictionary<string, List<CellRule>> byElement = new Dictionary<string, List<CellRule>>();
        readonly Dictionary<string, MachineDef> machines = new Dictionary<string, MachineDef>();
        readonly Dictionary<string, LookDef> elementLooks = new Dictionary<string, LookDef>();

        internal const string ElementLooksKey = "elementLooks";
        static readonly IReadOnlyList<CellRule> NoRules = Array.Empty<CellRule>();

        public IReadOnlyDictionary<string, TerrainDef> Terrain => terrain;
        public IReadOnlyDictionary<string, CoverDef> Covers => covers;
        public bool AnySpreads { get; private set; }
        public IReadOnlyDictionary<string, GasDef> Gases => gases;
        public IReadOnlyDictionary<string, MachineDef> Machines => machines;
        public IReadOnlyList<SpellDef> Spells => spells;
        public IReadOnlyDictionary<string, EntityDef> Entities => entities;
        public IReadOnlyList<CellRule> CellRules => cellRules;
        public SimParams Sim { get; private set; } = new SimParams();

        readonly JObject doc;
        JObject written;

        Rules(JObject doc) { this.doc = doc; }

        public SpellDef Spell(string id) => spells.FirstOrDefault(s => s.Id == id);

        public FaunaTables Fauna => fauna ??= new FaunaTables(doc);
        FaunaTables fauna;

        public LookDef ElementLook(string element) => element != null && elementLooks.TryGetValue(element, out var look) ? look : null;

        public IReadOnlyDictionary<string, LookDef> ElementLooks => elementLooks;

        public EntityDef Entity(string id)
        {
            if (id == null) return null;
            return entities.TryGetValue(id, out var e) ? e : HoldVariant(id);
        }

        public IReadOnlyList<CellRule> CellRulesFor(string element) =>
            element != null && byElement.TryGetValue(element, out var list) ? list : NoRules;

        public static Rules Load(string rulesDir)
        {
            var files = new Dictionary<string, string>();
            foreach (var path in Directory.GetFiles(rulesDir, "*.json"))
                files[Path.GetFileName(path)] = File.ReadAllText(path);
            return FromJson(files);
        }

        public static Rules FromJson(IDictionary<string, string> filesByName)
        {
            JToken Get(string name, bool required)
            {
                if (filesByName.TryGetValue(name, out var text)) return JToken.Parse(text);
                if (required) throw new ArgumentException($"Rules: {name} is missing");
                return JValue.CreateNull();
            }

            var doc = Doc(Get);
            var asWritten = (JObject)doc.DeepClone();
            var grouping = MaterialGroups.Ungroup(doc);
            if (grouping.Count > 0) throw new ArgumentException("Rules: " + string.Join("; ", grouping));
            var rules = FromDoc(doc);
            rules.written = asWritten;
            return rules;
        }

        internal static JObject Doc(Func<string, bool, JToken> get)
        {
            var materials = (JObject)get("materials.json", true);
            var reactions = (JObject)get("reactions.json", true);
            return new JObject
            {
                ["elements"] = new JArray(BaseElements),
                ["terrain"] = materials["terrain"] as JObject ?? new JObject(),
                ["covers"] = materials["covers"] as JObject ?? new JObject(),
                ["gases"] = materials["gases"] as JObject ?? new JObject(),
                [LiquidsKey] = materials[LiquidsKey] as JObject ?? new JObject(),
                ["cellRules"] = reactions["cell"] as JArray ?? new JArray(),
                ["entityRules"] = reactions["entity"] as JArray ?? new JArray(),
                ["spells"] = get("spells.json", false) as JArray ?? new JArray(),
                ["critters"] = get("critters.json", false) as JArray ?? new JArray(),
                ["entities"] = get("entities.json", false) as JObject ?? new JObject(),
                ["items"] = get("items.json", false) as JObject ?? new JObject(),
                ["machines"] = get("machines.json", false) as JObject ?? new JObject(),
                ["sim"] = get("sim.json", false) as JObject ?? new JObject(),
                ["groups"] = get("groups.json", false) as JObject ?? new JObject(),
                ["components"] = get("components.json", false) as JObject ?? new JObject(),
                ["combos"] = get("combos.json", false) as JObject ?? new JObject(),
                [BuildingTables.Key] = get("buildings.json", false) as JObject ?? new JObject(),
                ["hold"] = get("hold.json", false) as JObject ?? new JObject(),
                [ElementLooksKey] = get("elements.json", false) as JObject ?? new JObject(),
                [FaunaTables.SpeciesKey] = get("fauna.json", false) as JObject ?? new JObject(),
                [FaunaTables.PartsKey] = get("parts.json", false) as JObject ?? new JObject(),
                [TriggersKey] = get("triggers.json", false) as JObject ?? new JObject(),
            };
        }

        static void ParseMachines(Rules r, JObject machines)
        {
            foreach (var p in machines.Properties())
                r.machines[p.Name] = new MachineDef
                {
                    Id = p.Name,
                    Name = Str(p.Value["name"]) ?? p.Name,
                    Needs = Str(p.Value["needs"]),
                    Build = Int(p.Value["build"]) ?? 4,
                    Max = Int(p.Value["max"]) ?? 10,
                    DecayEvery = Int(p.Value["decayEvery"]) ?? 8,
                    Mass = Int(p.Value["mass"]) ?? 3,
                    Hold = Int(p.Value["hold"]) ?? 10,
                };
        }

        static Rules FromDoc(JObject doc)
        {
            var r = new Rules(doc);
            ParseComponents(r, doc["components"] as JObject ?? new JObject());
            foreach (var p in ((JObject)doc["terrain"]).Properties()) r.terrain[p.Name] = ParseTerrain(p.Name, (JObject)p.Value);
            foreach (var p in ((JObject)doc["covers"]).Properties()) r.covers[p.Name] = ParseCover(p.Name, (JObject)p.Value);
            r.AnySpreads = r.covers.Values.Any(c => c.SpreadEvery > 0);
            foreach (var p in ((JObject)doc["gases"]).Properties())
                r.gases[p.Name] = new GasDef
                {
                    Id = p.Name,
                    Name = Str(p.Value["name"]) ?? p.Name,
                    Scald = Int(p.Value["scald"]) ?? 0,
                    Decay = Num(p.Value["decay"]),
                    BlocksSightAt = Int(p.Value["blocksSightAt"]) ?? 0,
                    HidesAfter = Int(p.Value["hidesAfter"]) ?? 0,
                    CondenseEvery = Int(p.Value["condenseEvery"]) ?? 0,
                    CondenseAway = Int(p.Value["condenseAway"]) ?? 0,
                    RainEvery = Int(p.Value["rainEvery"]) ?? 0,
                    Rusts = Int(p.Value["rusts"]) ?? 0,
                    Flash = ParseFlash(p.Value["burn"]?["flash"]),
                    Sheds = Str(p.Value["sheds"]?["element"]),
                    ShedEvery = Int(p.Value["sheds"]?["every"]) ?? 0,
                    ShedChance = Num(p.Value["sheds"]?["chance"]) ?? 1,
                    Look = ParseLook(p.Value["look"]),
                };
            foreach (var p in (doc[ElementLooksKey] as JObject ?? new JObject()).Properties())
                if (ParseLook((p.Value as JObject)?["look"]) is LookDef look) r.elementLooks[p.Name] = look;
            ParseMachines(r, doc["machines"] as JObject ?? new JObject());
            r.ParseBuildings(doc);
            r.ParseThings(doc);
            r.ParseLays(doc);
            r.ParseTriggers(doc);

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
                if (p.Value is JObject e) r.entities[p.Name] = r.ParseEntity(p.Name, e);
            foreach (var p in ((JObject)doc["items"]).Properties())
                if (p.Value is JObject i) r.items[p.Name] = r.ParseItem(p.Name, i);
            r.ResolveMaterials();
            r.Sim = ParseSim((JObject)doc["sim"]);
            r.Sim.Hold = HoldParams.Parse(doc["hold"]);
            foreach (var p in (doc["hold"]?["raiders"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) r.holdKinds[p.Name] = r.ParseEntity(p.Name, (JObject)p.Value);
            r.ParseLiquids(doc);
            ParseGroups(r, (JObject)doc["groups"]);
            ParseCombos(r, (JObject)doc["combos"]);
            return r;
        }

        static string Str(JToken t) => t == null || t.Type == JTokenType.Null ? null : (string)t;
        static int? Int(JToken t) => t == null || t.Type == JTokenType.Null ? (int?)null : (int)Math.Floor((double)t);
        static double? Num(JToken t) => t == null || t.Type == JTokenType.Null ? (double?)null : (double)t;
        static bool? Bool(JToken t) => t == null || t.Type == JTokenType.Null ? (bool?)null : Truthy(t);

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
            t is JArray a ? a.Where(x => x.Type == JTokenType.String).Select(x => (string)x).ToList() : new List<string>();

        static TerrainDef ParseTerrain(string id, JObject o) => new TerrainDef
        {
            Id = id,
            Name = Str(o["name"]) ?? id,
            Tags = Strings(o["tags"]),
            Elevation = Int(o["elevation"]) ?? 0,
            Spring = Int(o["spring"]) ?? 0,
            Flammability = Num(o["flammability"]) ?? 0,
            Heat = Num(o["heat"]) ?? 1,
            FireLook = Str(o["fireLook"]),
            Look = ParseLook(o["look"]),
            Fuel = Int(o["fuel"]),
            BurnsTo = Str(o["burnsTo"]),
            BreaksTo = Str(o["breaksTo"]),
            BurnLog = Str(o["burnLog"]),
            UprootsTo = Str(o["uprootsTo"]),
            UprootLog = Str(o["uprootLog"]),
            DugTo = Str(o["dugTo"]),
            Hardness = Int(o["hardness"]) ?? 1,
            GrowsTo = Str(o["growsTo"]),
            GrowLog = Str(o["growLog"]),
            DissolvedBy = Str(o["dissolvedBy"]),
            DissolvesTo = Str(o["dissolvesTo"]),
            ScoursTo = Str(o["scoursTo"]),
            RunsEvery = Int(o["runsEvery"]) ?? 0,
            QuenchesTo = Str(o["quenchesTo"]),
            QuenchLog = Str(o["quenchLog"]),
            ScourWears = Truthy(o["scourWears"]),
            BakesTo = Str(o["bakesTo"]),
            SwingsTo = Str(o["swingsTo"]),
            SoakEvery = Int(o["soakEvery"]) ?? 0,
            SoakEveryWet = Int(o["soakEveryWet"]) ?? 0,
            Dampens = Num(o["dampens"]) ?? 0,
            RustEvery = Int(o["rustEvery"]) ?? 0,
            RustsTo = Str(o["rustsTo"]),
            ShattersTo = Str(o["shattersTo"]),
            Cracks = Truthy(o["cracks"]),
            HotFor = Int(o["hotFor"]) ?? 0,
            HotQuench = Int(o["hotQuench"]) ?? 0,
            SoaksTo = Str(o["soaksTo"]),
            BurnStatus = Int(o["burnStatus"]) ?? 0,
            FreezeStatus = Int(o["freezeStatus"]) ?? 0,
            SoakStatus = Int(o["soakStatus"]) ?? 0,
            ChainsWet = Bool(o["chainsWet"]) ?? false,
            DriesTo = Str(o["driesTo"]),
            Speed = Num(o["speed"]) ?? 1,
            WetSpeed = Num(o["wetSpeed"]) ?? 1,
            WaterTo = Str(o["waterTo"]),
            FreezesWater = Str(o["waterTo"]) == MaterialGroups.FreezesWater,
            BoilsWater = Str(o["waterTo"]) == MaterialGroups.BoilsWater,
            BurnAsh = Str(o["burnAsh"]),
            BurnSmoke = Str(o["burnSmoke"]),
            CrustsTo = Str(o["crustsTo"]),
            CrustFor = Int(o["crustFor"]) ?? 1,
            ThawsTo = Str(o["thawsTo"]),
            ThawWater = Int(o["thawWater"]) ?? 0,
            Lift = Int(o["liftSteps"]) ?? 0,
        };

        static CoverDef ParseCover(string id, JObject o) => new CoverDef
        {
            Id = id,
            Name = Str(o["name"]) ?? id,
            Tags = Strings(o["tags"]),
            Mix = Str(o[MixKey]),
            Flammability = Num(o["flammability"]) ?? 0,
            Fuel = Int(o["fuel"]),
            BurnIntensity = Int(o["burnIntensity"]),
            BurnAsh = Str(o["burnAsh"]),
            BurnSmoke = Str(o["burnSmoke"]),
            FireLook = Str(o["fireLook"]),
            MeltsTo = Str(o["meltsTo"]),
            MeltWater = Int(o["meltWater"]) ?? 0,
            Lasts = Int(o["lasts"]) ?? 0,
            WitherLog = Str(o["witherLog"]),
            Damage = Dice(o["damage"]),
            Speed = Num(o["speed"]) ?? 1,
            DissolvedBy = Str(o["dissolvedBy"]),
            DissolvesTo = Str(o["dissolvesTo"]),
            FloatEvery = Int(o["floatEvery"]) ?? 0,
            FloatReach = Int(o["floatReach"]) ?? 0,
            RotBreaks = Truthy(o["rotBreaks"]),
            SpreadEvery = Int(o["spreadEvery"]) ?? 0,
            SpreadChance = Num(o["spreadChance"]) ?? 1,
            SpreadOnto = Strings(o["spreadOnto"]),
            SpreadNotOnto = Strings(o["spreadNotOnto"]),
            SpreadReach = Int(o["spreadReach"]) ?? 0,
            Look = ParseLook(o["look"]),
        };

        static int[] Dice(JToken t) => t is JArray d && d.Count >= 2 ? new[] { (int)d[0], (int)d[1] } : Array.Empty<int>();

        void ResolveMaterials()
        {
            foreach (var e in entities.Values)
                if (e.Material != null && terrain.TryGetValue(e.Material, out var m)) e.MaterialDef = m;
        }

        EntityDef ParseEntity(string id, JObject o)
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
            var components = ThingComponents(o);
            return new EntityDef
            {
                Id = id,
                Name = Str(o["name"]) ?? id,
                Hp = Int(o["hp"]) ?? 1,
                Mass = Int(o["mass"]) ?? 1,
                Str = Int(o["str"]) ?? EntityDef.DefaultScore,
                Ac = Int(o["ac"]) ?? EntityDef.DefaultScore,
                Components = components,
                Flammable = components.Has(Component(Rules.Flammable)),
                Creature = components.Has(Component(Rules.Creature)),
                Player = Truthy(o["player"]),
                Material = Str(o["material"]),
                Explodes = boom,
                Raider = ParseRaider(o["raider"]),
                Look = ParseLook(o["look"]),
            };
        }

        static SpellDef ParseSpell(JObject o) => new SpellDef
        {
            Id = Str(o["id"]),
            Name = Str(o["name"]) ?? Str(o["id"]),
            Look = ParseLook(o["look"]),
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
            Strength = Int(o["strength"]) ?? 1,
            Height = Int(o["height"]),
            Terrain = Str(o["terrain"]),
            Taper = Truthy(o["taper"]),
            Inward = Truthy(o["inward"]),
            PullEvery = Int(o["pullEvery"]) ?? 0,
            Delay = Int(o["delay"]) ?? 0,
            Scatter = o["scatter"] is JObject sc ? new ScatterDef { Cover = Str(sc["cover"]), Count = Int(sc["count"]) ?? 0, Reach = Int(sc["reach"]) ?? 0 } : null,
            Core = Str(o["core"]),
            Then = Str(o["then"]),
            Jagged = Int(o["jagged"]) ?? 0,
            TravelEvery = Int(o["travelEvery"]) ?? 0,
            DropPower = Int(o["dropPower"]) ?? 1,
            Eye = Int(o["eye"]) ?? 1,
            LiftTicks = Int(o["lift"]?["ticks"]) ?? 12,
            LiftFling = Int(o["lift"]?["fling"]) ?? 6,
            LiftHurt = Int(o["lift"]?["hurt"]) ?? 2,
            Needs = o["needs"] is JObject nd ? ParseCond(nd) : null,
            Fizzle = Str(o["fizzle"]),
            Leaves = Str(o["leaves"]),
            Rolls = Truthy(o["rolls"]),
            Trail = Str(o["trail"]),
            Stepping = Int(o["stepping"]) ?? 0,
            ForceThrow = Int(o["force"]?["throw"]) ?? 0,
            ForcePush = Int(o["force"]?["push"]) ?? 0,
            SeekRange = Int(o["seeks"]?["range"]) ?? 0,
            SeekIf = o["seeks"]?["if"] is JObject si ? ParseCond(si) : null,
            Ramp = Truthy(o["ramp"]),
            Carries = (o["carries"] as JArray)?.OfType<JObject>().Select(ParseCarry).ToList() ?? (IReadOnlyList<CarryDef>)Array.Empty<CarryDef>(),
            LightBright = Int((o["light"] as JObject)?["bright"]) ?? 0,
            LightDim = Int((o["light"] as JObject)?["dim"]) ?? 0,
            Crater = ParseCrater(o["crater"]),
            Place = ParsePlace(o["place"]),
        };

        static PlaceDef ParsePlace(JToken t) => t is JObject p
            ? new PlaceDef
            {
                Thing = Str(p["thing"]),
                Animal = Str(p["animal"]),
                Terrain = Str(p["terrain"]),
                Cover = Str(p["cover"]),
                Count = Int(p["count"]) ?? 1,
                Within = Int(p["within"]) ?? -1,
                Spacing = Int(p["spacing"]) ?? 0,
                Lasts = Int(p["lasts"]) ?? 0,
                Onto = (p["onto"] as JArray)?.Select(x => (string)x).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
            }
            : null;

        static LookDef ParseLook(JToken t)
        {
            if (!(t is JObject o)) return null;
            var look = new LookDef { Family = Str(o["family"]), Form = Str(o["form"]) };
            if (o["color"] is JArray c && c.Count == 3)
            {
                look.R = (float)c[0];
                look.G = (float)c[1];
                look.B = (float)c[2];
            }
            look.Height = (float)(Num(o["height"]) ?? 0);
            look.Glow = (float)(Num(o["glow"]) ?? 0);
            look.Roughness = (float)(Num(o["roughness"]) ?? -1);
            look.Flow = (float)(Num(o["flow"]) ?? 0);
            look.Baked = Truthy(o["baked"]);
            look.Species = Str(o["species"]);
            look.Scale = (float)(Num(o["scale"]) ?? 1);
            look.Mix = LookMix.Parse(o["mix"], ScopedLook.IsTrue(o["blend"]));
            look.Scoped = ScopedLook.ParseAll(o["scoped"]);
            if (o["mix"] != null || o["scoped"] != null)
                look.Extra = new JObject { ["mix"] = o["mix"]?.DeepClone(), ["blend"] = o["blend"]?.DeepClone(), ["scoped"] = o["scoped"]?.DeepClone() }.ToString(Newtonsoft.Json.Formatting.None);
            return look;
        }

        static CraterDef ParseCrater(JToken t) =>
            t is JObject c ? new CraterDef { Radius = Int(c["radius"]) ?? 1, Depth = Int(c["depth"]) ?? 1, Strength = Int(c["strength"]) ?? 1, Leaves = Str(c["leaves"]) } : null;

        static FlashDef ParseFlash(JToken t) =>
            t is JObject f ? new FlashDef { Fire = Int(f["fire"]) ?? 1, Push = Int(f["push"]) ?? 0, Log = Str(f["log"]) } : null;

        static CarryDef ParseCarry(JObject o) => new CarryDef
        {
            Id = Str(o["id"]),
            Name = Str(o["name"]) ?? Str(o["id"]),
            Sheds = Str(o["sheds"]),
            Log = Str(o["log"]),
            If = o["if"] is JObject c ? ParseCond(c) : null,
        };

        static CellCond ParseCond(JObject c)
        {
            var cond = new CellCond
            {
                Cover = c["cover"] is JArray ? Strings(c["cover"]) : null,
                FireBelow = Num(c["fireBelow"]),
                Water = Bool(c["water"]),
                Frozen = Bool(c["frozen"]),
                Melts = Bool(c["melts"]),
                Hot = Bool(c["hot"]),
                Freezes = Bool(c["freezes"]),
                Conductive = Bool(c["conductive"]),
                Gas = Str(c["gas"]),
                Liquid = Str(c["liquid"]),
                TerrainTag = Str(c["terrainTag"]),
                NotTerrainTag = Str(c["notTerrainTag"]),
                Power = Int(c["power"]),
                Chance = Num(c["chance"]),
            };
            var fire = c["fire"];
            if (fire != null && fire.Type == JTokenType.Boolean) cond.FireFlag = (bool)fire;
            else if (fire != null && (fire.Type == JTokenType.Integer || fire.Type == JTokenType.Float)) cond.FireMin = (double)fire;
            return cond;
        }

        static CellRule ParseRule(JObject o)
        {
            var cond = o["if"] is JObject c ? ParseCond(c) : null;
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
            e.Liquid = Str(o["liquid"]);
            if (o.TryGetValue("setCover", out var sc))
            {
                e.HasSetCover = true;
                e.SetCover = Str(sc);
            }
            e.SpreadCover = Int(o["spreadCover"]);
            e.Break = Truthy(o["break"]);
            e.Heat = Truthy(o["heat"]);
            e.Bake = Truthy(o["bake"]);
            e.MeltCover = Truthy(o["meltCover"]);
            e.Crust = Truthy(o["crust"]);
            e.Thaw = Truthy(o["thaw"]);
            e.Shatter = Truthy(o["shatter"]);
            e.Dissolve = Str(o["dissolve"]);
            e.SetTerrain = Str(o["setTerrain"]);
            e.Raise = Str(o["raise"]);
            e.Conduct = Int(o["conduct"]);
            e.Fan = Truthy(o["fan"]);
            e.PushGas = Truthy(o["pushGas"]);
            e.ClearGas = Truthy(o["clearGas"]);
            e.Scour = Truthy(o["scour"]);
            e.PushCover = Truthy(o["pushCover"]);
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

        static void ParseStatus(SimParams s, JObject st)
        {
            s.BurnDamageEvery = Int(st["burnDamageEvery"]) ?? s.BurnDamageEvery;
            s.ScaldEvery = Int(st["scaldEvery"]) ?? s.ScaldEvery;
            if (!(st["shatter"] is JObject sh)) return;
            s.ShatterWhen = Str(sh["when"]);
            s.ShatterNotBy = Str(sh["notBy"]);
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
                s.Fire.SlickBoilChance = Num(f["slickBoilChance"]) ?? s.Fire.BoilChance;
                s.Fire.DownwindBonus = Num(f["downwindBonus"]) ?? s.Fire.DownwindBonus;
                s.Fire.SmokeChance = Num(f["smokeChance"]) ?? s.Fire.SmokeChance;
                s.Fire.FadeCells = Int(f["fadeCells"]) ?? s.Fire.FadeCells;
                s.Fire.Ash = Str(f["ash"]);
                s.Fire.Smoke = Str(f["smoke"]);
            }
            if (o["water"] is JObject w)
            {
                s.WaterMaxLevel = Int(w["maxLevel"]) ?? s.WaterMaxLevel;
                s.WaterBoils = Str(w["boils"]);
                s.WaterFreezes = Str(w["freezes"]);
                s.WaterUnitsPerStep = Int(w["unitsPerStep"]) ?? s.WaterUnitsPerStep;
                s.ThinWater = Int(w["thin"]) ?? s.ThinWater;
                s.SoakPass = Int(w["soakPass"]) ?? s.SoakPass;
                s.DampTicks = Int(w["dampTicks"]) ?? s.DampTicks;
                s.Seep = Int(w["seep"]) ?? s.Seep;
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
            s.CliffSteps = Int(o["fall"]?["cliffSteps"]) ?? s.CliffSteps;
            s.FallStepsPerHurt = Int(o["fall"]?["stepsPerHurt"]) ?? s.FallStepsPerHurt;
            s.MostShoves = Int(o["shove"]?["most"]) ?? s.MostShoves;
            s.ShoveStaggerTicks = Int(o["shove"]?["staggerTicks"]) ?? s.ShoveStaggerTicks;
            if (o["status"] is JObject st) ParseStatus(s, st);
            s.Defense = ParseDefense(o["defense"]);
            s.Golf = GolfParams.Parse(o["golf"]);
            s.Breath = BreathParams.Parse(o["breath"]);
            s.Downed = DownedParams.Parse(o["downed"]);
            s.Dragon = DragonParams.Parse(o["dragon"]);
            s.Strike = StrikeParams.Parse(o["strike"]);
            s.Player = PlayerParams.Parse(o["player"]);
            if (o["day"] is JObject day)
            {
                s.Day.LengthMinutes = Num(day["lengthMinutes"]) ?? s.Day.LengthMinutes;
                s.Day.StartHour = Num(day["startHour"]) ?? s.Day.StartHour;
                if (day["dawn"] is JArray dawn && dawn.Count == 2) (s.Day.DawnFrom, s.Day.DawnTo) = ((double)dawn[0], (double)dawn[1]);
                if (day["dusk"] is JArray dusk && dusk.Count == 2) (s.Day.DuskFrom, s.Day.DuskTo) = ((double)dusk[0], (double)dusk[1]);
                s.Day.NightFreeze = Num(day["nightFreeze"]) ?? s.Day.NightFreeze;
                s.Day.FireBright = Num(day["fireLight"]?["bright"]) ?? s.Day.FireBright;
                s.Day.FireDim = Num(day["fireLight"]?["dim"]) ?? s.Day.FireDim;
            }
            if (o["streaming"] is JObject so)
            {
                s.Streaming.Reach = Int(so["reach"]) ?? s.Streaming.Reach;
                s.Streaming.Keep = Int(so["keep"]) ?? s.Streaming.Keep;
                s.Streaming.Sight = Int(so["sight"]) ?? s.Streaming.Sight;
                s.Streaming.Know = Int(so["know"]) ?? s.Streaming.Know;
                s.Streaming.SettleTicks = Int(so["settleTicks"]) ?? s.Streaming.SettleTicks;
                s.Streaming.LingerChunks = Int(so["lingerChunks"]) ?? s.Streaming.LingerChunks;
                s.Streaming.LingerTicks = Int(so["lingerTicks"]) ?? s.Streaming.LingerTicks;
                s.Streaming.CatchUpTicks = Int(so["catchUpTicks"]) ?? s.Streaming.CatchUpTicks;
                s.Streaming.PackTicks = Int(so["packTicks"]) ?? s.Streaming.PackTicks;
                s.Streaming.PacksPerTick = Int(so["packsPerTick"]) ?? s.Streaming.PacksPerTick;
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
                w.DeepenChance = Num(snow["deepenChance"]) ?? w.DeepenChance;
                w.SnowCover = Str(snow["cover"]);
                w.BankCover = Str(snow["bank"]);
            }
            if (o["fog"] is JObject fog)
            {
                w.FogEvery = Int(fog["every"]) ?? w.FogEvery;
                w.FogChance = Num(fog["chance"]) ?? w.FogChance;
                w.FogGas = Str(fog["gas"]) ?? w.FogGas;
                w.FogRegionChunks = Int(fog["regionChunks"]) ?? w.FogRegionChunks;
                w.FogPatches = Int(fog["patches"]) ?? w.FogPatches;
                w.FogRadiusMin = Int(fog["radiusMin"]) ?? w.FogRadiusMin;
                w.FogRadiusMax = Int(fog["radiusMax"]) ?? w.FogRadiusMax;
                w.FogTries = Int(fog["tries"]) ?? w.FogTries;
                w.FogWetReach = Int(fog["wetReach"]) ?? w.FogWetReach;
                w.FogLow = Num(fog["low"]) ?? w.FogLow;
                w.FogWet = Num(fog["wet"]) ?? w.FogWet;
                w.FogDriftTicks = Int(fog["driftTicks"]) ?? w.FogDriftTicks;
            }
            if (o["meteors"] is JObject meteors)
            {
                w.ShowerChance = Num(meteors["showerChance"]) ?? w.ShowerChance;
                w.MeteorEvery = Int(meteors["every"]) ?? w.MeteorEvery;
                w.MeteorChance = Num(meteors["chance"]) ?? w.MeteorChance;
                w.MeteorFallTicks = Int(meteors["fallTicks"]) ?? w.MeteorFallTicks;
                w.MeteorSpell = Str(meteors["spell"]);
            }
        }
    }
}
