// sim.json's streaming, read with the rest of sim.json (RuleTables.cs); here so the land (Chem.Land: KnownRing) can use it
// below the rules.

namespace Chem
{
    public sealed class StreamingParams
    {
        public int Reach = 2, Keep = 3;
        public int Sight = 4, Know = 7;
        public int SettleTicks = 12;
        public int LingerChunks = 16;
        public int LingerTicks = 1800;
        public int CatchUpTicks = 720;
        public int PackTicks = 300, PacksPerTick = 2;
    }
}
