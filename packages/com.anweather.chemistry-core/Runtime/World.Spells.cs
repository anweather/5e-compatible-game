// Spell shapes and the cell part of casting. Port of spellCells / castSpell
// in sandbox/src/actions.ts, without pushes, damage, levitate or carry.
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed partial class World
    {
        public const int MaxCharge = 3;

        public static bool Chargeable(SpellDef spell) => spell != null;

        public static int ClampCharge(int charge) => System.Math.Max(1, System.Math.Min(MaxCharge, charge));

        public static int ChargedLength(int length, int charge) => length + length * (ClampCharge(charge) - 1) / 2;

        public static int ChargedGrowth(SpellDef s, int charge) => s.Effect == "terraform" ? 0 : ClampCharge(charge) - 1;

        public static (int x, int y) SnapAim(int aimX, int aimY)
        {
            if (System.Math.Abs(aimY) * 11 < System.Math.Abs(aimX)) return (System.Math.Sign(aimX), 0);
            if (System.Math.Abs(aimX) * 11 < System.Math.Abs(aimY)) return (0, System.Math.Sign(aimY));
            return (aimX, aimY);
        }

        public static int DirOf(int aimX, int aimY, int fallback = 1)
        {
            if (aimX == 0 && aimY == 0) return fallback;
            if (System.Math.Abs(aimX) >= System.Math.Abs(aimY)) return aimX > 0 ? 1 : 3;
            return aimY > 0 ? 2 : 0;
        }

        public List<(int x, int y)> SpellCells(SpellDef s, int x, int y, int dir, int charge = 1, int aimX = 0, int aimY = 0) =>
            IsSkySpell(s) ? new List<(int x, int y)>() : ShapeCells(s, x, y, dir, charge, aimX, aimY);

        public static bool IsSkySpell(SpellDef s) => s != null && (s.Effect == HasteEffect || s.Effect == WeatherEffect);

        List<(int x, int y)> ShapeCells(SpellDef s, int x, int y, int dir, int charge, int aimX, int aimY)
        {
            var output = new List<(int x, int y)>();
            if (s == null) return output;
            (aimX, aimY) = SnapAim(aimX, aimY);
            bool slant = aimX != 0 && aimY != 0;
            void Add(int cx, int cy) { if (InBounds(cx, cy)) output.Add((FoldX(cx), FoldY(cy))); }
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
                    Jag(s, dir, aimX, aimY, output);
                    break;
                case "line":
                    LineCells(s, x, y, dir, ChargedLength(s.Length, charge), output);
                    Jag(s, dir, aimX, aimY, output);
                    break;
                case "cone" when slant:
                    SlantCone(x, y, ChargedLength(s.Length, charge), aimX, aimY, Add);
                    break;
                case "cone":
                {
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

        void Jag(SpellDef s, int dir, int aimX, int aimY, List<(int x, int y)> cells)
        {
            if (s.Jagged <= 0) return;
            bool alongX = aimX != 0 || aimY != 0 ? System.Math.Abs(aimX) >= System.Math.Abs(aimY) : DirX(dir) != 0;
            int px = alongX ? 0 : 1, py = alongX ? 1 : 0;
            var jagged = new List<(int x, int y)>(cells.Count);
            for (int k = 0; k < cells.Count; k++)
            {
                int side = 0;
                if (k % 4 == 1) side = 1;
                else if (k % 4 == 3) side = -1;
                int cx = cells[k].x + px * side * s.Jagged, cy = cells[k].y + py * side * s.Jagged;
                if (InBounds(cx, cy)) jagged.Add((FoldX(cx), FoldY(cy)));
            }
            cells.Clear();
            cells.AddRange(jagged);
        }

        static void SpotCells(int x, int y, int r, System.Action<int, int> add)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                int span = r - System.Math.Abs(dy);
                for (int dx = -span; dx <= span; dx++) add(x + dx, y + dy);
            }
        }

        bool overhead;

        void OverheadFlyers(SpellDef s, List<(int x, int y)> cells, CastCommand c)
        {
            if (s.Shape != "line" || !RaiderSystem.List.Exists(Flying)) return;
            overhead = true;
            var whole = ShapeCells(s, c.X, c.Y, c.Dir, c.Charge, c.AimX, c.AimY);
            overhead = false;
            var reached = new HashSet<(int x, int y)>(cells);
            int arc = s.Element == "lightning" ? 1 : 0;
            foreach (var (lx, ly) in whole)
                for (int dy = -arc; dy <= arc; dy++)
                    for (int dx = -arc; dx <= arc; dx++)
                    {
                        int cx = lx + dx, cy = ly + dy;
                        var r = !IsActiveAt(cx, cy) || !reached.Add((FoldX(cx), FoldY(cy))) ? null : RaiderAt(cx, cy);
                        if (r == null || !r.Alive || !Flying(r)) continue;
                        if (s.Element != "lightning") RaiderSystem.ApplyRaiderElement(r, s.Element, true);
                        RaiderSystem.LandOnRaider(s, cx, cy);
                    }
        }

        bool FromAWall(SpellDef s, int x, int y, int dir) =>
            s.Origin == "self" && s.Effect != "terraform" && dir >= 0 && At(x - DirX(dir), y - DirY(dir)) is { } under && IsBlocking(under);

        void LineCells(SpellDef s, int x, int y, int dir, int length, List<(int x, int y)> output)
        {
            bool overWall = FromAWall(s, x, y, dir);
            int from = s.Effect == "terraform" && s.Origin == "self" ? -1 : 0;
            for (int k = from; k < from + length; k++)
            {
                int cx = x + DirX(dir) * k, cy = y + DirY(dir) * k;
                var c = At(cx, cy);
                if (c == null && k < 0) continue;
                if (c == null) break;
                if (overWall && IsBlocking(c)) continue;
                overWall = false;
                output.Add((FoldX(cx), FoldY(cy)));
                if (IsBlocking(c) && !overhead) break;
            }
        }

        bool VisitSlant(int cx, int cy, List<(int x, int y)> output, ref bool overWall)
        {
            var c = At(cx, cy);
            if (c == null) return false;
            if (overWall && IsBlocking(c)) return true;
            overWall = false;
            output.Add((FoldX(cx), FoldY(cy)));
            return overhead || !IsBlocking(c);
        }

        void SlantCells(SpellDef s, int x, int y, int dir, int length, (int x, int y) aim, List<(int x, int y)> output)
        {
            bool overWall = FromAWall(s, x, y, dir);
            if (s.Effect == "terraform" && s.Origin == "self" && At(x - DirX(dir), y - DirY(dir)) != null && !VisitSlant(x - DirX(dir), y - DirY(dir), output, ref overWall)) return;
            var (aimX, aimY) = aim;
            bool alongX = System.Math.Abs(aimX) >= System.Math.Abs(aimY);
            int major = System.Math.Max(System.Math.Abs(aimX), System.Math.Abs(aimY));
            long reach = (long)length * length * major * major, size = (long)aimX * aimX + (long)aimY * aimY;
            int px = x, py = y;
            for (int k = 0; (long)k * k * size < reach; k++)
            {
                int cx = x + RoundDiv(k * aimX, major), cy = y + RoundDiv(k * aimY, major);
                if (k > 0 && cx != px && cy != py && !VisitSlant(alongX ? cx : px, alongX ? py : cy, output, ref overWall)) return;
                if (!VisitSlant(cx, cy, output, ref overWall)) return;
                (px, py) = (cx, cy);
            }
        }

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

        static int RoundDiv(int n, int d) => FloorDivInt(2 * n + d, 2 * d);

        static int FloorDivInt(int a, int b) => a / b - (a % b != 0 && (a < 0) != (b < 0) ? 1 : 0);

        static int FloorDiv(int a, int b) => (int)System.Math.Floor((double)a / b);

        public void Cast(string spellId, int x, int y, int dir, int charge = 1, int aimX = 0, int aimY = 0)
        {
            FinishTick();
            var s = Rules.Spell(spellId);
            if (s == null || !InBounds(x, y)) return;
            _ = Release(s, (x, y), dir, charge, (aimX, aimY), s.Delay, $"You cast {s.Name}");
        }

        public void Cast(CastCommand c)
        {
            castBy = c.By;
            castPower = PowerOf(c.By, c.Spell);
            Cast(c.Spell, c.X, c.Y, c.Dir, c.Charge, c.AimX, c.AimY);
            castBy = null;
            castPower = 100;
        }

        string castBy;

        internal string CastBy => castBy;

        int castPower = 100;
        internal int CastPower => castPower;

        bool Release(SpellDef s, (int x, int y) at, int dir, int charge, (int x, int y) aim, int delay, string log)
        {
            var (x, y) = (FoldX(at.x), FoldY(at.y));
            var (aimX, aimY) = aim;
            var zone = ZoneAgainst(s, x, y);
            if (zone != null)
            {
                AddLog($"The {zone.Name}'s charm smothers your {s.Name}", LogKind.Notable);
                return false;
            }
            if (s.Effect == "levitate" || s.Effect == "carry") return false;
            AddLog(log, LogKind.Info);
            if (s.Effect == HasteEffect) Hasten(s, ClampCharge(charge));
            else if (s.Effect == WeatherEffect) HoldWeather(s, ClampCharge(charge));
            else if (s.Effect == LightEffect) LightUp(s, x, y);
            else if (delay > 0) AimStar(s, x, y, dir, ClampCharge(charge), delay);
            else if (IsWave(s)) StartWave(s, x, y, dir, ClampCharge(charge), aimX, aimY);
            else if (IsBoulder(s)) StartBoulder(s, x, y, dir, ClampCharge(charge), aimX, aimY);
            else Land(s, x, y, dir, ClampCharge(charge), aimX, aimY);
            return true;
        }

        void AimStar(SpellDef s, int x, int y, int dir, int charge, int delay)
        {
            var star = new Star { Spell = s.Id, X = x, Y = y, Dir = dir, Charge = charge, Radius = s.Radius + ChargedGrowth(s, charge), Lands = Tick + delay };
            stars.Add(star);
            MarkStar(star, 1);
        }

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

        public const string LightEffect = "light";

        void LightUp(SpellDef s, int x, int y)
        {
            if (!IsActiveAt(x, y)) return;
            var c = At(x, y);
            c.Lit = System.Math.Max(c.Lit, s.Duration);
        }

        static int InwardDir(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return -1;
            if (System.Math.Abs(dx) >= System.Math.Abs(dy)) return dx > 0 ? 3 : 1;
            return dy > 0 ? 0 : 2;
        }

        static int RingNext(int dx, int dy, int d)
        {
            if (dy == -d && dx < d) return 1;
            if (dx == d && dy < d) return 2;
            if (dy == d && dx > -d) return 3;
            return 0;
        }

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

        void Travel(Whirl ww)
        {
            int dir = WindDir >= 0 ? WindDir : ww.Dir;
            if (dir < 0) return;
            int nx = ww.X + DirX(dir), ny = ww.Y + DirY(dir);
            if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny))) return;
            int dx = nx - ww.X, dy = ny - ww.Y;
            var held = RaiderSystem.List.Where(r => r.Alive && r.StatusOf(LiftedStatus) > 0 && System.Math.Max(System.Math.Abs(DeltaX(ww.X, r.X)), System.Math.Abs(DeltaY(ww.Y, r.Y))) <= EyeOf(ww))
                .OrderByDescending(r => DeltaX(ww.X, r.X) * dx + DeltaY(ww.Y, r.Y) * dy).ThenBy(r => r.Id).ToList();
            foreach (var r in held)
            {
                int hx = r.X + dx, hy = r.Y + dy;
                if (!IsActiveAt(hx, hy) || IsBlocking(At(hx, hy)) || RaiderAt(hx, hy) != null) continue;
                r.X = FoldX(hx);
                r.Y = FoldY(hy);
            }
            ww.X = FoldX(nx);
            ww.Y = FoldY(ny);
        }

        int EyeOf(Whirl ww) => Rules.Spell(ww.Spell)?.Eye ?? 1;

        CarryDef CarriedBy(Whirl ww) =>
            ww.Carrying == null ? null : Rules.Spell(ww.Spell)?.Carries.FirstOrDefault(c => c.Id == ww.Carrying);

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
            int eye = EyeOf(ww);
            for (int dy = -eye; dy <= eye; dy++)
                for (int dx = -eye; dx <= eye; dx++)
                    if (IsActiveAt(ww.X + dx, ww.Y + dy) && Matches(At(ww.X + dx, ww.Y + dy), cond)) return true;
            return false;
        }

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
            if (carry != null) Shed(ww, carry, EyeOf(ww), 1);
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
            int Ring(Raider r) => System.Math.Max(System.Math.Abs(DeltaX(ww.X, r.X)), System.Math.Abs(DeltaY(ww.Y, r.Y)));
            var pulled = RaiderSystem.List.Where(r =>
            {
                var def = Rules.Entity(r.Kind);
                return r.Alive && Ring(r) > 0 && Ring(r) <= ww.Radius && def.Creature && def.Mass <= ww.MaxMass;
            }).OrderBy(Ring).ThenBy(r => r.Id).ToList();
            foreach (var r in RaiderSystem.List.Where(r => r.Alive && Ring(r) <= EyeOf(ww)).ToList()) LiftInTheEye(ww, r);
            foreach (var r in pulled)
            {
                int dir = InwardDir(DeltaX(ww.X, r.X), DeltaY(ww.Y, r.Y));
                int nx = r.X + DirX(dir), ny = r.Y + DirY(dir);
                if (!IsActiveAt(nx, ny) || IsBlocking(At(nx, ny)) || RaiderAt(nx, ny) != null) continue;
                r.X = FoldX(nx);
                r.Y = FoldY(ny);
            }
        }

        public const string LiftedStatus = "lifted";
        void LiftInTheEye(Whirl ww, Raider r)
        {
            var def = Rules.Entity(r.Kind);
            var s = Rules.Spell(ww.Spell);
            if (s == null || !def.Creature || def.Mass > ww.MaxMass) return;
            int left = r.StatusOf(LiftedStatus);
            string name = def.Name.ToLowerInvariant();
            if (left == 0)
            {
                r.Status[LiftedStatus] = s.LiftTicks;
                r.Status[ThrownStatus] = System.Math.Max(r.StatusOf(ThrownStatus), s.LiftTicks);
                LogOnce($"lifted#{r.Id}", $"The whirlwind lifts the {name} off its feet", LogKind.Notable);
                return;
            }
            if (left > ww.Every) return;
            r.Status.Remove(LiftedStatus);
            RaiderSystem.Hurl(r, def, ww.Dir >= 0 ? ww.Dir : 0, s.LiftFling, "whirlwind");
            if (!r.Alive) return;
            RaiderSystem.DamageRaider(r, Rules.Sim.FallDamage * s.LiftHurt, "the whirlwind", "air");
            if (r.Alive) r.Status[ThrownStatus] = System.Math.Max(r.StatusOf(ThrownStatus), Rules.Sim.TicksPerSecond);
            LogOnce($"flung#{r.Id}", $"The whirlwind flings the {name} away", LogKind.Notable);
        }

        (int x, int y)? Seek(SpellDef s, int x, int y)
        {
            (int x, int y)? best = null;
            int bestD = int.MaxValue, r = s.SeekRange;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int d = dx * dx + dy * dy;
                    if (d >= bestD || !IsActiveAt(x + dx, y + dy) || !Matches(At(x + dx, y + dy), s.SeekIf)) continue;
                    best = (FoldX(x + dx), FoldY(y + dy));
                    bestD = d;
                }
            return best;
        }

        public (int x, int y)? Sought(SpellDef s, int x, int y) => s != null && s.SeekRange > 0 ? Seek(s, x, y) : null;

        void Land(SpellDef s, int x, int y, int dir, int charge, int aimX, int aimY)
        {
            x = FoldX(x);
            y = FoldY(y);
            var cells = SpellCells(s, x, y, dir, charge, aimX, aimY);
            cells.RemoveAll(c => !IsActiveAt(c.x, c.y));
            if (s.SeekRange > 0 && Seek(s, x, y) is (int, int) sought) cells = new List<(int x, int y)> { sought };
            if (s.Needs != null)
            {
                cells.RemoveAll(c => !Matches(At(c.x, c.y), s.Needs));
                if (cells.Count == 0)
                {
                    AddLog(s.Fizzle ?? $"{s.Name} fizzles", LogKind.Notable);
                    return;
                }
            }
            if (s.Wind > 0) Wind = (dir, s.Wind);
            if (s.Inward && s.PullEvery > 0 && s.Duration > 0)
                whirls.Add(new Whirl { X = x, Y = y, Radius = s.Radius + ChargedGrowth(s, charge), Ticks = s.Duration, Every = s.PullEvery, MaxMass = s.MaxMass, Spell = s.Id, Dir = dir, Travel = s.TravelEvery });
            if (s.Effect == "terraform") Terraform(s, cells, x, y, charge);
            if (s.Crater != null) Blast(cells);
            Crater(x, y, s.Crater);
            RaiderSystem.BlastRaiders(s, cells, x, y);
            GolfSystem.SpellOnBall(s, cells, (x, y), dir, charge);
            var forced = new HashSet<int>();
            if (s.Element == null)
            {
                foreach (var (cx, cy) in cells) RaiderSystem.Force(s, cx, cy, dir, forced);
                Place(s, cells, x, y);
                return;
            }
            waveFrom = (x, y);
            foreach (var (cx, cy) in cells)
            {
                int way = s.Inward ? InwardDir(cx - x, cy - y) : dir;
                Apply(cx, cy, s.Element, s.Power * charge, way, true);
                if (s.Then != null) Apply(cx, cy, s.Then, s.Power * charge, way, true);
                RaiderSystem.Force(s, cx, cy, dir, forced, charge);
                RaiderSystem.LandOnRaider(s, cx, cy);
            }
            waveFrom = null;
            OverheadFlyers(s, cells, new CastCommand(s.Id, x, y, dir, charge, aimX, aimY));
            Scatter(s.Scatter, x, y);
            if (s.Core != null) Apply(x, y, s.Core, 1, -1, true);
            Place(s, cells, x, y);
        }
    }
}
