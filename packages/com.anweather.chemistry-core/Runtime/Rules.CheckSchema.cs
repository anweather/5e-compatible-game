// The checks a RuleSchema states for itself (cleanup item 7): each field's range and colour,
// read from the schema so the numbers are written once. What joins two fields stays with the
// table's own checks (Rules.CheckItems.cs, Rules.Check.cs).
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class Rules
    {
        sealed partial class Checker
        {
            /// <summary>A section's numbers and colours, each held to its field in the schema, in the schema's order.</summary>
            void Values(string what, string path, JToken section, RuleSchema schema)
            {
                foreach (var f in schema.Fields)
                {
                    var v = section[f.Name];
                    if (f.Type == RuleFieldType.Colour && v != null && !IsHexColor(v)) Errors.Add($"{what}: {path}{f.Name} must be #rrggbb");
                    if (!f.Ranged) continue;
                    bool missing = v == null || v.Type == JTokenType.Null;
                    if ((!missing || f.Required) && !Fits(f, v)) Errors.Add($"{what}: {path}{f.Name} must be a {Kind(f)} {RangeWords(f)}");
                }
            }

            /// <summary>True when every field of the section is there if required and in its range if given.</summary>
            static bool FitsAll(RuleSchema schema, JObject section)
            {
                foreach (var f in schema.Fields)
                {
                    var v = section[f.Name];
                    if (v == null ? f.Required : !Fits(f, v)) return false;
                }
                return true;
            }

            /// <summary>A value in the field's range: a number, whole for a whole field.</summary>
            static bool Fits(RuleField f, JToken v)
            {
                if (!(f.Type == RuleFieldType.Whole ? IsWholeNumber(v) : IsNumber(v))) return false;
                double n = (double)v;
                return n >= f.Min && (!f.Above || n > f.Min) && n <= f.Max;
            }

            static string Kind(RuleField f) => f.Type == RuleFieldType.Whole ? "whole number" : "number";

            static string RangeWords(RuleField f)
            {
                if (f.Above) return $"above {f.Min}";
                return f.Max < double.MaxValue ? $"from {f.Min} to {f.Max}" : $"from {f.Min}";
            }
        }
    }
}
