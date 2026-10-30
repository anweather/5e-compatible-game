// Spell shapes and the cell part of casting. Port of spellCells / castSpell
// in sandbox/src/actions.ts, without pushes, damage, levitate or carry.
using System.Collections.Generic;
using System.Linq;

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
        /// How many squares a charged spot, square or burst grows by: one for each level past a tap
        /// (a bolt spreads to the squares round it, a burst's radius and a square's side grow).
        /// Earth shaping digs or raises more steps instead, so its patch stays the same.
        /// </summary>
        public static int ChargedGrowth(SpellDef s, int charge) => s.Effect == "terraform" ? 0 : ClampCharge(charge) - 1;

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
                    SpotCells(x, y, ChargedGrowth(s, charge), Add);
                    break;
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
                    int n = s.Size + ChargedGrowth(s, charge), off = FloorDiv(n - 1, 2);
                    for (int dy = 0; dy < n; dy++)
                        for (int dx = 0; dx < n; dx++) Add(x - off + dx, y - off + dy);
                    break;
                }
                case "burst":
                {
                    int r = s.Radius + ChargedGrowth(s, charge);
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++) Add(x + dx, y + dy);
                    break;
                }
            }
            return output;
        }

        /// <summary>A spot and every square within <paramref name="r"/> steps of it (a diamond), row by row: a charged bolt.</summary>
        static void SpotCells(int x, int y, int r, System.Action<int, int> add)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                int span = r - System.Math.Abs(dy);
                for (int dx = -span; dx <= span; dx++) add(x + dx, y + dy);
            }
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

        // A slanted line's next cell: added to output if it is in the world; true while the line goes on (it isn't blocking).
        bool VisitSlant(int cx, int cy, List<(int x, int y)> output)
        {
            var c = At(cx, cy);
            if (c == null) return false;
            output.Add((cx, cy));
            return !IsBlocking(c);
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
            if (s.Effect == "terraform" && s.Origin == "self" && At(x - DirX(dir), y - DirY(dir)) != null && !VisitSlant(x - DirX(dir), y - DirY(dir), output)) return;
            var (aimX, aimY) = aim;
            bool alongX = System.Math.Abs(aimX) >= System.Math.Abs(aimY);
            int major = System.Math.Max(System.Math.Abs(aimX), System.Math.Abs(aimY));
            long reach = (long)length * length * major * major, size = (long)aimX * aimX + (long)aimY * aimY;
            int px = x, py = y;
            for (int k = 0; (long)k * k * size < reach; k++)
            {
                int cx = x + RoundDiv(k * aimX, major), cy = y + RoundDiv(k * aimY, major);
                if (k > 0 && cx != px && cy != py && !VisitSlant(alongX ? cx : px, alongX ? py : cy, output)) return;
                if (!VisitSlant(cx, cy, output)) return;
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
        /// teleport, shapeshift) do nothing to cells. A charged cast (held: charge 2 or 3) is that many
        /// times as strong, raises or digs that many times the steps, a line reaches half
        /// again as far per level, and a spot, square or burst grows (ChargedGrowth). A line or cone may be aimed off the grid's four ways
        /// (SpellCells); <paramref name="dir"/> is then the nearest of them (DirOf), which the
        /// element pushes along. A spell with a crater (Fireball) blows it out of the
        /// ground before its element lands.
        /// </summary>
        public void Cast(string spellId, int x, int y, int dir, int charge = 1, int aimX = 0, int aimY = 0)
        {
            FinishTick(); // casts land between whole ticks (StepSome)
            var s = Rules.Spell(spellId);
            if (s == null || !InBounds(x, y)) return;
            if (s.Effect == "levitate" || s.Effect == "carry") return;
            AddLog($"You cast {s.Name}", LogKind.Info);
            if (s.Effect == HasteEffect) Hasten(s, ClampCharge(charge));
            else if (s.Effect == WeatherEffect) HoldWeather(s, ClampCharge(charge));
            else if (s.Effect == LightEffect) LightUp(s, x, y);
            else if (s.Delay > 0) AimStar(s, x, y, dir, ClampCharge(charge));
            else if (IsWave(s)) StartWave(s, x, y, dir, ClampCharge(charge));
            else if (IsBoulder(s)) StartBoulder(s, x, y, dir, ClampCharge(charge));
            else Land(s, x, y, dir, ClampCharge(charge), aimX, aimY);
        }

        /// <summary>
        /// A delayed cast (Falling Star, aimStar in the TS): the cells round where it will land wear a target
        /// mark (Cell.Star) until it lands, Delay ticks on (LandStars).
        /// </summary>
        void AimStar(SpellDef s, int x, int y, int dir, int charge)
        {
            var star = new Star { Spell = s.Id, X = x, Y = y, Dir = dir, Charge = charge, Radius = s.Radius + ChargedGrowth(s, charge), Lands = Tick + s.Delay };
            stars.Add(star);
            MarkStar(star, 1);
        }

        // Adds to the mark on the square round a star; taking it off reaches ground that has gone inactive since.
        void MarkStar(Star star, int by)
        {
            int r = star.Radius;
            for (int y = star.Y - r; y <= star.Y + r; y++)
                for (int x = star.X - r; x <= star.X + r; x++)
                {
                    if (!InBounds(x, y) || (by > 0 && !IsActiveAt(x, y))) continue;
                    var c = At(x, y);
                    if (c != null) c.Star = System.Math.Max(0, c.Star + by);
                }
        }

        // Stars whose tick has come land, in the order cast; one over inactive ground leaves no mark.
        void LandStars()
        {
            if (stars.Count == 0) return;
            foreach (var star in stars)
            {
                if (star.Lands > Tick) continue;
                MarkStar(star, -1);
                var s = Rules.Spell(star.Spell);
                if (s == null || !IsActiveAt(star.X, star.Y)) continue;
                AddLog($"{s.Name} strikes", LogKind.Notable);
                Land(s, star.X, star.Y, star.Dir, star.Charge, 0, 0);
            }
            stars.RemoveAll(st => st.Lands <= Tick);
        }

        /// <summary>A spell's burning rock (spells.json scatter; scatter in the TS): each try lays the cover, alight, on open dry ground off the middle.</summary>
        void Scatter(ScatterDef sc, int x, int y)
        {
            if (sc == null) return;
            int span = sc.Reach * 2 + 1;
            for (int i = 0; i < sc.Count; i++)
            {
                int cx = x + (int)System.Math.Floor(Rand() * span) - sc.Reach;
                int cy = y + (int)System.Math.Floor(Rand() * span) - sc.Reach;
                if ((cx == x && cy == y) || !IsActiveAt(cx, cy)) continue;
                var c = At(cx, cy);
                if (IsBlocking(c) || c.Water > 0) continue;
                PutCover(c, sc.Cover);
                IgniteCover(c);
            }
        }

        /// <summary>The Light spell's effect (SRD Light).</summary>
        public const string LightEffect = "light";

        /// <summary>
        /// Light (SRD 5.2.1): the object touched sheds bright light for the spell's duration. The core keeps
        /// the ticks on its cell (Cell.Lit, counting down in the timer pass); the view draws the spell's light
        /// { bright, dim } round it.
        /// </summary>
        void LightUp(SpellDef s, int x, int y)
        {
            if (!IsActiveAt(x, y)) return;
            var c = At(x, y);
            c.Lit = System.Math.Max(c.Lit, s.Duration);
        }

        /// <summary>The direction from (dx, dy) off a burst's middle back toward it, -1 at the middle: along the longer axis, east or west on a tie (Whirlwind's inward).</summary>
        static int InwardDir(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return -1;
            if (System.Math.Abs(dx) >= System.Math.Abs(dy)) return dx > 0 ? 3 : 1;
            return dy > 0 ? 0 : 2;
        }

        /// <summary>
        /// The next cell clockwise round the square ring d out from a whirlwind's middle, from (dx, dy) on it
        /// (ringNext in the TS): east along the top, south down the right, west along the bottom, north up the left.
        /// </summary>
        static int RingNext(int dx, int dy, int d)
        {
            if (dy == -d && dx < d) return 1;
            if (dx == d && dy < d) return 2;
            if (dy == d && dx > -d) return 3;
            return 0;
        }

        /// <summary>
        /// Each whirlwind, in the order cast (tickWhirlwinds in the TS): every Every ticks it whirls once
        /// (Spin), every Travel ticks it moves a cell (Travel), and once its ticks are up it drops what it
        /// carries and is gone.
        /// </summary>
        void TickWhirlwinds()
        {
            foreach (var ww in whirls)
            {
                if (++ww.Age % ww.Every == 0) Spin(ww);
                if (ww.Travel > 0 && ww.Age % ww.Travel == 0) Travel(ww);
            }
            foreach (var ww in whirls)
                if (ww.Age >= ww.Ticks) DropCarried(ww);
            whirls.RemoveAll(ww => ww.Age >= ww.Ticks);
        }

        // A cell with the wind, or in still air the way it was cast; a wall or an inactive chunk holds it up.
        void Travel(Whirl ww)
        {
            int dir = WindDir >= 0 ? WindDir : ww.Dir;
            if (dir < 0) return;
            int nx = ww.X + DirX(dir), ny = ww.Y + DirY(dir);
            if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny))) return;
            ww.X = nx;
            ww.Y = ny;
        }

        // How far from its eye a whirlwind takes things on and sheds what it carries, in cells.
        const int WhirlEye = 1;

        CarryDef CarriedBy(Whirl ww) =>
            ww.Carrying == null ? null : Rules.Spell(ww.Spell)?.Carries.FirstOrDefault(c => c.Id == ww.Carrying);

        // The first of its spell's carries (strongest first) that the eye is over, stopping at what it already
        // carries: a fire's smoke reaches it first, and the fire still makes it a fire tornado.
        void TakeOn(Whirl ww)
        {
            var carries = Rules.Spell(ww.Spell)?.Carries;
            if (carries == null) return;
            foreach (var carry in carries.TakeWhile(c => c.Id != ww.Carrying))
            {
                if (!EyeOver(ww, carry.If)) continue;
                ww.Carrying = carry.Id;
                if (carry.Log != null) LogOnce($"carry#{carry.Id}", carry.Log, LogKind.Notable);
                return;
            }
        }

        bool EyeOver(Whirl ww, CellCond cond)
        {
            for (int dy = -WhirlEye; dy <= WhirlEye; dy++)
                for (int dx = -WhirlEye; dx <= WhirlEye; dx++)
                    if (IsActiveAt(ww.X + dx, ww.Y + dy) && Matches(At(ww.X + dx, ww.Y + dy), cond)) return true;
            return false;
        }

        // Applies what it carries over the square of cells within reach of its eye, row by row.
        void Shed(Whirl ww, CarryDef carry, int reach, int power)
        {
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++) Apply(ww.X + dx, ww.Y + dy, carry.Sheds, power, -1, false);
        }

        void DropCarried(Whirl ww)
        {
            var carry = CarriedBy(ww);
            if (carry == null) return;
            LogOnce($"drop#{carry.Id}", $"The {carry.Name} dies out and drops what it carried", LogKind.Notable);
            Shed(ww, carry, ww.Radius, Rules.Spell(ww.Spell).DropPower);
        }

        /// <summary>
        /// One turn of a whirlwind (spin in the TS), over the square of cells round its middle: fire burning
        /// on a ring lights the next cell clockwise round it; gas moves a cell in toward the middle, the inner
        /// rings first, each gas once; creatures of MaxMass or less, nearest first, step a cell in where it's
        /// open. Things (props, items) are the view's, not the core's.
        /// </summary>
        void Spin(Whirl ww)
        {
            int r = ww.Radius;
            var lit = new List<(int x, int y, int dir)>();
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int d = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                    if (d > 0 && IsActiveAt(ww.X + dx, ww.Y + dy) && At(ww.X + dx, ww.Y + dy).Fire > 0) lit.Add((ww.X + dx, ww.Y + dy, RingNext(dx, dy, d)));
                }
            foreach (var (bx, by, dir) in lit) Apply(bx + DirX(dir), by + DirY(dir), "fire", 1, -1, false);
            WhirlGas(ww);
            WhirlCreatures(ww);
            TakeOn(ww);
            var carry = CarriedBy(ww);
            if (carry != null) Shed(ww, carry, WhirlEye, 1);
        }

        void WhirlGas(Whirl ww)
        {
            var moved = new HashSet<(int, int)>();
            for (int d = 1; d <= ww.Radius; d++)
                for (int dy = -d; dy <= d; dy++)
                    for (int dx = -d; dx <= d; dx++)
                    {
                        int cx = ww.X + dx, cy = ww.Y + dy;
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != d || !IsActiveAt(cx, cy) || At(cx, cy).Gas == null || moved.Contains((cx, cy))) continue;
                        int dir = InwardDir(dx, dy);
                        if (ShoveGas(cx, cy, dir)) moved.Add((cx + DirX(dir), cy + DirY(dir)));
                    }
        }

        void WhirlCreatures(Whirl ww)
        {
            int Ring(Raider r) => System.Math.Max(System.Math.Abs(r.X - ww.X), System.Math.Abs(r.Y - ww.Y));
            var pulled = raiders.Where(r =>
            {
                var def = Rules.Entity(r.Kind);
                return r.Alive && Ring(r) > 0 && Ring(r) <= ww.Radius && def.Creature && def.Mass <= ww.MaxMass;
            }).OrderBy(Ring).ThenBy(r => r.Id).ToList();
            foreach (var r in pulled)
            {
                int dir = InwardDir(r.X - ww.X, r.Y - ww.Y);
                int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
                if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny)) || RaiderAt(nx, ny) != null) continue;
                r.X = nx;
                r.Y = ny;
            }
        }

        /// <summary>
        /// The nearest active cell within SeekRange (in steps either way) of (x, y) that matches SeekIf, by distance as the
        /// crow flies, the first in row order on a tie (seek in the TS); null for none.
        /// </summary>
        (int x, int y)? Seek(SpellDef s, int x, int y)
        {
            (int x, int y)? best = null;
            int bestD = int.MaxValue, r = s.SeekRange;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int d = dx * dx + dy * dy;
                    if (d >= bestD || !IsActiveAt(x + dx, y + dy) || !Matches(At(x + dx, y + dy), s.SeekIf)) continue;
                    best = (x + dx, y + dy);
                    bestD = d;
                }
            return best;
        }

        /// <summary>The cell a spell that seeks would strike if aimed at (x, y), or null, for the view's bolt.</summary>
        public (int x, int y)? Sought(SpellDef s, int x, int y) => s != null && s.SeekRange > 0 ? Seek(s, x, y) : null;

        /// <summary>What a spell does to cells where it lands, without the cast's log line (a meteor's landing too).</summary>
        void Land(SpellDef s, int x, int y, int dir, int charge, int aimX, int aimY)
        {
            var cells = SpellCells(s, x, y, dir, charge, aimX, aimY);
            // Squares in paused chunks are left alone (World.Chunks.cs).
            cells.RemoveAll(c => !IsActiveAt(c.x, c.y));
            // A spell that seeks (Lightning) strikes the nearest cell it seeks near where it's aimed instead of its shape.
            if (s.SeekRange > 0 && Seek(s, x, y) is (int, int) sought) cells = new List<(int x, int y)> { sought };
            // A spell that acts on what's already there (Geyser on water) lands only where that is, and fizzles where it isn't.
            if (s.Needs != null)
            {
                cells.RemoveAll(c => !Matches(At(c.x, c.y), s.Needs));
                if (cells.Count == 0)
                {
                    AddLog(s.Fizzle ?? $"{s.Name} fizzles", LogKind.Notable);
                    return;
                }
            }
            // A spell with wind (Gust of Wind, Flame Wave) sets the world's wind along its direction.
            if (s.Wind > 0) Wind = (dir, s.Wind);
            if (s.Inward && s.PullEvery > 0 && s.Duration > 0)
                whirls.Add(new Whirl { X = x, Y = y, Radius = s.Radius + ChargedGrowth(s, charge), Ticks = s.Duration, Every = s.PullEvery, MaxMass = s.MaxMass, Spell = s.Id, Dir = dir, Travel = s.TravelEvery });
            if (s.Effect == "terraform") Terraform(s, cells, x, y, charge);
            if (s.Crater != null) Blast(cells);
            Crater(x, y, s.Crater);
            BlastRaiders(s, cells, x, y);
            if (s.Element == null) return;
            waveFrom = (x, y);
            foreach (var (cx, cy) in cells)
            {
                Apply(cx, cy, s.Element, s.Power * charge, s.Inward ? InwardDir(cx - x, cy - y) : dir, true);
                Force(s, cx, cy, dir);
                LandOnRaider(s, cx, cy);
            }
            waveFrom = null;
            Scatter(s.Scatter, x, y);
            if (s.Core != null) Apply(x, y, s.Core, 1, -1, true);
        }
    }
}
