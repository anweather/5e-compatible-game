// The Dragon's flight (docs/BACKLOG.md 26; docs/design/architecture-review.md step 9, D9): the summons, a warning, long
// passes over the valley, then leaving. Port of sandbox/src/dragon.ts, line for line; World.Dragon.cs says what a pass
// puts back and keeps the starting state, the audit and the save. The path comes from the seed and the flight's number
// through Sky.Hash, never from the world's random numbers, so a world keeps its RNG sequence whether the dragon comes or
// not. The world owns one (World.DragonSystem) and runs it in the tick's dragon stage (World.RunDragon). It reads the
// world's size, cells and audit; it writes its DragonState and flight count, the chunks it resets (World.Freshen), the
// fires it snuffs, the raiders it knocks down and the log.
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Chem
{
    public sealed class DragonSystem
    {
        const uint PathSalt = 17;

        readonly World world;

        internal DragonSystem(World world)
        {
            this.world = world;
        }

        public DragonState State { get; internal set; }

        public int Flights { get; internal set; }

        public void Apply(DragonCommand command)
        {
            world.FinishTick();
            if (!world.HasStart)
            {
                world.AddLog("Nothing answers: this world keeps no starting state");
                return;
            }
            if (State != null)
            {
                world.AddLog("The dragon is already coming");
                return;
            }
            Summon();
        }

        internal void Summon()
        {
            var p = world.Rules.Sim.Dragon;
            uint h = Sky.Hash(world.Seed, Flights, PathSalt);
            Flights++;
            var d = new DragonState { Phase = DragonPhase.Warning, Until = world.Tick + p.WarningTicks, AlongX = (h >> 31) == 1, FromFar = ((h >> 30) & 1) == 1, Back = ((h >> 29) & 1) == 1 };
            State = d;
            int rows = RowsOf(d), width = Math.Min(p.BreathChunks, rows), step = Math.Max(1, width - p.OverlapChunks);
            d.Passes = 1 + (rows - width + step - 1) / step;
            world.AddLog("The sky darkens: a shadow crosses the valley", LogKind.Notable);
        }

        int AlongOf(DragonState d) => d.AlongX ? world.W : world.H;
        int AcrossOf(DragonState d) => d.AlongX ? world.H : world.W;
        int ColsOf(DragonState d) => Chunks.ChunkOf(AlongOf(d) - 1) + 1;
        int RowsOf(DragonState d) => Chunks.ChunkOf(AcrossOf(d) - 1) + 1;
        int WidthOf(DragonState d) => Math.Min(world.Rules.Sim.Dragon.BreathChunks, RowsOf(d));

        int StripOf(DragonState d)
        {
            int rows = RowsOf(d), width = WidthOf(d), step = Math.Max(1, width - world.Rules.Sim.Dragon.OverlapChunks);
            int at = Math.Min(d.Pass * step, rows - width);
            return d.FromFar ? rows - width - at : at;
        }

        static bool Backward(DragonState d) => d.Back ^ (d.Pass % 2 == 1);

        public (int middle, bool back) PassLine(DragonState flight, int pass)
        {
            var d = new DragonState { AlongX = flight.AlongX, FromFar = flight.FromFar, Back = flight.Back, Pass = pass };
            return ((StripOf(d) * 2 + WidthOf(d)) * Chunks.Size / 2, Backward(d));
        }

        static bool Breathing(DragonState d) => d != null && d.Phase == DragonPhase.Passes && d.Until == 0 && d.Travelled > 0;

        public bool BreathBox(out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = x1 = y1 = 0;
            var d = State;
            if (!Breathing(d)) return false;
            int p = world.Rules.Sim.Dragon.Depth, along = AlongOf(d), across = AcrossOf(d);
            int from = Math.Max(0, d.Travelled - p), to = Math.Min(along, d.Travelled);
            if (from >= to) return false;
            int a0 = Backward(d) ? along - to : from, a1 = Backward(d) ? along - from : to;
            int b0 = StripOf(d) * Chunks.Size, b1 = Math.Min(across, b0 + WidthOf(d) * Chunks.Size);
            (x0, x1, y0, y1) = d.AlongX ? (a0, a1, b0, b1) : (b0, b1, a0, a1);
            return true;
        }

        public bool CaughtByBreath(int x, int y) => CaughtInBox(world.FoldX(x), world.FoldY(y));

        bool CaughtInBox(int x, int y) =>
            BreathBox(out int x0, out int y0, out int x1, out int y1) && x >= x0 && x < x1 && y >= y0 && y < y1 && !ShelteredFromBreath(x, y);

        public bool ShelteredFromBreath(int x, int y)
        {
            var p = world.Rules.Sim.Dragon;
            var c = world.At(x, y);
            if (c == null) return false;
            if (world.DrownDepth(c) >= p.ShelterWater) return true;
            var d = State;
            if (d == null) return false;
            int back = Backward(d) ? 1 : -1;
            for (int k = 1; k <= p.WallReach; k++)
            {
                var n = d.AlongX ? world.At(x + back * k, y) : world.At(x, y + back * k);
                if (n != null && world.IsBlocking(n) && p.WallTerrains.Contains(n.T)) return true;
            }
            return false;
        }


        internal void Fly()
        {
            var d = State;
            if (d == null) return;
            var p = world.Rules.Sim.Dragon;
            switch (d.Phase)
            {
                case DragonPhase.Warning:
                    if (world.Tick < d.Until) return;
                    d.Phase = DragonPhase.Passes;
                    d.Until = 0;
                    world.AddLog("The dragon dives out of the clouds, breathing fire", LogKind.Notable);
                    SnuffFires();
                    break;
                case DragonPhase.Passes:
                    if (d.Until > 0)
                    {
                        if (world.Tick >= d.Until) d.Until = 0;
                        return;
                    }
                    d.Travelled = Math.Min(d.Travelled + p.Speed, AlongOf(d) + p.Depth);
                    KnockDown();
                    break;
                default:
                    if (world.Tick < d.Until) return;
                    State = null;
                    world.ForgetLandStamps();
                    world.AddLog("The dragon wheels away: the valley is fresh", LogKind.Notable);
                    break;
            }
        }

        void SnuffFires()
        {
            world.WriteEveryCell(c =>
            {
                if (c.Fire == 0) return;
                c.Fire = 0;
                c.Fuel = 0;
            });
        }

        void KnockDown()
        {
            int ticks = DownedTicks(world.Rules.Sim);
            foreach (var r in world.RaiderSystem.List)
            {
                if (r.StatusOf(World.DownedStatus) > 0 || !CaughtByBreath(r.X, r.Y)) continue;
                r.Status[World.DownedStatus] = ticks;
                world.LogOnce("dragon-downed", "The dragon's breath knocks them down", LogKind.Hurt);
            }
        }

        static int DownedTicks(SimParams s) => (int)Math.Floor(s.Downed.Seconds * s.TicksPerSecond);

        internal bool FreshenNext()
        {
            var d = State;
            if (d == null || d.Phase != DragonPhase.Passes || d.Until != 0 || d.Done >= ChunksReached(d)) return false;
            int width = WidthOf(d), col = d.Done / width, row = StripOf(d) + d.Done % width;
            if (Backward(d)) col = ColsOf(d) - 1 - col;
            var ch = world.FindChunk(d.AlongX ? col : row, d.AlongX ? row : col);
            d.Done++;
            world.Freshen(ch);
            return true;
        }

        int ChunksReached(DragonState d)
        {
            int along = AlongOf(d), lead = Math.Min(d.Travelled, along);
            if (lead <= 0) return 0;
            int cols = Backward(d) ? ColsOf(d) - Chunks.ChunkOf(along - lead) : Chunks.ChunkOf(lead - 1) + 1;
            return cols * WidthOf(d);
        }

        internal void EndPass()
        {
            var d = State;
            if (d == null || d.Phase != DragonPhase.Passes || d.Until != 0) return;
            var p = world.Rules.Sim.Dragon;
            if (d.Travelled < AlongOf(d) + p.Depth || d.Done < ColsOf(d) * WidthOf(d)) return;
            if (d.Pass + 1 < d.Passes)
            {
                d.Pass++;
                d.Travelled = 0;
                d.Done = 0;
                d.Until = world.Tick + Math.Max(1, p.TurnTicks);
                return;
            }
            d.Phase = DragonPhase.Leaving;
            d.Until = world.Tick + Math.Max(1, p.LeavingTicks);
            d.Travelled = 0;
            world.AddLog("The dragon lands on the mountain and roars", LogKind.Notable);
        }

        public string Text()
        {
            if (!world.HasStart) return "";
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(Flights.ToString(inv)).Append(':').Append(world.AuditAt.ToString(inv)).Append(':').Append(world.ChangedCount.ToString(inv));
            var d = State;
            if (d != null)
                sb.Append('|').Append(string.Join(":", new[] { (int)d.Phase, d.Until, d.AlongX ? 1 : 0, d.FromFar ? 1 : 0, d.Back ? 1 : 0, d.Pass, d.Passes, d.Travelled, d.Done }.Select(n => n.ToString(inv))));
            return sb.ToString();
        }

        public string Hash() => Fnv1a.Hash(Text());
    }
}
