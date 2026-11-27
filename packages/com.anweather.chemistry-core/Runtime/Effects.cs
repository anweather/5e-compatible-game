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
        Burn,
        Embers,
        Explode,
        Spill,
        Shatter,
        Dig,
        Crack,
        Swing,
    }

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
        public string Entity { get; }
        public ThingId Thing { get; }
    }

    public sealed partial class World
    {
        public void ApplyEffect(EffectCommand effect)
        {
            FinishTick();
            if (!InBounds(effect.X, effect.Y)) return;
            int x = FoldX(effect.X), y = FoldY(effect.Y);
            switch (effect.Kind)
            {
                case EffectKind.Burn:
                    Apply(x, y, "fire", 1, -1, false);
                    break;
                case EffectKind.Embers:
                    LeaveEmbers(x, y, Rules.Entity(effect.Entity));
                    break;
                case EffectKind.Explode:
                    Explode(x, y, Rules.Entity(effect.Entity));
                    break;
                case EffectKind.Spill:
                    Spill(x, y, Rules.Item(effect.Entity));
                    break;
                case EffectKind.Shatter:
                    Shatter(x, y);
                    break;
                case EffectKind.Dig:
                    Dig(x, y, Rules.Item(effect.Entity));
                    break;
                case EffectKind.Crack:
                    if (!effect.Thing.Equals(ThingId.None)) crackedThings.Add(effect.Thing.Value);
                    break;
                case EffectKind.Swing:
                    Swing(x, y);
                    break;
            }
        }
    }
}
