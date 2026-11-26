// Rolling boulders (spells.json rolls on a line with travelEvery: Rolling Boulder).
// Port of tickBoulders in sandbox/src/actions.ts.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        public sealed class Boulder : Mover
        {
            public string Spell;
            public int Left, Every, Age;
        }

        readonly List<Boulder> boulders = new List<Boulder>();
        public IReadOnlyList<Boulder> Boulders => boulders;

        public static bool IsBoulder(SpellDef s) => s != null && s.TravelEvery > 0 && s.Rolls && s.Shape == "line";

        void StartBoulder(SpellDef s, int x, int y, int dir, int charge, int aimX, int aimY)
        {
            if (!IsActiveAt(x, y) || IsBlocking(At(x, y))) return;
            var b = new Boulder { Spell = s.Id, Left = ChargedLength(s.Length, charge) - 1, Every = s.TravelEvery };
            b.SetOff(x, y, dir, aimX, aimY);
            Strike(b, s);
            if (b.Left > 0 && !InWater(At(x, y))) boulders.Add(b);
            else Rest(b, s);
        }

        void TickBoulders()
        {
            if (boulders.Count == 0) return;
            foreach (var b in boulders)
            {
                if (++b.Age % b.Every != 0) continue;
                var s = Rules.Spell(b.Spell);
                if (s == null || !RollOn(b, s)) b.Left = 0;
                if (b.Left <= 0) Rest(b, s);
            }
            boulders.RemoveAll(b => b.Left <= 0);
        }

        bool RollOn(Boulder b, SpellDef s)
        {
            int to = RollTo(b);
            if (to == NoRoll) return false;
            if (to == RollAhead) b.Advance();
            else b.Turn(to);
            b.X = FoldX(b.X);
            b.Y = FoldY(b.Y);
            b.Left--;
            Strike(b, s);
            return !InWater(At(b.X, b.Y));
        }

        static bool InWater(Cell c) => c.Water > 0 && !c.Frozen;

        const int RollAhead = 4, NoRoll = -1;

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

        void Strike(Boulder b, SpellDef s)
        {
            Shatter(b.X, b.Y);
            if (s.Trail != null) AddGas(At(b.X, b.Y), s.Trail, 2);
            var r = RaiderAt(b.X, b.Y);
            if (r != null && r.Alive && s.Damage.Length == 2) DamageRaider(r, Roll(s.Damage), s.Name.ToLowerInvariant(), s.Element);
            Force(s, b.X, b.Y, b.Dir);
        }

        void Rest(Boulder b, SpellDef s)
        {
            b.Left = 0;
            if (s == null) return;
            LogOnce($"rest#{s.Id}", $"The {s.Name.ToLowerInvariant()} comes to rest", LogKind.Info);
            if (s.Leaves != null) Apply(b.X, b.Y, s.Leaves, 1, -1, true);
        }

        public string BoulderState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in boulders) sb.Append("|o").Append(Whole(b.X)).Append(':').Append(Whole(b.Y)).Append(':').Append(Whole(b.Left)).Append(':').Append(Whole(b.Age));
            return sb.ToString();
        }
    }
}
