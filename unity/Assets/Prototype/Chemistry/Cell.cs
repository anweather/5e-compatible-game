using System.Globalization;

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
        /// <summary>
        /// Steps of earth raised (+) or dug (-) by terraform spells, on top of the terrain's own
        /// elevation, within sim.json's height range. At height.blockAt or more the cell is a wall.
        /// </summary>
        public int Height;
        /// <summary>
        /// Ticks until what is growing or lasting here ripens (World.Ripen): a cover with a lifespan
        /// (thorns) withers, and a terrain with growsTo (green shoots, a sapling) grows into it.
        /// 0: nothing pending.
        /// </summary>
        public int Life;
        /// <summary>
        /// The tick until which the ground here is damp from water that soaked in (World.Soak): damp
        /// ground soaks more slowly, as in rain. 0: dry.
        /// </summary>
        public int DampUntil;

        // World.TerrainOf's cache: the definition of the terrain id it was looked up for.
        // Not cell state (not hashed or saved); refreshed whenever T changes.
        internal string DefFor;
        internal TerrainDef Def;

        /// <summary>Drops that cache, for when the rules it was looked up in are replaced.</summary>
        internal void ForgetDef()
        {
            DefFor = null;
            Def = null;
        }

        public Cell() { }
        public Cell(string terrain) { T = terrain; }

        /// <summary>Back to a bare square of the given terrain (the sandbox's erase brush).</summary>
        public void Reset(string terrain = "dirt")
        {
            T = terrain; Water = 0; Frozen = false; Cover = null; Fire = 0; Fuel = 0; Gas = null; GasAmt = 0; Charge = 0; Height = 0; Life = 0; DampUntil = 0;
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

    /// <summary>32-bit FNV-1a as lowercase hex, the hash the TS golden script and hashWorld use.</summary>
    internal static class Fnv1a
    {
        public static string Hash(string s)
        {
            unchecked
            {
                uint h = 0x811c9dc5;
                for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 0x01000193u;
                return h.ToString("x", CultureInfo.InvariantCulture);
            }
        }
    }
}
