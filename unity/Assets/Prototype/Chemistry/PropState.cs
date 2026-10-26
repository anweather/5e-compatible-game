// The rules side of a physics prop (a crate, a barrel, a boulder): its hit
// points, burning and levitation, counted in sim ticks, and whether it floats.
// Unity's physics moves the prop (PhysicsProp); this decides what the cell
// under it does to it, without Unity, so it can be tested and, later, run on
// every machine in lockstep.
//
// Not a port: the sandbox moves entities square by square and has no physics.
// The numbers follow its rules (entities.json, sim.json status, and
// reactions.json's e-fire-flammable and e-water).
using System;

namespace Chem
{
    /// <summary>What happened to a prop during one tick.</summary>
    [Flags]
    public enum PropEvents
    {
        None = 0,
        Ignited = 1,
        Doused = 2,
        Burned = 4,
        Destroyed = 8,
        Landed = 16,
    }

    public sealed class PropState
    {
        /// <summary>Ticks a flammable thing burns after its last touch of fire (e-fire-flammable: burning 24).</summary>
        public const int BurnTicks = 24;
        /// <summary>Anything heavier than this sinks (crates and barrels float, boulders don't).</summary>
        public const int FloatMaxMass = 2;
        /// <summary>Hit points at or above this mean it can't be hurt (the TS's "boulders shrug").</summary>
        public const int Unbreakable = 999;

        const double FloatLift = 2.5, SinkLift = 0.4;

        public PropState(EntityDef def)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Hp = def.Hp;
        }

        public EntityDef Def { get; private set; }
        public int Hp { get; private set; }
        public bool Destroyed => Hp <= 0;
        public int BurningTicks { get; private set; }
        public bool Burning => BurningTicks > 0;

        /// <summary>The first tick it no longer levitates on, or -1 when it isn't levitating.</summary>
        public int LevitateUntil { get; private set; } = -1;
        public bool Levitating(int tick) => tick < LevitateUntil;

        /// <summary>Upward push when fully under water, as a multiple of its weight: above 1 floats.</summary>
        public double Lift => Def.Mass <= FloatMaxMass ? FloatLift : SinkLift;
        public bool Floats => Lift > 1;

        /// <summary>How much of something <paramref name="height"/> tall, bottom at <paramref name="bottom"/>, is below <paramref name="surface"/> (0..1).</summary>
        public static double Submerged(double bottom, double height, double surface) =>
            height <= 0 ? 0 : Math.Max(0, Math.Min(1, (surface - bottom) / height));

        /// <summary>Floats for <paramref name="duration"/> ticks from <paramref name="tick"/>.</summary>
        public void Levitate(int tick, int duration) => LevitateUntil = tick + Math.Max(0, duration);

        /// <summary>
        /// Switches to new numbers for the same thing (a live mod changed its row in entities.json).
        /// The damage already taken carries over, but a rule change never breaks it by itself: it
        /// keeps at least 1 hit point. Burning and levitation carry over unchanged.
        /// </summary>
        public void UseDef(EntityDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (Destroyed) return;
            int taken = Def.Hp >= Unbreakable ? 0 : Def.Hp - Hp;
            Def = def;
            Hp = Math.Max(1, def.Hp - taken);
        }

        /// <summary>Takes damage; returns true if this destroyed it.</summary>
        public bool Damage(int amount)
        {
            if (amount <= 0 || Destroyed || Def.Hp >= Unbreakable) return false;
            Hp -= amount;
            return Destroyed;
        }

        /// <summary>
        /// One sim tick spent over cell <paramref name="c"/>: fire lights a flammable prop,
        /// water puts it out, and a burning prop takes 1 damage every <paramref name="burnEvery"/> ticks.
        /// A levitating prop is above the cell and untouched by it.
        /// </summary>
        public PropEvents Step(Cell c, int tick, int burnEvery)
        {
            if (Destroyed) return PropEvents.None;
            var events = PropEvents.None;
            bool floating = Levitating(tick);
            if (!floating && LevitateUntil >= 0)
            {
                LevitateUntil = -1;
                events |= PropEvents.Landed;
            }
            if (c != null && !floating) events |= Touch(c);
            if (!Burning) return events;
            BurningTicks--;
            if (tick % Math.Max(1, burnEvery) != 0) return events;
            events |= PropEvents.Burned;
            if (Damage(1)) events |= PropEvents.Destroyed;
            return events;
        }

        PropEvents Touch(Cell c)
        {
            var events = PropEvents.None;
            if (c.Fire > 0 && Def.Flammable)
            {
                if (!Burning) events |= PropEvents.Ignited;
                BurningTicks = BurnTicks;
            }
            if (c.Water > 0 && !c.Frozen && Burning)
            {
                BurningTicks = 0;
                events |= PropEvents.Doused;
            }
            return events;
        }
    }
}
