// Which chunks are simulated, following the players (docs/WORLD_STREAMING.md,
// "Rings around each player"): each player's chunk and the ring around it
// wake; a chunk sleeps again once no player is within a wider ring, so walking
// along a chunk edge doesn't wake and sleep it every few steps. The plan is a
// list of switches that travel like any other command (TickBatch.Chunks): solo
// they apply before the next tick, online the host stamps them into its tick
// batches, so every peer pauses the same chunks at the same tick. C# only:
// the sandbox has no chunks.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A chunk starting (Active) or stopping being simulated, in a tick batch.</summary>
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
        public override string ToString() => $"{CX},{CY} {(Active ? "wakes" : "sleeps")}";
        public static bool operator ==(ChunkSwitch a, ChunkSwitch b) => a.Equals(b);
        public static bool operator !=(ChunkSwitch a, ChunkSwitch b) => !a.Equals(b);
    }

    public static class ChunkStreaming
    {
        /// <summary>Chunks within this many chunks of a player (in each direction) wake: 1 is the 3×3 around them.</summary>
        public const int WakeRing = 1;
        /// <summary>A chunk sleeps once no player is within this many chunks of it.</summary>
        public const int KeepRing = 2;

        /// <summary>
        /// The switches that bring the world's chunks in line with the players at these cells:
        /// sleeping chunks within <paramref name="wake"/> of a player wake, active ones further than
        /// <paramref name="keep"/> from every player sleep. Ordered by row then column, so the plan
        /// is the same however the players are listed. With no players nothing changes.
        /// </summary>
        public static List<ChunkSwitch> Plan(World world, IReadOnlyList<(int x, int y)> players, int wake = WakeRing, int keep = KeepRing)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var plan = new List<ChunkSwitch>();
            if (players == null || players.Count == 0) return plan;
            foreach (var (cx, cy) in world.ChunkCoords)
            {
                int near = int.MaxValue;
                foreach (var (x, y) in players)
                {
                    int d = Math.Max(Math.Abs(Chunks.ChunkOf(x) - cx), Math.Abs(Chunks.ChunkOf(y) - cy));
                    if (d < near) near = d;
                }
                bool active = world.IsActive(cx, cy);
                if (!active && near <= wake) plan.Add(new ChunkSwitch(cx, cy, true));
                else if (active && near > keep) plan.Add(new ChunkSwitch(cx, cy, false));
            }
            return plan;
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
