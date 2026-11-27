// Spell golf's system (docs/BACKLOG.md 24, docs/design/architecture-review.md step 9, D9): the hole played, over the
// world's cells, tick, rules and log. Port of sandbox/src/golf.ts, line for line; the data is Golf.cs, which says how the
// ball goes. The world owns one (World.GolfSystem) and ticks it after the boulders. It reads the cells (and the land's
// hills under them), the wizards' stands and the cast's player; it writes its GolfState, the log and the holed event.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The hole being played (World.GolfSystem).</summary>
    public sealed class GolfSystem
    {
        readonly World world;

        internal GolfSystem(World world)
        {
            this.world = world;
        }

        /// <summary>The hole being played, or null before anyone teed off.</summary>
        public GolfState State { get; internal set; }

        /// <summary>Puts the ball on the tee (startGolf in the TS): a new round, again after holing out. Between ticks, like a cast.</summary>
        public void Apply(GolfCommand command)
        {
            world.FinishTick(); // commands land between whole ticks (StepSome)
            if (!world.InBounds(command.TeeX, command.TeeY) || !world.InBounds(command.HoleX, command.HoleY)) return;
            State = new GolfState { TeeX = command.TeeX, TeeY = command.TeeY, HoleX = command.HoleX, HoleY = command.HoleY, X = command.TeeX, Y = command.TeeY, Dir = -1 };
            world.AddLog("The ball is on the tee: hole it in as few strokes as you can", LogKind.Notable);
        }

        /// <summary>
        /// A spell's force on the ball at (x, y) (hitBall in the TS): its push sets it rolling PushRoll cells a point along
        /// dir, its throw sends it flying ThrowCarry cells a point and rolling a cell a point after. A stroke.
        /// </summary>
        internal void HitBall(SpellDef s, int x, int y, int dir)
        {
            var p = world.Rules.Sim.Golf;
            StrokeBall(s, x, y, dir, s.ForcePush * p.PushRoll + s.ForceThrow, s.ForceThrow * p.ThrowCarry);
        }

        // A spell with no force that lands on the ball: away from where it landed, or along its way when that's the ball's own cell.
        internal void SpellOnBall(SpellDef s, List<(int x, int y)> cells, (int x, int y) landed, int dir, int charge)
        {
            var g = State;
            if (g == null || s.ForcePush > 0 || s.ForceThrow > 0 || !cells.Contains((g.X, g.Y))) return;
            var p = world.Rules.Sim.Golf;
            int off = Math.Max(Math.Abs(world.DeltaX(landed.x, g.X)), Math.Abs(world.DeltaY(landed.y, g.Y)));
            StrokeBall(s, g.X, g.Y, World.DirOf(world.DeltaX(landed.x, g.X), world.DeltaY(landed.y, g.Y), dir), Math.Max(1, (p.PushRoll + s.Level * p.LevelRoll) * charge - off), 0);
        }

        void StrokeBall(SpellDef s, int x, int y, int dir, int roll, int carry)
        {
            var g = State;
            if (g == null || !g.InPlay || g.X != x || g.Y != y || dir < 0 || (roll <= 0 && carry <= 0) || !TurnOpen(g)) return;
            if (world.CastBy != null) (g.LastBy, g.TurnEnds) = (world.CastBy, world.Tick + world.Rules.Sim.Golf.TurnTicks);
            g.Dir = dir;
            g.Roll = roll;
            g.Carry = carry;
            g.Age = 0;
            g.Strokes++;
            world.AddLog($"Stroke {g.Strokes.ToString(CultureInfo.InvariantCulture)}: the {s.Name.ToLowerInvariant()} sends the ball {DirName(dir)}");
        }

        // Whether the cast's player may hit the ball: not again straight after their own shot while someone else is playing.
        bool TurnOpen(GolfState g)
        {
            if (world.CastBy == null || world.CastBy != g.LastBy) return true;
            world.ReadStands();
            if (!Waiting(g)) return true;
            world.LogOnce($"turn#{World.Whole(g.Strokes)}", $"{TriggerDef.PeerOf(world.CastBy)} just played: it's someone else's shot", LogKind.Notable);
            return false;
        }

        /// <summary>Whether whoever hit the ball last is waiting for someone else's shot.</summary>
        public bool Waiting(GolfState g) =>
            g.LastBy != null && world.Tick < g.TurnEnds && world.Stands.Values.Any(w => w.Player != g.LastBy && !w.Down && InCourse(g, w.X, w.Y));

        // An act's ball or cup on its cell, if the ground's open and the other isn't there: a new round, and the first
        // one placed where there's no course leaves the other to come. Cells the act gave past the first aren't used.
        internal int Place(string what, List<(int x, int y)> cells)
        {
            if (cells.Count == 0) return 0;
            var (x, y) = cells[0];
            if (!world.InBounds(x, y) || !world.IsActiveAt(x, y) || world.IsBlocking(world.At(x, y)) || World.InWater(world.At(x, y))) return 0;
            var g = State ?? new GolfState { TeeX = x, TeeY = y, HoleX = x, HoleY = y, NoBall = true, NoCup = true };
            bool ball = what == World.GolfBall;
            if (ball ? !g.NoCup && g.HoleX == x && g.HoleY == y : !g.NoBall && g.TeeX == x && g.TeeY == y) return 0;
            if (ball) (g.TeeX, g.TeeY, g.NoBall) = (x, y, false);
            else (g.HoleX, g.HoleY, g.NoCup) = (x, y, false);
            if (g.NoCup) (g.HoleX, g.HoleY) = (g.TeeX, g.TeeY);
            if (g.NoBall) (g.TeeX, g.TeeY) = (g.HoleX, g.HoleY);
            (g.X, g.Y, g.Dir, g.Roll, g.Carry, g.Age, g.Strokes, g.Holed) = (g.TeeX, g.TeeY, -1, 0, 0, 0, 0, false);
            (g.LastBy, g.TurnEnds, g.Placed) = (null, 0, true);
            State = g;
            string placed = ball ? "ball" : "cup", wanted = ball ? "cup" : "ball";
            world.AddLog(g.NoBall || g.NoCup ? $"A {placed} is placed: it wants a {wanted}" : "Ball and cup are placed: hit it in with any spell, taking turns", LogKind.Notable);
            return 1;
        }

        /// <summary>The ball's tick (tickGolf in the TS): a cell flown or rolled when its time comes.</summary>
        internal void Tick()
        {
            var g = State;
            if (g == null || !g.Moving || ++g.Age % world.Rules.Sim.Golf.Every != 0) return;
            if (g.Carry > 0) Fly(g);
            else RollBall(g);
        }

        // A cell through the air: over water and hills, back off what blocks it; where it comes down it may land in the
        // water or (on the hole) drop in.
        void Fly(GolfState g)
        {
            int nx = g.X + World.DirX(g.Dir), ny = g.Y + World.DirY(g.Dir);
            if (!OnCourse(g, nx, ny))
            {
                LoseBall(g, "out of bounds");
                return;
            }
            if (world.IsBlocking(world.At(nx, ny))) g.Dir = (g.Dir + 2) % 4;
            else
            {
                g.X = world.FoldX(nx);
                g.Y = world.FoldY(ny);
            }
            if (--g.Carry == 0) Arrive(g);
        }

        // A cell along the ground: climbing costs a cell of roll more for each step (or part) it rises, a drop gives
        // one back for each whole step; what blocks it bounces it back at a cell's cost. Run out on a slope, it rolls on
        // down the steepest way.
        void RollBall(GolfState g)
        {
            int nx = g.X + World.DirX(g.Dir), ny = g.Y + World.DirY(g.Dir);
            if (!OnCourse(g, nx, ny))
            {
                LoseBall(g, "out of bounds");
                return;
            }
            if (world.IsBlocking(world.At(nx, ny)))
            {
                g.Dir = (g.Dir + 2) % 4;
                g.Roll--;
                world.LogOnce($"bounce#{World.Whole(nx)}:{World.Whole(ny)}", "The ball bounces back off the rock");
            }
            else
            {
                int step = world.Rules.Sim.WaterUnitsPerStep, rise = world.GroundUnits(nx, ny) - world.GroundUnits(g.X, g.Y);
                int cost = rise > 0 ? 1 + (rise + step - 1) / step : 1;
                if (cost > g.Roll) g.Roll = 0;
                else
                {
                    g.X = world.FoldX(nx);
                    g.Y = world.FoldY(ny);
                    g.Roll += -cost + (rise < 0 ? -rise / step : 0);
                    if (Arrive(g)) return;
                }
            }
            if (g.Roll <= 0) Downhill(g);
        }

        // Where the ball comes down or rolls onto: water loses it, the hole holes it. True when either did.
        bool Arrive(GolfState g)
        {
            if (World.InWater(world.At(g.X, g.Y)))
            {
                LoseBall(g, "in the water");
                return true;
            }
            int reach = g.Placed ? world.Rules.Sim.Golf.CupReach : 0;
            if (g.NoCup || Math.Abs(world.DeltaX(g.HoleX, g.X)) > reach || Math.Abs(world.DeltaY(g.HoleY, g.Y)) > reach) return false;
            (g.X, g.Y) = (g.HoleX, g.HoleY);
            g.Holed = true;
            g.Roll = g.Carry = 0;
            world.AddLog(g.Strokes == 1 ? "Holed in one!" : $"Holed in {g.Strokes.ToString(CultureInfo.InvariantCulture)}!", LogKind.Notable);
            world.BallHoled(g);
            return true;
        }

        // A ball that slows on a slope rolls on a cell down the steepest way (N, E, S, W on a tie); on the level it rests.
        void Downhill(GolfState g)
        {
            g.Roll = 0;
            int best = -1, bestLevel = world.GroundUnits(g.X, g.Y);
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = g.X + World.DirX(dir), ny = g.Y + World.DirY(dir);
                if (!OnCourse(g, nx, ny) || world.IsBlocking(world.At(nx, ny))) continue;
                int level = world.GroundUnits(nx, ny);
                if (level >= bestLevel) continue;
                best = dir;
                bestLevel = level;
            }
            if (best < 0) return;
            g.Dir = best;
            g.Roll = 1;
        }

        void LoseBall(GolfState g, string where)
        {
            g.X = g.TeeX;
            g.Y = g.TeeY;
            g.Roll = g.Carry = 0;
            g.Strokes++;
            world.AddLog($"The ball is {where}: back on the tee, a stroke's penalty", LogKind.Notable);
        }

        bool OnCourse(GolfState g, int x, int y) => world.InBounds(x, y) && world.IsActiveAt(x, y) && InCourse(g, x, y);

        bool InCourse(GolfState g, int x, int y)
        {
            int m = world.Rules.Sim.Golf.Margin;
            return x >= Math.Min(g.TeeX, g.HoleX) - m && x <= Math.Max(g.TeeX, g.HoleX) + m
                && y >= Math.Min(g.TeeY, g.HoleY) - m && y <= Math.Max(g.TeeY, g.HoleY) + m;
        }

        static string DirName(int dir) => dir switch { 0 => "north", 1 => "east", 2 => "south", _ => "west" };

        /// <summary>The hole as text, "" before anyone teed off (so worlds without it hash as they did; golfState in the TS).</summary>
        public string Text()
        {
            var g = State;
            if (g == null) return "";
            string text = "|g" + string.Join(":", World.Whole(g.X), World.Whole(g.Y), World.Whole(g.Strokes), g.Holed ? "1" : "0", World.Whole(g.Dir), World.Whole(g.Roll), World.Whole(g.Carry), World.Whole(g.Age));
            return g.Plain ? text : text + ":" + string.Join(":", World.Whole(g.TeeX), World.Whole(g.TeeY), World.Whole(g.HoleX), World.Whole(g.HoleY), g.LastBy ?? "", World.Whole(g.TurnEnds), World.Whole(GolfFlags(g)));
        }

        static int GolfFlags(GolfState g) => (g.Placed ? 1 : 0) | (g.NoBall ? 2 : 0) | (g.NoCup ? 4 : 0);

        /// <summary>FNV-1a of GolfText (part of CastHash, so of the online checksum).</summary>
        public string Hash() => Fnv1a.Hash(Text());

        // The hole into a save, when there is one: tee, hole, ball, strokes, holed and the way on it.
        internal void Save(JObject save)
        {
            if (State != null) save["golf"] = Row(State);
        }

        internal static JArray Row(GolfState g)
        {
            var row = new JArray(g.TeeX, g.TeeY, g.HoleX, g.HoleY, g.X, g.Y, g.Strokes, g.Holed ? 1 : 0, g.Dir, g.Roll, g.Carry, g.Age);
            if (g.Plain) return row;
            row.Add(g.LastBy);
            row.Add(g.TurnEnds);
            row.Add(GolfFlags(g));
            return row;
        }

        internal void Load(JObject s)
        {
            if (s["golf"] is JArray g) State = Read(g);
        }

        internal static GolfState Read(JArray g) =>
            new GolfState
            {
                TeeX = (int)g[0],
                TeeY = (int)g[1],
                HoleX = (int)g[2],
                HoleY = (int)g[3],
                X = (int)g[4],
                Y = (int)g[5],
                Strokes = (int)g[6],
                Holed = (int)g[7] != 0,
                Dir = (int)g[8],
                Roll = (int)g[9],
                Carry = (int)g[10],
                Age = (int)g[11],
                LastBy = g.Count > 12 ? (string)g[12] : null,
                TurnEnds = g.Count > 13 ? (int)g[13] : 0,
                Placed = g.Count > 14 && ((int)g[14] & 1) != 0,
                NoBall = g.Count > 14 && ((int)g[14] & 2) != 0,
                NoCup = g.Count > 14 && ((int)g[14] & 4) != 0,
            };
    }
}
