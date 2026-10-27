// Catching up (docs/WORLD_STREAMING.md, "Part 2, second slice"): a chunk that
// deactivated while still live (the linger caps let it go, or a plan without
// lingering) remembers the tick it stopped, and when it next activates it first
// runs the ticks it missed, up to sim.json streaming.catchUpTicks. It runs
// alone: its neighbours are walls meanwhile, and only the passes over its own
// cells run (fire, water, gas, the weather on its cells, timers), each at the
// tick it missed and with that tick's weather. Every peer activates it at the
// same tick from the same cells, so every peer catches it up the same: the
// chunk switch is the command. C# only: the sandbox has no chunks.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        // While a chunk catches up the log is left alone: what happened there is in the past.
        bool catchingUp;

        /// <summary>The ticks a loaded chunk ran to catch up when it last activated (0 if it had none to).</summary>
        public int CaughtUp(int cx, int cy) => ChunkOrThrow(cx, cy).CaughtUp;

        /// <summary>A chunk deactivating: it remembers the tick if something in it is still live.</summary>
        void Stop(Chunk ch) => ch.StoppedAt = Tick - ch.LastLive < Rules.Sim.Streaming.SettleTicks ? Tick : NeverLive;

        /// <summary>A chunk activating: it runs the ticks it missed while live, then counts as live now.</summary>
        void Resume(Chunk ch)
        {
            int stopped = ch.StoppedAt;
            ch.StoppedAt = NeverLive;
            ch.CaughtUp = stopped == NeverLive ? 0 : Math.Max(0, Math.Min(Tick - stopped, Rules.Sim.Streaming.CatchUpTicks));
            if (ch.CaughtUp > 0) CatchUp(ch, stopped, ch.CaughtUp);
            ch.LastLive = Tick;
        }

        void CatchUp(Chunk ch, int stopped, int ticks)
        {
            var rows = new List<RowSpan>();
            AddRowSpans(new List<Chunk> { ch }, 0, 0, rows);
            var span = rows.ToArray();
            var walls = new List<Chunk>();
            foreach (var n in new[] { ch.North, ch.East, ch.South, ch.West })
            {
                if (n == null || !n.Active) continue;
                n.Active = false;
                walls.Add(n);
            }
            int now = Tick;
            var weather = Weather;
            catchingUp = true;
            try
            {
                for (int t = stopped + 1; t <= stopped + ticks; t++)
                {
                    Tick = t;
                    Weather = SkyNow();
                    TickFire(span);
                    TickWater(span);
                    TickGas(span);
                    WeatherOnCells(span);
                    TickTimers(span);
                    Array.Clear(ch.Marks, 0, ch.Marks.Length);
                }
            }
            finally
            {
                catchingUp = false;
                Tick = now;
                Weather = weather;
                foreach (var n in walls) n.Active = true;
            }
            // An edge still live when the catch-up ended is live now.
            for (int d = 0; d < 4; d++) if (ch.EdgeLive[d] == stopped + ticks) ch.EdgeLive[d] = Tick;
        }
    }
}
