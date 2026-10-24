namespace Chem
{
    /// <summary>One 5 ft square. Mirrors the TS Cell in sandbox/src/world.ts.</summary>
    public sealed class Cell
    {
        /// <summary>Terrain id.</summary>
        public string T = "dirt";
        /// <summary>Units, 0..maxLevel (unitsPerStep units = one step of depth).</summary>
        public int Water;
        /// <summary>The water here is ice.</summary>
        public bool Frozen;
        /// <summary>oil, grease, ash, or null.</summary>
        public string Cover;
        /// <summary>0..3.</summary>
        public int Fire;
        /// <summary>Ticks of burning left.</summary>
        public int Fuel;
        public string Gas;
        public int GasAmt;
        /// <summary>3 on the tick lightning arrives, then decays.</summary>
        public int Charge;

        public Cell() { }
        public Cell(string terrain) { T = terrain; }

        /// <summary>Back to a bare square of the given terrain (the sandbox's erase brush).</summary>
        public void Reset(string terrain = "dirt")
        {
            T = terrain; Water = 0; Frozen = false; Cover = null; Fire = 0; Fuel = 0; Gas = null; GasAmt = 0; Charge = 0;
        }
    }

    public enum LogKind { Info, Notable, Power, Hurt }

    public sealed class LogEntry
    {
        public int Tick;
        public string Text;
        public LogKind Kind;
        /// <summary>How many times in a row this line happened (the log merges repeats).</summary>
        public int Count;
    }
}
