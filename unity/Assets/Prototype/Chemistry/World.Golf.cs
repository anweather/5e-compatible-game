// Spell golf (docs/BACKLOG.md 24): one hole, a big ball on a tee, hit with a
// spell's force (spells.json force: Gust of Wind pushes it, Raise Earth under
// it throws it up and along), no new element. Port of sandbox/src/golf.ts,
// line for line. A GolfCommand puts the ball on the tee; each tick (after the
// boulders) a ball with way on it flies or rolls a cell when its time comes:
// in the air it carries over water and hills, on the ground it rolls on,
// harder down a slope and slower up one, bounces back off what blocks it
// (rock, a raised wall) and runs on downhill when it slows. Water or leaving
// the course puts it back on the tee for a stroke's penalty; rolling onto the
// hole drops it in. Every cast that moves it is a stroke. All whole numbers.
//
// Anyone can play (docs/design/world-acts.md). An act places a ball and a cup anywhere, a new round each time. Any spell
// that lands on the ball moves it: a spell with no force sends it away from where it landed, further for its level and
// for a held charge, less the cells it landed off the ball, so a blast beside it is a tap. It only rolls the four ways, so
// a placed cup takes a ball that stops or passes within cupReach. Players at the course take turns: who just hit it waits for someone else's shot, or for turnTicks,
// so one who walks off stalls nobody. A cast's player comes from the batch; a delayed one (a star, a wave) has none, and
// takes no turn. The ball going in raises the holed event (World.Triggers.cs).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>sim.json's "golf": how far a spell's force sends the ball and how much room the course has.</summary>
    public sealed class GolfParams
    {
        /// <summary>The ball moves a cell every this many ticks.</summary>
        public int Every = 1;
        /// <summary>Cells it rolls for each point of a spell's force push, and flies for each point of its throw.</summary>
        public int PushRoll = 5, ThrowCarry = 3;
        /// <summary>Cells round the tee and the hole that are still the course; past them it's out of bounds.</summary>
        public int Margin = 16;
        /// <summary>Cells more a spell with no force sends it for each level, over a point of push.</summary>
        public int LevelRoll = 2;
        /// <summary>The ticks a player who hit the ball waits for someone else's shot.</summary>
        public int TurnTicks = 90;
        /// <summary>Cells round a placed cup that drop the ball in; the valley's hole takes only its own cell.</summary>
        public int CupReach = 1;

        internal static GolfParams Parse(JToken t)
        {
            var g = new GolfParams();
            if (!(t is JObject o)) return g;
            g.Every = Math.Max(1, (int?)o["every"] ?? g.Every);
            g.PushRoll = (int?)o["pushRoll"] ?? g.PushRoll;
            g.ThrowCarry = (int?)o["throwCarry"] ?? g.ThrowCarry;
            g.Margin = (int?)o["margin"] ?? g.Margin;
            g.LevelRoll = (int?)o["levelRoll"] ?? g.LevelRoll;
            g.TurnTicks = (int?)o["turnTicks"] ?? g.TurnTicks;
            g.CupReach = (int?)o["cupReach"] ?? g.CupReach;
            return g;
        }
    }

    /// <summary>Puts the ball on the tee of a hole (the tee's cell and the hole's), a new round with no strokes.</summary>
    public readonly struct GolfCommand
    {
        public GolfCommand(int teeX, int teeY, int holeX, int holeY)
        {
            TeeX = teeX;
            TeeY = teeY;
            HoleX = holeX;
            HoleY = holeY;
        }

        public int TeeX { get; }
        public int TeeY { get; }
        public int HoleX { get; }
        public int HoleY { get; }

        public const string FileName = "golf.json";

        /// <summary>The course in world/golf.json (tee and hole, grid cells), or null without the file.</summary>
        public static GolfCommand? Load(string worldDir)
        {
            string path = Path.Combine(worldDir, FileName);
            if (!File.Exists(path)) return null;
            var o = JObject.Parse(File.ReadAllText(path));
            if (!(o["tee"] is JArray tee) || tee.Count != 2 || !(o["hole"] is JArray hole) || hole.Count != 2)
                throw new FormatException($"{FileName}: the course needs \"tee\": [x, y] and \"hole\": [x, y]");
            return new GolfCommand((int)tee[0], (int)tee[1], (int)hole[0], (int)hole[1]);
        }
    }

    /// <summary>
    /// The hole being played: the tee and the hole, where the ball is, the strokes so far, whether it's in, and the way
    /// on it: the way it's going, cells still to fly (Carry) and then to roll (Roll), and its age in ticks.
    /// </summary>
    public sealed class GolfState
    {
        public int TeeX, TeeY, HoleX, HoleY, X, Y, Strokes, Dir, Roll, Carry, Age;
        public bool Holed;
        /// <summary>Who hit it last, null until a player's cast has, and the tick they may hit it again though others are playing.</summary>
        public string LastBy;
        public int TurnEnds;
        /// <summary>An act placed this round. Until both are placed one of the two is missing, and its cell is the other's.</summary>
        public bool Placed, NoBall, NoCup;
        public bool Moving => !Holed && (Roll > 0 || Carry > 0);
        /// <summary>Both are there and the ball isn't in yet: a spell can hit it.</summary>
        public bool InPlay => !Holed && !NoBall;
        internal bool Plain => LastBy == null && !Placed;
        internal GolfState Copy() => (GolfState)MemberwiseClone();
    }

    public sealed partial class World
    {
        /// <summary>What an act calls the golf ball and the cup; "ball" alone is the court's physics ball.</summary>
        public const string GolfBall = "golf-ball", GolfCup = "cup";

        /// <summary>The hole being played, or null before anyone teed off.</summary>
        public GolfState Golf { get; private set; }

        /// <summary>Puts the ball on the tee (startGolf in the TS): a new round, again after holing out. Between ticks, like a cast.</summary>
        public void ApplyGolf(GolfCommand command)
        {
            FinishTick(); // commands land between whole ticks (StepSome)
            if (!InBounds(command.TeeX, command.TeeY) || !InBounds(command.HoleX, command.HoleY)) return;
            Golf = new GolfState { TeeX = command.TeeX, TeeY = command.TeeY, HoleX = command.HoleX, HoleY = command.HoleY, X = command.TeeX, Y = command.TeeY, Dir = -1 };
            AddLog("The ball is on the tee: hole it in as few strokes as you can", LogKind.Notable);
        }

        /// <summary>
        /// A spell's force on the ball at (x, y) (hitBall in the TS): its push sets it rolling PushRoll cells a point along
        /// dir, its throw sends it flying ThrowCarry cells a point and rolling a cell a point after. A stroke.
        /// </summary>
        void HitBall(SpellDef s, int x, int y, int dir)
        {
            var p = Rules.Sim.Golf;
            StrokeBall(s, x, y, dir, s.ForcePush * p.PushRoll + s.ForceThrow, s.ForceThrow * p.ThrowCarry);
        }

        // A spell with no force that lands on the ball: away from where it landed, or along its way when that's the ball's own cell.
        void SpellOnBall(SpellDef s, List<(int x, int y)> cells, (int x, int y) landed, int dir, int charge)
        {
            var g = Golf;
            if (g == null || s.ForcePush > 0 || s.ForceThrow > 0 || !cells.Contains((g.X, g.Y))) return;
            var p = Rules.Sim.Golf;
            int off = Math.Max(Math.Abs(DeltaX(landed.x, g.X)), Math.Abs(g.Y - landed.y));
            StrokeBall(s, g.X, g.Y, DirOf(DeltaX(landed.x, g.X), g.Y - landed.y, dir), Math.Max(1, (p.PushRoll + s.Level * p.LevelRoll) * charge - off), 0);
        }

        void StrokeBall(SpellDef s, int x, int y, int dir, int roll, int carry)
        {
            var g = Golf;
            if (g == null || !g.InPlay || g.X != x || g.Y != y || dir < 0 || (roll <= 0 && carry <= 0) || !TurnOpen(g)) return;
            if (castBy != null) (g.LastBy, g.TurnEnds) = (castBy, Tick + Rules.Sim.Golf.TurnTicks);
            g.Dir = dir;
            g.Roll = roll;
            g.Carry = carry;
            g.Age = 0;
            g.Strokes++;
            AddLog($"Stroke {g.Strokes.ToString(CultureInfo.InvariantCulture)}: the {s.Name.ToLowerInvariant()} sends the ball {DirName(dir)}");
        }

        // Whether the cast's player may hit the ball: not again straight after their own shot while someone else is playing.
        bool TurnOpen(GolfState g)
        {
            if (castBy == null || castBy != g.LastBy) return true;
            ReadStands();
            if (!GolfWaiting(g)) return true;
            LogOnce($"turn#{Whole(g.Strokes)}", $"{TriggerDef.PeerOf(castBy)} just played: it's someone else's shot", LogKind.Notable);
            return false;
        }

        /// <summary>Whether whoever hit the ball last is waiting for someone else's shot.</summary>
        public bool GolfWaiting(GolfState g) =>
            g.LastBy != null && Tick < g.TurnEnds && stands.Values.Any(w => w.Player != g.LastBy && !w.Down && InCourse(g, w.X, w.Y));

        // An act's ball or cup on its cell, if the ground's open and the other isn't there: a new round, and the first
        // one placed where there's no course leaves the other to come. Cells the act gave past the first aren't used.
        int PlaceGolf(string what, List<(int x, int y)> cells)
        {
            if (cells.Count == 0) return 0;
            var (x, y) = cells[0];
            if (!InBounds(x, y) || !IsActiveAt(x, y) || IsBlocking(At(x, y)) || InWater(At(x, y))) return 0;
            var g = Golf ?? new GolfState { TeeX = x, TeeY = y, HoleX = x, HoleY = y, NoBall = true, NoCup = true };
            bool ball = what == GolfBall;
            if (ball ? !g.NoCup && g.HoleX == x && g.HoleY == y : !g.NoBall && g.TeeX == x && g.TeeY == y) return 0;
            if (ball) (g.TeeX, g.TeeY, g.NoBall) = (x, y, false);
            else (g.HoleX, g.HoleY, g.NoCup) = (x, y, false);
            if (g.NoCup) (g.HoleX, g.HoleY) = (g.TeeX, g.TeeY);
            if (g.NoBall) (g.TeeX, g.TeeY) = (g.HoleX, g.HoleY);
            (g.X, g.Y, g.Dir, g.Roll, g.Carry, g.Age, g.Strokes, g.Holed) = (g.TeeX, g.TeeY, -1, 0, 0, 0, 0, false);
            (g.LastBy, g.TurnEnds, g.Placed) = (null, 0, true);
            Golf = g;
            string placed = ball ? "ball" : "cup", wanted = ball ? "cup" : "ball";
            AddLog(g.NoBall || g.NoCup ? $"A {placed} is placed: it wants a {wanted}" : "Ball and cup are placed: hit it in with any spell, taking turns", LogKind.Notable);
            return 1;
        }

        /// <summary>The ball's tick (tickGolf in the TS): a cell flown or rolled when its time comes.</summary>
        void TickGolf()
        {
            var g = Golf;
            if (g == null || !g.Moving || ++g.Age % Rules.Sim.Golf.Every != 0) return;
            if (g.Carry > 0) Fly(g);
            else RollBall(g);
        }

        // A cell through the air: over water and hills, back off what blocks it; where it comes down it may land in the
        // water or (on the hole) drop in.
        void Fly(GolfState g)
        {
            int nx = g.X + DirX(g.Dir), ny = g.Y + DirY(g.Dir);
            if (!OnCourse(g, nx, ny))
            {
                LoseBall(g, "out of bounds");
                return;
            }
            if (IsBlocking(At(nx, ny))) g.Dir = (g.Dir + 2) % 4;
            else
            {
                g.X = FoldX(nx);
                g.Y = ny;
            }
            if (--g.Carry == 0) Arrive(g);
        }

        // A cell along the ground: climbing costs a cell of roll more for each step (or part) it rises, a drop gives
        // one back for each whole step; what blocks it bounces it back at a cell's cost. Run out on a slope, it rolls on
        // down the steepest way.
        void RollBall(GolfState g)
        {
            int nx = g.X + DirX(g.Dir), ny = g.Y + DirY(g.Dir);
            if (!OnCourse(g, nx, ny))
            {
                LoseBall(g, "out of bounds");
                return;
            }
            if (IsBlocking(At(nx, ny)))
            {
                g.Dir = (g.Dir + 2) % 4;
                g.Roll--;
                LogOnce($"bounce#{Whole(nx)}:{Whole(ny)}", "The ball bounces back off the rock");
            }
            else
            {
                int step = Rules.Sim.WaterUnitsPerStep, rise = GroundUnits(nx, ny) - GroundUnits(g.X, g.Y);
                int cost = rise > 0 ? 1 + (rise + step - 1) / step : 1;
                if (cost > g.Roll) g.Roll = 0;
                else
                {
                    g.X = FoldX(nx);
                    g.Y = ny;
                    g.Roll += -cost + (rise < 0 ? -rise / step : 0);
                    if (Arrive(g)) return;
                }
            }
            if (g.Roll <= 0) Downhill(g);
        }

        // Where the ball comes down or rolls onto: water loses it, the hole holes it. True when either did.
        bool Arrive(GolfState g)
        {
            if (InWater(At(g.X, g.Y)))
            {
                LoseBall(g, "in the water");
                return true;
            }
            int reach = g.Placed ? Rules.Sim.Golf.CupReach : 0;
            if (g.NoCup || Math.Abs(DeltaX(g.HoleX, g.X)) > reach || Math.Abs(g.Y - g.HoleY) > reach) return false;
            (g.X, g.Y) = (g.HoleX, g.HoleY);
            g.Holed = true;
            g.Roll = g.Carry = 0;
            AddLog(g.Strokes == 1 ? "Holed in one!" : $"Holed in {g.Strokes.ToString(CultureInfo.InvariantCulture)}!", LogKind.Notable);
            BallHoled(g);
            return true;
        }

        // A ball that slows on a slope rolls on a cell down the steepest way (N, E, S, W on a tie); on the level it rests.
        void Downhill(GolfState g)
        {
            g.Roll = 0;
            int best = -1, bestLevel = GroundUnits(g.X, g.Y);
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = g.X + DirX(dir), ny = g.Y + DirY(dir);
                if (!OnCourse(g, nx, ny) || IsBlocking(At(nx, ny))) continue;
                int level = GroundUnits(nx, ny);
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
            AddLog($"The ball is {where}: back on the tee, a stroke's penalty", LogKind.Notable);
        }

        bool OnCourse(GolfState g, int x, int y) => InBounds(x, y) && IsActiveAt(x, y) && InCourse(g, x, y);

        bool InCourse(GolfState g, int x, int y)
        {
            int m = Rules.Sim.Golf.Margin;
            return x >= Math.Min(g.TeeX, g.HoleX) - m && x <= Math.Max(g.TeeX, g.HoleX) + m
                && y >= Math.Min(g.TeeY, g.HoleY) - m && y <= Math.Max(g.TeeY, g.HoleY) + m;
        }

        /// <summary>The ground's level at (x, y) in water units: the cell's elevation and the land's hills under it.</summary>
        int GroundUnits(int x, int y)
        {
            int units = ElevationUnits(At(x, y));
            int land = LandAtCell(x, y);
            if (land != NoLand) units += land;
            return units;
        }

        static string DirName(int dir) => dir switch { 0 => "north", 1 => "east", 2 => "south", _ => "west" };

        /// <summary>The hole as text, "" before anyone teed off (so worlds without it hash as they did; golfState in the TS).</summary>
        public string GolfText()
        {
            var g = Golf;
            if (g == null) return "";
            string text = "|g" + string.Join(":", Whole(g.X), Whole(g.Y), Whole(g.Strokes), g.Holed ? "1" : "0", Whole(g.Dir), Whole(g.Roll), Whole(g.Carry), Whole(g.Age));
            return g.Plain ? text : text + ":" + string.Join(":", Whole(g.TeeX), Whole(g.TeeY), Whole(g.HoleX), Whole(g.HoleY), g.LastBy ?? "", Whole(g.TurnEnds), Whole(GolfFlags(g)));
        }

        static int GolfFlags(GolfState g) => (g.Placed ? 1 : 0) | (g.NoBall ? 2 : 0) | (g.NoCup ? 4 : 0);

        /// <summary>FNV-1a of GolfText (part of CastHash, so of the online checksum).</summary>
        public string GolfHash() => Fnv1a.Hash(GolfText());

        // The hole into a save, when there is one: tee, hole, ball, strokes, holed and the way on it.
        void SaveGolf(JObject save)
        {
            if (Golf != null) save["golf"] = GolfRow(Golf);
        }

        static JArray GolfRow(GolfState g)
        {
            var row = new JArray(g.TeeX, g.TeeY, g.HoleX, g.HoleY, g.X, g.Y, g.Strokes, g.Holed ? 1 : 0, g.Dir, g.Roll, g.Carry, g.Age);
            if (g.Plain) return row;
            row.Add(g.LastBy);
            row.Add(g.TurnEnds);
            row.Add(GolfFlags(g));
            return row;
        }

        void LoadGolf(JObject s)
        {
            if (s["golf"] is JArray g) Golf = ReadGolf(g);
        }

        static GolfState ReadGolf(JArray g) =>
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
