// Travelling waves (spells.json travelEvery on a line that doesn't whirl: Wave).
// Port of tickWaves in sandbox/src/actions.ts.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        public sealed class Wave : Mover
        {
            public string Spell;
            public int Width, Left, Every, Age, Power, MaxMass;
        }

        readonly List<Wave> waves = new List<Wave>();
        public IReadOnlyList<Wave> Waves => waves;

        public static bool IsWave(SpellDef s) => s != null && s.TravelEvery > 0 && !s.Inward && !s.Rolls && s.Shape == "line";

        void StartWave(SpellDef s, int x, int y, int dir, int charge, int aimX, int aimY)
        {
            var wv = new Wave
            {
                Spell = s.Id,
                Width = s.Radius + ChargedGrowth(s, charge),
                Left = ChargedLength(s.Length, charge) - 1,
                Every = s.TravelEvery,
                Power = s.Power * charge,
                MaxMass = s.MaxMass,
            };
            wv.SetOff(x, y, dir, aimX, aimY);
            Wash(wv, s);
            if (wv.Left > 0) waves.Add(wv);
            else Break(wv, s);
        }

        void TickWaves()
        {
            if (waves.Count == 0) return;
            foreach (var wv in waves)
            {
                if (++wv.Age % wv.Every != 0) continue;
                var s = Rules.Spell(wv.Spell);
                var (nx, ny) = wv.Ahead();
                if (s != null && IsActiveAt(nx, ny) && !IsBlocking(At(nx, ny)))
                {
                    wv.Advance();
                    wv.X = FoldX(wv.X);
                    wv.Y = FoldY(wv.Y);
                    wv.Left--;
                    Wash(wv, s);
                }
                else wv.Left = 0;
                if (wv.Left <= 0) Break(wv, s);
            }
            waves.RemoveAll(wv => wv.Left <= 0);
        }

        public List<(int x, int y)> WaveFront(Wave wv)
        {
            var cells = new List<(int x, int y)>();
            bool alongX = System.Math.Abs(wv.AX) >= System.Math.Abs(wv.AY);
            int px = alongX ? 0 : 1, py = alongX ? 1 : 0;
            for (int o = -wv.Width; o <= wv.Width; o++)
            {
                int cx = wv.X + px * o, cy = wv.Y + py * o;
                if (IsActiveAt(cx, cy) && !IsBlocking(At(cx, cy))) cells.Add((FoldX(cx), FoldY(cy)));
            }
            return cells;
        }

        void Wash(Wave wv, SpellDef s)
        {
            var front = WaveFront(wv);
            foreach (var (cx, cy) in front) LandOnRaider(s, cx, cy);
            foreach (var (cx, cy) in front)
            {
                Apply(cx, cy, s.Element, wv.Power, wv.Dir, true);
                if (s.Then != null) Apply(cx, cy, s.Then, wv.Power, wv.Dir, true);
            }
            foreach (var (cx, cy) in front) Force(s, cx, cy, wv.Dir);
        }

        void Break(Wave wv, SpellDef s)
        {
            wv.Left = 0;
            if (s == null) return;
            LogOnce($"wave#{s.Id}", $"The {s.Name.ToLowerInvariant()} breaks", LogKind.Info);
            if (s.Leaves == null) return;
            foreach (var (cx, cy) in WaveFront(wv)) Apply(cx, cy, s.Leaves, 1, -1, true);
        }

        public string WaveState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var wv in waves) sb.Append("|~").Append(Whole(wv.X)).Append(':').Append(Whole(wv.Y)).Append(':').Append(Whole(wv.Left)).Append(':').Append(Whole(wv.Age));
            return sb.ToString();
        }
    }
}
