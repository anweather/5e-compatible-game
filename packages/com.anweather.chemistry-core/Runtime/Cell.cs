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
        int water, liquid, fire, height;
        bool frozen;

        internal ChunkBlock Home;

        void Touch()
        {
            if (Home != null) Home.Touched();
        }

        Rare Kept => rare ??= new Rare();

        public string T
        {
            get => t;
            set
            {
                if (ReferenceEquals(t, value)) return;
                Def = null;
                bool same = t == value;
                t = value;
                if (!same) Touch();
            }
        }
        public int Water
        {
            get => water;
            set
            {
                if (water == value) return;
                water = value;
                Touch();
            }
        }
        public int Liquid
        {
            get => liquid;
            set
            {
                if (liquid == value) return;
                liquid = value;
                Touch();
            }
        }
        public int Kind => Water > 0 ? Liquid : 0;
        public int Sets
        {
            get => rare?.Sets ?? 0;
            set
            {
                if (Sets == value) return;
                Kept.Sets = value;
                Touch();
            }
        }
        public int Setting => Kind != 0 ? Sets : 0;
        public bool Frozen
        {
            get => frozen;
            set
            {
                if (frozen == value) return;
                frozen = value;
                Touch();
            }
        }
        public string Cover
        {
            get => rare?.Cover;
            set
            {
                if (Cover == value) return;
                Kept.Cover = value;
                Touch();
            }
        }
        public int Fire
        {
            get => fire;
            set
            {
                if (fire == value) return;
                fire = value;
                Touch();
            }
        }
        public int Fuel
        {
            get => rare?.Fuel ?? 0;
            set
            {
                if (Fuel == value) return;
                Kept.Fuel = value;
                Touch();
            }
        }
        public string Gas
        {
            get => rare?.Gas;
            set
            {
                if (Gas == value) return;
                Kept.Gas = value;
                Touch();
            }
        }
        public int GasAmt
        {
            get => rare?.GasAmt ?? 0;
            set
            {
                if (GasAmt == value) return;
                Kept.GasAmt = value;
                Touch();
            }
        }
        public int Charge
        {
            get => rare?.Charge ?? 0;
            set
            {
                if (Charge == value) return;
                Kept.Charge = value;
                Touch();
            }
        }
        public int Height
        {
            get => height;
            set
            {
                if (height == value) return;
                height = value;
                Touch();
            }
        }
        public int Life
        {
            get => rare?.Life ?? 0;
            set
            {
                if (Life == value) return;
                Kept.Life = value;
                Touch();
            }
        }
        public int DampUntil
        {
            get => rare?.DampUntil ?? 0;
            set
            {
                if (DampUntil == value) return;
                Kept.DampUntil = value;
                Touch();
            }
        }
        public int Rust
        {
            get => rare?.Rust ?? 0;
            set
            {
                if (Rust == value) return;
                Kept.Rust = value;
                Touch();
            }
        }
        public int Heat
        {
            get => rare?.Heat ?? 0;
            set
            {
                if (Heat == value) return;
                Kept.Heat = value;
                Touch();
            }
        }
        public int Lit
        {
            get => rare?.Lit ?? 0;
            set
            {
                if (Lit == value) return;
                Kept.Lit = value;
                Touch();
            }
        }
        public int Slick
        {
            get => rare?.Slick ?? 0;
            set
            {
                if (Slick == value) return;
                Kept.Slick = value;
                Touch();
            }
        }
        public int Star
        {
            get => rare?.Star ?? 0;
            set
            {
                if (Star == value) return;
                Kept.Star = value;
                Touch();
            }
        }
        public int Cracked
        {
            get => rare?.Cracked ?? 0;
            set
            {
                if (Cracked == value) return;
                Kept.Cracked = value;
                Touch();
            }
        }

        internal TerrainDef Def;
        internal int DefRules;

        internal void ForgetDef() => Def = null;

        public Cell() { }
        public Cell(string terrain) { t = terrain; }

        internal void CopyFrom(Cell o)
        {
            T = o.t; Water = o.Water; Liquid = o.Liquid; Frozen = o.Frozen; Fire = o.Fire; Height = o.Height;
            if (rare != null || o.rare != null) Touch();
            rare = o.rare == null || o.rare.Empty ? null : o.rare.Copy();
        }

        public void Reset(string terrain = "dirt")
        {
            T = terrain; Water = 0; Liquid = 0; Frozen = false; Fire = 0; Height = 0;
            if (rare != null) Touch();
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
