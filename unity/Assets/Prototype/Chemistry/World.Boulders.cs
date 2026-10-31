// Rolling boulders (spells.json rolls on a line with travelEvery: Rolling Boulder).
// Port of tickBoulders in sandbox/src/actions.ts.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>
        /// A boulder still rolling (Rolling Boulder): the cell it's on and the line it rolls along (Mover), the cells
        /// it may still roll, how often it rolls one and its age in ticks.
        /// </summary>
        public sealed class Boulder : Mover
        {
            public string Spell;
            public int Left, Every, Age;
        }

        readonly List<Boulder> boulders = new List<Boulder>();
        /// <summary>The boulders still rolling, in the order cast (the view draws each one).</summary>
        public IReadOnlyList<Boulder> Boulders => boulders;

        /// <summary>Whether a spell rolls out as a boulder (a line with travelEvery that rolls) rather than landing at once.</summary>
        public static bool IsBoulder(SpellDef s) => s != null && s.TravelEvery > 0 && s.Rolls && s.Shape == "line";

        /// <summary>
        /// A boulder's cast (startBoulder in the TS): it starts on the cast's cell and strikes it, then rolls a cell
        /// every TravelEvery ticks along the aim (Mover), to the line's length in all (TickBoulders).
        /// </summary>
        void StartBoulder(SpellDef s, int x, int y, int dir, int charge, int aimX, int aimY)
        {
            if (!IsActiveAt(x, y) || IsBlocking(At(x, y))) return;
            var b = new Boulder { Spell = s.Id, Left = ChargedLength(s.Length, charge) - 1, Every = s.TravelEvery };
            b.SetOff(x, y, dir, aimX, aimY);
            Strike(b, s);
            if (b.Left > 0 && !InWater(At(x, y))) boulders.Add(b);
            else Rest(b, s);
        }

        /// <summary>
        /// Each boulder, in the order cast, rolls a cell when its time comes, the way the ground falls (RollTo): on
        /// along its aim, or turned onto a grid way down a slope, and strikes it; once it can't roll on, reaches water
        /// or its length's end, it comes to rest (Rest) and is gone.
        /// </summary>
        void TickBoulders()
        {
            if (boulders.Count == 0) return;
            foreach (var b in boulders)
            {
                if (++b.Age % b.Every != 0) continue;
                var s = Rules.Spell(b.Spell);
                int to = s != null ? RollTo(b) : NoRoll;
                if (to != NoRoll)
                {
                    if (to == RollAhead) b.Advance();
                    else b.Turn(to);
                    b.Left--;
                    Strike(b, s);
                    if (InWater(At(b.X, b.Y))) b.Left = 0;
                }
                else b.Left = 0;
                if (b.Left <= 0) Rest(b, s);
            }
            boulders.RemoveAll(b => b.Left <= 0);
        }

        static bool InWater(Cell c) => c.Water > 0 && !c.Frozen;

        // RollTo's answers besides a grid way to turn onto.
        const int RollAhead = 4, NoRoll = -1;

        /// <summary>
        /// Where a boulder rolls next: of the open cells ahead along its aim, to its left and to its right (in that
        /// order), the lowest that isn't higher than where it is, so it rolls on over flat ground and turns down a
        /// slope. RollAhead for on along its aim, the grid way of a turn, or NoRoll when every way is uphill or shut.
        /// </summary>
        int RollTo(Boulder b)
        {
            int best = NoRoll, bestLevel = ElevationUnits(At(b.X, b.Y));
            var ahead = b.Ahead();
            foreach (int to in new[] { RollAhead, (b.Dir + 3) % 4, (b.Dir + 1) % 4 })
            {
                var (nx, ny) = to == RollAhead ? ahead : (b.X + DirX(to), b.Y + DirY(to));
                if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny))) continue;
                int level = ElevationUnits(At(nx, ny));
                if (level < bestLevel || (level == bestLevel && best == NoRoll))
                {
                    best = to;
                    bestLevel = level;
                }
            }
            return best;
        }

        // The boulder strikes the cell it rolls onto: what is brittle there cracks or shatters (World.Shatter), it
        // leaves its trail of gas, and the creature there takes its damage and its force (thrown off its feet).
        void Strike(Boulder b, SpellDef s)
        {
            Shatter(b.X, b.Y);
            if (s.Trail != null) AddGas(At(b.X, b.Y), s.Trail, 2);
            var r = RaiderAt(b.X, b.Y);
            if (r != null && r.Alive && s.Damage.Length == 2) DamageRaider(r, Roll(s.Damage), s.Name.ToLowerInvariant(), s.Element);
            Force(s, b.X, b.Y, b.Dir);
        }

        // Where it stops it comes to rest, and its Leaves (earth) lands there: in a stream it fills the bed (a dam), on
        // dry ground it stands as a boulder.
        void Rest(Boulder b, SpellDef s)
        {
            b.Left = 0;
            if (s == null) return;
            LogOnce($"rest#{s.Id}", $"The {s.Name.ToLowerInvariant()} comes to rest", LogKind.Info);
            if (s.Leaves != null) Apply(b.X, b.Y, s.Leaves, 1, -1, true);
        }

        /// <summary>The boulders as text, "" when there are none: where each is, the cells it has left and its age (boulderState in the TS).</summary>
        public string BoulderState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in boulders) sb.Append("|o").Append(Whole(b.X)).Append(':').Append(Whole(b.Y)).Append(':').Append(Whole(b.Left)).Append(':').Append(Whole(b.Age));
            return sb.ToString();
        }
    }
}
