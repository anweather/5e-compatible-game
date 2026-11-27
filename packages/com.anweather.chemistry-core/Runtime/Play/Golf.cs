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
// takes no turn. The ball going in raises the holed event (World.Triggers.cs). This file is the hole's data; GolfSystem.cs
// is its system, which the world owns.
using System;
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
}
