// The defense field's data (docs/BACKLOG.md 17): raiders (the sandbox's
// entities with a "raider" row in rules/entities.json) walk from a spawn gate
// to a flag in waves, and players stop them with spells. This file holds the
// rule rows (raider stats, sim.json's "defense", reactions.json's "entity"
// rules), the command that starts the waves and the state the world keeps;
// World.Raiders.cs and World.Defense.cs are the systems, mirroring
// sandbox/src/entities.ts and sandbox/src/defense.ts. Everything here is
// whole numbers and ordered lists, so every machine in lockstep agrees.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>What makes an entity a raider (entities.json "raider"): its pace and what hurts it more or less.</summary>
    public sealed class RaiderDef
    {
        /// <summary>Ticks a square takes to walk.</summary>
        public int TicksPerSquare = 4;
        /// <summary>Elements it takes double damage from, and half (rounded up).</summary>
        public IReadOnlyList<string> Weak = Array.Empty<string>(), Resist = Array.Empty<string>();
    }

    /// <summary>sim.json's "defense": the waves, the lives and how raiders find their way.</summary>
    public sealed class DefenseParams
    {
        public int Lives = 10, Waves = 5, First = 3, Growth = 2, HpPerWave = 2;
        /// <summary>Ticks before the first raider, between raiders of a wave, and between waves.</summary>
        public int Warmup = 24, SpawnEvery = 9, BetweenWaves = 60;
        /// <summary>Squares round the gate and the flag the raiders may walk in.</summary>
        public int Margin = 10;
        /// <summary>
        /// Path costs: a raised wall (battered down a step every BatterTicks), and rough going (deep
        /// water, a climb or drop of more than a step), which also takes RoughTicks longer to cross.
        /// </summary>
        public int WallCost = 30, RoughCost = 4, RoughTicks = 6, BatterTicks = 18;
        /// <summary>Water units from which a square is deep (rough going) unless frozen.</summary>
        public int DeepWater = 8;
        /// <summary>The raider kinds (entities.json ids), in the order they come, round and round.</summary>
        public IReadOnlyList<string> Kinds = Array.Empty<string>();
    }

    /// <summary>The "if" of an entity rule (reactions.json "entity"): a status it has, components it has and lacks.</summary>
    public sealed class EntityCond
    {
        public string Status;
        /// <summary>Components it must have, and must lack (Components.cs).</summary>
        public ComponentSet Has, Lacks;
        /// <summary>Its material gives a status by this verb (burn, freeze, soak: TerrainDef.StatusTicks); null any.</summary>
        public string Verb;
    }

    /// <summary>One effect of an entity rule: statuses set (at least this many ticks), one cleared, a damage multiplier.</summary>
    public sealed class EntityEffect
    {
        public IReadOnlyList<KeyValuePair<string, int>> Status = Array.Empty<KeyValuePair<string, int>>();
        public string Clear;
        public int DamageMul;
        /// <summary>The status a verb gives (burn burning, freeze frozen, soak wet), for its material's ticks of it.</summary>
        public string StatusOf;
    }

    /// <summary>An element arriving at an entity (reactions.json "entity", applyEntityElement in the TS).</summary>
    public sealed class EntityRule
    {
        public string Id, Element, Log;
        public EntityCond If;
        public IReadOnlyList<EntityEffect> Do;
        public bool Notable, Stop;
    }

    public sealed partial class Rules
    {
        readonly Dictionary<string, List<EntityRule>> entityByElement = new Dictionary<string, List<EntityRule>>();
        static readonly IReadOnlyList<EntityRule> NoEntityRules = Array.Empty<EntityRule>();

        /// <summary>Entity rules for one element, top to bottom.</summary>
        public IReadOnlyList<EntityRule> EntityRulesFor(string element) =>
            element != null && entityByElement.TryGetValue(element, out var list) ? list : NoEntityRules;

        static void ParseEntityRules(Rules r, JToken rules)
        {
            foreach (var o in rules.OfType<JObject>())
            {
                var rule = new EntityRule
                {
                    Id = Str(o["id"]),
                    Element = Str(o["element"]),
                    Log = Str(o["log"]),
                    Notable = Truthy(o["notable"]),
                    Stop = Truthy(o["stop"]),
                    If = o["if"] is JObject c ? r.ParseEntityCond(c) : null,
                    Do = o["do"] is JArray d ? d.OfType<JObject>().Select(ParseEntityEffect).ToList() : new List<EntityEffect>(),
                };
                if (rule.Element == null) continue;
                if (!r.entityByElement.TryGetValue(rule.Element, out var list)) r.entityByElement[rule.Element] = list = new List<EntityRule>();
                list.Add(rule);
            }
        }

        static EntityEffect ParseEntityEffect(JObject o) => new EntityEffect
        {
            Status = o["status"] is JObject s
                ? s.Properties().Select(p => new KeyValuePair<string, int>(p.Name, Int(p.Value) ?? 0)).ToList()
                : new List<KeyValuePair<string, int>>(),
            Clear = Str(o["clear"]),
            DamageMul = Int(o["damageMul"]) ?? 0,
            StatusOf = Str(o["statusOf"]),
        };

        static RaiderDef ParseRaider(JToken t) =>
            t is JObject o
                ? new RaiderDef { TicksPerSquare = Math.Max(1, Int(o["ticksPerSquare"]) ?? 4), Weak = Strings(o["weak"]), Resist = Strings(o["resist"]) }
                : null;

        static DefenseParams ParseDefense(JToken t)
        {
            var d = new DefenseParams();
            if (!(t is JObject o)) return d;
            d.Lives = Int(o["lives"]) ?? d.Lives;
            d.Waves = Int(o["waves"]) ?? d.Waves;
            d.First = Int(o["first"]) ?? d.First;
            d.Growth = Int(o["growth"]) ?? d.Growth;
            d.HpPerWave = Int(o["hpPerWave"]) ?? d.HpPerWave;
            d.Warmup = Int(o["warmup"]) ?? d.Warmup;
            d.SpawnEvery = Int(o["spawnEvery"]) ?? d.SpawnEvery;
            d.BetweenWaves = Int(o["betweenWaves"]) ?? d.BetweenWaves;
            d.Margin = Int(o["margin"]) ?? d.Margin;
            d.WallCost = Int(o["wallCost"]) ?? d.WallCost;
            d.RoughCost = Int(o["roughCost"]) ?? d.RoughCost;
            d.RoughTicks = Int(o["roughTicks"]) ?? d.RoughTicks;
            d.BatterTicks = Int(o["batterTicks"]) ?? d.BatterTicks;
            d.DeepWater = Int(o["deepWater"]) ?? d.DeepWater;
            d.Kinds = Strings(o["kinds"]);
            return d;
        }
    }

    /// <summary>What a defense command does. Only one kind so far: start (or restart) the waves.</summary>
    public enum DefenseKind
    {
        Start,
    }

    /// <summary>
    /// A command to the defense field, like a cast: pressing the start pedestal (or the probe) sends
    /// it; solo it runs at once, online the host stamps it into a TickBatch, and a late joiner
    /// replays it from the host's history. It carries the gate's and the flag's squares, so the core
    /// needs no map of the field.
    /// </summary>
    public readonly struct DefenseCommand
    {
        public DefenseCommand(DefenseKind kind, int gateX, int gateY, int flagX, int flagY)
        {
            Kind = kind;
            GateX = gateX;
            GateY = gateY;
            FlagX = flagX;
            FlagY = flagY;
        }

        public DefenseKind Kind { get; }
        public int GateX { get; }
        public int GateY { get; }
        public int FlagX { get; }
        public int FlagY { get; }
    }

    public enum DefensePhase
    {
        Idle,
        Running,
        Won,
        Lost,
    }

    /// <summary>The waves under way (World.Defense): where they come from and go, and how it stands.</summary>
    public sealed class DefenseState
    {
        public DefensePhase Phase;
        /// <summary>The wave under way (1 for the first), lives left, raiders of this wave still to come, the tick the next comes on, and how many have come in all.</summary>
        public int Wave, Lives, ToSpawn, NextSpawn, Spawned;
        public int GateX, GateY, FlagX, FlagY;
        /// <summary>The squares the raiders may walk in: [X0, X1] × [Y0, Y1].</summary>
        public int X0, Y0, X1, Y1;
    }

    /// <summary>
    /// A raider on the board: the core's port of the sandbox's Entity (entities.ts), for the defense
    /// field. Hp, statuses (ticks left, by name) and Wait (ticks until it may take its next step).
    /// </summary>
    public sealed class Raider
    {
        public int Id;
        public string Kind;
        public int X, Y, Hp, MaxHp, Wait;
        public Dictionary<string, int> Status { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool Alive = true;

        /// <summary>Ticks left of a status, 0 without it.</summary>
        public int StatusOf(string name) => Status.TryGetValue(name, out int v) ? v : 0;
    }
}
