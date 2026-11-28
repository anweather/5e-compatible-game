// The world modes' numbers in the rules (sim.json, D22): each mode's own key, read with the rest of the sim's rules so
// a mod can change them. Golf's (sim.json "golf"; Runtime/Golf, docs/BACKLOG.md 24): how often the ball moves, how far
// a push rolls it and a throw carries it, the course's margin round the tee and the hole, the roll a spell's level
// adds, how long a turn waits for the others, and how near a placed cup takes the ball.
using System;
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
}
