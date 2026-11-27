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
    /// <summary>The dragon's flight (World.DragonSystem).</summary>
    public sealed class DragonSystem
    {
        const uint PathSalt = 17;

        readonly World world;

        internal DragonSystem(World world)
        {
            this.world = world;
        }

        /// <summary>The flight under way, or null.</summary>
        public DragonState State { get; internal set; }

        /// <summary>Flights begun in this world (the next one's path comes from it).</summary>
        public int Flights { get; internal set; }

        /// <summary>The dragon's summons (a Reset): the warning starts now, unless one is flying or the world keeps no starting state.</summary>
        public void Apply(DragonCommand command)
        {
            world.FinishTick(); // commands land between whole ticks (StepSome)
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

        // ---------- the flight's shape: pure sums on the world's size and the state ----------

        // Cells along a pass, and chunks along and across it.
        int AlongOf(DragonState d) => d.AlongX ? world.W : world.H;
        int AcrossOf(DragonState d) => d.AlongX ? world.H : world.W;
        int ColsOf(DragonState d) => Chunks.ChunkOf(AlongOf(d) - 1) + 1;
        int RowsOf(DragonState d) => Chunks.ChunkOf(AcrossOf(d) - 1) + 1;
        int WidthOf(DragonState d) => Math.Min(world.Rules.Sim.Dragon.BreathChunks, RowsOf(d));

        // The first chunk row of a pass's strip.
        int StripOf(DragonState d)
        {
            int rows = RowsOf(d), width = WidthOf(d), step = Math.Max(1, width - world.Rules.Sim.Dragon.OverlapChunks);
            int at = Math.Min(d.Pass * step, rows - width);
            return d.FromFar ? rows - width - at : at;
        }

        // This pass runs toward 0: passes go there and back.
        static bool Backward(DragonState d) => d.Back ^ (d.Pass % 2 == 1);

        /// <summary>
        /// A pass's line, for the view to fly: the middle of its strip, in cells across the passes, and whether it runs
        /// toward 0. A sum on the world's size and the flight's first flags, changing nothing.
        /// </summary>
        public (int middle, bool back) PassLine(DragonState flight, int pass)
        {
            var d = new DragonState { AlongX = flight.AlongX, FromFar = flight.FromFar, Back = flight.Back, Pass = pass };
            return ((StripOf(d) * 2 + WidthOf(d)) * Chunks.Size / 2, Backward(d));
        }

        // Whether the breath is out: on a pass, not wheeling round, and started.
        static bool Breathing(DragonState d) => d != null && d.Phase == DragonPhase.Passes && d.Until == 0 && d.Travelled > 0;

        /// <summary>
        /// The cells under the breath this tick, [x0, x1) × [y0, y1), clipped to the world (false when it isn't out):
        /// a strip of chunks across, Depth cells along, up to its lead (the view draws it).
        /// </summary>
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

        /// <summary>
        /// Whether the breath catches what stands at (x, y) this tick: it's in the breath's cells and not sheltered.
        /// The core's raiders take it from here; the view asks for its wizards, whose health is their own (WizardDown).
        /// </summary>
        public bool CaughtByBreath(int x, int y) => CaughtInBox(world.FoldX(x), world.FoldY(y));

        bool CaughtInBox(int x, int y) =>
            BreathBox(out int x0, out int y0, out int x1, out int y1) && x >= x0 && x < x1 && y >= y0 && y < y1 && !ShelteredFromBreath(x, y);

        /// <summary>
        /// The shelter rule: water at least ShelterWater deep (not frozen) on the cell, or a wall on the breath's side:
        /// a blocking cell of WallTerrains within WallReach cells straight toward where the dragon flies from.
        /// </summary>
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

        // As it dives every fire dies, on all the chunks at once: a wildfire left burning would walk back over the
        // chunks already reset (a burning valley of grass ended a third burnt without this). One tick, cheaply: it's a test on each cell.
        void SnuffFires()
        {
            world.WriteEveryCell(c =>
            {
                if (c.Fire == 0) return;
                c.Fire = 0;
                c.Fuel = 0;
            });
        }

        // Everyone the breath catches goes down (sim.json downed), unless they already are.
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

        // The next chunk the breath has reached on this pass, reset; false if there's none yet.
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

        // The chunks of this pass the breath's lead has reached, column by column along the pass.
        int ChunksReached(DragonState d)
        {
            int along = AlongOf(d), lead = Math.Min(d.Travelled, along);
            if (lead <= 0) return 0;
            int cols = Backward(d) ? ColsOf(d) - Chunks.ChunkOf(along - lead) : Chunks.ChunkOf(lead - 1) + 1;
            return cols * WidthOf(d);
        }

        // A pass ends when its breath is off the far edge and all its chunks are done: the next, or leaving.
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


        /// <summary>The dragon's state as text ("" for a world with no starting state), the same text as dragonState in the TS.</summary>
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

        /// <summary>A short fingerprint of DragonText, for the netcode checksum.</summary>
        public string Hash() => Fnv1a.Hash(Text());
    }
}
