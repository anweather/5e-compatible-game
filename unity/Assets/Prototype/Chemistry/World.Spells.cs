// Spell shapes and the cell part of casting. Port of spellCells / castSpell
// in sandbox/src/actions.ts, without pushes, damage, levitate or carry.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>The most a cast can be charged (held before release): 1 is a tap.</summary>
        public const int MaxCharge = 3;

        /// <summary>A cast's charge, kept to 1..MaxCharge.</summary>
        public static int ClampCharge(int charge) => System.Math.Max(1, System.Math.Min(MaxCharge, charge));

        /// <summary>A line spell's length at a charge: half again for each level past a tap.</summary>
        public static int ChargedLength(int length, int charge) => length + length * (ClampCharge(charge) - 1) / 2;

        /// <summary>
        /// An aim (a whole-number vector on the grid, x east and y south) snapped to the grid's four
        /// ways when it's within about 5 degrees of one (its smaller part under a 11th of its
        /// larger), so a nearly straight line comes out straight. (0, 0) stays (0, 0).
        /// </summary>
        public static (int x, int y) SnapAim(int aimX, int aimY)
        {
            if (System.Math.Abs(aimY) * 11 < System.Math.Abs(aimX)) return (System.Math.Sign(aimX), 0);
            if (System.Math.Abs(aimX) * 11 < System.Math.Abs(aimY)) return (0, System.Math.Sign(aimY));
            return (aimX, aimY);
        }

        /// <summary>The grid way (0 north, 1 east, 2 south, 3 west) nearest an aim; ties go east or west. <paramref name="fallback"/> for (0, 0).</summary>
        public static int DirOf(int aimX, int aimY, int fallback = 1)
        {
            if (aimX == 0 && aimY == 0) return fallback;
            if (System.Math.Abs(aimX) >= System.Math.Abs(aimY)) return aimX > 0 ? 1 : 3;
            return aimY > 0 ? 2 : 0;
        }

        /// <summary>
        /// Cells a spell would touch, in the order it touches them (for previews too). A line or a
        /// cone points along <paramref name="dir"/>, or along an aim off the grid's four ways
        /// (<paramref name="aimX"/>, <paramref name="aimY"/>; SnapAim) when one is given.
        /// </summary>
        public List<(int x, int y)> SpellCells(SpellDef s, int x, int y, int dir, int charge = 1, int aimX = 0, int aimY = 0) =>
            // The time spells (Hasten Time, Control Weather) change the sky, not squares.
            IsSkySpell(s) ? new List<(int x, int y)>() : ShapeCells(s, x, y, dir, charge, aimX, aimY);

        /// <summary>Whether a spell changes the sky for everyone (Hasten Time, Control Weather) rather than squares.</summary>
        public static bool IsSkySpell(SpellDef s) => s != null && (s.Effect == HasteEffect || s.Effect == WeatherEffect);

        List<(int x, int y)> ShapeCells(SpellDef s, int x, int y, int dir, int charge, int aimX, int aimY)
        {
            var output = new List<(int x, int y)>();
            if (s == null) return output;
            (aimX, aimY) = SnapAim(aimX, aimY);
            bool slant = aimX != 0 && aimY != 0;
            void Add(int cx, int cy) { if (InBounds(cx, cy)) output.Add((cx, cy)); }
            switch (s.Shape)
            {
                case "point":
                case "target":
                    Add(x, y);
                    break;
                case "line" when slant:
                    SlantCells(s, x, y, dir, ChargedLength(s.Length, charge), (aimX, aimY), output);
                    break;
                case "line":
                    LineCells(s, x, y, dir, ChargedLength(s.Length, charge), output);
                    break;
                case "cone" when slant:
                    SlantCone(x, y, ChargedLength(s.Length, charge), aimX, aimY, Add);
                    break;
                case "cone":
                {
                    // From the square in front of the caster, widening a square each side per square
                    // out (a 15 ft cone: 1, 3, then 5 squares); it washes over walls.
                    int len = ChargedLength(s.Length, charge), px = DirY(dir), py = DirX(dir);
                    for (int k = 0; k < len; k++)
                        for (int o = -k; o <= k; o++) Add(x + DirX(dir) * k + px * o, y + DirY(dir) * k + py * o);
                    break;
                }
                case "square":
                {
                    int n = s.Size, off = FloorDiv(n - 1, 2);
                    for (int dy = 0; dy < n; dy++)
                        for (int dx = 0; dx < n; dx++) Add(x - off + dx, y - off + dy);
                    break;
                }
                case "burst":
                {
                    int r = s.Radius;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++) Add(x + dx, y + dy);
                    break;
                }
            }
            return output;
        }

        /// <summary>
        /// A line from (x, y) up to its first blocking cell. A terraform line from the caster
        /// (Earthen Trench) starts under their feet, one square behind the aim, so a trench cut
        /// from a pond's bank opens into the pond.
        /// </summary>
        void LineCells(SpellDef s, int x, int y, int dir, int length, List<(int x, int y)> output)
        {
            int from = s.Effect == "terraform" && s.Origin == "self" ? -1 : 0;
            for (int k = from; k < from + length; k++)
            {
                int cx = x + DirX(dir) * k, cy = y + DirY(dir) * k;
                var c = At(cx, cy);
                if (c == null && k < 0) continue;
                if (c == null) break;
                output.Add((cx, cy));
                if (IsBlocking(c)) break;
            }
        }

        /// <summary>
        /// A line along a slanted aim, like LineCells: from (x, y) (for a trench, from the caster's
        /// square behind it first), out to <paramref name="length"/> squares as the crow flies, up
        /// to its first blocking cell. Each square shares a side with the one before (a corner is
        /// turned through the square along the aim's longer part), so water runs along a slanted
        /// trench and a slanted wall has no gaps.
        /// </summary>
        void SlantCells(SpellDef s, int x, int y, int dir, int length, (int x, int y) aim, List<(int x, int y)> output)
        {
            bool Visit(int cx, int cy)
            {
                var c = At(cx, cy);
                if (c == null) return false;
                output.Add((cx, cy));
                return !IsBlocking(c);
            }
            if (s.Effect == "terraform" && s.Origin == "self" && At(x - DirX(dir), y - DirY(dir)) != null && !Visit(x - DirX(dir), y - DirY(dir))) return;
            var (aimX, aimY) = aim;
            bool alongX = System.Math.Abs(aimX) >= System.Math.Abs(aimY);
            int major = System.Math.Max(System.Math.Abs(aimX), System.Math.Abs(aimY));
            long reach = (long)length * length * major * major, size = (long)aimX * aimX + (long)aimY * aimY;
            int px = x, py = y;
            for (int k = 0; (long)k * k * size < reach; k++)
            {
                int cx = x + RoundDiv(k * aimX, major), cy = y + RoundDiv(k * aimY, major);
                if (k > 0 && cx != px && cy != py && !Visit(alongX ? cx : px, alongX ? py : cy)) return;
                if (!Visit(cx, cy)) return;
                (px, py) = (cx, cy);
            }
        }

        /// <summary>
        /// A cone along a slanted aim, from its apex (x, y): every square within 45 degrees of the aim
        /// and nearer than <paramref name="length"/> squares along it, in row order.
        /// </summary>
        static void SlantCone(int x, int y, int length, int aimX, int aimY, System.Action<int, int> add)
        {
            long reach = (long)length * length * ((long)aimX * aimX + (long)aimY * aimY);
            for (int dy = -length; dy <= length; dy++)
                for (int dx = -length; dx <= length; dx++)
                {
                    long along = (long)dx * aimX + (long)dy * aimY, side = System.Math.Abs((long)dx * aimY - (long)dy * aimX);
                    if (along >= 0 && side <= along && along * along < reach) add(x + dx, y + dy);
                }
        }

        /// <summary>n / d (d &gt; 0) rounded to the nearest whole number, halves up, in whole numbers.</summary>
        static int RoundDiv(int n, int d) => FloorDivInt(2 * n + d, 2 * d);

        static int FloorDivInt(int a, int b) => a / b - (a % b != 0 && (a < 0) != (b < 0) ? 1 : 0);

        static int FloorDiv(int a, int b) => (int)System.Math.Floor((double)a / b);

        /// <summary>
        /// Applies the spell's element to its cells, like castSpell's cell part. Gust of
        /// Wind (air + wind) also sets the world's wind. A terraform spell raises or digs
        /// its cells first (World.Terraform.cs). The other effects (levitate, carry,
        /// teleport) do nothing to cells. A charged cast (held: charge 2 or 3) is that many
        /// times as strong, raises or digs that many times the steps, and a line reaches
        /// half again as far per level. A line or cone may be aimed off the grid's four ways
        /// (SpellCells); <paramref name="dir"/> is then the nearest of them (DirOf), which the
        /// element pushes along. A spell with a crater (Fireball) blows it out of the
        /// ground before its element lands.
        /// </summary>
        public void Cast(string spellId, int x, int y, int dir, int charge = 1, int aimX = 0, int aimY = 0)
        {
            var s = Rules.Spell(spellId);
            if (s == null || !InBounds(x, y)) return;
            if (s.Effect == "levitate" || s.Effect == "carry") return;
            AddLog($"You cast {s.Name}", LogKind.Info);
            if (s.Effect == HasteEffect) Hasten(s, ClampCharge(charge));
            else if (s.Effect == WeatherEffect) HoldWeather(s, ClampCharge(charge));
            else Land(s, x, y, dir, ClampCharge(charge), aimX, aimY);
        }

        /// <summary>What a spell does to cells where it lands, without the cast's log line (a meteor's landing too).</summary>
        void Land(SpellDef s, int x, int y, int dir, int charge, int aimX, int aimY)
        {
            var cells = SpellCells(s, x, y, dir, charge, aimX, aimY);
            // Squares in paused chunks are left alone (World.Chunks.cs).
            cells.RemoveAll(c => !IsActiveAt(c.x, c.y));
            if (s.Element == "air" && s.Wind > 0) Wind = (dir, s.Wind);
            if (s.Effect == "terraform") Terraform(s, cells, x, y, charge);
            Crater(x, y, s.Crater);
            if (s.Element == null) return;
            foreach (var (cx, cy) in cells) Apply(cx, cy, s.Element, s.Power * charge, dir, true);
        }
    }
}
