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
            return Plan(world.ChunkCoords, world.IsActive, characters, s.Reach, s.Keep);
        }

        /// <summary>
        /// The switches against a set of chunks and which of them are active (for a host that plans
        /// ahead of its own world, whose stamped switches apply a tick or so later): inactive chunks
        /// within <paramref name="reach"/> chunks of a character activate, active ones further than
        /// <paramref name="keep"/> from every character deactivate. Distances are in chunks, the
        /// larger of the two axes. Ordered by row then column, so the plan is the same however the
        /// characters are listed. With no characters nothing changes.
        /// </summary>
        public static List<ChunkSwitch> Plan(IReadOnlyList<(int cx, int cy)> chunks, Func<int, int, bool> isActive,
            IReadOnlyList<(int x, int y)> characters, int reach, int keep)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            if (isActive == null) throw new ArgumentNullException(nameof(isActive));
            var plan = new List<ChunkSwitch>();
            if (characters == null || characters.Count == 0) return plan;
            foreach (var (cx, cy) in chunks)
            {
                int near = Nearest(characters, cx, cy);
                bool active = isActive(cx, cy);
                if (!active && near <= reach) plan.Add(new ChunkSwitch(cx, cy, true));
                else if (active && near > keep) plan.Add(new ChunkSwitch(cx, cy, false));
            }
            return plan;
        }

        // How many chunks the nearest character is from a chunk.
        static int Nearest(IReadOnlyList<(int x, int y)> characters, int cx, int cy)
        {
            int near = int.MaxValue;
            foreach (var (x, y) in characters)
            {
                int d = Math.Max(Math.Abs(Chunks.ChunkOf(x) - cx), Math.Abs(Chunks.ChunkOf(y) - cy));
                if (d < near) near = d;
            }
            return near;
        }

        /// <summary>Applies switches to the world (unknown chunks are skipped). Between ticks, the same on every peer.</summary>
        public static void Apply(World world, IEnumerable<ChunkSwitch> switches)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            foreach (var s in switches ?? Array.Empty<ChunkSwitch>())
                if (world.HasChunk(s.CX, s.CY)) world.SetActive(s.CX, s.CY, s.Active);
        }

        // How many chunks the nearest character is from a chunk (for ChunkPlanner).
        internal static int Distance(IReadOnlyList<(int x, int y)> characters, int cx, int cy) => Nearest(characters, cx, cy);
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
            return Plan(world, world.IsActive, characters);
        }

        /// <summary>
        /// The switches for these characters, against which chunks are (or are planned to be)
        /// active; whether chunks are live is read from the world. Ordered by row then column.
        /// With no characters nothing changes.
        /// </summary>
        public List<ChunkSwitch> Plan(World world, Func<int, int, bool> isActive, IReadOnlyList<(int x, int y)> characters)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (isActive == null) throw new ArgumentNullException(nameof(isActive));
            var plan = new List<ChunkSwitch>();
            if (characters == null || characters.Count == 0) return plan;
            var s = world.Rules.Sim.Streaming;
            // Lingering candidates: (distance, cy, cx, active now).
            var linger = new List<(int near, int cy, int cx, bool active)>();
            foreach (var (cx, cy) in world.ChunkCoords)
            {
                int near = ChunkStreaming.Distance(characters, cx, cy);
                bool active = isActive(cx, cy);
                if (near <= s.Keep)
                {
                    if (WithinKeep(world, isActive, near <= s.Reach, cx, cy, active)) plan.Add(new ChunkSwitch(cx, cy, true));
                }
                else if (active ? Lingers(world, s, cx, cy) : Knocked(world, isActive, cx, cy)) linger.Add((near, cy, cx, active));
                else if (active) plan.Add(new ChunkSwitch(cx, cy, false));
            }
            Cap(linger, s.LingerChunks, world.Tick, plan);
            plan.Sort((a, b) => a.CY != b.CY ? a.CY.CompareTo(b.CY) : a.CX.CompareTo(b.CX));
            return plan;
        }

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
                int nx = cx + Sides[d].dx, ny = cy + Sides[d].dy;
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
