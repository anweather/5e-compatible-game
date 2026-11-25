using System.Globalization;

namespace Chem
{
    /// <summary>
    /// One 5 ft square. Mirrors the TS Cell in sandbox/src/world.ts. What most cells hold (terrain, water, height, fire) is
    /// its own; what few hold at once (cover, gas, the timers) is kept in a Rare, made when one of them is first set, so a
    /// cell of bare ground is 64 bytes rather than 128 (cell-memory: 3 million of them on a 1728 x 1728 map). Read and
    /// set them alike: a cell without its Rare reads 0 or null for each.
    /// </summary>
    public sealed class Cell
    {
        // The fields few cells hold, apart: none of it means anything but what the cell's properties say.
        sealed class Rare
        {
            public string Cover, Gas;
            public int Sets, Fuel, GasAmt, Charge, Life, DampUntil, Rust, Heat, Lit, Slick, Star, Cracked;

            public bool Empty => Cover == null && Gas == null && Sets == 0 && Fuel == 0 && GasAmt == 0 && Charge == 0 && Life == 0 &&
                DampUntil == 0 && Rust == 0 && Heat == 0 && Lit == 0 && Slick == 0 && Star == 0 && Cracked == 0;

            public Rare Copy() => (Rare)MemberwiseClone();
        }

        string t = "dirt";
        Rare rare;

        // Its Rare, made now if it has none.
        Rare Kept => rare ??= new Rare();

        /// <summary>Terrain id.</summary>
        public string T
        {
            get => t;
            set
            {
                // World.TerrainOf's cache follows the id (a reference check, as before: T is replaced, not edited).
                if (!ReferenceEquals(t, value)) Def = null;
                t = value;
            }
        }
        /// <summary>Units, 0..maxLevel (unitsPerStep units = one step of depth).</summary>
        public int Water;
        /// <summary>
        /// What the water here is: an index into Rules.Liquids, 0 water (Rules.Liquids.cs). Read it
        /// through Kind: a dry cell's is left as it was, and means nothing.
        /// </summary>
        public int Liquid;
        /// <summary>The liquid here: Liquid while it holds some, else 0 (water).</summary>
        public int Kind => Water > 0 ? Liquid : 0;
        /// <summary>Ticks until the liquid here sets into its terrain (sets {into, after}, World.SetLiquid). 0: it doesn't set, or the cell is dry.</summary>
        public int Sets
        {
            get => rare?.Sets ?? 0;
            set { if (rare != null || value != 0) Kept.Sets = value; }
        }
        /// <summary>The count of the liquid setting here: Sets while a liquid besides water holds it, else 0. What's hashed and saved, as Kind is.</summary>
        public int Setting => Kind != 0 ? Sets : 0;
        /// <summary>The water here is ice.</summary>
        public bool Frozen;
        /// <summary>oil, grease, ash, or null.</summary>
        public string Cover
        {
            get => rare?.Cover;
            set { if (rare != null || value != null) Kept.Cover = value; }
        }
        /// <summary>0..3.</summary>
        public int Fire;
        /// <summary>Ticks of burning left.</summary>
        public int Fuel
        {
            get => rare?.Fuel ?? 0;
            set { if (rare != null || value != 0) Kept.Fuel = value; }
        }
        public string Gas
        {
            get => rare?.Gas;
            set { if (rare != null || value != null) Kept.Gas = value; }
        }
        public int GasAmt
        {
            get => rare?.GasAmt ?? 0;
            set { if (rare != null || value != 0) Kept.GasAmt = value; }
        }
        /// <summary>3 on the tick lightning arrives, then decays.</summary>
        public int Charge
        {
            get => rare?.Charge ?? 0;
            set { if (rare != null || value != 0) Kept.Charge = value; }
        }
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
        public int Life
        {
            get => rare?.Life ?? 0;
            set { if (rare != null || value != 0) Kept.Life = value; }
        }
        /// <summary>
        /// The tick until which the ground here is damp from water that soaked in (World.Soak): damp
        /// ground soaks more slowly, as in rain. 0: dry.
        /// </summary>
        public int DampUntil
        {
            get => rare?.DampUntil ?? 0;
            set { if (rare != null || value != 0) Kept.DampUntil = value; }
        }
        /// <summary>
        /// Ticks it has been wet in all, toward its terrain's rust (World.Rust): at rust.every it rusts
        /// through. 0: none.
        /// </summary>
        public int Rust
        {
            get => rare?.Rust ?? 0;
            set { if (rare != null || value != 0) Kept.Rust = value; }
        }
        /// <summary>Ticks it stays hot (World.Cool): metal fire reached stays hot after the fire goes, and burns creatures on it. 0: cold.</summary>
        public int Heat
        {
            get => rare?.Heat ?? 0;
            set { if (rare != null || value != 0) Kept.Heat = value; }
        }
        /// <summary>Ticks it still sheds the Light spell's light (World.LightUp; the radius is the spell's light, for the view). 0: unlit.</summary>
        public int Lit
        {
            get => rare?.Lit ?? 0;
            set { if (rare != null || value != 0) Kept.Lit = value; }
        }
        /// <summary>How many more cells the floating cover here may spread (float.reach, World.Float); 0 when it has no cover, or no end to its reach.</summary>
        public int Slick
        {
            get => rare?.Slick ?? 0;
            set { if (rare != null || value != 0) Kept.Slick = value; }
        }
        /// <summary>How many falling stars are aimed here (World.AimStar): the target mark the view tints. 0: none.</summary>
        public int Star
        {
            get => rare?.Star ?? 0;
            set { if (rare != null || value != 0) Kept.Star = value; }
        }
        /// <summary>Its ice or terrain is cracked (shatter.cracks, World.Shatter): the tick it cracked, plus 1, so a blast that hits it twice cracks it once. 0: whole.</summary>
        public int Cracked
        {
            get => rare?.Cracked ?? 0;
            set { if (rare != null || value != 0) Kept.Cracked = value; }
        }

        // World.TerrainOf's cache: the definition of the terrain id it was looked up for, dropped when T changes, and
        // the rules it was looked up in (World's rules stamp: a switch makes every cell's stale at once, #181). Not cell
        // state (not hashed or saved).
        internal TerrainDef Def;
        internal int DefRules;

        /// <summary>Drops that cache, for when the rules it was looked up in are replaced.</summary>
        internal void ForgetDef() => Def = null;

        public Cell() { }
        public Cell(string terrain) { t = terrain; }

        /// <summary>Takes every field of another cell (the starting state, World.KeepStart).</summary>
        internal void CopyFrom(Cell o)
        {
            T = o.t; Water = o.Water; Liquid = o.Liquid; Frozen = o.Frozen; Fire = o.Fire; Height = o.Height;
            // A Rare of nothing is copied as none, so a cell put back to bare ground is small again.
            rare = o.rare == null || o.rare.Empty ? null : o.rare.Copy();
        }

        /// <summary>Back to a bare square of the given terrain (the sandbox's erase brush).</summary>
        public void Reset(string terrain = "dirt")
        {
            T = terrain; Water = 0; Liquid = 0; Frozen = false; Fire = 0; Height = 0;
            rare = null;
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
