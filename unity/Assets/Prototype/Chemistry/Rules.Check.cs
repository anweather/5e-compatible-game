// Checks a composed rule set before it can switch on: validateRules from
// sandbox/src/rules.ts with the same messages, then the numbers this port
// reads, which the TS leaves to JavaScript.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class Rules
    {
        /// <summary>Every problem with a rule document; empty means consistent.</summary>
        static List<string> Check(JObject r)
        {
            var check = new Checker(r);
            check.CrossReferences();
            check.Items();
            check.Components();
            check.Groups();
            check.Numbers();
            return check.Errors;
        }

        sealed partial class Checker
        {
            static readonly HashSet<string> CondKeys = new HashSet<string>
            {
                "cover", "fire", "fireBelow", "water", "frozen", "gas", "terrainTag", "notTerrainTag", "conductive", "status", "flammable", "has", "lacks", "power", "chance",
            };
            static readonly HashSet<string> EffectKeys = new HashSet<string>
            {
                "ignite", "boil", "melt", "freeze", "extinguish", "condense", "addWater", "setCover", "spreadCover", "break",
                "setTerrain", "raise", "conduct", "fan", "pushGas", "clearGas", "mark", "gas", "n", "status", "clear", "damageMul", "grow", "ticks", "wave",
            };
            static readonly HashSet<string> Shapes = new HashSet<string> { "point", "line", "square", "burst", "target", "cone" };
            static readonly HashSet<string> SpellEffects = new HashSet<string> { "levitate", "carry", "teleport", "terraform", "haste", "weather", "shapeshift" };

            static readonly string[] SpellNumbers = { "level", "power", "length", "size", "radius", "push", "wind", "range", "duration", "maxMass", "rate", "radiusPerCharge" };
            static readonly string[] EffectNumbers = { "boil", "spreadCover", "conduct", "n", "wave" };
            /// <summary>Sim numbers the world divides by or sizes things with.</summary>
            static readonly HashSet<string> PositiveSim = new HashSet<string> { "ticksPerSecond", "water.maxLevel", "water.unitsPerStep", "height.blockAt", "weather.rain.fillEvery", "weather.rain.dryEvery", "weather.storm.strikeEvery", "weather.snow.freezeEvery", "weather.meteors.every", "weather.meteors.fallTicks", "streaming.reach", "streaming.keep", "streaming.settleTicks", "streaming.lingerChunks", "streaming.lingerTicks", "streaming.catchUpTicks" };

            readonly JObject r, terrain, covers, gases;
            readonly HashSet<string> elements;

            public Checker(JObject r)
            {
                this.r = r;
                terrain = (JObject)r["terrain"];
                covers = (JObject)r["covers"];
                gases = (JObject)r["gases"];
                elements = new HashSet<string>(Strings(r["elements"]));
            }

            public List<string> Errors { get; } = new List<string>();

            // ---------- validateRules, line for line (JavaScript truthiness included) ----------

            public void CrossReferences()
            {
                foreach (var p in terrain.Properties()) TerrainRefs(p.Name, p.Value);
                foreach (var p in covers.Properties().Where(p => p.Value["damage"] != null && !IsDice(p.Value["damage"])))
                    Errors.Add($"cover {p.Name}: damage must be [dice, sides]");
                var seen = new HashSet<string>();
                foreach (var rule in r["cellRules"].Concat(r["entityRules"])) Rule(rule, seen);
                foreach (var s in r["spells"]) SpellRefs(s);
                foreach (var c in r["critters"].Where(c => !IsElement(c["element"])))
                    Errors.Add($"critter {Show(c["id"])}: unknown element {Show(c["element"])}");
                foreach (var c in r["critters"].Where(c => c["hours"] != null && !IsHours(c["hours"])))
                    Errors.Add($"critter {Show(c["id"])}: hours must be [from, to], hours from 0 to 24");
                foreach (var p in ((JObject)r["machines"]).Properties())
                {
                    if (terrain[p.Name] == null) Errors.Add($"machine {p.Name}: no terrain with that id");
                    else if (!Strings(terrain[p.Name]["tags"]).Contains("machine")) Errors.Add($"machine {p.Name}: terrain lacks the machine tag");
                }
                foreach (var p in ((JObject)r["entities"]).Properties()) EntityRefs(p);
                DefenseRefs();
            }

            // A raider's pace and the elements it's weak to or resists (Defense.cs).
            void RaiderRefs(JProperty p)
            {
                if (!(p.Value["raider"] is JObject raider)) return;
                var pace = raider["ticksPerSquare"];
                if (pace != null && !(IsWholeNumber(pace) && (double)pace >= 1)) Errors.Add($"entity {p.Name}: raider ticksPerSquare must be a whole number from 1");
                foreach (var e in Strings(raider["weak"]).Where(e => !elements.Contains(e))) Errors.Add($"entity {p.Name}: raider is weak to unknown element {e}");
                foreach (var e in Strings(raider["resist"]).Where(e => !elements.Contains(e))) Errors.Add($"entity {p.Name}: raider resists unknown element {e}");
            }

            // The defense field's raider kinds must be raiders.
            void DefenseRefs()
            {
                var entities = (JObject)r["entities"];
                foreach (var k in Strings(r["sim"]?["defense"]?["kinds"]))
                    if (!(entities[k] is JObject e && e["raider"] is JObject)) Errors.Add($"sim defense: kind {k} is not a raider");
            }

            void EntityRefs(JProperty p)
            {
                RaiderRefs(p);
                ComponentList($"entity {p.Name}", p.Value["components"]);
                var explodes = p.Value["explodes"];
                if (Truthy(explodes) && !(explodes is JObject && IsIn(covers, explodes["cover"]))) Errors.Add($"entity {p.Name}: explodes into unknown cover");
                if (explodes is JObject && !IsCrater(explodes["crater"])) Errors.Add($"entity {p.Name}: crater needs a whole depth from 1, radius from 0 and strength from 1");
            }

            void SpellRefs(JToken s)
            {
                if (Truthy(s["element"]) && !IsElement(s["element"])) Errors.Add($"spell {Show(s["id"])}: unknown element {Show(s["element"])}");
                if (!Shapes.Contains(Text(s["shape"]) ?? "")) Errors.Add($"spell {Show(s["id"])}: unknown shape {Show(s["shape"])}");
                if (!Truthy(s["name"])) Errors.Add($"spell {Show(s["id"])}: needs a name");
                if (s["effect"] != null && !SpellEffects.Contains(Text(s["effect"]) ?? "")) Errors.Add($"spell {Show(s["id"])}: unknown effect {Show(s["effect"])}");
                if (Text(s["effect"]) == "terraform") TerraformRefs(s);
                if (!IsCrater(s["crater"])) Errors.Add($"spell {Show(s["id"])}: crater needs a whole depth from 1, radius from 0 and strength from 1");
            }

            /// <summary>No crater, or one with a whole depth from 1 and (if given) a whole radius from 0.</summary>
            static bool IsCrater(JToken c) =>
                c == null || (c is JObject && IsWholeNumber(c["depth"]) && (double)c["depth"] >= 1
                    && (c["radius"] == null || (IsWholeNumber(c["radius"]) && (double)c["radius"] >= 0))
                    && (c["strength"] == null || (IsWholeNumber(c["strength"]) && (double)c["strength"] >= 1)));

            void TerraformRefs(JToken s)
            {
                var range = r["sim"]?["height"];
                if (new[] { "raise", "dig", "height" }.Count(k => s[k] != null) != 1)
                    Errors.Add($"spell {Show(s["id"])}: terraform needs exactly one of raise, dig or height");
                foreach (var key in new[] { "raise", "dig" })
                    if (s[key] != null && !(IsWholeNumber(s[key]) && (double)s[key] >= 1))
                        Errors.Add($"spell {Show(s["id"])}: {key} must be a whole number of steps from 1");
                if (s["height"] != null && !IsHeightIn(s["height"], range))
                    Errors.Add($"spell {Show(s["id"])}: height must be a whole number from {Show(range?["min"])} to {Show(range?["max"])} (sim.height)");
                TerraformExtras(s);
            }

            // A terraform's terrain to leave, taper and strength.
            void TerraformExtras(JToken s)
            {
                var to = s["terrain"];
                if (to != null && !IsIn(terrain, to)) Errors.Add($"spell {Show(s["id"])}: terrain {Show(to)} unknown");
                else if (to != null && Strings(terrain[Text(to)]["tags"]).Contains("blocking"))
                    Errors.Add($"spell {Show(s["id"])}: terrain {Show(to)} is blocking (walls come from raise)");
                if (s["taper"] != null && s["taper"].Type != JTokenType.Boolean) Errors.Add($"spell {Show(s["id"])}: taper must be true or false");
                if (s["strength"] != null && !(IsWholeNumber(s["strength"]) && (double)s["strength"] >= 1)) Errors.Add($"spell {Show(s["id"])}: strength must be a whole number from 1");
            }

            /// <summary>A whole number within sim.height min..max (a bound that isn't a number doesn't bind, as in JavaScript).</summary>
            static bool IsHeightIn(JToken h, JToken range)
            {
                bool Below(JToken bound) => IsNumber(bound) && (double)h < (double)bound;
                bool Above(JToken bound) => IsNumber(bound) && (double)h > (double)bound;
                return IsWholeNumber(h) && !Below(range?["min"]) && !Above(range?["max"]);
            }

            static bool IsNumber(JToken v) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);

            static bool IsWholeNumber(JToken v) =>
                v.Type == JTokenType.Integer || (v.Type == JTokenType.Float && System.Math.Abs((double)v - System.Math.Floor((double)v)) < 1e-9);

            void TerrainRefs(string id, JToken t)
            {
                foreach (var key in new[] { "burnsTo", "breaksTo", "dugTo" }) KnownTerrain(id, t, key);
                if (t["hardness"] != null && !(IsWholeNumber(t["hardness"]) && (double)t["hardness"] >= 1)) Errors.Add($"terrain {id}: hardness must be a whole number from 1");
                if (Truthy(t["uprootsTo"]) && !IsIn(terrain, t["uprootsTo"])) Errors.Add($"terrain {id}: uprootsTo {Show(t["uprootsTo"])} unknown");
                else if (Truthy(t["uprootsTo"]) && Strings(terrain[Text(t["uprootsTo"])]["tags"]).Contains("blocking"))
                    Errors.Add($"terrain {id}: uprootsTo {Show(t["uprootsTo"])} is blocking");
                KnownTerrain(id, t, "growsTo");
                if (Strings(t["tags"]).Contains("flammable") && !(Truthy(t["fuel"]) && Truthy(t["flammability"])))
                    Errors.Add($"terrain {id}: flammable without fuel/flammability");
            }

            // A terrain's link to another (burnsTo, breaksTo, dugTo, growsTo) names a terrain there is.
            void KnownTerrain(string id, JToken t, string key)
            {
                if (Truthy(t[key]) && !IsIn(terrain, t[key])) Errors.Add($"terrain {id}: {key} {Show(t[key])} unknown");
            }

            void Rule(JToken rule, HashSet<string> seen)
            {
                var id = rule is JObject ? Text(rule["id"]) : null;
                if (id == null)
                {
                    Errors.Add("a reaction rule has no id");
                    return;
                }
                if (!seen.Add(id)) Errors.Add($"rule {id}: duplicate id");
                if (!IsElement(rule["element"])) Errors.Add($"rule {id}: unknown element {Show(rule["element"])}");
                if (!(rule["do"] is JArray effects))
                {
                    Errors.Add($"rule {id}: \"do\" must be a list");
                    return;
                }
                var cond = rule["if"] as JObject;
                foreach (var k in Keys(cond).Where(k => !CondKeys.Contains(k))) Errors.Add($"rule {id}: unknown condition \"{k}\"");
                foreach (var k in effects.SelectMany(Keys).Where(k => !EffectKeys.Contains(k)))
                    Errors.Add($"rule {id}: unknown effect \"{k}\" (new verbs need code; see docs/MODDING.md)");
                foreach (var e in effects.OfType<JObject>()) EffectRefs(id, e);
                ComponentCond(id, cond, "has");
                ComponentCond(id, cond, "lacks");
                if (cond?["cover"] is JArray coverIds)
                    foreach (var cv in coverIds.Where(cv => !IsIn(covers, cv))) Errors.Add($"rule {id}: cover {Show(cv)} unknown");
            }

            void EffectRefs(string id, JObject e)
            {
                if (Truthy(e["setTerrain"]) && !IsIn(terrain, e["setTerrain"])) Errors.Add($"rule {id}: setTerrain {Show(e["setTerrain"])} unknown");
                if (Truthy(e["raise"]) && !IsIn(terrain, e["raise"])) Errors.Add($"rule {id}: raise {Show(e["raise"])} unknown");
                if (Text(e["setCover"]) != null && !IsIn(covers, e["setCover"])) Errors.Add($"rule {id}: cover {Show(e["setCover"])} unknown");
                if (Truthy(e["gas"]) && !IsIn(gases, e["gas"])) Errors.Add($"rule {id}: gas {Show(e["gas"])} unknown");
                if (Truthy(e["grow"]) && !IsIn(terrain, e["grow"])) Errors.Add($"rule {id}: grow {Show(e["grow"])} unknown");
                else if (Truthy(e["grow"]) && Strings(terrain[Text(e["grow"])]["tags"]).Contains("blocking"))
                    Errors.Add($"rule {id}: grow {Show(e["grow"])} is blocking (let it growsTo one)");
                if (e["ticks"] != null && !IsTickRange(e["ticks"])) Errors.Add($"rule {id}: ticks must be [min, max], whole numbers from 1");
            }

            /// <summary>[min, max]: whole numbers, 1 &lt;= min &lt;= max.</summary>
            static bool IsTickRange(JToken t) =>
                t is JArray a && a.Count == 2 && IsNumber(a[0]) && IsNumber(a[1]) && IsWholeNumber(a[0]) && IsWholeNumber(a[1])
                && (double)a[0] >= 1 && (double)a[1] >= (double)a[0];

            /// <summary>[dice, sides]: whole numbers from 1.</summary>
            static bool IsDice(JToken t) =>
                t is JArray a && a.Count == 2 && IsNumber(a[0]) && IsNumber(a[1]) && IsWholeNumber(a[0]) && IsWholeNumber(a[1])
                && (double)a[0] >= 1 && (double)a[1] >= 1;

            bool IsElement(JToken t) => elements.Contains(Text(t) ?? "");
            static bool IsIn(JObject table, JToken id) => Text(id) != null && table[Text(id)] != null;
            static IEnumerable<string> Keys(JToken t) => t is JObject o ? o.Properties().Select(p => p.Name) : Enumerable.Empty<string>();

            // ---------- numbers: not in the TS ----------

            /// <summary>
            /// Numbers the typed tables read must be numbers (the TS would carry on with NaN), and
            /// the ones that can't sensibly be negative aren't.
            /// </summary>
            public void Numbers()
            {
                foreach (var p in terrain.Properties())
                {
                    Tags(p.Value["tags"], $"terrain {p.Name}");
                    Number(p.Value["flammability"], $"terrain {p.Name}: flammability", 0);
                    Number(p.Value["fuel"], $"terrain {p.Name}: fuel", 0);
                    Number(p.Value["spring"], $"terrain {p.Name}: spring", 0);
                    Number(p.Value["elevation"], $"terrain {p.Name}: elevation");
                }
                foreach (var p in covers.Properties())
                {
                    Tags(p.Value["tags"], $"cover {p.Name}");
                    Number(p.Value["flammability"], $"cover {p.Name}: flammability", 0);
                    Number(p.Value["fuel"], $"cover {p.Name}: fuel", 0);
                    Number(p.Value["burnIntensity"], $"cover {p.Name}: burnIntensity", 0);
                    Number(p.Value["lasts"], $"cover {p.Name}: lasts", 0);
                    Number(p.Value["speed"], $"cover {p.Name}: speed", 0);
                }
                foreach (var p in gases.Properties())
                {
                    Number(p.Value["scald"], $"gas {p.Name}: scald", 0);
                    Number(p.Value["decay"], $"gas {p.Name}: decay", 0);
                    Number(p.Value["blocksSightAt"], $"gas {p.Name}: blocksSightAt", 0);
                }
                foreach (var s in r["spells"])
                    foreach (var key in SpellNumbers) Number(s[key], $"spell {Show(s["id"])}: {key}", 0);
                foreach (var rule in r["cellRules"].OfType<JObject>()) RuleNumbers(rule);
                Sim((JObject)r["sim"], "");
                var min = (r["sim"]["height"] as JObject)?["min"];
                if (min != null && (min.Type == JTokenType.Integer || min.Type == JTokenType.Float) && (double)min > 0)
                    Errors.Add("sim height.min must be at most 0");
                // A chunk must stay active at least as far out as it activates, or it would flicker.
                var streaming = r["sim"]["streaming"] as JObject;
                if (streaming?["reach"] is JValue reach && streaming["keep"] is JValue keep
                    && (reach.Type == JTokenType.Integer || reach.Type == JTokenType.Float)
                    && (keep.Type == JTokenType.Integer || keep.Type == JTokenType.Float)
                    && (double)keep < (double)reach)
                    Errors.Add("sim streaming.keep must be at least streaming.reach");
            }

            void RuleNumbers(JObject rule)
            {
                var id = Show(rule["id"]);
                Number((rule["if"] as JObject)?["fireBelow"], $"rule {id}: fireBelow");
                Number((rule["if"] as JObject)?["power"], $"rule {id}: power");
                Number((rule["if"] as JObject)?["chance"], $"rule {id}: chance", 0);
                if (!(rule["do"] is JArray effects)) return;
                foreach (var e in effects.OfType<JObject>())
                    foreach (var key in EffectNumbers) Number(e[key], $"rule {id}: {key}");
            }

            void Sim(JObject o, string path)
            {
                foreach (var p in o.Properties()) SimEntry(path + p.Name, p.Value);
            }

            // One sim.json entry: a section to go into, hours, names, or a number.
            void SimEntry(string key, JToken value)
            {
                if (key == "defense.kinds") return; // names, checked by DefenseRefs
                if (key == "day.dawn" || key == "day.dusk")
                {
                    if (!IsHours(value)) Errors.Add($"sim {key} must be [from, to], hours from 0 to 24");
                    return;
                }
                if (value is JObject sub) Sim(sub, key + ".");
                else if (key == "height.min") Number(value, $"sim {key}");
                else Number(value, $"sim {key}", PositiveSim.Contains(key) ? 1 : 0);
            }

            /// <summary>[from, to]: two hours of the day, 0 to 24 (Sky.InHours; from after to runs across midnight).</summary>
            static bool IsHours(JToken t) =>
                t is JArray a && a.Count == 2 && a.All(h => (h.Type == JTokenType.Integer || h.Type == JTokenType.Float) && (double)h >= 0 && (double)h <= 24);

            void Number(JToken v, string what, int? min = null)
            {
                if (v == null || v.Type == JTokenType.Null) return;
                if (v.Type != JTokenType.Integer && v.Type != JTokenType.Float) Errors.Add($"{what} must be a number");
                else if (min.HasValue && (double)v < min.Value) Errors.Add($"{what} must be at least {min.Value}");
            }

            void Tags(JToken v, string what)
            {
                if (!(v is JArray a && a.All(x => x.Type == JTokenType.String))) Errors.Add($"{what}: tags must be a list of names");
            }

            /// <summary>How a JavaScript template literal prints a value.</summary>
            static string Show(JToken t)
            {
                if (t == null || t.Type == JTokenType.Undefined) return "undefined";
                if (t.Type == JTokenType.Null) return "null";
                return t.Type == JTokenType.String ? (string)t : t.ToString(Formatting.None);
            }
        }
    }
}
