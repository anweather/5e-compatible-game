// Spell golf's mode (docs/BACKLOG.md 24, D22, docs/design/minigames.md): the hole played, over the world's cells, tick,
// rules and log. Port of sandbox/src/golf.ts, line for line; the data is Golf.cs, which says how the ball goes. A world
// that plays golf installs one (GolfMode.Install), and a joiner's world takes it from the one it replaces. The ball is
// a Body: the world steps it after the boulders, and this answers its events (out of the course, a bounce, where it
// comes to). Spells strike it or land on it through the world's events. It reads the cells (and the land's hills under
// them), the wizards' stands and the cast's player; it writes its GolfState, the log and the holed event. Its slot is
// what the world wrote for golf before it was a mode: "golf" in the save, after the machines, "|g..." in the casts
// checksum text after the boulders, and "golf" first in the dragon's start. A map's golf.json lays its hole.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class GolfMode : WorldMode
    {
        public const string ActKind = "golf", Ball = "golf-ball", Cup = "cup";

        GolfState state, start;
        Body[] bodies = Array.Empty<Body>();

        GolfMode(World world)
            : base(world)
        {
        }

        public static World Install(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            world.Install(new GolfMode(world));
            return world;
        }

        public static GolfMode Of(World world) => world?.Mode<GolfMode>();

        public override WorldMode MadeFor(World world) => new GolfMode(world);

        public GolfState State
        {
            get => state;
            internal set
            {
                state = value;
                bodies = value == null ? Array.Empty<Body>() : new[] { value.Ball };
            }
        }

        public override IReadOnlyList<Body> Bodies => bodies;

        GolfParams P => World.Rules.Sim.Golf;

        public void Apply(GolfCommand command)
        {
            var world = World;
            world.FinishTick();
            if (!world.InBounds(command.TeeX, command.TeeY) || !world.InBounds(command.HoleX, command.HoleY)) return;
            State = new GolfState { TeeX = command.TeeX, TeeY = command.TeeY, HoleX = command.HoleX, HoleY = command.HoleY, X = command.TeeX, Y = command.TeeY, Dir = -1 };
            world.AddLog("The ball is on the tee: hole it in as few strokes as you can", LogKind.Notable);
        }

        public override void MapLaid(string dir)
        {
            if (GolfCommand.Load(dir) is { } hole) Apply(hole);
        }

        public override void SpellStruck(Body body, SpellDef spell, int dir)
        {
            if (spell.ForcePush <= 0 && spell.ForceThrow <= 0) return;
            StrokeBall(spell, body.X, body.Y, dir, spell.ForcePush * P.PushRoll + spell.ForceThrow, spell.ForceThrow * P.ThrowCarry);
        }

        public override void SpellLanded(Body body, SpellDef spell, List<(int x, int y)> cells, (int x, int y) landed, int dir, int charge)
        {
            var g = State;
            if (g == null || spell.ForcePush > 0 || spell.ForceThrow > 0) return;
            var world = World;
            int off = Math.Max(Math.Abs(world.DeltaX(landed.x, g.X)), Math.Abs(world.DeltaY(landed.y, g.Y)));
            StrokeBall(spell, g.X, g.Y, World.DirOf(world.DeltaX(landed.x, g.X), world.DeltaY(landed.y, g.Y), dir), Math.Max(1, (P.PushRoll + spell.Level * P.LevelRoll) * charge - off), 0);
        }

        void StrokeBall(SpellDef s, int x, int y, int dir, int roll, int carry)
        {
            var g = State;
            if (g == null || !g.InPlay || g.X != x || g.Y != y || dir < 0 || (roll <= 0 && carry <= 0) || !TurnOpen(g)) return;
            var world = World;
            if (world.CastBy != null) (g.LastBy, g.TurnEnds) = (world.CastBy, world.Tick + P.TurnTicks);
            g.Ball.Launch(dir, roll, carry);
            g.Strokes++;
            world.AddLog($"Stroke {g.Strokes.ToString(CultureInfo.InvariantCulture)}: the {s.Name.ToLowerInvariant()} sends the ball {DirName(dir)}");
        }

        bool TurnOpen(GolfState g)
        {
            var world = World;
            if (world.CastBy == null || world.CastBy != g.LastBy) return true;
            world.ReadStands();
            if (!Waiting(g)) return true;
            world.LogOnce($"turn#{World.Whole(g.Strokes)}", $"{TriggerDef.PeerOf(world.CastBy)} just played: it's someone else's shot", LogKind.Notable);
            return false;
        }

        public bool Waiting(GolfState g) =>
            g.LastBy != null && World.Tick < g.TurnEnds && World.Stands.Values.Any(w => w.Player != g.LastBy && !w.Down && InCourse(g, w.X, w.Y));

        public override bool KnowsAct(string kind, string what) => kind == ActKind && (what == Ball || what == Cup);

        public override string ActStays(string kind) => kind == ActKind ? "a ball or cup can't be taken back: place it again somewhere else" : null;

        public override int Act(string kind, string what, List<(int x, int y)> cells)
        {
            if (cells.Count == 0) return 0;
            var world = World;
            var (x, y) = cells[0];
            if (!world.InBounds(x, y) || !world.IsActiveAt(x, y) || world.IsBlocking(world.At(x, y)) || World.InWater(world.At(x, y))) return 0;
            var g = State ?? new GolfState { TeeX = x, TeeY = y, HoleX = x, HoleY = y, NoBall = true, NoCup = true };
            bool ball = what == Ball;
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

        public override bool BodyMoves(Body body) => State != null && !State.Holed;

        public override int BodyEvery(Body body) => P.Every;

        public override bool BodyMayEnter(Body body, int x, int y) => State != null && InCourse(State, x, y);

        public override void BodyLeft(Body body) => LoseBall(State, "out of bounds");

        public override void BodyBounced(Body body, int x, int y) =>
            World.LogOnce($"bounce#{World.Whole(x)}:{World.Whole(y)}", "The ball bounces back off the rock");

        public override bool BodyArrived(Body body)
        {
            var g = State;
            var world = World;
            if (World.InWater(world.At(g.X, g.Y)))
            {
                LoseBall(g, "in the water");
                return true;
            }
            int reach = g.Placed ? P.CupReach : 0;
            if (g.NoCup || Math.Abs(world.DeltaX(g.HoleX, g.X)) > reach || Math.Abs(world.DeltaY(g.HoleY, g.Y)) > reach) return false;
            (g.X, g.Y) = (g.HoleX, g.HoleY);
            g.Holed = true;
            g.Roll = g.Carry = 0;
            world.AddLog(g.Strokes == 1 ? "Holed in one!" : $"Holed in {g.Strokes.ToString(CultureInfo.InvariantCulture)}!", LogKind.Notable);
            world.RaiseHoled(g.LastBy, g.HoleX, g.HoleY);
            return true;
        }

        void LoseBall(GolfState g, string where)
        {
            g.X = g.TeeX;
            g.Y = g.TeeY;
            g.Roll = g.Carry = 0;
            g.Strokes++;
            World.AddLog($"The ball is {where}: back on the tee, a stroke's penalty", LogKind.Notable);
        }

        bool InCourse(GolfState g, int x, int y)
        {
            int m = P.Margin;
            return x >= Math.Min(g.TeeX, g.HoleX) - m && x <= Math.Max(g.TeeX, g.HoleX) + m
                && y >= Math.Min(g.TeeY, g.HoleY) - m && y <= Math.Max(g.TeeY, g.HoleY) + m;
        }

        static string DirName(int dir) => dir switch { 0 => "north", 1 => "east", 2 => "south", _ => "west" };

        public override void KeepStart() => start = State?.Copy();

        public override void ChunkReset(int cx, int cy)
        {
            var tee = State ?? start;
            if (tee != null && Chunks.ChunkOf(tee.TeeX) == cx && Chunks.ChunkOf(tee.TeeY) == cy) State = start?.Copy();
        }

        public override string SlotText() => Text();

        public string Text()
        {
            var g = State;
            if (g == null) return "";
            string text = "|g" + string.Join(":", World.Whole(g.X), World.Whole(g.Y), World.Whole(g.Strokes), g.Holed ? "1" : "0", World.Whole(g.Dir), World.Whole(g.Roll), World.Whole(g.Carry), World.Whole(g.Age));
            return g.Plain ? text : text + ":" + string.Join(":", World.Whole(g.TeeX), World.Whole(g.TeeY), World.Whole(g.HoleX), World.Whole(g.HoleY), g.LastBy ?? "", World.Whole(g.TurnEnds), World.Whole(GolfFlags(g)));
        }

        static int GolfFlags(GolfState g) => (g.Placed ? 1 : 0) | (g.NoBall ? 2 : 0) | (g.NoCup ? 4 : 0);

        public string Hash() => Fnv1a.Hash(Text());

        public override void SaveSlot(JObject save)
        {
            if (State != null) save["golf"] = Row(State);
        }

        public override void SaveStart(JObject start)
        {
            if (this.start != null) start["golf"] = Row(this.start);
        }

        public override void LoadSlot(JObject save)
        {
            if (save == null) return;
            if (save["golf"] is JArray g) State = Read(g);
            if (save["start"] is JObject saved) start = saved["golf"] is JArray s ? Read(s) : null;
        }

        static JArray Row(GolfState g)
        {
            var row = new JArray(g.TeeX, g.TeeY, g.HoleX, g.HoleY, g.X, g.Y, g.Strokes, g.Holed ? 1 : 0, g.Dir, g.Roll, g.Carry, g.Age);
            if (g.Plain) return row;
            row.Add(g.LastBy);
            row.Add(g.TurnEnds);
            row.Add(GolfFlags(g));
            return row;
        }

        static GolfState Read(JArray g) =>
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
