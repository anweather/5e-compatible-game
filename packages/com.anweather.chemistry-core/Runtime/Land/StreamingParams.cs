// sim.json's streaming, read with the rest of sim.json (RuleTables.cs); here so the land (Chem.Land: KnownRing) can use it
// below the rules.

namespace Chem
{
    /// <summary>
    /// sim.json's streaming (ChunkStreaming, docs/WORLD_STREAMING.md): a character's reach, the
    /// chunks within Reach of the one it stands in, is kept active; a chunk deactivates once no
    /// character is within Keep. C# only: the sandbox has no chunks.
    /// </summary>
    public sealed class StreamingParams
    {
        public int Reach = 2, Keep = 3;
        /// <summary>A chunk is generated within this of a character, and known (a coarse far chunk, KnownRing) out to know.</summary>
        public int Sight = 4, Know = 7;
        /// <summary>A chunk is settled once nothing in it has been live for this many ticks.</summary>
        public int SettleTicks = 12;
        /// <summary>At most this many chunks out of every reach stay active while live (linger).</summary>
        public int LingerChunks = 16;
        /// <summary>A chunk lingers at most this many ticks before it deactivates, live or not.</summary>
        public int LingerTicks = 1800;
        /// <summary>A chunk let go while live runs at most this many of the ticks it missed when it next activates.</summary>
        public int CatchUpTicks = 720;
        /// <summary>
        /// The sparse World (World.Pack.cs): a chunk packs once it has been inactive, not beside an active chunk and not
        /// drawn for this many ticks, at most PacksPerTick a tick (0: never). Memory only: nothing shared follows them.
        /// </summary>
        public int PackTicks = 300, PacksPerTick = 2;
    }
}
