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
        public int BurnTime => Def.MaterialDef?.Burns == true ? Def.MaterialDef.Fuel ?? BurnTicks : BurnTicks;

        int DamageEvery(int burnEvery) =>
            Def.MaterialDef?.Burns == true ? Math.Max(burnEvery, BurnTime / Math.Max(1, Def.Hp)) : burnEvery;

        bool Catches(int tick)
        {
            var m = Def.MaterialDef;
            if (Burning || m == null || !m.Burns || m.Flammability <= 0) return true;
            if (heatedSince < 0) heatedSince = tick;
            return (tick - heatedSince + 1) * m.Flammability >= 1;
        }

        public const int BurnTicks = 24;
        public const int FloatMaxMass = 2;
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
        int heatedSince = -1;
        public bool Burning => BurningTicks > 0;

        public int LevitateUntil { get; private set; } = -1;
        public bool Levitating(int tick) => tick < LevitateUntil;

        public double Lift => Def.Mass <= FloatMaxMass ? FloatLift : SinkLift;
        public bool Floats => Lift > 1;

        public static double Submerged(double bottom, double height, double surface) =>
            height <= 0 ? 0 : Math.Max(0, Math.Min(1, (surface - bottom) / height));

        public void Levitate(int tick, int duration) => LevitateUntil = tick + Math.Max(0, duration);

        public void UseDef(EntityDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (Destroyed) return;
            int taken = Def.Hp >= Unbreakable ? 0 : Def.Hp - Hp;
            Def = def;
            Hp = Math.Max(1, def.Hp - taken);
        }

        public bool Damage(int amount)
        {
            if (amount <= 0 || Destroyed || Def.Hp >= Unbreakable) return false;
            Hp -= amount;
            return Destroyed;
        }

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
            if (c != null && !floating) events |= Touch(c, tick);
            if (!Burning) return events;
            BurningTicks--;
            if (tick % Math.Max(1, DamageEvery(burnEvery)) != 0) return events;
            events |= PropEvents.Burned;
            if (Damage(1)) events |= PropEvents.Destroyed;
            return events;
        }

        PropEvents Touch(Cell c, int tick)
        {
            var events = PropEvents.None;
            if (c.Fire > 0 && Def.Flammable && Catches(tick))
            {
                if (!Burning) events |= PropEvents.Ignited;
                BurningTicks = BurnTime;
            }
            else if (c.Fire == 0) heatedSince = -1;
            if (World.IsOpenWater(c) && Burning)
            {
                BurningTicks = 0;
                events |= PropEvents.Doused;
            }
            return events;
        }
    }
}
