// Checks a composed rule set before it can switch on: validateRules from
// sandbox/src/rules.ts with the same messages, then the numbers this port
// reads, which the TS leaves to JavaScript.
using System;
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
            check.Combos();
            check.Numbers();
            check.Lapses();
            check.Liquids();
            check.Buildings();
            check.Things();
            check.Lays();
            check.Triggers();
            check.Fauna();
            return check.Errors;
        }

        /// <summary>Whether a rule's or a spell's condition has this name.</summary>
        internal static bool IsCondition(string key) => Checker.CondKeys.Contains(key);

        /// <summary>Whether a rule's effect has this name.</summary>
        internal static bool IsEffect(string key) => Checker.EffectKeys.Contains(key);

        /// <summary>The conditions, for a sentence saying what there is.</summary>
        internal static IEnumerable<string> Conditions => Checker.CondKeys.OrderBy(k => k, StringComparer.Ordinal);

        /// <summary>The effects, for a sentence saying what there is.</summary>
        internal static IEnumerable<string> Effects => Checker.EffectKeys.OrderBy(k => k, StringComparer.Ordinal);

        sealed partial class Checker
        {
            internal static readonly HashSet<string> CondKeys = new HashSet<string>(RuleSchema.Conditions.Names);
            internal static readonly HashSet<string> EffectKeys = new HashSet<string>(RuleSchema.Effects.Names);
            static readonly HashSet<string> Shapes = new HashSet<string> { "point", "line", "square", "burst", "target", "cone" };
            static readonly HashSet<string> SpellEffects = new HashSet<string> { "levitate", "carry", "teleport", "terraform", "haste", "weather", "shapeshift", "light" };

            static readonly string[] SpellNumbers = { "level", "power", "length", "size", "radius", "push", "wind", "range", "duration", "maxMass", "rate", "radiusPerCharge" };
            /// <summary>Sim numbers the world divides by or sizes things with.</summary>
            static readonly HashSet<string> PositiveSim = new HashSet<string> { "ticksPerSecond", "water.maxLevel", "water.unitsPerStep", "height.blockAt", "weather.rain.fillEvery", "weather.rain.dryEvery", "weather.storm.strikeEvery", "weather.snow.freezeEvery", "weather.fog.every", "weather.fog.regionChunks", "weather.fog.driftTicks", "weather.meteors.every", "weather.meteors.fallTicks", "streaming.reach", "streaming.keep", "streaming.settleTicks", "streaming.lingerChunks", "streaming.lingerTicks", "streaming.catchUpTicks", "dragon.speed", "dragon.depth", "dragon.breathChunks", "dragon.chunksPerTick", "breath.seconds", "breath.drownEvery", "breath.drownSides", "strike.reach", "dragon.scale" };

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
                FogRef();
                ProductRefs();
            }

            // Fog weather lays a gas the rules know (sim weather.fog.gas).
            void FogRef()
            {
                var gas = r["sim"]?["weather"]?["fog"]?["gas"];
                if (gas != null && !(gas.Type == JTokenType.String && gases[(string)gas] is JObject)) Errors.Add($"sim weather.fog.gas: unknown gas {Show(gas)}");
            }

            // A raider's pace and the elements it's weak to or resists (Defense.cs).
            void RaiderRefs(JProperty p)
            {
                if (!(p.Value["raider"] is JObject raider)) return;
                Values($"entity {p.Name}", "raider ", raider, RuleSchema.Entities.SectionOf("raider"));
                foreach (var e in Strings(raider["weak"]).Where(e => !elements.Contains(e))) Errors.Add($"entity {p.Name}: raider is weak to unknown element {e}");
                foreach (var e in Strings(raider["resist"]).Where(e => !elements.Contains(e))) Errors.Add($"entity {p.Name}: raider resists unknown element {e}");
            }

            // The defense field's raider kinds must be raiders.
            void DefenseRefs()
            {
                var entities = (JObject)r["entities"];
                foreach (var k in Strings(r["sim"]?["defense"]?["kinds"]))
                    if (!(entities[k] is JObject e && e["raider"] is JObject)) Errors.Add($"sim defense: kind {k} is not a raider");
                foreach (var t in Strings(r["sim"]?["dragon"]?["wallTerrains"]))
                    if (terrain[t] == null) Errors.Add($"sim dragon: unknown terrain {t}");
            }

            // A thing's material is a terrain (what it's made of).
            internal void MaterialRef(string what, JToken m)
            {
                if (m != null && !IsIn(terrain, m)) Errors.Add($"{what}: material {Show(m)} unknown");
            }

            void EntityRefs(JProperty p)
            {
                RaiderRefs(p);
                ComponentList($"entity {p.Name}", p.Value["components"]);
                MaterialRef($"entity {p.Name}", p.Value["material"]);
                var explodes = p.Value["explodes"];
                if (Truthy(explodes) && !(explodes is JObject && IsIn(covers, explodes["cover"]))) Errors.Add($"entity {p.Name}: explodes into unknown cover");
                if (explodes is JObject && !IsCrater(explodes["crater"])) Errors.Add($"entity {p.Name}: crater needs a whole depth from 1, radius from 0 and strength from 1");
                Values($"entity {p.Name}", "", p.Value, RuleSchema.Entities);
            }

            void SpellRefs(JToken s)
            {
                if (Truthy(s["element"]) && !IsElement(s["element"])) Errors.Add($"spell {Show(s["id"])}: unknown element {Show(s["element"])}");
                if (!Shapes.Contains(Text(s["shape"]) ?? "")) Errors.Add($"spell {Show(s["id"])}: unknown shape {Show(s["shape"])}");
                if (!Truthy(s["name"])) Errors.Add($"spell {Show(s["id"])}: needs a name");
                if (s["inward"] != null && s["inward"].Type != JTokenType.Boolean) Errors.Add($"spell {Show(s["id"])}: inward must be true or false");
                if (s["light"] != null && !(s["light"] is JObject l && new[] { "bright", "dim" }.All(k => l[k] != null && IsWholeNumber(l[k]) && (double)l[k] >= 0)))
                    Errors.Add($"spell {Show(s["id"])}: light must be {{ bright, dim }}, whole feet from 0");
                if (s["effect"] != null && !SpellEffects.Contains(Text(s["effect"]) ?? "")) Errors.Add($"spell {Show(s["id"])}: unknown effect {Show(s["effect"])}");
                if (Text(s["effect"]) == "terraform") TerraformRefs(s);
                if (!IsCrater(s["crater"])) Errors.Add($"spell {Show(s["id"])}: crater needs a whole depth from 1, radius from 0 and strength from 1");
                CraterLeaves(s);
                PullEvery(s);
                Landing(s);
                Carries(s);
            }

            // A delayed cast's ticks, what it scatters and the element at its core.
            void Landing(JToken s)
            {
                if (s["delay"] != null && !(IsWholeNumber(s["delay"]) && (double)s["delay"] >= 1)) Errors.Add($"spell {Show(s["id"])}: delay must be a whole number from 1");
                if (s["scatter"] != null && !IsScatter(s["scatter"])) Errors.Add($"spell {Show(s["id"])}: scatter needs a known cover and a whole count and reach from 1");
                if (s["core"] != null && !IsElement(s["core"])) Errors.Add($"spell {Show(s["id"])}: unknown core {Show(s["core"])}");
                if (s["leaves"] != null && !IsElement(s["leaves"])) Errors.Add($"spell {Show(s["id"])}: unknown leaves {Show(s["leaves"])}");
                if (s["trail"] != null && !IsIn(gases, s["trail"])) Errors.Add($"spell {Show(s["id"])}: trail {Show(s["trail"])} is not a gas");
                foreach (var k in Keys(s["needs"] as JObject).Where(k => !CondKeys.Contains(k))) Errors.Add($"spell {Show(s["id"])}: needs has unknown condition \"{k}\"");
                if (s["place"] != null) PlaceRefs(s["place"], $"spell {Show(s["id"])}: place");
            }

            static readonly string[] PlaceWhats = { "thing", "animal", "terrain", "cover" };
            static readonly string[] PlaceKeys = PlaceWhats.Concat(new[] { "count", "within", "spacing", "lasts", "onto" }).ToArray();

            // What a spell or a trigger places: exactly one row that exists, whole numbers, and tags some terrain has.
            void PlaceRefs(JToken place, string who)
            {
                if (!(place is JObject p))
                {
                    Errors.Add($"{who} must be an object");
                    return;
                }
                foreach (var k in Keys(p).Where(k => !PlaceKeys.Contains(k))) Errors.Add($"{who} has no field \"{k}\"; it takes {string.Join(", ", PlaceKeys)}");
                var whats = PlaceWhats.Where(k => p[k] != null).ToList();
                if (whats.Count != 1) Errors.Add($"{who} needs exactly one of thing, animal, terrain or cover");
                if (p["thing"] != null && !IsIn(r["entities"] as JObject, p["thing"]) && !IsIn(r["items"] as JObject, p["thing"])) Errors.Add($"{who}: thing {Show(p["thing"])} is neither an entity nor an item");
                if (p["animal"] != null && !IsIn(r[FaunaTables.SpeciesKey] as JObject, p["animal"])) Errors.Add($"{who}: animal {Show(p["animal"])} is not a species of fauna.json");
                if (p["terrain"] != null && !IsIn(terrain, p["terrain"])) Errors.Add($"{who}: unknown terrain {Show(p["terrain"])}");
                if (p["cover"] != null && !IsIn(covers, p["cover"])) Errors.Add($"{who}: unknown cover {Show(p["cover"])}");
                if (p["count"] != null && !(IsWholeNumber(p["count"]) && (double)p["count"] >= 1)) Errors.Add($"{who}: count must be a whole number from 1");
                foreach (var k in new[] { "within", "spacing", "lasts" })
                    if (p[k] != null && !(IsWholeNumber(p[k]) && (double)p[k] >= 0)) Errors.Add($"{who}: {k} must be a whole number from 0");
                if (p["lasts"] != null && p["thing"] == null) Errors.Add($"{who}: only a thing lasts a while; cells stay as laid and an animal lives on");
                if (p["onto"] == null) return;
                var tags = new HashSet<string>(terrain.Properties().SelectMany(t => Strings(t.Value["tags"])));
                if (!(p["onto"] is JArray onto) || onto.Any(t => t.Type != JTokenType.String)) Errors.Add($"{who}: onto must be a list of terrain tags");
                else foreach (var t in onto.Where(t => !tags.Contains((string)t))) Errors.Add($"{who}: onto names the tag {Show(t)}, which no terrain has");
            }

            bool IsScatter(JToken c) =>
                c is JObject && IsIn(covers, c["cover"]) && IsWholeNumber(c["count"]) && (double)c["count"] >= 1 && IsWholeNumber(c["reach"]) && (double)c["reach"] >= 1;

            // How often a whirling spell pulls is a whole number of ticks.
            void PullEvery(JToken s)
            {
                if (s["pullEvery"] != null && !(IsWholeNumber(s["pullEvery"]) && (double)s["pullEvery"] >= 1)) Errors.Add($"spell {Show(s["id"])}: pullEvery must be a whole number from 1");
            }

            // How a whirling spell travels, and what it can take on: known conditions and a known element.
            void Carries(JToken s)
            {
                foreach (var key in new[] { "travelEvery", "dropPower" })
                    if (s[key] != null && !(IsWholeNumber(s[key]) && (double)s[key] >= 1)) Errors.Add($"spell {Show(s["id"])}: {key} must be a whole number from 1");
                foreach (var c in (s["carries"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    if (!IsElement(c["sheds"])) Errors.Add($"spell {Show(s["id"])}: carries {Show(c["id"])} sheds unknown element {Show(c["sheds"])}");
                    foreach (var k in Keys(c["if"] as JObject).Where(k => !CondKeys.Contains(k))) Errors.Add($"spell {Show(s["id"])}: carries {Show(c["id"])} has unknown condition \"{k}\"");
                }
            }

            // A gas's flash goes up with a whole fire from 1 and a whole push from 0.
            void GasFlash(JProperty gas)
            {
                var f = gas.Value["burn"]?["flash"];
                if (f == null) return;
                if (!(IsWholeNumber(f["fire"]) && (double)f["fire"] >= 1)) Errors.Add($"gas {gas.Name}: burn.flash.fire must be a whole number from 1");
                if (f["push"] != null && !(IsWholeNumber(f["push"]) && (double)f["push"] >= 0)) Errors.Add($"gas {gas.Name}: burn.flash.push must be a whole number from 0");
            }

            // A crater's leaves names a terrain there is.
            void CraterLeaves(JToken s)
            {
                var leaves = (s["crater"] as JObject)?["leaves"];
                if (Truthy(leaves) && !IsIn(terrain, leaves)) Errors.Add($"spell {Show(s["id"])}: crater leaves {Show(leaves)} unknown");
            }

            /// <summary>No crater, or one with a whole depth from 1 and (if given) a whole radius from 0.</summary>
            static bool IsCrater(JToken c) => c == null || (c is JObject crater && FitsAll(RuleSchema.Crater, crater));

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

            // What water does on it: soak rates (whole ticks), how much being damp dampens its burning, and what it turns into.
            void WaterOn(string id, JToken t)
            {
                foreach (var key in new[] { "soakEvery", "soakEveryWet", "rustEvery", "hotFor", "hotQuench", "crustFor", "thawWater", "liftSteps" })
                    if (t[key] != null && !(IsWholeNumber(t[key]) && (double)t[key] >= 1)) Errors.Add($"terrain {id}: {key} must be a whole number from 1");
                if (t["dampens"] != null && !(IsNumber(t["dampens"]) && (double)t["dampens"] >= 0 && (double)t["dampens"] <= 1)) Errors.Add($"terrain {id}: dampens must be a number from 0 to 1");
                if (t["waterTo"] != null && Text(t["waterTo"]) != MaterialGroups.FreezesWater && Text(t["waterTo"]) != MaterialGroups.BoilsWater)
                    Errors.Add($"terrain {id}: waterTo must be {MaterialGroups.FreezesWater} or {MaterialGroups.BoilsWater}");
            }

            static readonly string[] FireLooks = { "flare", "wood", "oil" };

            // How a terrain's or cover's fire looks: one the view knows.
            void FireLookOf(string what, JToken t)
            {
                if (t["fireLook"] != null && !FireLooks.Contains(Text(t["fireLook"]))) Errors.Add($"{what}: fireLook must be flare, wood or oil");
            }

            // A "look" group's shape; which families exist, and what each takes, is ModValidator's check
            // (rules/looks.json). A cover or gas may give only a colour (no family of theirs is drawn yet).
            void LookOf(string what, JToken t, bool needsFamily = true)
            {
                if (t["look"] == null) return;
                if (!(t["look"] is JObject look) || (needsFamily && Text(look["family"]) == null))
                {
                    Errors.Add(needsFamily ? $"{what}: look must be an object naming its family" : $"{what}: look must be an object");
                    return;
                }
                if (look["family"] != null && Text(look["family"]) == null) Errors.Add($"{what}: look.family must be a name");
                if (look["color"] != null && !(look["color"] is JArray c && c.Count == 3 && c.All(x => IsNumber(x) && (double)x >= 0 && (double)x <= 1)))
                    Errors.Add($"{what}: look.color must be three numbers from 0 to 1");
                foreach (var key in new[] { "height", "glow", "flow" })
                    if (look[key] != null && !(IsNumber(look[key]) && (double)look[key] >= 0)) Errors.Add($"{what}: look.{key} must be a number from 0");
                if (look["roughness"] != null && !(IsNumber(look["roughness"]) && (double)look["roughness"] >= 0 && (double)look["roughness"] <= 1))
                    Errors.Add($"{what}: look.roughness must be a number from 0 to 1");
                foreach (var m in LookScopes.ShapeProblems(look)) Errors.Add($"{what}: {m}");
            }

            // The element entries (elements.json, a mod's "elements" by id): known elements, each with only
            // a look, and that look only a colour. Only looks, so the fingerprint can leave the table out.
            void ElementLooks()
            {
                if (!(r[ElementLooksKey] is JObject table)) return;
                foreach (var p in table.Properties())
                {
                    string what = $"element {p.Name}";
                    if (!elements.Contains(p.Name)) Errors.Add($"{what}: unknown element (a look needs the element in the rules)");
                    if (!(p.Value is JObject entry))
                    {
                        Errors.Add($"{what}: must be an object such as {{\"look\": {{\"color\": [1, 0.4, 0.1]}}}}");
                        continue;
                    }
                    foreach (var key in entry.Properties().Select(x => x.Name).Where(k => k != "look"))
                        Errors.Add($"{what}: {key} unknown (an element's entry holds only its look)");
                    LookOf(what, entry, false);
                    if (entry["look"] is JObject look && look["family"] != null)
                        Errors.Add($"{what}: an element's look names no family; it is the colour of what the element makes, whatever draws it");
                }
            }

            void TerrainRefs(string id, JToken t)
            {
                StatusTicks(id, t);
                LookOf($"terrain {id}", t);
                if (t["chainsWet"] != null && t["chainsWet"].Type != JTokenType.Boolean) Errors.Add($"terrain {id}: conduct.chainsWet must be true or false");
                foreach (var key in new[] { "burnsTo", "breaksTo", "dugTo", "rustsTo", "soaksTo", "driesTo", "shattersTo", "bakesTo", "crustsTo", "thawsTo" }) KnownTerrain(id, t, key);
                if (t["hardness"] != null && !(IsWholeNumber(t["hardness"]) && (double)t["hardness"] >= 0)) Errors.Add($"terrain {id}: hardness must be a whole number from 0 (0 never digs)");
                WaterOn(id, t);
                FireLookOf($"terrain {id}", t);
                if (Truthy(t["uprootsTo"]) && !IsIn(terrain, t["uprootsTo"])) Errors.Add($"terrain {id}: uprootsTo {Show(t["uprootsTo"])} unknown");
                else if (Truthy(t["uprootsTo"]) && Strings(terrain[Text(t["uprootsTo"])]["tags"]).Contains("blocking"))
                    Errors.Add($"terrain {id}: uprootsTo {Show(t["uprootsTo"])} is blocking");
                KnownTerrain(id, t, "growsTo");
                Dissolves($"terrain {id}", t, terrain);
                Cracks(id, t);
                if (Truthy(t["dissolvedBy"]) && !Truthy(t["dissolvesTo"])) Errors.Add($"terrain {id}: dissolve without leaves");
                if (Strings(t["tags"]).Contains("flammable") && !(Truthy(t["fuel"]) && Truthy(t["flammability"])))
                    Errors.Add($"terrain {id}: flammable without fuel/flammability");
            }

            void Cracks(string id, JToken t)
            {
                if (t["cracks"] != null && t["cracks"].Type != JTokenType.Boolean) Errors.Add($"terrain {id}: cracks must be true or false");
            }

            // What a cover melts into (one of the covers) and the water it gives.
            void Melts(string id, JToken t)
            {
                if (Truthy(t["meltsTo"]) && !IsIn(covers, t["meltsTo"])) Errors.Add($"cover {id}: meltsTo {Show(t["meltsTo"])} unknown");
                if (t["meltWater"] != null && !(IsWholeNumber(t["meltWater"]) && (double)t["meltWater"] >= 1)) Errors.Add($"cover {id}: meltWater must be a whole number from 1");
            }

            // A cover's spread (World.Spread.cs): whole ticks and cells from 1, a chance from 0 to 1, and onto and
            // notOnto lists of terrain ids or tags some terrain has.
            void Spreads(string id, JToken t)
            {
                foreach (var key in new[] { "spreadEvery", "spreadReach" })
                    if (t[key] != null && !(IsWholeNumber(t[key]) && (double)t[key] >= 1)) Errors.Add($"cover {id}: {MaterialGroups.GroupedName(true, key)} must be a whole number from 1");
                if (t["spreadChance"] != null && !(IsNumber(t["spreadChance"]) && (double)t["spreadChance"] > 0 && (double)t["spreadChance"] <= 1))
                    Errors.Add($"cover {id}: spread.chance must be a number above 0, up to 1");
                foreach (var key in new[] { "spreadOnto", "spreadNotOnto" })
                    if (t[key] != null) SpreadNames(id, key, t[key]);
            }

            void SpreadNames(string id, string key, JToken list)
            {
                if (!(list is JArray names) || names.Any(n => Text(n) == null))
                {
                    Errors.Add($"cover {id}: {MaterialGroups.GroupedName(true, key)} must be a list of terrain ids or tags");
                    return;
                }
                var tags = new HashSet<string>(terrain.Properties().SelectMany(p => Strings(p.Value["tags"])));
                foreach (var name in names.Select(Text).Where(n => terrain[n] == null && !tags.Contains(n)))
                    Errors.Add($"cover {id}: {MaterialGroups.GroupedName(true, key)} {name} is no terrain or terrain tag");
            }

            // What dissolves a terrain or cover (an element) and what it leaves (one of its own kind).
            void Dissolves(string what, JToken t, JObject kind)
            {
                if (Truthy(t["dissolvedBy"]) && !IsElement(t["dissolvedBy"])) Errors.Add($"{what}: dissolves by unknown element {Show(t["dissolvedBy"])}");
                if (Truthy(t["dissolvesTo"]) && !IsIn(kind, t["dissolvesTo"])) Errors.Add($"{what}: dissolvesTo {Show(t["dissolvesTo"])} unknown");
            }

            // A terrain's link to another (burnsTo, breaksTo, dugTo, rustsTo, soaksTo, driesTo, growsTo) names a terrain there is.
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
                foreach (var e in effects.OfType<JObject>())
                {
                    EffectRefs(id, e);
                    DissolveRef(id, e);
                }
                LiquidRef(id, cond, effects.OfType<JObject>());
                ComponentCond(id, cond, "has");
                ComponentCond(id, cond, "lacks");
                VerbCond(id, cond);
                if (cond?["cover"] is JArray coverIds)
                    foreach (var cv in coverIds.Where(cv => !IsIn(covers, cv))) Errors.Add($"rule {id}: cover {Show(cv)} unknown");
            }

            // A dissolve effect eats the cover or the terrain; a statusOf names a verb a material gives a status by.
            void DissolveRef(string id, JObject e)
            {
                if (e["dissolve"] != null && Text(e["dissolve"]) != "cover" && Text(e["dissolve"]) != "terrain") Errors.Add($"rule {id}: dissolve must be cover or terrain");
                if (e["statusOf"] != null && TerrainDef.StatusOfVerb(Text(e["statusOf"])) == null) Errors.Add($"rule {id}: statusOf must be burn, freeze or soak");
            }

            // An entity rule's verb condition names a verb a material gives a status by.
            void VerbCond(string id, JToken cond)
            {
                if (cond?["verb"] != null && TerrainDef.StatusOfVerb(Text(cond["verb"])) == null) Errors.Add($"rule {id}: verb must be burn, freeze or soak");
            }

            // A material's status ticks (burn.status, freeze.status, soak.status) are whole numbers from 1.
            void StatusTicks(string id, JToken t)
            {
                foreach (var key in new[] { "burnStatus", "freezeStatus", "soakStatus" })
                    if (t[key] != null && !(IsWholeNumber(t[key]) && (double)t[key] >= 1)) Errors.Add($"terrain {id}: {key} must be a whole number from 1");
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
                    Number(p.Value["heat"], $"terrain {p.Name}: heat", 0);
                    Number(p.Value["speed"], $"terrain {p.Name}: speed", 0);
                    Number(p.Value["elevation"], $"terrain {p.Name}: elevation");
                }
                foreach (var p in covers.Properties())
                {
                    Tags(p.Value["tags"], $"cover {p.Name}");
                    FireLookOf($"cover {p.Name}", p.Value);
                    LookOf($"cover {p.Name}", p.Value, false);
                    Number(p.Value["flammability"], $"cover {p.Name}: flammability", 0);
                    Number(p.Value["fuel"], $"cover {p.Name}: fuel", 0);
                    Number(p.Value["burnIntensity"], $"cover {p.Name}: burnIntensity", 0);
                    Number(p.Value["lasts"], $"cover {p.Name}: lasts", 0);
                    Number(p.Value["speed"], $"cover {p.Name}: speed", 0);
                    if (p.Value["floatEvery"] != null && !(IsWholeNumber(p.Value["floatEvery"]) && (double)p.Value["floatEvery"] >= 1)) Errors.Add($"cover {p.Name}: floatEvery must be a whole number from 1");
                    if (p.Value["floatReach"] != null && !(IsWholeNumber(p.Value["floatReach"]) && (double)p.Value["floatReach"] >= 1)) Errors.Add($"cover {p.Name}: floatReach must be a whole number from 1");
                    Dissolves($"cover {p.Name}", p.Value, covers);
                    Melts(p.Name, p.Value);
                    Spreads(p.Name, p.Value);
                }
                foreach (var p in gases.Properties())
                {
                    GasNumbers(p);
                    LookOf($"gas {p.Name}", p.Value, false);
                }
                ElementLooks();
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
                foreach (var f in RuleSchema.Conditions.Fields.Where(f => f.Type == RuleFieldType.Number))
                    Number((rule["if"] as JObject)?[f.Name], $"rule {id}: {f.Name}", f.Ranged ? (int)f.Min : (int?)null);
                if (!(rule["do"] is JArray effects)) return;
                foreach (var e in effects.OfType<JObject>())
                    foreach (var f in RuleSchema.Effects.Fields.Where(f => f.Type == RuleFieldType.Number)) Number(e[f.Name], $"rule {id}: {f.Name}");
            }

            // One gas's numbers and its flash (materials.json gases).
            void GasNumbers(JProperty p)
            {
                Number(p.Value["scald"], $"gas {p.Name}: scald", 0);
                Number(p.Value["decay"], $"gas {p.Name}: decay", 0);
                Number(p.Value["blocksSightAt"], $"gas {p.Name}: blocksSightAt", 0);
                if (p.Value["hidesAfter"] != null && !(IsWholeNumber(p.Value["hidesAfter"]) && (double)p.Value["hidesAfter"] >= 1)) Errors.Add($"gas {p.Name}: hidesAfter must be a whole number from 1");
                if (p.Value["condenseEvery"] != null && !(IsWholeNumber(p.Value["condenseEvery"]) && (double)p.Value["condenseEvery"] >= 1)) Errors.Add($"gas {p.Name}: condenseEvery must be a whole number from 1");
                if (p.Value["condenseAway"] != null && !(IsWholeNumber(p.Value["condenseAway"]) && (double)p.Value["condenseAway"] >= 0)) Errors.Add($"gas {p.Name}: condenseAway must be a whole number from 0");
                if (p.Value["rainEvery"] != null && !(IsWholeNumber(p.Value["rainEvery"]) && (double)p.Value["rainEvery"] >= 1)) Errors.Add($"gas {p.Name}: rainEvery must be a whole number from 1");
                GasFlash(p);
            }

            void Sim(JObject o, string path)
            {
                foreach (var p in o.Properties()) SimEntry(path + p.Name, p.Value);
            }

            // One sim.json entry: a section to go into, hours, names, or a number.
            void SimEntry(string key, JToken value)
            {
                if (key == "defense.kinds" || key == "weather.fog.gas" || key == "dragon.wallTerrains" || IsSimProduct(key)) return; // names, checked by DefenseRefs, FogRef, DragonRefs and ProductRefs
                if (key.StartsWith("dragon.", StringComparison.Ordinal) && key != "dragon.scale" && !IsWholeNumber(value)) Errors.Add($"sim {key} must be a whole number");
                if (key == "status.shatter.when" || key == "status.shatter.notBy")
                {
                    if (value.Type != JTokenType.String) Errors.Add($"sim {key} must be a name");
                    return;
                }
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
