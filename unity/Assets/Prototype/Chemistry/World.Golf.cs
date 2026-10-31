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
using System;
using System.Globalization;
using System.IO;
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

        internal static GolfParams Parse(JToken t)
        {
            var g = new GolfParams();
            if (!(t is JObject o)) return g;
            g.Every = Math.Max(1, (int?)o["every"] ?? g.Every);
            g.PushRoll = (int?)o["pushRoll"] ?? g.PushRoll;
            g.ThrowCarry = (int?)o["throwCarry"] ?? g.ThrowCarry;
            g.Margin = (int?)o["margin"] ?? g.Margin;
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
        public bool Moving => !Holed && (Roll > 0 || Carry > 0);
    }

    public sealed partial class World
    {
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
            var g = Golf;
            if (g == null || g.Holed || g.X != x || g.Y != y || dir < 0) return;
            var p = Rules.Sim.Golf;
            int roll = s.ForcePush * p.PushRoll + s.ForceThrow, carry = s.ForceThrow * p.ThrowCarry;
            if (roll <= 0 && carry <= 0) return;
            g.Dir = dir;
            g.Roll = roll;
            g.Carry = carry;
            g.Age = 0;
            g.Strokes++;
            AddLog($"Stroke {g.Strokes.ToString(CultureInfo.InvariantCulture)}: the {s.Name.ToLowerInvariant()} sends the ball {DirName(dir)}");
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
                g.X = nx;
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
                    g.X = nx;
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
            if (g.X != g.HoleX || g.Y != g.HoleY) return false;
            g.Holed = true;
            g.Roll = g.Carry = 0;
            AddLog(g.Strokes == 1 ? "Holed in one!" : $"Holed in {g.Strokes.ToString(CultureInfo.InvariantCulture)}!", LogKind.Notable);
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

        bool OnCourse(GolfState g, int x, int y)
        {
            int m = Rules.Sim.Golf.Margin;
            return InBounds(x, y) && IsActiveAt(x, y)
                && x >= Math.Min(g.TeeX, g.HoleX) - m && x <= Math.Max(g.TeeX, g.HoleX) + m
                && y >= Math.Min(g.TeeY, g.HoleY) - m && y <= Math.Max(g.TeeY, g.HoleY) + m;
        }

        /// <summary>The ground's level at (x, y) in water units: the cell's elevation and the land's hills under it.</summary>
        int GroundUnits(int x, int y)
        {
            int units = ElevationUnits(At(x, y));
            if (land != null && land[Idx(x, y)] != NoLand) units += land[Idx(x, y)];
            return units;
        }

        static string DirName(int dir) => dir switch { 0 => "north", 1 => "east", 2 => "south", _ => "west" };

        /// <summary>The hole as text, "" before anyone teed off (so worlds without it hash as they did; golfState in the TS).</summary>
        public string GolfText()
        {
            var g = Golf;
            if (g == null) return "";
            return "|g" + string.Join(":", Whole(g.X), Whole(g.Y), Whole(g.Strokes), g.Holed ? "1" : "0", Whole(g.Dir), Whole(g.Roll), Whole(g.Carry), Whole(g.Age));
        }

        /// <summary>FNV-1a of GolfText (part of CastHash, so of the online checksum).</summary>
        public string GolfHash() => Fnv1a.Hash(GolfText());

        // The hole into a save, when there is one: tee, hole, ball, strokes, holed and the way on it.
        void SaveGolf(JObject save)
        {
            var g = Golf;
            if (g != null) save["golf"] = new JArray(g.TeeX, g.TeeY, g.HoleX, g.HoleY, g.X, g.Y, g.Strokes, g.Holed ? 1 : 0, g.Dir, g.Roll, g.Carry, g.Age);
        }

        void LoadGolf(JObject s)
        {
            if (!(s["golf"] is JArray g)) return;
            Golf = new GolfState
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
            };
        }
    }
}
