// One 5 ft square. Mirrors the TS Cell in sandbox/src/world.ts. What most cells hold (terrain, water, height, fire) is
// its own; what few hold at once (cover, gas, the timers) is kept in a Rare, made when one of them is first set, so a
// cell of bare ground is 64 bytes rather than 128 (cell-memory: 3 million of them on a 1728 x 1728 map). Read and
// set them alike: a cell without its Rare reads 0 or null for each.
using System.Globalization;

namespace Chem
{
    public sealed class Cell
    {
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

        Rare Kept => rare ??= new Rare();

        public string T
        {
            get => t;
            set
            {
                if (!ReferenceEquals(t, value)) Def = null;
                t = value;
            }
        }
        public int Water;
        public int Liquid;
        public int Kind => Water > 0 ? Liquid : 0;
        public int Sets
        {
            get => rare?.Sets ?? 0;
            set { if (rare != null || value != 0) Kept.Sets = value; }
        }
        public int Setting => Kind != 0 ? Sets : 0;
        public bool Frozen;
        public string Cover
        {
            get => rare?.Cover;
            set { if (rare != null || value != null) Kept.Cover = value; }
        }
        public int Fire;
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
        public int Charge
        {
            get => rare?.Charge ?? 0;
            set { if (rare != null || value != 0) Kept.Charge = value; }
        }
        public int Height;
        public int Life
        {
            get => rare?.Life ?? 0;
            set { if (rare != null || value != 0) Kept.Life = value; }
        }
        public int DampUntil
        {
            get => rare?.DampUntil ?? 0;
            set { if (rare != null || value != 0) Kept.DampUntil = value; }
        }
        public int Rust
        {
            get => rare?.Rust ?? 0;
            set { if (rare != null || value != 0) Kept.Rust = value; }
        }
        public int Heat
        {
            get => rare?.Heat ?? 0;
            set { if (rare != null || value != 0) Kept.Heat = value; }
        }
        public int Lit
        {
            get => rare?.Lit ?? 0;
            set { if (rare != null || value != 0) Kept.Lit = value; }
        }
        public int Slick
        {
            get => rare?.Slick ?? 0;
            set { if (rare != null || value != 0) Kept.Slick = value; }
        }
        public int Star
        {
            get => rare?.Star ?? 0;
            set { if (rare != null || value != 0) Kept.Star = value; }
        }
        public int Cracked
        {
            get => rare?.Cracked ?? 0;
            set { if (rare != null || value != 0) Kept.Cracked = value; }
        }

        internal TerrainDef Def;
        internal int DefRules;

        internal void ForgetDef() => Def = null;

        public Cell() { }
        public Cell(string terrain) { t = terrain; }

        internal void CopyFrom(Cell o)
        {
            T = o.t; Water = o.Water; Liquid = o.Liquid; Frozen = o.Frozen; Fire = o.Fire; Height = o.Height;
            rare = o.rare == null || o.rare.Empty ? null : o.rare.Copy();
        }

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
        public int Count;
    }

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
