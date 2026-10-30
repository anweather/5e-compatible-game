// Travelling waves (spells.json travelEvery on a line that doesn't whirl: Wave).
// Port of tickWaves in sandbox/src/actions.ts.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>
        /// A wave still rolling (Wave): its front's middle, the way it rolls, how far the front reaches to each
        /// side, the cells it may still roll, how often it rolls one, its age in ticks, the power its element
        /// lands with and the heaviest creature it knocks back.
        /// </summary>
        public sealed class Wave
        {
            public string Spell;
            public int X, Y, Dir, Width, Left, Every, Age, Power, MaxMass;
        }

        readonly List<Wave> waves = new List<Wave>();
        /// <summary>The waves still rolling, in the order cast (the view carries light props along on their fronts).</summary>
        public IReadOnlyList<Wave> Waves => waves;

        /// <summary>Whether a spell rolls out as a wave (a line with travelEvery that doesn't whirl) rather than landing at once.</summary>
        public static bool IsWave(SpellDef s) => s != null && s.TravelEvery > 0 && !s.Inward && !s.Rolls && s.Shape == "line";

        /// <summary>
        /// A wave's cast (startWave in the TS): the front starts on the cast's square, a line across the way it
        /// rolls, Width cells to each side, and washes over it; it rolls one cell every TravelEvery ticks, to the
        /// line's length in all (TickWaves).
        /// </summary>
        void StartWave(SpellDef s, int x, int y, int dir, int charge)
        {
            var wv = new Wave
            {
                Spell = s.Id,
                X = x,
                Y = y,
                Dir = dir,
                Width = s.Radius + ChargedGrowth(s, charge),
                Left = ChargedLength(s.Length, charge) - 1,
                Every = s.TravelEvery,
                Power = s.Power * charge,
                MaxMass = s.MaxMass,
            };
            Wash(wv, s);
            if (wv.Left > 0) waves.Add(wv);
            else Break(wv, s);
        }

        /// <summary>Each wave, in the order cast, rolls a cell when its time comes; against a wall, an inactive chunk or its length's end it breaks (Break) and is gone.</summary>
        void TickWaves()
        {
            if (waves.Count == 0) return;
            foreach (var wv in waves)
            {
                if (++wv.Age % wv.Every != 0) continue;
                var s = Rules.Spell(wv.Spell);
                int nx = wv.X + DirX(wv.Dir), ny = wv.Y + DirY(wv.Dir);
                if (s != null && IsActiveAt(nx, ny) && !IsBlocking(At(nx, ny)))
                {
                    wv.X = nx;
                    wv.Y = ny;
                    wv.Left--;
                    Wash(wv, s);
                }
                else wv.Left = 0;
                if (wv.Left <= 0) Break(wv, s);
            }
            waves.RemoveAll(wv => wv.Left <= 0);
        }

        // The open cells of a wave's front, from one side to the other.
        List<(int x, int y)> Front(Wave wv)
        {
            var cells = new List<(int x, int y)>();
            int px = DirY(wv.Dir), py = DirX(wv.Dir);
            for (int o = -wv.Width; o <= wv.Width; o++)
            {
                int cx = wv.X + px * o, cy = wv.Y + py * o;
                if (IsActiveAt(cx, cy) && !IsBlocking(At(cx, cy))) cells.Add((cx, cy));
            }
            return cells;
        }

        // The wave's element lands on its front (it douses fires and soaks what it meets), then each creature
        // there light enough is knocked a cell on ahead of it, where that's open.
        void Wash(Wave wv, SpellDef s)
        {
            var front = Front(wv);
            foreach (var (cx, cy) in front) Apply(cx, cy, s.Element, wv.Power, wv.Dir, true);
            foreach (var (cx, cy) in front)
            {
                var r = RaiderAt(cx, cy);
                if (r == null || !r.Alive || Rules.Entity(r.Kind).Mass > wv.MaxMass) continue;
                int nx = cx + DirX(wv.Dir), ny = cy + DirY(wv.Dir);
                if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny)) || RaiderAt(nx, ny) != null) continue;
                r.X = nx;
                r.Y = ny;
            }
        }

        // Where it stops it breaks, and its Leaves (mud) lands over the front.
        void Break(Wave wv, SpellDef s)
        {
            wv.Left = 0;
            if (s == null) return;
            LogOnce($"wave#{s.Id}", $"The {s.Name.ToLowerInvariant()} breaks", LogKind.Info);
            if (s.Leaves == null) return;
            foreach (var (cx, cy) in Front(wv)) Apply(cx, cy, s.Leaves, 1, -1, true);
        }

        /// <summary>The waves as text, "" when there are none: where each front is, the cells it has left and its age (waveState in the TS).</summary>
        public string WaveState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var wv in waves) sb.Append("|~").Append(Whole(wv.X)).Append(':').Append(Whole(wv.Y)).Append(':').Append(Whole(wv.Left)).Append(':').Append(Whole(wv.Age));
            return sb.ToString();
        }
    }
}
