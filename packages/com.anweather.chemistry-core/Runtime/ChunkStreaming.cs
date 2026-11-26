// Which chunks are active, following the characters (docs/WORLD_STREAMING.md,
// "Part 2, second slice"): every chunk within a character's reach activates,
// and a chunk deactivates once no character is within a wider distance, so
// walking along a chunk edge doesn't activate and deactivate it every few
// steps. Reaches that overlap are one active region: a chunk is active once,
// whoever's reach it is in. The distances are sim.json's streaming (reach,
// keep). The plan is a list of switches that travel like any other command
// (TickBatch.Chunks): solo they apply before the next tick, online the host
// stamps them into its tick batches, so every peer switches the same chunks at
// the same tick. C# only: the sandbox has no chunks.
using System;
using System.Collections.Generic;

namespace Chem
{
    public readonly struct ChunkSwitch : IEquatable<ChunkSwitch>
    {
        public ChunkSwitch(int cx, int cy, bool active)
        {
            CX = cx;
            CY = cy;
            Active = active;
        }

        public int CX { get; }
        public int CY { get; }
        public bool Active { get; }

        public bool Equals(ChunkSwitch other) => CX == other.CX && CY == other.CY && Active == other.Active;
        public override bool Equals(object obj) => obj is ChunkSwitch s && Equals(s);
        public override int GetHashCode() => (CX * 397) ^ (CY * 31) ^ (Active ? 1 : 0);
        public override string ToString() => $"{CX},{CY} {(Active ? "activates" : "deactivates")}";
        public static bool operator ==(ChunkSwitch a, ChunkSwitch b) => a.Equals(b);
        public static bool operator !=(ChunkSwitch a, ChunkSwitch b) => !a.Equals(b);
    }

    public static class ChunkStreaming
    {
        public static List<ChunkSwitch> Plan(World world, IReadOnlyList<(int x, int y)> characters)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var s = world.Rules.Sim.Streaming;
            return Plan(world.ChunkCoords, world.IsActive, characters, s.Reach, s.Keep, world.WrapChunks, world.WrapChunksY);
        }

        public static List<ChunkSwitch> Plan(IReadOnlyList<(int cx, int cy)> chunks, Func<int, int, bool> isActive,
            IReadOnlyList<(int x, int y)> characters, int reach, int keep, int wrapChunks = 0, int wrapChunksY = 0)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            if (isActive == null) throw new ArgumentNullException(nameof(isActive));
            var plan = new List<ChunkSwitch>();
            if (characters == null || characters.Count == 0) return plan;
            foreach (var (cx, cy) in chunks)
            {
                int near = Nearest(characters, cx, cy, wrapChunks, wrapChunksY);
                bool active = isActive(cx, cy);
                if (!active && near <= reach) plan.Add(new ChunkSwitch(cx, cy, true));
                else if (active && near > keep) plan.Add(new ChunkSwitch(cx, cy, false));
            }
            return plan;
        }

        static int Nearest(IReadOnlyList<(int x, int y)> characters, int cx, int cy, int wrapChunks, int wrapChunksY)
        {
            int near = int.MaxValue;
            foreach (var (x, y) in characters)
            {
                int d = Math.Max(Math.Abs(World.ChunkDelta(Chunks.ChunkOf(x), cx, wrapChunks)), Math.Abs(World.ChunkDelta(Chunks.ChunkOf(y), cy, wrapChunksY)));
                if (d < near) near = d;
            }
            return near;
        }

        public static List<ChunkSwitch> StartPlan(World world, IReadOnlyList<(int x, int y)> characters)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return StartPlan(world.ChunkCoords, world.IsActive, characters, world.Rules.Sim.Streaming.Reach, world.WrapChunks, world.WrapChunksY);
        }

        public static List<ChunkSwitch> StartPlan(IReadOnlyList<(int cx, int cy)> chunks, Func<int, int, bool> isActive,
            IReadOnlyList<(int x, int y)> characters, int reach, int wrapChunks = 0, int wrapChunksY = 0)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            if (isActive == null) throw new ArgumentNullException(nameof(isActive));
            var plan = new List<ChunkSwitch>();
            if (characters == null || characters.Count == 0) return plan;
            foreach (var (cx, cy) in chunks)
                if (isActive(cx, cy) && Nearest(characters, cx, cy, wrapChunks, wrapChunksY) > reach) plan.Add(new ChunkSwitch(cx, cy, false));
            return plan;
        }

        public static void Apply(World world, IEnumerable<ChunkSwitch> switches)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            switches ??= Array.Empty<ChunkSwitch>();
            if (world.Source is IGeneratedSource)
            {
                var near = new List<(int cx, int cy)>();
                foreach (var s in switches)
                    if (s.Active && world.HasChunk(s.CX, s.CY) && !world.IsActive(s.CX, s.CY))
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (world.HasChunk(s.CX + dx, s.CY + dy)) near.Add((world.FoldChunkX(s.CX + dx), world.FoldChunkY(s.CY + dy)));
                world.MakeNow(near);
            }
            foreach (var s in switches)
                if (world.HasChunk(s.CX, s.CY)) world.SetActive(s.CX, s.CY, s.Active);
        }

        public static List<(int cx, int cy)> Within(IReadOnlyList<(int cx, int cy)> chunks, double x, double y, double radius, int wrapCells = 0, int wrapCellsY = 0)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            var near = new List<(int cx, int cy)>();
            foreach (var (cx, cy) in chunks)
            {
                double x0 = cx * Chunks.Size, y0 = cy * Chunks.Size;
                if (wrapCells > 0) x0 += wrapCells * Math.Round((x - (x0 + Chunks.Size / 2.0)) / wrapCells);
                if (wrapCellsY > 0) y0 += wrapCellsY * Math.Round((y - (y0 + Chunks.Size / 2.0)) / wrapCellsY);
                double dx = Math.Max(0, Math.Max(x0 - x, x - (x0 + Chunks.Size)));
                double dy = Math.Max(0, Math.Max(y0 - y, y - (y0 + Chunks.Size)));
                if (dx * dx + dy * dy <= radius * radius) near.Add((cx, cy));
            }
            near.Sort((a, b) => a.cy != b.cy ? a.cy.CompareTo(b.cy) : a.cx.CompareTo(b.cx));
            return near;
        }

        internal static int Distance(IReadOnlyList<(int x, int y)> characters, int cx, int cy, int wrapChunks, int wrapChunksY) => Nearest(characters, cx, cy, wrapChunks, wrapChunksY);
    }

    public sealed class ChunkPlanner
    {
        static readonly (int dx, int dy)[] Sides = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        readonly Dictionary<(int cx, int cy), int> lingering = new Dictionary<(int cx, int cy), int>();
        readonly HashSet<(int cx, int cy)> cutOff = new HashSet<(int cx, int cy)>();

        public IReadOnlyCollection<(int cx, int cy)> Lingering => lingering.Keys;

        public List<ChunkSwitch> Plan(World world, IReadOnlyList<(int x, int y)> characters)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return Plan(world, world.IsActive, characters, world.ActiveChunks);
        }

        public List<ChunkSwitch> Plan(World world, Func<int, int, bool> isActive, IReadOnlyList<(int x, int y)> characters) =>
            Plan(world, isActive, characters, null);

        public List<ChunkSwitch> Plan(World world, Func<int, int, bool> isActive, IReadOnlyList<(int x, int y)> characters, IEnumerable<(int cx, int cy)> activeNow)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (isActive == null) throw new ArgumentNullException(nameof(isActive));
            var plan = new List<ChunkSwitch>();
            if (characters == null || characters.Count == 0) return plan;
            var s = world.Rules.Sim.Streaming;
            var linger = lingerScratch;
            linger.Clear();
            var ahead = world.Source is IGeneratedSource ? aheadScratch : null;
            ahead?.Clear();
            var visit = activeNow == null ? world.ChunkCoords : Candidates(world, characters, activeNow, s.Keep + AheadChunks);
            foreach (var (cx, cy) in visit)
            {
                int near = ChunkStreaming.Distance(characters, cx, cy, world.WrapChunks, world.WrapChunksY);
                if (ahead != null && near <= s.Keep + AheadChunks && !world.IsActive(cx, cy)) ahead.Add((near, cy, cx));
                bool active = isActive(cx, cy);
                if (near <= s.Keep)
                {
                    if (WithinKeep(world, isActive, near <= s.Reach, cx, cy, active)) plan.Add(new ChunkSwitch(cx, cy, true));
                }
                else if (active ? Lingers(world, s, cx, cy) : Knocked(world, isActive, cx, cy)) linger.Add((near, cy, cx, active));
                else if (active) plan.Add(new ChunkSwitch(cx, cy, false));
            }
            Cap(linger, s.LingerChunks, world.Tick, plan);
            plan.Sort(ByRowThenColumn);
            if (ahead != null)
            {
                ahead.Sort();
                aheadCells.Clear();
                foreach (var a in ahead) aheadCells.Add((a.cx, a.cy));
                world.MakeAhead(aheadCells);
            }
            return plan;
        }

        static readonly Comparison<ChunkSwitch> ByRowThenColumn = (a, b) => a.CY != b.CY ? a.CY.CompareTo(b.CY) : a.CX.CompareTo(b.CX);

        readonly List<(int near, int cy, int cx, bool active)> lingerScratch = new List<(int near, int cy, int cx, bool active)>();
        readonly List<(int near, int cy, int cx)> aheadScratch = new List<(int near, int cy, int cx)>();
        readonly List<(int cx, int cy)> aheadCells = new List<(int cx, int cy)>();
        readonly List<(int cx, int cy)> candidates = new List<(int cx, int cy)>();
        readonly HashSet<long> seen = new HashSet<long>();

        List<(int cx, int cy)> Candidates(World world, IReadOnlyList<(int x, int y)> characters, IEnumerable<(int cx, int cy)> activeNow, int around)
        {
            candidates.Clear();
            seen.Clear();
            void Visit(int cx, int cy)
            {
                cx = world.FoldChunkX(cx);
                cy = world.FoldChunkY(cy);
                if (world.HasChunk(cx, cy) && seen.Add(Chunks.Key(cx, cy))) candidates.Add((cx, cy));
            }
            foreach (var (x, y) in characters)
            {
                int ccx = Chunks.ChunkOf(x), ccy = Chunks.ChunkOf(y);
                for (int dy = -around; dy <= around; dy++)
                    for (int dx = -around; dx <= around; dx++) Visit(ccx + dx, ccy + dy);
            }
            foreach (var (cx, cy) in activeNow)
            {
                Visit(cx, cy);
                for (int d = 0; d < 4; d++) Visit(cx + Sides[d].dx, cy + Sides[d].dy);
            }
            return candidates;
        }

        public const int AheadChunks = 2;

        bool WithinKeep(World world, Func<int, int, bool> isActive, bool inReach, int cx, int cy, bool active)
        {
            lingering.Remove((cx, cy));
            if (inReach) cutOff.Remove((cx, cy));
            return !active && (inReach || Knocked(world, isActive, cx, cy));
        }

        bool Lingers(World world, StreamingParams s, int cx, int cy)
        {
            if (world.IsSettled(cx, cy))
            {
                lingering.Remove((cx, cy));
                return false;
            }
            if (!lingering.TryGetValue((cx, cy), out int since)) return true;
            if (world.Tick - since < s.LingerTicks) return true;
            lingering.Remove((cx, cy));
            cutOff.Add((cx, cy));
            return false;
        }

        bool Knocked(World world, Func<int, int, bool> isActive, int cx, int cy)
        {
            if (cutOff.Contains((cx, cy))) return false;
            for (int d = 0; d < 4; d++)
            {
                int nx = world.FoldChunkX(cx + Sides[d].dx), ny = world.FoldChunkY(cy + Sides[d].dy);
                if (world.HasChunk(nx, ny) && isActive(nx, ny) && world.IsLiveAtEdge(nx, ny, (d + 2) % 4)) return true;
            }
            return false;
        }

        void Cap(List<(int near, int cy, int cx, bool active)> linger, int most, int tick, List<ChunkSwitch> plan)
        {
            linger.Sort();
            for (int k = 0; k < linger.Count; k++)
            {
                var (_, cy, cx, active) = linger[k];
                if (k < most)
                {
                    if (!lingering.ContainsKey((cx, cy))) lingering[(cx, cy)] = tick;
                    if (!active) plan.Add(new ChunkSwitch(cx, cy, true));
                    continue;
                }
                lingering.Remove((cx, cy));
                if (!active) continue;
                cutOff.Add((cx, cy));
                plan.Add(new ChunkSwitch(cx, cy, false));
            }
        }
    }
}
