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
    }
}
