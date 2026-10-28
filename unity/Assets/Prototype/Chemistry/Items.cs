// Little things to pick up, carry, drop and throw (rules/items.json): a ball
// with the Light cantrip on it, a bullseye lantern, a spyglass, sending
// stones, a Dancing Lights mote. The core only reads and checks them; Unity's
// Carryable and its item behaviours do the rest. The sandbox carries the
// table through mods (sandbox/src/rules.ts) but has no items on its board.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>What pressing "use" does with a held item (items.json "use").</summary>
    public static class ItemUses
    {
        /// <summary>Covers or uncovers its light.</summary>
        public const string Light = "light";
        /// <summary>Looks through it: things appear <see cref="ItemDef.Zoom"/> times closer.</summary>
        public const string Zoom = "zoom";
        /// <summary>Sends <see cref="ItemDef.Message"/> to whoever has the other stone of the pair.</summary>
        public const string Send = "send";
        /// <summary>Steers a mote of light to where the holder aims, up to <see cref="ItemDef.Reach"/> feet away.</summary>
        public const string Point = "point";

        internal static readonly HashSet<string> All = new HashSet<string> { Light, Zoom, Send, Point };
    }

    /// <summary>
    /// The light an item sheds, in the SRD's terms: bright light out to <see cref="Bright"/> feet and
    /// dim light for <see cref="Dim"/> feet beyond, all round or in a cone <see cref="Cone"/> degrees wide.
    /// </summary>
    public sealed class ItemLightDef
    {
        public const double FeetToMetres = 0.3048;

        public double Bright, Dim;
        /// <summary>"#rrggbb".</summary>
        public string Color;
        /// <summary>
        /// Full width of the cone in degrees; 0 means all round. A 5e cone is as wide as it is
        /// long, so an SRD cone is 2·atan(1/2), about 53°.
        /// </summary>
        public double Cone;

        public double BrightMetres => Bright * FeetToMetres;
        /// <summary>Where the light ends: the bright and the dim parts together.</summary>
        public double RangeMetres => (Bright + Dim) * FeetToMetres;
    }

    /// <summary>An item that breaks when it lands hard, and what it spills (a lantern's burning oil).</summary>
    public sealed class ItemBreakDef
    {
        /// <summary>Impact speed, in m/s, from which it breaks.</summary>
        public double Speed;
        /// <summary>Null when it breaks without spilling anything.</summary>
        public ExplosionDef Spill;
    }

    /// <summary>A thing to carry (items.json).</summary>
    public sealed class ItemDef
    {
        public string Id, Name, Use, Message, Srd;
        /// <summary>What it is made of or does (items.json "components"; Components.cs).</summary>
        public ComponentSet Components;
        /// <summary>Kilograms. The physics body's mass; a shove moves it like the lightest entity (Forces).</summary>
        public double Mass;
        /// <summary>Null when it sheds no light.</summary>
        public ItemLightDef Light;
        /// <summary>Use "zoom": how many times closer things look.</summary>
        public double Zoom;
        /// <summary>Use "point": how far away its light can go, in feet.</summary>
        public double Reach;
        /// <summary>Null when it doesn't break.</summary>
        public ItemBreakDef Breaks;

        /// <summary>True if landing at <paramref name="impactSpeed"/> m/s breaks it.</summary>
        public bool BreaksAt(double impactSpeed) => Breaks != null && impactSpeed >= Breaks.Speed;

        /// <summary>
        /// The entity a physics prop runs this item as: light (mass 1, so it floats), not
        /// flammable, and unbreakable by damage (it breaks only by landing hard, <see cref="BreaksAt"/>).
        /// </summary>
        public EntityDef AsEntity() => new EntityDef { Id = Id, Name = Name, Hp = PropState.Unbreakable, Mass = 1 };
    }

    public sealed partial class Rules
    {
        readonly Dictionary<string, ItemDef> items = new Dictionary<string, ItemDef>();

        public IReadOnlyDictionary<string, ItemDef> Items => items;

        /// <summary>The item with this id, or null.</summary>
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
                Components = ThingComponents(o),
                Use = Str(o["use"]),
                Message = Str(o["message"]),
                Srd = Str(o["srd"]) ?? "",
                Mass = Num(o["mass"]) ?? 1,
                Light = light,
                Zoom = Num(o["zoom"]) ?? 1,
                Reach = Num(o["reach"]) ?? 0,
                Breaks = breaks,
            };
        }
    }
}
