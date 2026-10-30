// What a physics prop or a carried item does to cells, as a value: "the
// barrel blew up at (4, 7)", "the lantern smashed and spilled there". The
// props themselves are Unity physics and not in the core; this is the small,
// deterministic result they hand over (D4 in docs/DECISIONS.md). Solo it is
// applied at once; online the host stamps it into a TickBatch next to that
// tick's casts, so every machine applies it at the same tick, and a late
// joiner replays it from the host's history like a cast. It names the thing
// by its stable id (WorldThings), never by a runtime object id.
namespace Chem
{
    public enum EffectKind
    {
        /// <summary>A burning prop sets its cell alight (like a burning entity in the sandbox).</summary>
        Burn,
        /// <summary>A prop burned away and left its fire behind.</summary>
        Embers,
        /// <summary>A prop blew up (entities.json "explodes").</summary>
        Explode,
        /// <summary>An item landed hard enough to break and spilled what it held (items.json "breaks").</summary>
        Spill,
        /// <summary>A heavy thing hit a window pane hard (World.Shatter): it cracks, or if cracked shatters.</summary>
        Shatter,
        /// <summary>A held tool swung at a cell (items.json "use": "dig").</summary>
        Dig,
        /// <summary>A thing of a material that cracks took its first hard hit (shatter.cracks): cracked by its Thing id, not a cell.</summary>
        Crack,
    }

    /// <summary>A prop's effect on one cell: small enough to send like a cast.</summary>
    public readonly struct EffectCommand
    {
        public EffectCommand(EffectKind kind, int x, int y, string entity) : this(kind, x, y, entity, ThingId.None) { }

        public EffectCommand(EffectKind kind, int x, int y, string entity, ThingId thing)
        {
            Kind = kind;
            X = x;
            Y = y;
            Entity = entity;
            Thing = thing;
        }

        public EffectKind Kind { get; }
        public int X { get; }
        public int Y { get; }
        /// <summary>The prop's id in rules/entities.json, or the item's in rules/items.json.</summary>
        public string Entity { get; }
        /// <summary>
        /// Which prop or item did it: its stable id from world/things.json (WorldThings), the same on
        /// every machine and for a late joiner, or ThingId.None. The kind's rules come from Entity.
        /// </summary>
        public ThingId Thing { get; }
    }

    public sealed partial class World
    {
        /// <summary>
        /// Applies a prop's effect to the cells. An effect off the map, or for an entity or item the
        /// rules don't know (a mod removed it), changes nothing, the same on every machine.
        /// </summary>
        public void ApplyEffect(EffectCommand effect)
        {
            FinishTick(); // effects land between whole ticks (StepSome)
            if (!InBounds(effect.X, effect.Y)) return;
            switch (effect.Kind)
            {
                case EffectKind.Burn:
                    Apply(effect.X, effect.Y, "fire", 1, -1, false);
                    break;
                case EffectKind.Embers:
                    LeaveEmbers(effect.X, effect.Y, Rules.Entity(effect.Entity));
                    break;
                case EffectKind.Explode:
                    Explode(effect.X, effect.Y, Rules.Entity(effect.Entity));
                    break;
                case EffectKind.Spill:
                    Spill(effect.X, effect.Y, Rules.Item(effect.Entity));
                    break;
                case EffectKind.Shatter:
                    Shatter(effect.X, effect.Y);
                    break;
                case EffectKind.Dig:
                    Dig(effect.X, effect.Y, Rules.Item(effect.Entity));
                    break;
                case EffectKind.Crack:
                    if (!effect.Thing.Equals(ThingId.None)) crackedThings.Add(effect.Thing.Value);
                    break;
            }
        }
    }
}
