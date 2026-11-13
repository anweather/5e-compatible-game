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
    /// <summary>A chunk activating (Active) or deactivating, in a tick batch.</summary>
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
        /// <summary>
        /// The switches that bring the world's chunks in line with the characters at these cells,
        /// at the world's rules' reach and keep distances (sim.json streaming).
        /// </summary>
        public static List<ChunkSwitch> Plan(World world, IReadOnlyList<(int x, int y)> characters)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var s = world.Rules.Sim.Streaming;
            return Plan(world.ChunkCoords, world.IsActive, characters, s.Reach, s.Keep, world.WrapChunks);
        }

        /// <summary>
        /// The switches against a set of chunks and which of them are active (for a host that plans
        /// ahead of its own world, whose stamped switches apply a tick or so later): inactive chunks
        /// within <paramref name="reach"/> chunks of a character activate, active ones further than
        /// <paramref name="keep"/> from every character deactivate. Distances are in chunks, the
        /// larger of the two axes. Ordered by row then column, so the plan is the same however the
        /// characters are listed. With no characters nothing changes. On a world <paramref name="wrapChunks"/> chunks
        /// round (World.WrapChunks; 0 for none) east-west distances go the short way round the seam.
        /// </summary>
        public static List<ChunkSwitch> Plan(IReadOnlyList<(int cx, int cy)> chunks, Func<int, int, bool> isActive,
            IReadOnlyList<(int x, int y)> characters, int reach, int keep, int wrapChunks = 0)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            if (isActive == null) throw new ArgumentNullException(nameof(isActive));
            var plan = new List<ChunkSwitch>();
            if (characters == null || characters.Count == 0) return plan;
            foreach (var (cx, cy) in chunks)
            {
                int near = Nearest(characters, cx, cy, wrapChunks);
                bool active = isActive(cx, cy);
                if (!active && near <= reach) plan.Add(new ChunkSwitch(cx, cy, true));
                else if (active && near > keep) plan.Add(new ChunkSwitch(cx, cy, false));
            }
            return plan;
        }

        // How many chunks the nearest character is from a chunk (the short way round a world wrapChunks round).
        static int Nearest(IReadOnlyList<(int x, int y)> characters, int cx, int cy, int wrapChunks)
        {
            int near = int.MaxValue;
            foreach (var (x, y) in characters)
            {
                int d = Math.Max(Math.Abs(World.ChunkDelta(Chunks.ChunkOf(x), cx, wrapChunks)), Math.Abs(Chunks.ChunkOf(y) - cy));
                if (d < near) near = d;
            }
            return near;
        }

        /// <summary>
        /// The switches that leave only the chunks within reach of these characters active, for a
        /// world just built (every chunk starts active): every active chunk further than the reach
        /// from every character deactivates, by row then column. The keep distance and lingering
        /// don't come in, since nothing has moved or burned yet. Every peer builds its world from
        /// the same map and start, so each applies the same switches before the first tick (#209).
        /// With no characters nothing changes.
        /// </summary>
        public static List<ChunkSwitch> StartPlan(World world, IReadOnlyList<(int x, int y)> characters)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return StartPlan(world.ChunkCoords, world.IsActive, characters, world.Rules.Sim.Streaming.Reach, world.WrapChunks);
        }

        /// <summary>StartPlan against a set of chunks and which are active (for a host planning a reset it hasn't run yet; wrapChunks as Plan's).</summary>
        public static List<ChunkSwitch> StartPlan(IReadOnlyList<(int cx, int cy)> chunks, Func<int, int, bool> isActive,
            IReadOnlyList<(int x, int y)> characters, int reach, int wrapChunks = 0)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            if (isActive == null) throw new ArgumentNullException(nameof(isActive));
            var plan = new List<ChunkSwitch>();
            if (characters == null || characters.Count == 0) return plan;
            foreach (var (cx, cy) in chunks)
                if (isActive(cx, cy) && Nearest(characters, cx, cy, wrapChunks) > reach) plan.Add(new ChunkSwitch(cx, cy, false));
            return plan;
        }

        /// <summary>Applies switches to the world (unknown chunks are skipped). Between ticks, the same on every peer.</summary>
        public static void Apply(World world, IEnumerable<ChunkSwitch> switches)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            switches ??= Array.Empty<ChunkSwitch>();
            // On a generated map, the chunks that activate and those round them (they unpack with it) made first on every
            // core, those not made ahead: the time it takes, never what's made.
            if (world.Source is IGeneratedSource)
            {
                var near = new List<(int cx, int cy)>();
                foreach (var s in switches)
                    if (s.Active && world.HasChunk(s.CX, s.CY) && !world.IsActive(s.CX, s.CY))
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (world.HasChunk(s.CX + dx, s.CY + dy)) near.Add((world.FoldChunkX(s.CX + dx), s.CY + dy));
                world.MakeNow(near);
            }
            foreach (var s in switches)
                if (world.HasChunk(s.CX, s.CY)) world.SetActive(s.CX, s.CY, s.Active);
        }

        /// <summary>
        /// The chunks any part of which lies within <paramref name="radius"/> cells of (x, y), a point
        /// in cell units (cell (0, 0) spans 0 to 1), by row then column. For what a view draws by
        /// distance from its camera, such as the terrains' grass (ChemGrid.DetailChunks): a chunk
        /// left out has no part near enough to draw any. On a world <paramref name="wrapCells"/> cells round (World.W
        /// when it wraps; 0 for none) a chunk is measured the short way round the seam.
        /// </summary>
        public static List<(int cx, int cy)> Within(IReadOnlyList<(int cx, int cy)> chunks, double x, double y, double radius, int wrapCells = 0)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            var near = new List<(int cx, int cy)>();
            foreach (var (cx, cy) in chunks)
            {
                double x0 = cx * Chunks.Size, y0 = cy * Chunks.Size;
                // The copy of the chunk nearest x: its middle within half a world of it.
                if (wrapCells > 0) x0 += wrapCells * Math.Round((x - (x0 + Chunks.Size / 2.0)) / wrapCells);
                double dx = Math.Max(0, Math.Max(x0 - x, x - (x0 + Chunks.Size)));
                double dy = Math.Max(0, Math.Max(y0 - y, y - (y0 + Chunks.Size)));
                if (dx * dx + dy * dy <= radius * radius) near.Add((cx, cy));
            }
            near.Sort((a, b) => a.cy != b.cy ? a.cy.CompareTo(b.cy) : a.cx.CompareTo(b.cx));
            return near;
        }

        // How many chunks the nearest character is from a chunk (for ChunkPlanner).
        internal static int Distance(IReadOnlyList<(int x, int y)> characters, int cx, int cy, int wrapChunks) => Nearest(characters, cx, cy, wrapChunks);
    }

    /// <summary>
    /// The plan with lingering (docs/WORLD_STREAMING.md, "Part 2, second slice"), kept by whoever
    /// plans (the host online, the grid solo): the reach activates and the keep distance holds as in
    /// ChunkStreaming.Plan, but a chunk that leaves every keep distance while something in it is
    /// live (World.IsSettled) stays active, lingering, until it settles. Fire, water or gas live on
    /// an active chunk's edge activates the inactive chunk across it, which lingers too: a fire
    /// walked away from burns on, and spreads, then burns out. Within caps (sim.json streaming):
    /// at most lingerChunks linger at once, the nearest to a character first, and none for more
    /// than lingerTicks. A chunk the caps let go while live is cut off: spread doesn't activate it
    /// again until a character's reach does. Only the switches are shared, so this state lives
    /// with the planner, not in the world.
    /// </summary>
    public sealed class ChunkPlanner
    {
        static readonly (int dx, int dy)[] Sides = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        // Lingering chunks and the tick each began to linger; chunks the caps cut off.
        readonly Dictionary<(int cx, int cy), int> lingering = new Dictionary<(int cx, int cy), int>();
        readonly HashSet<(int cx, int cy)> cutOff = new HashSet<(int cx, int cy)>();

        /// <summary>The chunks lingering now (active out of every keep distance), in no set order.</summary>
        public IReadOnlyCollection<(int cx, int cy)> Lingering => lingering.Keys;

        /// <summary>The plan against the world's own active chunks.</summary>
        public List<ChunkSwitch> Plan(World world, IReadOnlyList<(int x, int y)> characters)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return Plan(world, world.IsActive, characters, world.ActiveChunks);
        }

        /// <summary>The plan against which chunks are planned to be active, every chunk visited (Plan with activeNow null).</summary>
        public List<ChunkSwitch> Plan(World world, Func<int, int, bool> isActive, IReadOnlyList<(int x, int y)> characters) =>
            Plan(world, isActive, characters, null);

        /// <summary>
        /// The switches for these characters, against which chunks are (or are planned to be)
        /// active; whether chunks are live is read from the world. Ordered by row then column.
        /// With no characters nothing changes.
        /// </summary>
        /// <remarks>
        /// With <paramref name="activeNow"/>, every chunk <paramref name="isActive"/> says is active, it visits only the chunks
        /// a plan can touch (planner-reach; walking all 9,720 of the Scale Island's was 8-11 ms a tick): those within keep and
        /// AheadChunks of a character, and the active ones and the four round each, which is how spread comes in. Each chunk's
        /// part of the plan depends on that chunk alone, and the plan, the lingering and the chunks made ahead are sorted
        /// after, so it's the plan visiting every chunk makes. Without it (null), every chunk is visited.
        /// </remarks>
        public List<ChunkSwitch> Plan(World world, Func<int, int, bool> isActive, IReadOnlyList<(int x, int y)> characters, IEnumerable<(int cx, int cy)> activeNow)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (isActive == null) throw new ArgumentNullException(nameof(isActive));
            var plan = new List<ChunkSwitch>();
            if (characters == null || characters.Count == 0) return plan;
            var s = world.Rules.Sim.Streaming;
            // Lingering candidates: (distance, cy, cx, active now).
            var linger = lingerScratch;
            linger.Clear();
            // On a generated map, the chunks a character may come to next, to be made ahead (World.MakeAhead).
            var ahead = world.Source is IGeneratedSource ? aheadScratch : null;
            ahead?.Clear();
            var visit = activeNow == null ? world.ChunkCoords : Candidates(world, characters, activeNow, s.Keep + AheadChunks);
            foreach (var (cx, cy) in visit)
            {
                int near = ChunkStreaming.Distance(characters, cx, cy, world.WrapChunks);
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

        // Kept from plan to plan, so a plan makes no garbage of its own but the plan it returns.
        readonly List<(int near, int cy, int cx, bool active)> lingerScratch = new List<(int near, int cy, int cx, bool active)>();
        readonly List<(int near, int cy, int cx)> aheadScratch = new List<(int near, int cy, int cx)>();
        readonly List<(int cx, int cy)> aheadCells = new List<(int cx, int cy)>();
        readonly List<(int cx, int cy)> candidates = new List<(int cx, int cy)>();
        readonly HashSet<long> seen = new HashSet<long>();

        // The chunks a plan can touch, each once: within `around` of a character (across the seam on a world that wraps),
        // and the active ones and the four round each.
        List<(int cx, int cy)> Candidates(World world, IReadOnlyList<(int x, int y)> characters, IEnumerable<(int cx, int cy)> activeNow, int around)
        {
            candidates.Clear();
            seen.Clear();
            void Visit(int cx, int cy)
            {
                cx = world.FoldChunkX(cx);
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

        /// <summary>
        /// How far past the keep distance a generated map's chunks are made ahead of a character (World.MakeAhead): the
        /// ring that unpacks with an activating chunk, and one more for a character running.
        /// </summary>
        public const int AheadChunks = 2;

        // Whether a chunk within a keep distance activates. Nothing lingers there; the reach
        // activates (and lifts a cut off), and between reach and keep only something crossing in does.
        bool WithinKeep(World world, Func<int, int, bool> isActive, bool inReach, int cx, int cy, bool active)
        {
            lingering.Remove((cx, cy));
            if (inReach) cutOff.Remove((cx, cy));
            return !active && (inReach || Knocked(world, isActive, cx, cy));
        }

        // Whether an active chunk out of every keep distance stays: live, and not too long. One
        // that has lingered too long is cut off.
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

        // Whether something live on an active neighbour's facing edge may cross into this inactive chunk.
        bool Knocked(World world, Func<int, int, bool> isActive, int cx, int cy)
        {
            if (cutOff.Contains((cx, cy))) return false;
            for (int d = 0; d < 4; d++)
            {
                int nx = world.FoldChunkX(cx + Sides[d].dx), ny = cy + Sides[d].dy; // across the seam on a world that wraps
                // The neighbour's edge facing back this way is the opposite side, (d + 2) % 4.
                if (world.HasChunk(nx, ny) && isActive(nx, ny) && world.IsLiveAtEdge(nx, ny, (d + 2) % 4)) return true;
            }
            return false;
        }

        // The nearest lingerChunks candidates linger (activating those that aren't active yet);
        // the rest deactivate, cut off while still live.
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
