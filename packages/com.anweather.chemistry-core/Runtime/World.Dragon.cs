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
//
// The flight itself (the summons, the passes and their breath, who it knocks down, its text) is DragonSystem
// (Play/DragonSystem.cs, architecture-review.md step 9); this file keeps what the world is put back to and how: the
// starting state, the audit, a chunk's reset (Freshen), the tick's dragon stage that runs them, and the save.
using System;
using System.Collections.Generic;
using System.Linq;
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

        // The starting state: what each chunk's cells are against (each chunk's Start holds its changes), null when none
        // is kept, and the ball as built. The scratch cells its reads make a start cell in.
        IChunkSource startSource;
        GolfState startGolf;
        readonly Cell startScratch = new Cell(), startPeek = new Cell();
        // The chunks by row then column, and the next of them the audit looks at.
        Chunk[] auditChunks;
        int auditAt;

        // The audit's place, for the dragon's text (DragonSystem.Text).
        internal int AuditAt => auditAt;

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

        // ---------- the tick's dragon stage ----------

        // One slice of the stage: the flight first, then a chunk a slice, then the audit. True when it's done.
        bool RunDragon()
        {
            if (stagePos == 0)
            {
                LandFloating();
                if (startSource == null) return true;
                DragonSystem.Fly();
                stagePos = 1;
                return false;
            }
            if (stagePos <= Rules.Sim.Dragon.ChunksPerTick && DragonSystem.FreshenNext())
            {
                stagePos++;
                return false;
            }
            DragonSystem.EndPass();
            AuditNext();
            int budget = Rules.Sim.Dragon.Budget;
            if (DragonSystem.State == null && budget > 0 && ChangedCount > budget) DragonSystem.Summon();
            return true;
        }

        // A chunk back as it started: its cells, active or not (like map building), what's kept beside
        // them, its machines and gates, its cracked things, and the ball if its tee is here. An
        // inactive chunk forgets the ticks it missed, so it doesn't catch up on what the breath wiped.
        internal void Freshen(Chunk ch)
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
            if (tee != null && Chunks.ChunkOf(tee.TeeX) == ch.X && Chunks.ChunkOf(tee.TeeY) == ch.Y) GolfSystem.State = startGolf?.Copy();
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

        // Only for a world that keeps a starting state, so other saves read as they did.
        void SaveDragon(JObject save)
        {
            if (startSource == null) return;
            var start = new JObject();
            if (startGolf != null) start["golf"] = GolfSystem.Row(startGolf);
            save["start"] = start;
            SaveStarts(start);
            save["audit"] = new JArray(DragonSystem.Flights, auditAt);
            var d = DragonSystem.State;
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
            startGolf = start["golf"] is JArray g ? GolfSystem.Read(g) : null;
            DragonSystem.Flights = (int)s["audit"][0];
            auditAt = (int)s["audit"][1];
            if (!(s["dragon"] is JArray a)) return;
            DragonSystem.State = new DragonState { Phase = (DragonPhase)(int)a[0], Until = (int)a[1], AlongX = (bool)a[2], FromFar = (bool)a[3], Back = (bool)a[4], Pass = (int)a[5], Passes = (int)a[6], Travelled = (int)a[7], Done = (int)a[8] };
        }

        // Once the chunks have their saved counts.
        void TotalChanged()
        {
            ChangedCount = 0;
            foreach (var ch in ChunksInOrder) ChangedCount += ch.Changed;
        }
    }
}
