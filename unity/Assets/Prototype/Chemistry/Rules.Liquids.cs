// Liquids (docs/design/primitives.md §3, P1): what a cell's water is. Cell.Water stays
// the amount and Cell.Liquid is the kind, an index into Rules.Liquids, where 0 is
// water, built in, so a world with no liquids table is exactly today's. The rest are
// the "liquids" table's rows (materials.json, then each mod's in mod order), each
// interned at its place in the list: the index is the core's alone, never on the wire
// or on disk, which carry the id (World.Liquids.cs remaps cells at a rule switch). A
// mod's liquid is temporary like any material it adds (TempMaterials, World.Lapse.cs).
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One row of the liquids table (water is row 0).</summary>
    public sealed class LiquidDef
    {
        public string Id, Name;
        /// <summary>Its place in Rules.Liquids: the Cell.Liquid of a cell holding it.</summary>
        public int Index;
        public IReadOnlyList<string> Tags = System.Array.Empty<string>();
        /// <summary>It flows on one tick in Thick (water 1); 0 never flows (quicksand).</summary>
        public int Thick = 1;
        /// <summary>The drop in units it needs to flow on the flat (water 2), so a thick one stands in a mound.</summary>
        public int Heap = WaterHeap;
        /// <summary>Water's heap: a still puddle doesn't slosh back and forth for ever.</summary>
        public const int WaterHeap = 2;
        /// <summary>Thin pools of it sink into the ground (World.Soak); covers ride it (World.Float); its depth counts for breath, wading and sinking.</summary>
        public bool Soaks = true, Floats = true, Drowns = true;
        /// <summary>The gas it boils off as on hot ground; null, it doesn't boil (water's is sim.json water.boils, World.BoilsOf).</summary>
        public string Boils;
        /// <summary>The element it lays, at power 1, on a cell of another liquid it would flow into; null, the other is a wall.</summary>
        public string Touch;
        /// <summary>How fast a creature wades it (1 unhindered). Carried for the view and movers.</summary>
        public double Speed = 1;
        /// <summary>How the view draws it (the water mesh's colour), or null for water's own. The view's only.</summary>
        public LookDef Look;

        public bool Has(string tag) => Tags.Contains(tag);
    }

    public sealed partial class Rules
    {
        /// <summary>The rule document's liquids table (materials.json "liquids", and a mod's).</summary>
        public const string LiquidsKey = "liquids";

        /// <summary>The id of liquid 0, built in: no row may take it.</summary>
        public const string WaterLiquid = "water";

        /// <summary>The most liquids a rule set may hold, water too (a byte a cell, as struct-of-arrays later).</summary>
        public const int MaxLiquids = 255;

        readonly List<LiquidDef> liquids = new List<LiquidDef>();
        readonly Dictionary<string, int> liquidIndex = new Dictionary<string, int>();

        /// <summary>Every liquid by Cell.Liquid: water first, then the table's rows in order.</summary>
        public IReadOnlyList<LiquidDef> Liquids => liquids;

        /// <summary>Whether the rules hold a liquid besides water (else every cell's kind is 0).</summary>
        public bool HasLiquids => liquids.Count > 1;

        /// <summary>Liquid <paramref name="index"/> (Cell.Liquid): for the tick's hot loops, no interface call.</summary>
        public LiquidDef Liquid(int index) => liquids[index];

        /// <summary>The Cell.Liquid of the liquid with this id, or -1 if the rules have none.</summary>
        public int LiquidIndex(string id) => id != null && liquidIndex.TryGetValue(id, out int i) ? i : -1;

        /// <summary>Whether liquid <paramref name="index"/> has this id or tag (a reaction's liquid condition).</summary>
        public bool LiquidIs(int index, string idOrTag)
        {
            var l = liquids[index];
            return l.Id == idOrTag || l.Has(idOrTag);
        }

        // Water from sim.json's products, then each row of the table.
        void ParseLiquids(JObject doc)
        {
            Add(new LiquidDef { Id = WaterLiquid, Name = "Water" });
            foreach (var p in (doc[LiquidsKey] as JObject ?? new JObject()).Properties())
                if (p.Value is JObject o) Add(ParseLiquid(p.Name, o));

            void Add(LiquidDef l)
            {
                l.Index = liquids.Count;
                liquids.Add(l);
                liquidIndex[l.Id] = l.Index;
            }
        }

        static LiquidDef ParseLiquid(string id, JObject o) => new LiquidDef
        {
            Id = id,
            Name = Str(o["name"]) ?? id,
            Tags = Strings(o["tags"]),
            Thick = Int(o["thick"]) ?? 1,
            Heap = Int(o["heap"]) ?? LiquidDef.WaterHeap,
            Soaks = Bool(o["soaks"]) ?? true,
            Floats = Bool(o["floats"]) ?? true,
            Drowns = Bool(o["drowns"]) ?? true,
            Boils = Str(o["boils"]),
            Touch = Str(o["touch"]),
            Speed = Num(o["speed"]) ?? 1,
            Look = ParseLook(o["look"]),
        };

        sealed partial class Checker
        {
            /// <summary>The liquids table: known fields in their ranges, its gas and element known, water's id kept, at most MaxLiquids.</summary>
            public void Liquids()
            {
                if (!(r[LiquidsKey] is JObject rows)) return;
                if (rows.Count + 1 > MaxLiquids) Errors.Add($"liquids: at most {MaxLiquids - 1} besides water");
                foreach (var p in rows.Properties())
                {
                    string what = "liquid " + p.Name;
                    if (!(p.Value is JObject o))
                    {
                        Errors.Add($"{what}: must be an object");
                        continue;
                    }
                    if (p.Name == WaterLiquid) Errors.Add($"{what}: water is built in (liquid 0); give the new liquid its own id");
                    foreach (var k in o.Properties().Select(k => k.Name).Where(k => RuleSchema.Liquids.Field(k) == null))
                        Errors.Add($"{what}: unknown field \"{k}\"; a liquid takes {string.Join(", ", RuleSchema.Liquids.Names)}");
                    Values(what, "", o, RuleSchema.Liquids);
                    LiquidRefs(what, o);
                }
            }

            void LiquidRefs(string what, JObject o)
            {
                if (o["tags"] != null) Tags(o["tags"], what);
                if (o["boils"] != null && !IsIn(gases, o["boils"])) Errors.Add($"{what}: boils {Show(o["boils"])} isn't a gas");
                if (o["touch"] != null && !IsElement(o["touch"])) Errors.Add($"{what}: touch {Show(o["touch"])} isn't an element");
                LookOf(what, o, false);
                if (!(o[LapseKey] is JObject lapse)) return;
                foreach (var k in lapse.Properties().Select(k => k.Name).Where(k => !LapseFields.Contains(k)))
                    Errors.Add($"{what}: lapse.{k} unknown; lapse takes into and after");
                if (lapse["after"] != null && !(IsWholeNumber(lapse["after"]) && (double)lapse["after"] >= 1))
                    Errors.Add($"{what}: lapse.after must be a whole number of ticks from 1");
                if (lapse["into"] != null && Text(lapse["into"]) != WaterLiquid)
                    Errors.Add($"{what}: lapse.into {Show(lapse["into"])} must be water (or give none and it dries)");
            }

            /// <summary>A reaction's liquid condition and effect name a liquid (the condition may name a tag instead).</summary>
            void LiquidRef(string id, JToken cond, IEnumerable<JObject> effects)
            {
                var rows = r[LiquidsKey] as JObject ?? new JObject();
                if (cond?["liquid"] != null && Text(cond["liquid"]) == null) Errors.Add($"rule {id}: liquid must be a liquid's id or a tag");
                foreach (var e in effects.Where(e => e["liquid"] != null))
                {
                    if (!(Text(e["liquid"]) == WaterLiquid || IsIn(rows, e["liquid"]))) Errors.Add($"rule {id}: liquid {Show(e["liquid"])} unknown");
                    if (e["addWater"] == null) Errors.Add($"rule {id}: liquid goes with addWater (the liquid it pours)");
                }
            }
        }
    }
}
