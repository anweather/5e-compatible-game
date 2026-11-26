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
    public sealed class LiquidDef
    {
        public string Id, Name;
        public int Index;
        public IReadOnlyList<string> Tags = System.Array.Empty<string>();
        public int Thick = 1;
        public int Heap = WaterHeap;
        public const int WaterHeap = 2;
        public bool Soaks = true, Floats = true, Drowns = true;
        public string Boils;
        public string Touch;
        public int HotFor;
        public string Freezes;
        public string SetsInto;
        public int SetsAfter;
        public double Speed = 1;
        public string Mix;
        public LookDef Look;

        public bool Has(string tag) => Tags.Contains(tag);
    }

    public sealed partial class Rules
    {
        public const string LiquidsKey = "liquids";

        public const string WaterLiquid = "water";

        public static readonly IReadOnlyList<string> HotFields = new[] { "for" }, SetsFields = new[] { "into", "after" };

        public const int MaxLiquids = 255;

        public const int MaxLiquidTicks = 21600;

        readonly List<LiquidDef> liquids = new List<LiquidDef>();
        readonly Dictionary<string, int> liquidIndex = new Dictionary<string, int>();

        public IReadOnlyList<LiquidDef> Liquids => liquids;

        public bool HasLiquids => liquids.Count > 1;

        public LiquidDef Liquid(int index) => liquids[index];

        public int LiquidIndex(string id) => id != null && liquidIndex.TryGetValue(id, out int i) ? i : -1;

        public bool LiquidIs(int index, string idOrTag)
        {
            var l = liquids[index];
            return l.Id == idOrTag || l.Has(idOrTag);
        }

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
            HotFor = Int((o["hot"] as JObject)?["for"]) ?? 0,
            Freezes = Str(o["freezes"]),
            SetsInto = Str((o["sets"] as JObject)?["into"]),
            SetsAfter = Int((o["sets"] as JObject)?["after"]) ?? 0,
            Speed = Num(o["speed"]) ?? 1,
            Mix = Str(o[MixKey]),
            Look = ParseLook(o["look"]),
        };

        sealed partial class Checker
        {
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
                LiquidProducts(what, o);
                if (!(o[LapseKey] is JObject lapse)) return;
                foreach (var k in lapse.Properties().Select(k => k.Name).Where(k => !LapseFields.Contains(k)))
                    Errors.Add($"{what}: lapse.{k} unknown; lapse takes into and after");
                if (lapse["after"] != null && !(IsWholeNumber(lapse["after"]) && (double)lapse["after"] >= 1))
                    Errors.Add($"{what}: lapse.after must be a whole number of ticks from 1");
                if (lapse["into"] != null && Text(lapse["into"]) != WaterLiquid)
                    Errors.Add($"{what}: lapse.into {Show(lapse["into"])} must be water (or give none and it dries)");
            }

            void LiquidProducts(string what, JObject o)
            {
                if (o["hot"] != null) TicksSection(what, "hot", o["hot"], HotFields, "for");
                if (o["freezes"] != null && !IsIn(terrain, o["freezes"]) && !IsIn(covers, o["freezes"]))
                    Errors.Add($"{what}: freezes {Show(o["freezes"])} isn't a terrain or cover");
                if (o["sets"] == null) return;
                TicksSection(what, "sets", o["sets"], SetsFields, "after");
                var into = (o["sets"] as JObject)?["into"];
                if (o["sets"] is JObject && !(into != null && IsIn(terrain, into))) Errors.Add($"{what}: sets.into {Show(into)} isn't a terrain");
            }

            void TicksSection(string what, string key, JToken v, IReadOnlyList<string> fields, string ticks)
            {
                if (!(v is JObject o))
                {
                    Errors.Add($"{what}: {key} must be an object ({string.Join(", ", fields)})");
                    return;
                }
                foreach (var k in o.Properties().Select(k => k.Name).Where(k => !fields.Contains(k)))
                    Errors.Add($"{what}: {key}.{k} unknown; {key} takes {string.Join(" and ", fields)}");
                var t = o[ticks];
                if (!(t != null && IsWholeNumber(t) && (double)t >= 1 && (double)t <= MaxLiquidTicks))
                    Errors.Add($"{what}: {key}.{ticks} must be a whole number of ticks from 1 to {MaxLiquidTicks}");
            }

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
