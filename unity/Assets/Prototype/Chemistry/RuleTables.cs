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
        /// <summary>How hot it burns (materials.json "heat", 1 when absent): a burning cell of it is this many times as likely to light its neighbours (thatch flares, so it lights the log walls beside it).</summary>
        public double Heat = 1;
        /// <summary>How its fire looks (materials.json "fireLook": flare, wood or oil; null the plain fire). The view's only.</summary>
        public string FireLook;
        /// <summary>Null when the table leaves it out (code falls back to 5, like the TS).</summary>
        public int? Fuel;
        public string BurnsTo, BreaksTo, BurnLog;
        /// <summary>What earth shaping leaves of it (a tree topples into deadfall) before shaping the ground, or null; and the log line.</summary>
        public string UprootsTo, UprootLog;
        /// <summary>What digging leaves (grass is dug down to dirt), or null to keep the terrain.</summary>
        public string DugTo;
        /// <summary>How hard it is to dig (materials.json dig.hardness, 1 when absent): a dig needs at least this strength; 0 never digs (metal, bedrock).</summary>
        public int Hardness = 1;
        /// <summary>
        /// What it grows into once its Cell.Life runs out (green shoots into grass, a sapling into a
        /// tree), or null; and the line logged when it does.
        /// </summary>
        public string GrowsTo, GrowLog;
        /// <summary>
        /// Thin water on it soaks in and dries up a unit every this many ticks (World.Soak), on dry
        /// ground and on damp ground (wet from rain or water that soaked in); 0 never.
        /// </summary>
        public int SoakEvery, SoakEveryWet;
        /// <summary>
        /// How much of its catch (Flammability) it loses while damp (materials.json soak.dampens, 0 to
        /// 1, 0 when absent): a damp cell of it (raining, or Cell.DampUntil) or a wet thing of it
        /// catches with Flammability * (1 - Dampens).
        /// </summary>
        public double Dampens;
        /// <summary>Wet (water on it, raining, or damp) this many ticks in all, it rusts through into RustsTo (materials.json rust.every, rust.leaves; World.Rust); 0 never.</summary>
        public int RustEvery;
        public string RustsTo;
        /// <summary>What a cell of it leaves when it shatters (shatter.leaves: a window pane to an open gap, World.Shatter); null it doesn't.</summary>
        public string ShattersTo;
        /// <summary>It goes intact, cracked, shattered (shatter.cracks): the first hit only cracks it (Cell.Cracked, World.Shatter).</summary>
        public bool Cracks;
        /// <summary>Fire reaching it leaves it hot this many ticks (materials.json hot.for; World.Cool), and each unit of water it boils off takes hot.quench of them; 0 never hot.</summary>
        public int HotFor, HotQuench;
        /// <summary>
        /// What water soaking into it turns it into (dirt to mud: soak.leaves) and what it dries back to once
        /// it is dry again (mud to dirt: soak.dries, World.DryOut); null neither.
        /// </summary>
        public string SoaksTo, DriesTo;
        /// <summary>
        /// The ticks of burning, frozen and wet a creature of it takes (materials.json burn.status, freeze.status,
        /// soak.status: the entity rules' statusOf); 0 when its material has none (bone doesn't burn).
        /// </summary>
        public int BurnStatus, FreezeStatus, SoakStatus;
        /// <summary>A wet creature of it is shocked by charged water next to it, not only under it (conduct.chainsWet; World.Hazards).</summary>
        public bool ChainsWet;
        /// <summary>A verb's status ticks: burn, freeze or soak (0 for any other).</summary>
        public int StatusTicks(string verb) => verb switch
        {
            "burn" => BurnStatus,
            "freeze" => FreezeStatus,
            "soak" => SoakStatus,
            _ => 0,
        };
        /// <summary>The status a verb gives a creature: burn burning, freeze frozen, soak wet; null for any other.</summary>
        public static string StatusOfVerb(string verb) => verb switch
        {
            "burn" => "burning",
            "freeze" => "frozen",
            "soak" => "wet",
            _ => null,
        };
        /// <summary>How fast a creature walks across it (mud 0.5: a step takes 1/speed as long, World.Thorns); 1 unhindered.</summary>
        public double Speed = 1;
        /// <summary>The element that dissolves it (materials.json dissolve.by, acid) and the terrain it leaves; null neither.</summary>
        public string DissolvedBy, DissolvesTo;
        /// <summary>What a sand blast turns it into (materials.json scour.leaves: rusted metal back to metal, a log wall worn thin), or null; and whether raised ground of it wears down a step (scour.wears).</summary>
        public string ScoursTo;
        public bool ScourWears;
        /// <summary>The terrain heat turns it into (materials.json bake.into: sand to glass, mud to brick), or null.</summary>
        public string BakesTo;
        /// <summary>What water on it turns into, any depth: "ice" (arctic ground) or "steam" (lava); null stays water.</summary>
        public string WaterTo;

        // Tags don't change once the rules are loaded; a set makes the sim's many tag checks O(1).
        HashSet<string> tagSet;
        public bool Has(string tag) => (tagSet ??= new HashSet<string>(Tags)).Contains(tag);

        /// <summary>
        /// Its verbs (docs/MATERIALS.md): it burns, breaks, conducts. What code asks instead of the
        /// tags, read from them once (a burn group gives the flammable tag, MaterialGroups).
        /// </summary>
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
        /// <summary>How its fire looks (materials.json "fireLook"); the view's only.</summary>
        public string FireLook;
        /// <summary>Heat turns it into this cover (materials.json melt.into; null, none) and this much water (melt.water; 0 doesn't melt).</summary>
        public string MeltsTo;
        public int MeltWater;
        /// <summary>Ticks it lasts once laid (Cell.Life) before it withers away (thorns), or 0 for good; and the line logged then.</summary>
        public int Lasts;
        public string WitherLog;
        /// <summary>What it does to a creature walking through it: [dice, sides] damage a square (empty for none), and its speed (1 unhindered).</summary>
        public int[] Damage;
        public double Speed = 1;
        /// <summary>The element that dissolves it (materials.json dissolve.by, acid) and the cover it leaves (null, none).</summary>
        public string DissolvedBy, DissolvesTo;
        /// <summary>On open water it spreads to the open water next to it every this many ticks (materials.json float.every; World.Float); 0 sinks or stays.</summary>
        public int FloatEvery;
        /// <summary>How many cells a slick of it reaches from where it was laid (float.reach; Cell.Slick); 0 without end.</summary>
        public int FloatReach;
        /// <summary>When it withers (Lasts), its roots break the terrain under it into its BreaksTo (materials.json rot.breaks: vines crack a stone wall).</summary>
        public bool RotBreaks;
        public bool Has(string tag) => Tags.Contains(tag);
        /// <summary>It burns (a burn group: the flammable tag); what code asks instead of the tag.</summary>
        public bool Burns => Has("flammable");
    }

    public sealed class GasDef
    {
        public string Id, Name;
        public int Scald;
        /// <summary>Its own chance per tick a unit decays (fog lingers), or null for sim.gas.decayChance.</summary>
        public double? Decay;
        /// <summary>From how many units a square of it hides what's behind (World.LineOfSight), or 0 if it never does.</summary>
        public int BlocksSightAt;
        /// <summary>Every this many of its units that decay in a cell leave a unit of water there (steam condenses, World.Drift); 0 never.</summary>
        public int CondenseEvery;
        /// <summary>It condenses only this many cells or more from heat: none within it burning or hot (Cell.Heat), so steam doesn't rain straight back onto a fire (World.NearHeat).</summary>
        public int CondenseAway;
        /// <summary>A cell of it rains a unit of water every this many ticks (a rain cloud, World.RainFrom); 0 never.</summary>
        public int RainEvery;
        /// <summary>A gas that goes up all at once when fire meets it (burn.flash: dust, World.FlashGas), or null.</summary>
        public FlashDef Flash;
        /// <summary>An element a cell of it lets fall on its cell every ShedEvery ticks, at ShedChance (sheds: a storm cloud's lightning, a snow cloud's snow, a rain cloud's growth; World.ShedFrom), or null.</summary>
        public string Sheds;
        public int ShedEvery;
        public double ShedChance = 1;
    }

    /// <summary>A machine (machines.json): what powers it, how much pressure it builds and holds (steam), the mass that presses it (a plate), and how long a hit holds it (flow, charge, wind).</summary>
    public sealed class MachineDef
    {
        public string Id, Name, Needs;
        public int Build = 4, Max = 10, DecayEvery = 8, Mass = 3, Hold = 10;
    }

    /// <summary>A gas's burn.flash: the fire each of its cells goes up with, how hard that shoves what's there, and the line it logs.</summary>
    public sealed class FlashDef
    {
        public int Fire, Push;
        public string Log;
    }

    /// <summary>
    /// What a travelling whirlwind can take on (spells.json "carries"): when its eye crosses a cell matching If, it
    /// carries this from then on, shedding the element Sheds around its eye and dropping more where it dies out.
    /// </summary>
    public sealed class CarryDef
    {
        public string Id, Name, Sheds, Log;
        public CellCond If;
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
        /// <summary>A digging spell's strength (spells.json "strength", 1 when absent): it digs terrain no harder than this.</summary>
        public int Strength = 1;
        public int? Height;
        public string Terrain;
        public bool Taper;
        /// <summary>A burst whose element arrives at each cell heading in toward its middle (Whirlwind): air shoves gas and fans fire inward.</summary>
        public bool Inward;
        /// <summary>
        /// An inward burst that keeps whirling for Duration ticks (Whirlwind): every PullEvery ticks it draws gas
        /// and creatures of MaxMass or less a cell in toward its middle and runs fire round its rings
        /// (World.TickWhirlwinds); 0 lands once and is gone.
        /// </summary>
        public int PullEvery;
        /// <summary>Ticks between the cast and its landing, with a target mark on the cells (Falling Star; World.AimStar); 0 lands at once.</summary>
        public int Delay;
        /// <summary>Burning rock thrown round where it lands (spells.json scatter), or null.</summary>
        public ScatterDef Scatter;
        /// <summary>An element that lands on its middle cell last of all (Falling Star's lava), or null.</summary>
        public string Core;
        /// <summary>A whirling spell moves a cell every this many ticks (World.Travel), 0 never; it drops what it carries at DropPower.</summary>
        public int TravelEvery, DropPower = 1;
        /// <summary>A spell that acts on what's already there (Geyser on water): it lands only on the cells matching this, and with none fizzles (Fizzle logged); null lands everywhere.</summary>
        public CellCond Needs;
        /// <summary>The line logged when a spell with Needs finds no cell to act on, or null for "{Name} fizzles".</summary>
        public string Fizzle;
        /// <summary>A travelling wave's or rolling boulder's element where it stops (Wave's mud, the boulder's earth; World.Waves, World.Boulders), or null.</summary>
        public string Leaves;
        /// <summary>A line with TravelEvery that rolls rather than washes (Rolling Boulder; World.Boulders): one cell that follows the ground downhill.</summary>
        public bool Rolls;
        /// <summary>The gas a rolling boulder leaves behind it (dust), or null.</summary>
        public string Trail;
        /// <summary>A spell that seeks (Lightning): it strikes the nearest cell within SeekRange of where it's aimed that matches SeekIf (metal or water) instead of its shape, and its shape when there is none.</summary>
        public int SeekRange;
        public CellCond SeekIf;
        /// <summary>Earth shaping to a Height: cells holding open water rise only this high (Wall of Stone's stepping stones); 0 as high as the rest.</summary>
        public int Stepping;
        /// <summary>Earth shaping to a Height along a line: a step higher each cell from the first, up to the height (Stone Ramp).</summary>
        public bool Ramp;
        /// <summary>What a whirling spell can take on, strongest first: one replaces any after it (CarryDef); empty for none.</summary>
        public IReadOnlyList<CarryDef> Carries = Array.Empty<CarryDef>();
        /// <summary>The light the Light effect sheds, in feet (spells.json light { bright, dim }), for the view to draw; 0 none.</summary>
        public int LightBright, LightDim;
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
        /// <summary>How hard a ground the blast can dig (1 when absent), against a terrain's hardness.</summary>
        public int Strength = 1;
        /// <summary>What its middle cell becomes once dug (Molten Boulder's lava); null stays.</summary>
        public string Leaves;
    }

    /// <summary>A spell's scatter: Count tries at a cell within Reach of the middle; each lays Cover, burning, on open dry ground.</summary>
    public sealed class ScatterDef
    {
        public string Cover;
        public int Count, Reach;
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
        /// <summary>Flammable and Creature mirror those components (Components.cs), for the code that reads them.</summary>
        public bool Flammable, Creature, Player;
        /// <summary>What it's made of: a terrain id in materials.json (a crate is wood), or null.</summary>
        public string Material;
        /// <summary>That material's definition, or null (set once the rules are read).</summary>
        public TerrainDef MaterialDef;
        /// <summary>What it is made of or does (entities.json "components").</summary>
        public ComponentSet Components;
        /// <summary>Null unless it blows up when destroyed.</summary>
        public ExplosionDef Explodes;
        /// <summary>Null unless it raids the defense field (Defense.cs).</summary>
        public RaiderDef Raider;
    }

    public sealed class FireParams
    {
        public double SpreadChance = 0.5, HeatChance = 0.04, BoilChance = 0.35, SlickBoilChance = 0.35, DownwindBonus = 0.35, SmokeChance = 0.25;

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
        /// <summary>The hours of dawn and dusk: dim light from the first to the second, bright between them, dark between dusk and dawn (Sky.Daylight).</summary>
        public double DawnFrom = 5, DawnTo = 7, DuskFrom = 18, DuskTo = 20;
        /// <summary>How many times as fast snow freezes standing water at night.</summary>
        public double NightFreeze = 2;
        /// <summary>The light a burning cell sheds, in feet as items.json lights are: bright out to FireBright, dim for FireDim beyond.</summary>
        public double FireBright = 20, FireDim = 20;
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
        /// <summary>At most this many chunks out of every reach stay active while live (linger).</summary>
        public int LingerChunks = 16;
        /// <summary>A chunk lingers at most this many ticks before it deactivates, live or not.</summary>
        public int LingerTicks = 1800;
        /// <summary>A chunk let go while live runs at most this many of the ticks it missed when it next activates.</summary>
        public int CatchUpTicks = 720;
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
        /// <summary>
        /// Soaking (World.Soak): water this deep or less is thin (it and its four neighbours, so a
        /// lake's shore isn't), the pass runs every SoakPass ticks, and soaked-in water leaves the
        /// ground damp for DampTicks. SoakPass 0: no soaking.
        /// </summary>
        public int ThinWater = 2, SoakPass = 8, DampTicks = 600;
        public int ConductLimit = 80;
        // Not used by the cell port, carried so a view can read them.
        public int FallDamage = 4, ZapDamage = 3;
        /// <summary>A burning entity takes 1 damage on every tick divisible by this (sim.json status).</summary>
        public int BurnDamageEvery = 3;
        /// <summary>A creature in thick steam is scalded on every tick divisible by this (sim.json status).</summary>
        public int ScaldEvery = 6;
        /// <summary>
        /// A thing with status ShatterWhen (frozen) shatters when it takes damage of any element but
        /// ShatterNotBy (cold only chills it): frozen things shatter, docs/MATERIALS.md (sim.json
        /// status.shatter when, notBy); null never.
        /// </summary>
        public string ShatterWhen, ShatterNotBy;
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
        /// <summary>hot: whether the cell is hot (Cell.Heat above 0).</summary>
        public bool? Hot;
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
        /// <summary>heat: a terrain that gets hot (hot.for) is left hot for that long (World.Cool).</summary>
        public bool Heat;
        /// <summary>bake: the terrain becomes its bakesTo, if it has one.</summary>
        public bool Bake;
        /// <summary>meltCover: the cover becomes its meltsTo and gives its meltWater, if it melts.</summary>
        public bool MeltCover;
        /// <summary>shatter: a hit on the cell's ice or terrain, as a blast's (World.Shatter): it cracks, or if cracked shatters.</summary>
        public bool Shatter;
        /// <summary>dissolve: "cover" or "terrain", eaten away into its dissolvesTo when the rule's element is its dissolvedBy.</summary>
        public string Dissolve;
        public string SetTerrain, Raise;
        public int? Conduct;
        public bool Fan, PushGas, ClearGas;
        /// <summary>scour: a blast of sand strips a cover that scours (scourable), or turns the terrain to its scoursTo, or wears raised ground of it (scour.wears) down a step.</summary>
        public bool Scour;
        /// <summary>pushCover: a cover that floats (float) is pushed a cell along the element's way onto open water with no cover (a gust drives oil across a pond).</summary>
        public bool PushCover;
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
        static readonly string[] BaseElements = { "fire", "cold", "water", "air", "earth", "lightning", "grease", "oil", "thunder", "fog", "growth", "thorns", "acid", "steam", "lava", "mud", "dust", "rain", "sand", "heat", "smoke", "snow", "geyser", "wave" };

        readonly Dictionary<string, TerrainDef> terrain = new Dictionary<string, TerrainDef>();
        readonly Dictionary<string, CoverDef> covers = new Dictionary<string, CoverDef>();
        readonly Dictionary<string, GasDef> gases = new Dictionary<string, GasDef>();
        readonly List<SpellDef> spells = new List<SpellDef>();
        readonly Dictionary<string, EntityDef> entities = new Dictionary<string, EntityDef>();
        readonly List<CellRule> cellRules = new List<CellRule>();
        readonly Dictionary<string, List<CellRule>> byElement = new Dictionary<string, List<CellRule>>();
        readonly Dictionary<string, MachineDef> machines = new Dictionary<string, MachineDef>();
        static readonly IReadOnlyList<CellRule> NoRules = Array.Empty<CellRule>();

        public IReadOnlyDictionary<string, TerrainDef> Terrain => terrain;
        public IReadOnlyDictionary<string, CoverDef> Covers => covers;
        public IReadOnlyDictionary<string, GasDef> Gases => gases;
        /// <summary>machines.json by terrain id: what powers each machine (World.TickMachines).</summary>
        public IReadOnlyDictionary<string, MachineDef> Machines => machines;
        public IReadOnlyList<SpellDef> Spells => spells;
        public IReadOnlyDictionary<string, EntityDef> Entities => entities;
        /// <summary>reactions.json "cell", in file order.</summary>
        public IReadOnlyList<CellRule> CellRules => cellRules;
        public SimParams Sim { get; private set; } = new SimParams();

        /// <summary>
        /// Every table as JSON, in the shape of the sandbox's `Rules` object (elements, terrain,
        /// covers, gases, cellRules, entityRules, spells, critters, entities, items, machines, sim, groups, combos).
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

            var doc = Doc(Get);
            // A material spelled as verb groups reads as the flat keys (MaterialGroups).
            var grouping = MaterialGroups.Ungroup(doc);
            if (grouping.Count > 0) throw new ArgumentException("Rules: " + string.Join("; ", grouping));
            return FromDoc(doc);
        }

        // The rule document from the files (get: a file's JSON, or null for an optional one that's missing).
        static JObject Doc(Func<string, bool, JToken> get)
        {
            var materials = (JObject)get("materials.json", true);
            var reactions = (JObject)get("reactions.json", true);
            return new JObject
            {
                ["elements"] = new JArray(BaseElements),
                ["terrain"] = materials["terrain"] as JObject ?? new JObject(),
                ["covers"] = materials["covers"] as JObject ?? new JObject(),
                ["gases"] = materials["gases"] as JObject ?? new JObject(),
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
            };
        }

        /// <summary>The typed tables for a rule document (see <see cref="doc"/>), which the result keeps.</summary>
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
            foreach (var p in ((JObject)doc["gases"]).Properties())
                r.gases[p.Name] = new GasDef
                {
                    Id = p.Name,
                    Name = Str(p.Value["name"]) ?? p.Name,
                    Scald = Int(p.Value["scald"]) ?? 0,
                    Decay = Num(p.Value["decay"]),
                    BlocksSightAt = Int(p.Value["blocksSightAt"]) ?? 0,
                    CondenseEvery = Int(p.Value["condenseEvery"]) ?? 0,
                    CondenseAway = Int(p.Value["condenseAway"]) ?? 0,
                    RainEvery = Int(p.Value["rainEvery"]) ?? 0,
                    Flash = ParseFlash(p.Value["burn"]?["flash"]),
                    Sheds = Str(p.Value["sheds"]?["element"]),
                    ShedEvery = Int(p.Value["sheds"]?["every"]) ?? 0,
                    ShedChance = Num(p.Value["sheds"]?["chance"]) ?? 1,
                };
            ParseMachines(r, doc["machines"] as JObject ?? new JObject());

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
            ParseGroups(r, (JObject)doc["groups"]);
            ParseCombos(r, (JObject)doc["combos"]);
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
            Heat = Num(o["heat"]) ?? 1,
            FireLook = Str(o["fireLook"]),
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
            ScourWears = Truthy(o["scourWears"]),
            BakesTo = Str(o["bakesTo"]),
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
            WaterTo = Str(o["waterTo"]),
        };

        static CoverDef ParseCover(string id, JObject o) => new CoverDef
        {
            Id = id,
            Name = Str(o["name"]) ?? id,
            Tags = Strings(o["tags"]),
            Flammability = Num(o["flammability"]) ?? 0,
            Fuel = Int(o["fuel"]),
            BurnIntensity = Int(o["burnIntensity"]),
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
        };

        static int[] Dice(JToken t) => t is JArray d && d.Count >= 2 ? new[] { (int)d[0], (int)d[1] } : Array.Empty<int>();

        // Each thing's material, from its id (read once the terrain is).
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
                Components = components,
                Flammable = components.Has(Component(Rules.Flammable)),
                Creature = components.Has(Component(Rules.Creature)),
                Player = Truthy(o["player"]),
                Material = Str(o["material"]),
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
            Strength = Int(o["strength"]) ?? 1,
            Height = Int(o["height"]),
            Terrain = Str(o["terrain"]),
            Taper = Truthy(o["taper"]),
            Inward = Truthy(o["inward"]),
            PullEvery = Int(o["pullEvery"]) ?? 0,
            Delay = Int(o["delay"]) ?? 0,
            Scatter = o["scatter"] is JObject sc ? new ScatterDef { Cover = Str(sc["cover"]), Count = Int(sc["count"]) ?? 0, Reach = Int(sc["reach"]) ?? 0 } : null,
            Core = Str(o["core"]),
            TravelEvery = Int(o["travelEvery"]) ?? 0,
            DropPower = Int(o["dropPower"]) ?? 1,
            Needs = o["needs"] is JObject nd ? ParseCond(nd) : null,
            Fizzle = Str(o["fizzle"]),
            Leaves = Str(o["leaves"]),
            Rolls = Truthy(o["rolls"]),
            Trail = Str(o["trail"]),
            Stepping = Int(o["stepping"]) ?? 0,
            SeekRange = Int(o["seeks"]?["range"]) ?? 0,
            SeekIf = o["seeks"]?["if"] is JObject si ? ParseCond(si) : null,
            Ramp = Truthy(o["ramp"]),
            Carries = (o["carries"] as JArray)?.OfType<JObject>().Select(ParseCarry).ToList() ?? (IReadOnlyList<CarryDef>)Array.Empty<CarryDef>(),
            LightBright = Int((o["light"] as JObject)?["bright"]) ?? 0,
            LightDim = Int((o["light"] as JObject)?["dim"]) ?? 0,
            Crater = ParseCrater(o["crater"]),
        };

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
                Hot = Bool(c["hot"]),
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
            // setCover: null is meaningful (clear the cover), so presence is what counts.
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

        /// <summary>sim.json status: burning and scalding rates, and what shatters.</summary>
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
                // Bare water beside a burning slick boils at its own chance (sim.fire.slickBoilChance), else as other water does.
                s.Fire.SlickBoilChance = Num(f["slickBoilChance"]) ?? s.Fire.BoilChance;
                s.Fire.DownwindBonus = Num(f["downwindBonus"]) ?? s.Fire.DownwindBonus;
                s.Fire.SmokeChance = Num(f["smokeChance"]) ?? s.Fire.SmokeChance;
                s.Fire.FadeCells = Int(f["fadeCells"]) ?? s.Fire.FadeCells;
            }
            if (o["water"] is JObject w)
            {
                s.WaterMaxLevel = Int(w["maxLevel"]) ?? s.WaterMaxLevel;
                s.WaterUnitsPerStep = Int(w["unitsPerStep"]) ?? s.WaterUnitsPerStep;
                s.ThinWater = Int(w["thin"]) ?? s.ThinWater;
                s.SoakPass = Int(w["soakPass"]) ?? s.SoakPass;
                s.DampTicks = Int(w["dampTicks"]) ?? s.DampTicks;
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
            if (o["status"] is JObject st) ParseStatus(s, st);
            s.Defense = ParseDefense(o["defense"]);
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
                s.Streaming.SettleTicks = Int(so["settleTicks"]) ?? s.Streaming.SettleTicks;
                s.Streaming.LingerChunks = Int(so["lingerChunks"]) ?? s.Streaming.LingerChunks;
                s.Streaming.LingerTicks = Int(so["lingerTicks"]) ?? s.Streaming.LingerTicks;
                s.Streaming.CatchUpTicks = Int(so["catchUpTicks"]) ?? s.Streaming.CatchUpTicks;
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
