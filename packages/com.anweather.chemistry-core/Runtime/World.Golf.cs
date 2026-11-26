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
    public sealed class GolfParams
    {
        public int Every = 1;
        public int PushRoll = 5, ThrowCarry = 3;
        public int Margin = 16;
        public int LevelRoll = 2;
        public int TurnTicks = 90;
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

    public sealed class GolfState
    {
        public int TeeX, TeeY, HoleX, HoleY, X, Y, Strokes, Dir, Roll, Carry, Age;
        public bool Holed;
        public string LastBy;
        public int TurnEnds;
        public bool Placed, NoBall, NoCup;
        public bool Moving => !Holed && (Roll > 0 || Carry > 0);
        public bool InPlay => !Holed && !NoBall;
        internal bool Plain => LastBy == null && !Placed;
        internal GolfState Copy() => (GolfState)MemberwiseClone();
    }

    public sealed partial class World
    {
        public const string GolfBall = "golf-ball", GolfCup = "cup";

        public GolfState Golf { get; private set; }

        public void ApplyGolf(GolfCommand command)
        {
            FinishTick();
            if (!InBounds(command.TeeX, command.TeeY) || !InBounds(command.HoleX, command.HoleY)) return;
            Golf = new GolfState { TeeX = command.TeeX, TeeY = command.TeeY, HoleX = command.HoleX, HoleY = command.HoleY, X = command.TeeX, Y = command.TeeY, Dir = -1 };
            AddLog("The ball is on the tee: hole it in as few strokes as you can", LogKind.Notable);
        }

        void HitBall(SpellDef s, int x, int y, int dir)
        {
            var p = Rules.Sim.Golf;
            StrokeBall(s, x, y, dir, s.ForcePush * p.PushRoll + s.ForceThrow, s.ForceThrow * p.ThrowCarry);
        }

        void SpellOnBall(SpellDef s, List<(int x, int y)> cells, (int x, int y) landed, int dir, int charge)
        {
            var g = Golf;
            if (g == null || s.ForcePush > 0 || s.ForceThrow > 0 || !cells.Contains((g.X, g.Y))) return;
            var p = Rules.Sim.Golf;
            int off = Math.Max(Math.Abs(DeltaX(landed.x, g.X)), Math.Abs(DeltaY(landed.y, g.Y)));
            StrokeBall(s, g.X, g.Y, DirOf(DeltaX(landed.x, g.X), DeltaY(landed.y, g.Y), dir), Math.Max(1, (p.PushRoll + s.Level * p.LevelRoll) * charge - off), 0);
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

        bool TurnOpen(GolfState g)
        {
            if (castBy == null || castBy != g.LastBy) return true;
            ReadStands();
            if (!GolfWaiting(g)) return true;
            LogOnce($"turn#{Whole(g.Strokes)}", $"{TriggerDef.PeerOf(castBy)} just played: it's someone else's shot", LogKind.Notable);
            return false;
        }

        public bool GolfWaiting(GolfState g) =>
            g.LastBy != null && Tick < g.TurnEnds && stands.Values.Any(w => w.Player != g.LastBy && !w.Down && InCourse(g, w.X, w.Y));

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

        void TickGolf()
        {
            var g = Golf;
            if (g == null || !g.Moving || ++g.Age % Rules.Sim.Golf.Every != 0) return;
            if (g.Carry > 0) Fly(g);
            else RollBall(g);
        }

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
                g.Y = FoldY(ny);
            }
            if (--g.Carry == 0) Arrive(g);
        }

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
                    g.Y = FoldY(ny);
                    g.Roll += -cost + (rise < 0 ? -rise / step : 0);
                    if (Arrive(g)) return;
                }
            }
            if (g.Roll <= 0) Downhill(g);
        }

        bool Arrive(GolfState g)
        {
            if (InWater(At(g.X, g.Y)))
            {
                LoseBall(g, "in the water");
                return true;
            }
            int reach = g.Placed ? Rules.Sim.Golf.CupReach : 0;
            if (g.NoCup || Math.Abs(DeltaX(g.HoleX, g.X)) > reach || Math.Abs(DeltaY(g.HoleY, g.Y)) > reach) return false;
            (g.X, g.Y) = (g.HoleX, g.HoleY);
            g.Holed = true;
            g.Roll = g.Carry = 0;
            AddLog(g.Strokes == 1 ? "Holed in one!" : $"Holed in {g.Strokes.ToString(CultureInfo.InvariantCulture)}!", LogKind.Notable);
            BallHoled(g);
            return true;
        }

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

        int GroundUnits(int x, int y)
        {
            int units = ElevationUnits(At(x, y));
            int land = LandAtCell(x, y);
            if (land != NoLand) units += land;
            return units;
        }

        static string DirName(int dir) => dir switch { 0 => "north", 1 => "east", 2 => "south", _ => "west" };

        public string GolfText()
        {
            var g = Golf;
            if (g == null) return "";
            string text = "|g" + string.Join(":", Whole(g.X), Whole(g.Y), Whole(g.Strokes), g.Holed ? "1" : "0", Whole(g.Dir), Whole(g.Roll), Whole(g.Carry), Whole(g.Age));
            return g.Plain ? text : text + ":" + string.Join(":", Whole(g.TeeX), Whole(g.TeeY), Whole(g.HoleX), Whole(g.HoleY), g.LastBy ?? "", Whole(g.TurnEnds), Whole(GolfFlags(g)));
        }

        static int GolfFlags(GolfState g) => (g.Placed ? 1 : 0) | (g.NoBall ? 2 : 0) | (g.NoCup ? 4 : 0);

        public string GolfHash() => Fnv1a.Hash(GolfText());

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
