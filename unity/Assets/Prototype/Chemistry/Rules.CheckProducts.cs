// Checks what materials leave behind (docs/design/primitives.md P3): sim.json's products
// (fire's ash and smoke, water's boils and freezes, snow's covers, the meteor's spell) and a
// terrain's or cover's own burn.ash and burn.smoke name a material, or spell, the rules know.
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class Rules
    {
        sealed partial class Checker
        {
            /// <summary>The sim.json keys that name a product, and the table each must be in.</summary>
            internal static readonly (string path, string table)[] SimProducts =
            {
                ("fire.ash", "covers"), ("fire.smoke", "gases"), ("water.boils", "gases"), ("water.freezes", "terrain"),
                ("weather.snow.cover", "covers"), ("weather.snow.bank", "covers"), ("weather.meteors.spell", "spells"),
            };

            internal static bool IsSimProduct(string path) => SimProducts.Any(p => p.path == path);

            public void ProductRefs()
            {
                foreach (var (path, table) in SimProducts)
                {
                    var name = r["sim"]?.SelectToken(path);
                    if (name != null && !Knows(table, name)) Errors.Add($"sim {path}: unknown {Single(table)} {Show(name)}");
                }
                foreach (var p in terrain.Properties()) BurnProducts($"terrain {p.Name}", p.Value);
                foreach (var p in covers.Properties()) BurnProducts($"cover {p.Name}", p.Value);
            }

            void BurnProducts(string what, JToken t)
            {
                if (t["burnAsh"] != null && !IsIn(covers, t["burnAsh"])) Errors.Add($"{what}: burn.ash {Show(t["burnAsh"])} is not a cover");
                if (t["burnSmoke"] != null && !IsIn(gases, t["burnSmoke"])) Errors.Add($"{what}: burn.smoke {Show(t["burnSmoke"])} is not a gas");
            }

            bool Knows(string table, JToken name)
            {
                if (name.Type != JTokenType.String) return false;
                if (table == "spells") return (r["spells"] as JArray ?? new JArray()).Any(s => Text(s["id"]) == (string)name);
                return IsIn((JObject)r[table], name);
            }

            static string Single(string table) => table == "gases" ? "gas" : table.TrimEnd('s');
        }
    }
}
