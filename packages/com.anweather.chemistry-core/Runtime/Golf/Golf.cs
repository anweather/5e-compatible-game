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
// takes no turn. The ball going in raises the holed event (World.Triggers.cs). This file is the hole's data, the ball a
// Body the core steps (D22); GolfMode.cs is golf's mode, installed on a world that plays it; its numbers are sim.json's
// "golf" (GolfParams, RuleModes.cs).
using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Chem
{
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
        public int TeeX, TeeY, HoleX, HoleY, Strokes;
        public bool Holed;
        public string LastBy;
        public int TurnEnds;
        public bool Placed, NoBall, NoCup;

        public Body Ball { get; private set; } = new Body();

        public int X { get => Ball.X; set => Ball.X = value; }
        public int Y { get => Ball.Y; set => Ball.Y = value; }
        public int Dir { get => Ball.Dir; set => Ball.Dir = value; }
        public int Roll { get => Ball.Roll; set => Ball.Roll = value; }
        public int Carry { get => Ball.Carry; set => Ball.Carry = value; }
        public int Age { get => Ball.Age; set => Ball.Age = value; }

        public bool Moving => !Holed && Ball.Moving;
        public bool InPlay => !Holed && !NoBall;
        internal bool Plain => LastBy == null && !Placed;

        internal GolfState Copy()
        {
            var copy = (GolfState)MemberwiseClone();
            copy.Ball = Ball.Copy();
            return copy;
        }
    }
}
