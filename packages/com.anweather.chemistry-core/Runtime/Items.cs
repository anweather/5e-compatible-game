// Little things to pick up, carry, drop and throw (rules/items.json): a ball
// with the Light cantrip on it, a bullseye lantern, a spyglass, sending
// stones, a Dancing Lights mote, a pick. The core only reads and checks them; Unity's
// Carryable and its item behaviours do the rest. The sandbox carries the
// table through mods (sandbox/src/rules.ts) but has no items on its board.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public static class ItemUses
    {
        public const string Light = "light";
        public const string Zoom = "zoom";
        public const string Send = "send";
        public const string Point = "point";
        public const string Dig = "dig";

        internal static readonly HashSet<string> All = new HashSet<string> { Light, Zoom, Send, Point, Dig };
    }

    public sealed class ItemLightDef
    {
        public const double FeetToMetres = 0.3048;

        public double Bright, Dim;
        public string Color;
        public double Cone;

        public double BrightMetres => Bright * FeetToMetres;
        public double RangeMetres => (Bright + Dim) * FeetToMetres;
    }

    public sealed class ItemBreakDef
    {
        public double Speed;
        public ExplosionDef Spill;
    }

    public sealed class ItemDef
    {
        public string Id, Name, Use, Message, Srd;
        public string Material;
        public ComponentSet Components;
        public double Mass;
        public ItemLightDef Light;
        public double Zoom;
        public double Reach;
        public int Strength;
        public ItemBreakDef Breaks;
        public LookDef Look;

        public bool BreaksAt(double impactSpeed) => Breaks != null && impactSpeed >= Breaks.Speed;

        public EntityDef AsEntity() => new EntityDef { Id = Id, Name = Name, Hp = PropState.Unbreakable, Mass = 1 };
    }

    public sealed partial class Rules
    {
        readonly Dictionary<string, ItemDef> items = new Dictionary<string, ItemDef>();

        public IReadOnlyDictionary<string, ItemDef> Items => items;

        public ItemDef Item(string id) => id != null && items.TryGetValue(id, out var i) ? i : null;

        ItemDef ParseItem(string id, JObject o)
        {
            ItemLightDef light = null;
            if (o["light"] is JObject l)
                light = new ItemLightDef
                {
                    Bright = Num(l["bright"]) ?? 0,
                    Dim = Num(l["dim"]) ?? 0,
                    Color = Str(l["color"]) ?? "#ffffff",
                    Cone = Num(l["cone"]) ?? 0,
                };
            ItemBreakDef breaks = null;
            if (o["breaks"] is JObject b)
                breaks = new ItemBreakDef
                {
                    Speed = Num(b["speed"]) ?? 0,
                    Spill = b["spill"] is JObject s
                        ? new ExplosionDef { Cover = Str(s["cover"]), Radius = Int(s["radius"]) ?? 0, Fire = Int(s["fire"]) ?? 0, Damage = Array.Empty<int>() }
                        : null,
                };
            return new ItemDef
            {
                Id = id,
                Name = Str(o["name"]) ?? id,
                Material = Str(o["material"]),
                Components = ThingComponents(o),
                Use = Str(o["use"]),
                Message = Str(o["message"]),
                Srd = Str(o["srd"]) ?? "",
                Mass = Num(o["mass"]) ?? 1,
                Light = light,
                Zoom = Num(o["zoom"]) ?? 1,
                Reach = Num(o["reach"]) ?? 0,
                Strength = Int(o["strength"]) ?? 0,
                Breaks = breaks,
                Look = ParseLook(o["look"]),
            };
        }
    }
}
