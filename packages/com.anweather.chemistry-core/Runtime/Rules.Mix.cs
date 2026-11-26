// Mixes (docs/design/primitives.md §4, P5; J-062): rows of a table sharing a `mix` group that
// meet make a derived row with no reaction between them, a pure function of the two rows.
// Its id is its parents' ids sorted and joined by "+", so a mix meeting one of its own parents
// is the mix again, and three paints come to one id whichever met first. Numbers are the mean,
// rounded down when both are whole; flags hold if both do; a list keeps what both have; a text
// (or a section holding one) stays only if they agree; the colour mixes like paint (Colors.cs).
// A field only one row gives counts its default where the table has one, else it's left out.
// `lapse` isn't derived: a mix goes when its last parent does (World.Mix.cs). The room keeps the
// row it made, since a parent may be gone by the time a save or a late joiner needs it.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class RoomMix
    {
        public string Table, Id, Row;
        public IReadOnlyList<string> Parents = Array.Empty<string>();
        public int Tick;
    }

    public sealed partial class Rules
    {
        public const string MixKey = "mix";

        const char MixJoin = '+';

        static readonly JObject LiquidDefaults = new JObject
        {
            ["thick"] = 1,
            ["heap"] = LiquidDef.WaterHeap,
            ["speed"] = 1.0,
            ["soaks"] = true,
            ["floats"] = true,
            ["drowns"] = true,
        };

        static readonly JObject CoverDefaults = new JObject { ["speed"] = 1.0 };

        static readonly string[] NotMerged = { "name", "look", LapseKey, MixKey };

        public static string MixId(string a, string b) =>
            string.Join(MixJoin.ToString(), MixParents(a).Concat(MixParents(b)).Distinct().OrderBy(p => p, StringComparer.Ordinal));

        public static IReadOnlyList<string> MixParents(string id) => id.Split(MixJoin);

        internal JObject MaterialRow(string table, string id) => doc[table]?[id] as JObject;

        internal static JObject DeriveMix(string table, string a, JObject rowA, string b, JObject rowB)
        {
            if (string.CompareOrdinal(a, b) > 0)
            {
                (rowA, rowB) = (rowB, rowA);
            }
            var defaults = table == LiquidsKey ? LiquidDefaults : CoverDefaults;
            var row = (JObject)Merged(WithDefaults(rowA, defaults), WithDefaults(rowB, defaults), false);
            row["name"] = $"{Str(rowA["name"])} and {Str(rowB["name"])}";
            row[MixKey] = rowA[MixKey].DeepClone();
            var look = MixedLook(rowA["look"] as JObject, rowB["look"] as JObject);
            if (look.Type != JTokenType.Null) row["look"] = look;
            return row;
        }

        static JObject WithDefaults(JObject row, JObject defaults)
        {
            var filled = (JObject)row.DeepClone();
            foreach (var d in defaults.Properties())
                if (filled[d.Name] == null) filled[d.Name] = d.Value.DeepClone();
            return filled;
        }

        static JToken Merged(JObject a, JObject b, bool section)
        {
            var row = new JObject();
            foreach (var p in a.Properties().Where(p => !NotMerged.Contains(p.Name) && b[p.Name] != null))
            {
                var value = MergedValue(p.Value, b[p.Name]);
                if (value.Type != JTokenType.Null) row[p.Name] = value;
                else if (section) return Nothing;
            }
            return row;
        }

        static JToken MergedValue(JToken a, JToken b)
        {
            bool whole = a.Type == JTokenType.Integer && b.Type == JTokenType.Integer;
            if (whole) return (long)Math.Floor(((long)a + (long)b) / 2.0);
            if (IsNumeric(a) && IsNumeric(b)) return ((double)a + (double)b) / 2;
            if (a.Type == JTokenType.Boolean && b.Type == JTokenType.Boolean) return (bool)a && (bool)b;
            if (a is JObject oa && b is JObject ob) return Merged(oa, ob, true);
            if (a is JArray la && b is JArray lb && la.All(v => v.Type == JTokenType.String)) return new JArray(la.Where(v => lb.Any(w => JToken.DeepEquals(v, w))).Select(v => v.DeepClone()));
            return JToken.DeepEquals(a, b) ? a.DeepClone() : Nothing;
        }

        static JToken Nothing => JValue.CreateNull();

        static bool IsNumeric(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;

        static JToken MixedLook(JObject a, JObject b)
        {
            if (a == null || b == null) return (a ?? b ?? Nothing).DeepClone();
            var look = (JObject)a.DeepClone();
            if (a["color"] == null || b["color"] == null) return look;
            LookDef la = ParseLook(a), lb = ParseLook(b);
            var mixed = Colors.Mix(new LookColour(la.R, la.G, la.B), new LookColour(lb.R, lb.G, lb.B));
            look["color"] = new JArray((double)mixed.R, (double)mixed.G, (double)mixed.B);
            return look;
        }
    }
}
