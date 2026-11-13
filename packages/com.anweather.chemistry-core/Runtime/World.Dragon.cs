// The Dragon (docs/BACKLOG.md 26): the world reset as an event. A DragonCommand,
// or the changed-cell budget a mod set passed (the rules' 0 is never, J-073), starts a flight: a warning, long passes
// over the valley, then leaving. Each pass sweeps a strip of chunks, and every
// chunk its breath reaches goes back to the starting state, a few a tick, in
// the tick's own stage (TickStage.Dragon), so StepSome can stop between them.
// Whoever the breath catches is downed unless sheltered (CaughtByBreath).
//
// The path comes from the seed and the flight's number through Sky.Hash, never
// from the world's random numbers, so a world keeps its RNG sequence whether
// the dragon comes or not. Port of sandbox/src/dragon.ts, line for line; the
// sandbox has no chunk store, so there a chunk is just its 64 × 64 cells, and
// all are active.
//
// The starting state (KeepStart) is each chunk's cells as the world was built, held
// as a packed chunk's are (World.Pack.cs): its changes against the starting state's
// source, which for most chunks of a big map are none. The changed-cell count
// is an audit, not a write barrier: cells are written from all over the core, so
// each tick one active chunk is compared with the starting state (the next in
// row-major order), and the world's total is the sum of what each chunk last
// counted. A chunk that isn't active can't change, so its count stands.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>sim.json's "dragon": the flight's timing and shape, the changed-cell budget and the shelter rule.</summary>
    public sealed class DragonParams
    {
        /// <summary>Ticks of warning, of wheeling round between passes, and of leaving after the last.</summary>
        public int WarningTicks = 180, TurnTicks = 24, LeavingTicks = 120;
        /// <summary>Cells the breath goes a tick; how deep it is along the pass; chunks across a pass, and how many of those the next pass sweeps again.</summary>
        public int Speed = 6, Depth = 12, BreathChunks = 3, OverlapChunks = 1;
        /// <summary>Chunks reset at most in one tick.</summary>
        public int ChunksPerTick = 1;
        /// <summary>Cells that may differ from the starting state before the dragon comes by itself; 0: never.</summary>
        public int Budget;
        /// <summary>Water units from which a cell's water (not frozen) hides what's in it from the breath.</summary>
        public int ShelterWater = 8;
        /// <summary>Cells from a creature, toward the dragon, in which a wall of WallTerrains shelters it.</summary>
        public int WallReach = 2;
        /// <summary>The terrains that shelter when they block: a wall of these stops the breath.</summary>
        public IReadOnlyList<string> WallTerrains = new[] { "stone", "rock" };
        /// <summary>How big the dragon is drawn, against its model's 70 m wingspan. The view's alone: nothing in the core reads it, and no reach follows it.</summary>
        public double Scale = 1.5;

        internal static DragonParams Parse(JToken t)
        {
            var d = new DragonParams();
            if (!(t is JObject o)) return d;
            d.WarningTicks = (int?)o["warningTicks"] ?? d.WarningTicks;
            d.TurnTicks = (int?)o["turnTicks"] ?? d.TurnTicks;
            d.LeavingTicks = (int?)o["leavingTicks"] ?? d.LeavingTicks;
            d.Speed = Math.Max(1, (int?)o["speed"] ?? d.Speed);
            d.Depth = Math.Max(1, (int?)o["depth"] ?? d.Depth);
            d.BreathChunks = Math.Max(1, (int?)o["breathChunks"] ?? d.BreathChunks);
            d.OverlapChunks = (int?)o["overlapChunks"] ?? d.OverlapChunks;
            d.ChunksPerTick = Math.Max(1, (int?)o["chunksPerTick"] ?? d.ChunksPerTick);
            d.Budget = (int?)o["budget"] ?? d.Budget;
            d.ShelterWater = (int?)o["shelterWater"] ?? d.ShelterWater;
            d.WallReach = (int?)o["wallReach"] ?? d.WallReach;
            if (o["wallTerrains"] is JArray a) d.WallTerrains = a.Select(x => (string)x).ToList();
            d.Scale = (double?)o["scale"] ?? d.Scale;
            return d;
        }
    }

    /// <summary>Where a flight is: warning, flying its passes, leaving. None: no dragon.</summary>
    public enum DragonPhase { None, Warning, Passes, Leaving }

    /// <summary>Summons the dragon, if the world has a starting state and none is flying. Stamped like a cast (TickBatch.Dragon).</summary>
    public readonly struct DragonCommand { }

    /// <summary>A flight under way (dragon in the TS). Whole numbers and flags, in the checksum and the save.</summary>
    public sealed class DragonState
    {
        public DragonPhase Phase;
        /// <summary>The tick the warning or the leaving ends, or the wheeling round between passes; 0 while breathing.</summary>
        public int Until;
        /// <summary>From the seed: passes run east-west (else north-south), strips come from the far side, the first pass runs toward 0.</summary>
        public bool AlongX, FromFar, Back;
        /// <summary>This pass, and how many there are.</summary>
        public int Pass, Passes;
        /// <summary>Cells the breath's lead has come on this pass, and how many of this pass's chunks are reset.</summary>
        public int Travelled, Done;
    }

    /// <summary>A chunk the breath has put back as it started, so the view redraws it and brings its things back.</summary>
    public readonly struct ChunkFresh
    {
        public ChunkFresh(int cx, int cy)
        {
            CX = cx;
            CY = cy;
        }

        public int CX { get; }
        public int CY { get; }
    }

    public sealed partial class World
    {
        /// <summary>What the breath's status on a creature is called: it stays down this many ticks (sim.json downed).</summary>
        public const string DownedStatus = "downed";

        const uint PathSalt = 17;

        // The starting state: what each chunk's cells are against (each chunk's Start holds its changes), null when none
        // is kept, and the ball as built. The scratch cells its reads make a start cell in.
        IChunkSource startSource;
        GolfState startGolf;
        readonly Cell startScratch = new Cell(), startPeek = new Cell();
        // The chunks by row then column, and the next of them the audit looks at.
        Chunk[] auditChunks;
        int auditAt;

        /// <summary>The flight under way, or null.</summary>
        public DragonState Dragon { get; private set; }

        /// <summary>Flights begun in this world (the next one's path comes from it).</summary>
        public int DragonFlights { get; private set; }

        /// <summary>Cells that differed from the starting state when their chunks were last audited.</summary>
        public int ChangedCount { get; private set; }

        /// <summary>Whether the world knows its starting state (KeepStart), without which no dragon comes.</summary>
        public bool HasStart => startSource != null;

        /// <summary>The cells the starting state holds apart from its source (each chunk's changes), for the memory it takes.</summary>
        public int StartShapes => startSource == null ? 0 : ChunksInOrder.Sum(ch => ch.Start.Count);

        /// <summary>What the starting state holds, its chunks' changes, in bytes (those a packed chunk shares with it counted too).</summary>
        public long StartBytes => startSource == null ? 0 : ChunksInOrder.Sum(ch => ch.Start.Bytes);

        /// <summary>Raised for each chunk reset, in the tick's dragon stage. For the view: a handler must not change the world.</summary>
        public event Action<ChunkFresh> ChunkFreshened;

        /// <summary>
        /// Takes the world as it stands (or, given a source, as the source makes it) as its starting state, for the
        /// dragon to put back. Between ticks, once the world is built; every peer builds the same one, so
        /// every peer keeps the same one. A world that never calls this behaves as if there were no dragon.
        /// </summary>
        public void KeepStart(IChunkSource source = null)
        {
            FinishTick();
            // Given a source, every chunk is as it makes it; else a packed chunk's changes are its start as they stand (a
            // PackedChunk never changes), and an unpacked chunk's are diffed from its block.
            startSource = source ?? Source;
            foreach (var ch in ChunksInOrder)
                ch.Start = source != null ? PackedChunk.Empty(ch.X, ch.Y) : ch.Packed ?? Changes(ch, ch.Held);
            startGolf = Golf?.Copy();
            auditChunks = SortedChunks(false).ToArray();
            auditAt = 0;
            ChangedCount = 0;
            foreach (var ch in auditChunks)
            {
                ch.Changed = 0;
                Recount(ch);
            }
        }

        // A chunk's starting cells, read in ascending local index (null outside the world's extent).
        ChunkReader StartOf(Chunk ch, Cell scratch, int from = 0) => new ChunkReader(this, ch, ch.Start, startSource, scratch, from);

        // The starting cell at (x, y) on the world, for reading only, until the next StartCell.
        Cell StartCell(int x, int y)
        {
            var ch = FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
            int i = LocalIndex(x, y);
            return StartOf(ch, startPeek, i).At(i);
        }

        Cell StartCell(int index) => StartCell(index % W, index / W);

        /// <summary>
        /// A chunk's starting terrain, cell by cell in local index (row by row in the chunk), to <paramref name="each"/>;
        /// none outside the world's extent. Without a start kept, the cells as they are. A view reads what the world began
        /// as here, not its cells now: a world loaded from a save (a late joiner's) has its changes in already. A cell a
        /// mod laid or built on, whose start follows it (so the dragon keeps it), gives what stood there before.
        /// </summary>
        public void StartTerrainOf(int cx, int cy, Action<int, string> each)
        {
            if (each == null) throw new ArgumentNullException(nameof(each));
            var ch = FindChunk(cx, cy);
            if (ch == null) return;
            var reader = startSource != null ? StartOf(ch, new Cell()) : new ChunkReader(this, ch, new Cell());
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = reader.At(i);
                if (c == null) continue;
                int idx = Idx(CellX(ch, i), CellY(ch, i));
                string terrain = c.T;
                if (underLaid.TryGetValue(idx, out var laid)) terrain = laid.terrain;
                else if (underBuildings.TryGetValue(idx, out var built)) terrain = built.terrain;
                each(i, terrain);
            }
        }

        /// <summary>The dragon's summons (a Reset): the warning starts now, unless one is flying or the world keeps no starting state.</summary>
        public void ApplyDragon(DragonCommand command)
        {
            FinishTick(); // commands land between whole ticks (StepSome)
            if (startSource == null)
            {
                AddLog("Nothing answers: this world keeps no starting state");
                return;
            }
            if (Dragon != null)
            {
                AddLog("The dragon is already coming");
                return;
            }
            Summon();
        }

        void Summon()
        {
            var p = Rules.Sim.Dragon;
            uint h = Sky.Hash(Seed, DragonFlights, PathSalt);
            DragonFlights++;
            var d = new DragonState { Phase = DragonPhase.Warning, Until = Tick + p.WarningTicks, AlongX = (h >> 31) == 1, FromFar = ((h >> 30) & 1) == 1, Back = ((h >> 29) & 1) == 1 };
            Dragon = d;
            int rows = RowsOf(d), width = Math.Min(p.BreathChunks, rows), step = Math.Max(1, width - p.OverlapChunks);
            d.Passes = 1 + (rows - width + step - 1) / step;
            AddLog("The sky darkens: a shadow crosses the valley", LogKind.Notable);
        }

        // ---------- the flight's shape: pure sums on the world's size and the state ----------

        // Cells along a pass, and chunks along and across it.
        int AlongOf(DragonState d) => d.AlongX ? W : H;
        int AcrossOf(DragonState d) => d.AlongX ? H : W;
        int ColsOf(DragonState d) => Chunks.ChunkOf(AlongOf(d) - 1) + 1;
        int RowsOf(DragonState d) => Chunks.ChunkOf(AcrossOf(d) - 1) + 1;
        int WidthOf(DragonState d) => Math.Min(Rules.Sim.Dragon.BreathChunks, RowsOf(d));

        // The first chunk row of a pass's strip.
        int StripOf(DragonState d)
        {
            int rows = RowsOf(d), width = WidthOf(d), step = Math.Max(1, width - Rules.Sim.Dragon.OverlapChunks);
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
            var d = Dragon;
            if (!Breathing(d)) return false;
            int p = Rules.Sim.Dragon.Depth, along = AlongOf(d), across = AcrossOf(d);
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
        public bool CaughtByBreath(int x, int y) => CaughtInBox(FoldX(x), y);

        bool CaughtInBox(int x, int y) =>
            BreathBox(out int x0, out int y0, out int x1, out int y1) && x >= x0 && x < x1 && y >= y0 && y < y1 && !ShelteredFromBreath(x, y);

        /// <summary>
        /// The shelter rule: water at least ShelterWater deep (not frozen) on the cell, or a wall on the breath's side:
        /// a blocking cell of WallTerrains within WallReach cells straight toward where the dragon flies from.
        /// </summary>
        public bool ShelteredFromBreath(int x, int y)
        {
            var p = Rules.Sim.Dragon;
            var c = At(x, y);
            if (c == null) return false;
            if (DrownDepth(c) >= p.ShelterWater) return true;
            var d = Dragon;
            if (d == null) return false;
            int back = Backward(d) ? 1 : -1;
            for (int k = 1; k <= p.WallReach; k++)
            {
                var n = d.AlongX ? At(x + back * k, y) : At(x, y + back * k);
                if (n != null && IsBlocking(n) && p.WallTerrains.Contains(n.T)) return true;
            }
            return false;
        }

        // ---------- the tick's dragon stage ----------

        // One slice of the stage: the flight first, then a chunk a slice, then the audit. True when it's done.
        bool RunDragon()
        {
            if (stagePos == 0)
            {
                LandFloating();
                if (startSource == null) return true;
                FlyDragon();
                stagePos = 1;
                return false;
            }
            if (stagePos <= Rules.Sim.Dragon.ChunksPerTick && FreshenNext())
            {
                stagePos++;
                return false;
            }
            EndPass();
            AuditNext();
            int budget = Rules.Sim.Dragon.Budget;
            if (Dragon == null && budget > 0 && ChangedCount > budget) Summon();
            return true;
        }

        void FlyDragon()
        {
            var d = Dragon;
            if (d == null) return;
            var p = Rules.Sim.Dragon;
            switch (d.Phase)
            {
                case DragonPhase.Warning:
                    if (Tick < d.Until) return;
                    d.Phase = DragonPhase.Passes;
                    d.Until = 0;
                    AddLog("The dragon dives out of the clouds, breathing fire", LogKind.Notable);
                    SnuffFires();
                    break;
                case DragonPhase.Passes:
                    if (d.Until > 0)
                    {
                        if (Tick >= d.Until) d.Until = 0;
                        return;
                    }
                    d.Travelled = Math.Min(d.Travelled + p.Speed, AlongOf(d) + p.Depth);
                    KnockDown();
                    break;
                default:
                    if (Tick < d.Until) return;
                    Dragon = null;
                    ForgetLandStamps();
                    AddLog("The dragon wheels away: the valley is fresh", LogKind.Notable);
                    break;
            }
        }

        // As it dives every fire dies, on all the chunks at once: a wildfire left burning would walk back over the
        // chunks already reset (a burning valley of grass ended a third burnt without this). One tick, cheaply: it's a test on each cell.
        void SnuffFires()
        {
            WriteEveryCell(c =>
            {
                if (c.Fire == 0) return;
                c.Fire = 0;
                c.Fuel = 0;
            });
        }

        // Everyone the breath catches goes down (sim.json downed), unless they already are.
        void KnockDown()
        {
            int ticks = DownedTicks(Rules.Sim);
            foreach (var r in raiders)
            {
                if (r.StatusOf(DownedStatus) > 0 || !CaughtByBreath(r.X, r.Y)) continue;
                r.Status[DownedStatus] = ticks;
                LogOnce("dragon-downed", "The dragon's breath knocks them down", LogKind.Hurt);
            }
        }

        static int DownedTicks(SimParams s) => (int)Math.Floor(s.Downed.Seconds * s.TicksPerSecond);

        // The next chunk the breath has reached on this pass, reset; false if there's none yet.
        bool FreshenNext()
        {
            var d = Dragon;
            if (d == null || d.Phase != DragonPhase.Passes || d.Until != 0 || d.Done >= ChunksReached(d)) return false;
            int width = WidthOf(d), col = d.Done / width, row = StripOf(d) + d.Done % width;
            if (Backward(d)) col = ColsOf(d) - 1 - col;
            var ch = FindChunk(d.AlongX ? col : row, d.AlongX ? row : col);
            d.Done++;
            Freshen(ch);
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
        void EndPass()
        {
            var d = Dragon;
            if (d == null || d.Phase != DragonPhase.Passes || d.Until != 0) return;
            var p = Rules.Sim.Dragon;
            if (d.Travelled < AlongOf(d) + p.Depth || d.Done < ColsOf(d) * WidthOf(d)) return;
            if (d.Pass + 1 < d.Passes)
            {
                d.Pass++;
                d.Travelled = 0;
                d.Done = 0;
                d.Until = Tick + Math.Max(1, p.TurnTicks);
                return;
            }
            d.Phase = DragonPhase.Leaving;
            d.Until = Tick + Math.Max(1, p.LeavingTicks);
            d.Travelled = 0;
            AddLog("The dragon lands on the mountain and roars", LogKind.Notable);
        }

        // A chunk back as it started: its cells, active or not (like map building), what's kept beside
        // them, its machines and gates, its cracked things, and the ball if its tee is here. An
        // inactive chunk forgets the ticks it missed, so it doesn't catch up on what the breath wiped.
        void Freshen(Chunk ch)
        {
            if (ch.Packed != null) UnpackChunk(ch); // a write, not a read: it packs again once its hold is up (World.Pack.cs)
            var start = StartOf(ch, startScratch);
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = ch.CellAt(i);
                if (c == null) continue;
                c.CopyFrom(start.At(i));
                c.ForgetDef();
                Sanitize(c);
            }
            Fitted(ch); // its cells fit the rules in force now (World.Fit.cs)
            Array.Clear(ch.FireGeneration, 0, ch.FireGeneration.Length);
            Array.Clear(ch.Marks, 0, ch.Marks.Length);
            ch.LastLive = NeverLive;
            for (int d = 0; d < 4; d++) ch.EdgeLive[d] = NeverLive;
            ch.StoppedAt = NeverLive;
            ch.CaughtUp = 0;
            ch.SnapValid = false;
            ChangedCount -= ch.Changed;
            ch.Changed = 0;
            FreshenMachines(ch);
            crackedThings.RemoveWhere(t => new ThingId(t).ChunkX == ch.X && new ThingId(t).ChunkY == ch.Y);
            var tee = Golf ?? startGolf;
            if (tee != null && Chunks.ChunkOf(tee.TeeX) == ch.X && Chunks.ChunkOf(tee.TeeY) == ch.Y) Golf = startGolf?.Copy();
            ChunkFreshened?.Invoke(new ChunkFresh(ch.X, ch.Y));
        }

        bool InChunk(Chunk ch, int index) => Chunks.ChunkOf(index % W) == ch.X && Chunks.ChunkOf(index / W) == ch.Y;

        // Its machines forget their pressure, and its linked gates are as built.
        void FreshenMachines(Chunk ch)
        {
            foreach (int m in machines.Keys.Where(k => InChunk(ch, k)).ToList()) machines.Remove(m);
            foreach (var l in gateLinks)
                if (InChunk(ch, l.Gate)) l.Open = StartCell(l.Gate).T == "gate_open";
        }

        // ---------- the changed-cell count ----------

        // The next active chunk's count, afresh: at most one chunk a tick, however many have changed.
        void AuditNext()
        {
            for (int k = 0; k < auditChunks.Length; k++)
            {
                var ch = auditChunks[auditAt];
                auditAt = (auditAt + 1) % auditChunks.Length;
                if (!ch.Active) continue;
                Recount(ch);
                return;
            }
        }

        void Recount(Chunk ch)
        {
            int count = 0;
            // A packed chunk whose changes are its start's, against the same source, is its start: none differ, read or not.
            if (ch.Packed != null && ch.Packed == ch.Start && startSource == Source)
            {
                ChangedCount -= ch.Changed;
                ch.Changed = 0;
                return;
            }
            var read = new ChunkReader(this, ch, walkScratch);
            var start = StartOf(ch, startScratch);
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = read.At(i);
                if (c != null && Differs(c, start.At(i))) count++;
            }
            ChangedCount += count - ch.Changed;
            ch.Changed = count;
        }

        // What lasts: the ground, its height and its cover. What goes by itself doesn't count: fire, gas, charge and timers,
        // but also water, ice, snow and mud that will dry, or the weather alone would bring the dragon (rain wets 40% of
        // the cells and muddies the dirt, snow lies on all of them).
        bool Differs(Cell c, Cell start) =>
            c.Height != start.Height || (c.T != start.T && (TerrainOf(c).DriesTo ?? c.T) != start.T)
            || (c.Cover != start.Cover && LastingCover(c) != LastingCover(start));

        string LastingCover(Cell c) => CoverOf(c)?.MeltWater > 0 ? null : c.Cover;

        // ---------- checksum and save ----------

        /// <summary>The dragon's state as text ("" for a world with no starting state), the same text as dragonState in the TS.</summary>
        public string DragonText()
        {
            if (startSource == null) return "";
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(DragonFlights.ToString(inv)).Append(':').Append(auditAt.ToString(inv)).Append(':').Append(ChangedCount.ToString(inv));
            var d = Dragon;
            if (d != null)
                sb.Append('|').Append(string.Join(":", new[] { (int)d.Phase, d.Until, d.AlongX ? 1 : 0, d.FromFar ? 1 : 0, d.Back ? 1 : 0, d.Pass, d.Passes, d.Travelled, d.Done }.Select(n => n.ToString(inv))));
            return sb.ToString();
        }

        /// <summary>A short fingerprint of DragonText, for the netcode checksum.</summary>
        public string DragonHash() => Fnv1a.Hash(DragonText());

        // Only for a world that keeps a starting state, so other saves read as they did.
        void SaveDragon(JObject save)
        {
            if (startSource == null) return;
            var start = new JObject();
            if (startGolf != null) start["golf"] = GolfRow(startGolf);
            save["start"] = start;
            SaveStarts(start);
            save["audit"] = new JArray(DragonFlights, auditAt);
            var d = Dragon;
            if (d != null) save["dragon"] = new JArray((int)d.Phase, d.Until, d.AlongX, d.FromFar, d.Back, d.Pass, d.Passes, d.Travelled, d.Done);
        }

        // The start's own cells, where they aren't what its source makes: what the map laid before KeepStart (puzzles, map
        // stamps, rows) and what mods' buildings put in since, by chunk, in SavePacked's form. A load can't make them again
        // from the source alone (late-join-snapshot, question 8: the save keeps them). None when the start is the source's,
        // so those saves read as they did.
        void SaveStarts(JObject start)
        {
            var chunksOut = new JArray();
            Cell c = new Cell(), made = new Cell();
            foreach (var ch in SortedChunks(false))
            {
                var p = ch.Start;
                if (p == null || (p.Count == 0 && p.FireAt.Length == 0)) continue;
                var o = new JObject { ["x"] = ch.X, ["y"] = ch.Y };
                var cells = new JArray();
                for (int k = 0; k < p.Count; k++)
                {
                    int i = p.At[k], x = CellX(ch, i), y = CellY(ch, i);
                    made.Reset();
                    startSource.Make(x, y, made);
                    c.Reset();
                    startSource.Make(x, y, c);
                    LayPacked(p, k, c);
                    cells.Add(SaveCell(i, c, LiquidsOf(ch)));
                }
                if (cells.Count > 0) o["cells"] = cells;
                if (p.FireAt.Length > 0) o["fireGen"] = new JArray(p.FireAt.Select((i, f) => new JArray(i, p.FireGen[f])));
                chunksOut.Add(o);
            }
            if (chunksOut.Count > 0) start["chunks"] = chunksOut;
        }

        // Once the chunks are laid and fit: each chunk's start made from the save's record of it, one pass a chunk.
        void LoadStarts(JObject s)
        {
            if (!(s["start"]?["chunks"] is JArray saved)) return;
            Cell c = new Cell(), made = new Cell();
            foreach (var o in saved.OfType<JObject>())
            {
                var ch = FindChunk((int)o["x"], (int)o["y"]) ?? throw new SaveException($"This save doesn't fit: no chunk ({o["x"]}, {o["y"]}) for its start");
                var at = new List<int>();
                var cells = new List<PackedCell>();
                var rares = new List<PackedRare>();
                foreach (var a in (o["cells"] as JArray ?? new JArray()).OfType<JArray>())
                {
                    int i = (int)a[0];
                    made.Reset();
                    startSource.Make(CellX(ch, i), CellY(ch, i), made);
                    c.Reset();
                    LoadCell(c, a, LiquidsOf(ch));
                    at.Add(i);
                    cells.Add(PackCell(c, made, rares));
                }
                var fire = (o["fireGen"] as JArray ?? new JArray()).OfType<JArray>().ToList();
                ch.Start = new PackedChunk
                {
                    X = ch.X,
                    Y = ch.Y,
                    At = at.ToArray(),
                    Cells = cells.ToArray(),
                    Rares = rares.ToArray(),
                    FireAt = fire.Select(f => (int)f[0]).ToArray(),
                    FireGen = fire.Select(f => (int)f[1]).ToArray(),
                };
            }
        }

        // Before the save's cells are laid over: the world has just been made from the source, so it's the starting state
        // (each chunk's own start, where the map laid more, comes after the cells: LoadStarts).
        void LoadStart(JObject s)
        {
            if (!(s["start"] is JObject start)) return;
            KeepStart();
            startGolf = start["golf"] is JArray g ? ReadGolf(g) : null;
            DragonFlights = (int)s["audit"][0];
            auditAt = (int)s["audit"][1];
            if (!(s["dragon"] is JArray a)) return;
            Dragon = new DragonState { Phase = (DragonPhase)(int)a[0], Until = (int)a[1], AlongX = (bool)a[2], FromFar = (bool)a[3], Back = (bool)a[4], Pass = (int)a[5], Passes = (int)a[6], Travelled = (int)a[7], Done = (int)a[8] };
        }

        // Once the chunks have their saved counts.
        void TotalChanged()
        {
            ChangedCount = 0;
            foreach (var ch in ChunksInOrder) ChangedCount += ch.Changed;
        }
    }
}
