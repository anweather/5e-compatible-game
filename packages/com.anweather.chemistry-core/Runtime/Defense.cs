// The defense field's data (docs/BACKLOG.md 17): raiders (the sandbox's
// entities with a "raider" row in rules/entities.json) walk from a spawn gate
// to a flag in waves, and players stop them with spells. This file holds the
// rule rows (raider stats, sim.json's "defense", reactions.json's "entity"
// rules), the command that starts the waves and the state the world keeps;
// Play/RaiderSystem.cs and Play/DefenseSystem.cs are the systems, mirroring
// sandbox/src/entities.ts and sandbox/src/defense.ts. Everything here is
// whole numbers and ordered lists, so every machine in lockstep agrees.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class RaiderDef
    {
        public int TicksPerSquare = 4;
        public IReadOnlyList<string> Weak = Array.Empty<string>(), Resist = Array.Empty<string>();
        public int[] AttackDice = Array.Empty<int>();
        public int AttackEvery = 12;
        public string Route;
        public string Body;
        public int Size = 100;
    }

    public sealed class DefenseParams
    {
        public int Lives = 10, Waves = 5, First = 3, Growth = 2, HpPerWave = 2;
        public int Warmup = 24, SpawnEvery = 9, BetweenWaves = 60;
        public int Margin = 10;
        public int WallCost = 30, RoughCost = 4, RoughTicks = 6, BatterTicks = 18;
        public int DeepWater = 8;
        public IReadOnlyList<string> Kinds = Array.Empty<string>();
    }

    public sealed class EntityCond
    {
        public string Status;
        public ComponentSet Has, Lacks;
        public string Verb;
    }

    public sealed class EntityEffect
    {
        public IReadOnlyList<KeyValuePair<string, int>> Status = Array.Empty<KeyValuePair<string, int>>();
        public string Clear;
        public int DamageMul;
        public string StatusOf;
    }

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
                ? new RaiderDef
                {
                    TicksPerSquare = Math.Max(1, Int(o["ticksPerSquare"]) ?? 4),
                    Weak = Strings(o["weak"]),
                    Resist = Strings(o["resist"]),
                    AttackDice = (o["attack"]?["dice"] as JArray)?.Select(v => (int)v).ToArray() ?? Array.Empty<int>(),
                    AttackEvery = Math.Max(1, Int(o["attack"]?["every"]) ?? 12),
                    Route = Str(o["route"]),
                    Body = Str(o["body"]),
                    Size = Math.Max(1, Int(o["size"]) ?? 100),
                }
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

    public enum DefenseKind
    {
        Start,
    }

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

    public sealed class DefenseState
    {
        public DefensePhase Phase;
        public int Wave, Lives, ToSpawn, NextSpawn, Spawned;
        public int GateX, GateY, FlagX, FlagY;
        public int X0, Y0, X1, Y1;
    }

    public sealed class Raider
    {
        public int Id;
        public string Kind;
        public int X, Y, Hp, MaxHp, Wait;
        public Dictionary<string, int> Status { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool Alive = true;
        public bool Shattered;

        public int StatusOf(string name) => Status.TryGetValue(name, out int v) ? v : 0;
    }
}
