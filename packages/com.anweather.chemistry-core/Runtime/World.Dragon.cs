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
// (DragonSystem.cs, architecture-review.md step 9); this file keeps what the world is put back to and how: the
// starting state, the audit, a chunk's reset (Freshen), the tick's dragon stage that runs them, and the save.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class DragonParams
    {
        public int WarningTicks = 180, TurnTicks = 24, LeavingTicks = 120;
        public int Speed = 6, Depth = 12, BreathChunks = 3, OverlapChunks = 1;
        public int ChunksPerTick = 1;
        public int Budget;
        public int ShelterWater = 8;
        public int WallReach = 2;
        public IReadOnlyList<string> WallTerrains = new[] { "stone", "rock" };
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

    public enum DragonPhase { None, Warning, Passes, Leaving }

    public readonly struct DragonCommand { }

    public sealed class DragonState
    {
        public DragonPhase Phase;
        public int Until;
        public bool AlongX, FromFar, Back;
        public int Pass, Passes;
        public int Travelled, Done;
    }

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
        public const string DownedStatus = "downed";

        IChunkSource startSource;
        readonly Cell startScratch = new Cell(), startPeek = new Cell();
        Chunk[] auditChunks;
        int auditAt;

        internal int AuditAt => auditAt;

        public int ChangedCount { get; private set; }

        public bool HasStart => startSource != null;

        public int StartShapes => startSource == null ? 0 : ChunksInOrder.Sum(ch => ch.Start.Count);

        public long StartBytes => startSource == null ? 0 : ChunksInOrder.Sum(ch => ch.Start.Bytes);

        public event Action<ChunkFresh> ChunkFreshened;

        public void KeepStart(IChunkSource source = null)
        {
            FinishTick();
            startSource = source ?? Source;
            foreach (var ch in ChunksInOrder)
                ch.Start = source != null ? PackedChunk.Empty(ch.X, ch.Y) : ch.Packed ?? Changes(ch, ch.Held);
            KeepModesStart();
            auditChunks = SortedChunks(false).ToArray();
            auditAt = 0;
            ChangedCount = 0;
            foreach (var ch in auditChunks)
            {
                ch.Changed = 0;
                Recount(ch);
            }
        }

        ChunkReader StartOf(Chunk ch, Cell scratch, int from = 0) => new ChunkReader(this, ch, ch.Start, startSource, scratch, from);

        Cell StartCell(int x, int y)
        {
            var ch = FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
            int i = LocalIndex(x, y);
            return StartOf(ch, startPeek, i).At(i);
        }

        Cell StartCell(int index) => StartCell(index % W, index / W);

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

        internal void Freshen(Chunk ch)
        {
            if (ch.Packed != null) UnpackChunk(ch);
            var start = StartOf(ch, startScratch);
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = ch.CellAt(i);
                if (c == null) continue;
                c.CopyFrom(start.At(i));
                c.ForgetDef();
                Sanitize(c);
            }
            Fitted(ch);
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
            ResetModesChunk(ch);
            ChunkFreshened?.Invoke(new ChunkFresh(ch.X, ch.Y));
        }

        bool InChunk(Chunk ch, int index) => Chunks.ChunkOf(index % W) == ch.X && Chunks.ChunkOf(index / W) == ch.Y;

        void FreshenMachines(Chunk ch)
        {
            foreach (int m in machines.Keys.Where(k => InChunk(ch, k)).ToList()) machines.Remove(m);
            foreach (var l in gateLinks)
                if (InChunk(ch, l.Gate)) l.Open = StartCell(l.Gate).T == "gate_open";
        }

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

        bool Differs(Cell c, Cell start) =>
            c.Height != start.Height || (c.T != start.T && (TerrainOf(c).DriesTo ?? c.T) != start.T)
            || (c.Cover != start.Cover && LastingCover(c) != LastingCover(start));

        string LastingCover(Cell c) => CoverOf(c)?.MeltWater > 0 ? null : c.Cover;

        void SaveDragon(JObject save)
        {
            if (startSource == null) return;
            var start = new JObject();
            SaveModesStart(start);
            save["start"] = start;
            SaveStarts(start);
            save["audit"] = new JArray(DragonSystem.Flights, auditAt);
            var d = DragonSystem.State;
            if (d != null) save["dragon"] = new JArray((int)d.Phase, d.Until, d.AlongX, d.FromFar, d.Back, d.Pass, d.Passes, d.Travelled, d.Done);
        }

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

        void LoadStart(JObject s)
        {
            if (!(s["start"] is JObject)) return;
            KeepStart();
            DragonSystem.Flights = (int)s["audit"][0];
            auditAt = (int)s["audit"][1];
            if (!(s["dragon"] is JArray a)) return;
            DragonSystem.State = new DragonState { Phase = (DragonPhase)(int)a[0], Until = (int)a[1], AlongX = (bool)a[2], FromFar = (bool)a[3], Back = (bool)a[4], Pass = (int)a[5], Passes = (int)a[6], Travelled = (int)a[7], Done = (int)a[8] };
        }

        void TotalChanged()
        {
            ChangedCount = 0;
            foreach (var ch in ChunksInOrder) ChangedCount += ch.Changed;
        }
    }
}
