// Checks a mod before anyone switches it on (docs/RULES_MCP.md, ADR 0013): the
// patch's shape (ModPatch.Parse), its references and numbers (Rules.WithMods),
// then what those don't cover: the design rules, determinism, the budgets in
// rules/budgets.json and the looks (every new thing names a family of
// rules/looks.json the view draws). The rules MCP server (core/Rules.Mcp) calls it; the
// game can too. Every problem is one plain sentence in the glossary's words.
// C# only: the sandbox has no counterpart yet.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>Which of validate's checks a problem comes from.</summary>
    public enum ModCheck { Shape, References, Design, Determinism, Budget, Looks, Site }

    /// <summary>One problem with a mod: the check that found it and one sentence saying what to fix.</summary>
    public readonly struct ModProblem
    {
        public ModProblem(ModCheck check, string message)
        {
            Check = check;
            Message = message;
        }

        public ModCheck Check { get; }
        public string Message { get; }

        public override string ToString() => $"{Check.ToString().ToLowerInvariant()}: {Message}";
    }

    /// <summary>rules/budgets.json: what one mod may ask of the engine.</summary>
    public sealed class ModBudgets
    {
        ModBudgets(JObject json) { Json = json; }

        internal JObject Json { get; }

        /// <summary>The look and effect families a mod's new things may name (rules/looks.json); null skips that check.</summary>
        public LookFamilies Looks { get; private set; }

        /// <summary>The world's places a scoped look may name (world/places.json beside rules/); empty when there's none.</summary>
        public WorldPlaces Places { get; private set; } = WorldPlaces.Empty;

        /// <summary>The buildings a world was made with (world/buildings.json, villages.json), which a mod may edit by id.</summary>
        public WorldBuildings Buildings { get; private set; } = WorldBuildings.Empty;

        /// <summary>The world's things (world/things.json): a building's site keeps clear of them.</summary>
        public WorldThings Things { get; private set; } = WorldThings.Empty;

        public static ModBudgets Parse(string json) => new ModBudgets(JObject.Parse(json));

        /// <summary>rules/budgets.json with the family list of rules/looks.json: all a peer checks a mod against.</summary>
        public static ModBudgets Load(string rulesDir)
        {
            var budgets = Parse(File.ReadAllText(Path.Combine(rulesDir, "budgets.json")));
            budgets.Looks = LookFamilies.Load(rulesDir);
            string worldDir = new GameFolders(rulesDir).World;
            budgets.Places = WorldPlaces.Load(worldDir);
            budgets.Buildings = WorldBuildings.Load(worldDir);
            if (Directory.Exists(worldDir)) budgets.Things = WorldThings.Load(worldDir);
            return budgets;
        }

        /// <summary>A number under a dotted path such as "spells.maxRadius", or the fallback.</summary>
        public double Number(string path, double fallback) =>
            Json.SelectToken(path) is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (double)v : fallback;

        /// <summary>The sim parameters with declared bounds, as (dotted path, min, max).</summary>
        public IEnumerable<(string path, double min, double max)> SimBounds =>
            (Json["sim"] as JObject)?.Properties()
                .Where(p => p.Value is JArray a && a.Count == 2)
                .Select(p => (p.Name, (double)p.Value[0], (double)p.Value[1]))
            ?? Enumerable.Empty<(string, double, double)>();
    }

    public static class ModValidator
    {
        static readonly HashSet<string> Sections = new HashSet<string>(ModPatch.Tables.Concat(new[] { "elements", "reactions", "spells", "critters", "sim", "combos", BuildingTables.Key, "world", Rules.TriggersKey }));
        static readonly string[] RandomKeys = { "seed", "random", "rng" };

        /// <summary>
        /// Every problem with a patch (bare or {"title", "patch"}) over these base rules; empty means
        /// it may switch on. Stops after the shape check if the patch can't be read. A room passes its
        /// temporary materials by id with the mod each is from (TempMaterials.Owners): no other mod may name one.
        /// </summary>
        public static IReadOnlyList<ModProblem> Validate(Rules baseRules, ModBudgets budgets, string patchJson, IReadOnlyDictionary<string, string> temporary = null)
        {
            var problems = new List<ModProblem>();
            ModPatch mod;
            try
            {
                mod = ModPatch.Parse(patchJson);
            }
            catch (ModException e)
            {
                problems.AddRange(e.Errors.Select(m => new ModProblem(ModCheck.Shape, Sentence(m))));
                return problems;
            }
            var baseDoc = JObject.Parse(baseRules.ToJson());
            var v = new Pass(baseDoc, mod.Json, budgets, problems);
            v.Shape();
            JObject composed = null;
            try
            {
                composed = JObject.Parse(baseRules.WithMods(new[] { mod }).ToJson());
            }
            catch (ModException e)
            {
                problems.AddRange(e.Errors.Select(m => new ModProblem(ModCheck.References, Sentence(m))));
            }
            v.Design(composed);
            v.Determinism();
            v.Budgets(composed);
            v.Temporary(temporary);
            if (budgets.Looks != null) v.Looks(budgets.Looks, composed);
            return problems;
        }

        /// <summary>A spell's numbers past the budget's caps, one sentence each: what validate says, and set_spell at once.</summary>
        internal static IEnumerable<string> SpellCaps(ModBudgets budgets, string id, JObject s)
        {
            if (s["place"] is JObject place)
                foreach (var (key, cap, why) in new[] { ("count", "maxCount", "things a cast places"), ("within", "maxWithin", "cells round the aim it places within"), ("lasts", "maxLasts", "ticks a placed thing lasts") })
                {
                    double max = budgets.Number("place." + cap, double.MaxValue);
                    if (place[key] != null && (place[key].Type == JTokenType.Integer || place[key].Type == JTokenType.Float) && (double)place[key] > max)
                        yield return $"Spell {id} places with a {key} of {place[key]}; the most {why} is {max.ToString("0.###", CultureInfo.InvariantCulture)}.";
                }
            foreach (var (key, cap) in new[] { ("power", "maxPower"), ("radius", "maxRadius"), ("length", "maxLength"), ("size", "maxSize"), ("push", "maxPush"), ("raise", "maxRaise"), ("dig", "maxDig") })
            {
                double max = budgets.Number("spells." + cap, double.MaxValue);
                if (s[key] != null && (s[key].Type == JTokenType.Integer || s[key].Type == JTokenType.Float) && (double)s[key] > max)
                    yield return $"Spell {id} has a {key} of {s[key]}; a mod's spell may have at most {max.ToString("0.###", CultureInfo.InvariantCulture)}, so one cast can't cover a chunk or stall the tick.";
            }
        }

        /// <summary>The engine's terse messages ("spell x: unknown element y") as a sentence.</summary>
        static string Sentence(string m)
        {
            if (string.IsNullOrEmpty(m)) return m;
            m = char.ToUpperInvariant(m[0]) + m.Substring(1);
            return m.EndsWith('.') ? m : m + ".";
        }

        sealed class Pass
        {
            readonly JObject baseDoc, patch;
            readonly ModBudgets budgets;
            readonly List<ModProblem> problems;

            public Pass(JObject baseDoc, JObject patch, ModBudgets budgets, List<ModProblem> problems)
            {
                this.baseDoc = baseDoc;
                this.patch = patch;
                this.budgets = budgets;
                this.problems = problems;
            }

            static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
            static string Text(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
            static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);
            static bool IsWhole(JToken t) => IsNumber(t) && Math.Abs((double)t - Math.Floor((double)t)) < 1e-9;

            void Add(ModCheck check, string message) => problems.Add(new ModProblem(check, message));

            IEnumerable<JObject> PatchSpells => (patch["spells"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
            IEnumerable<JObject> PatchCritters => (patch["critters"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
            IEnumerable<JObject> PatchRules =>
                new[] { "add", "addEntity" }.SelectMany(k => (patch["reactions"]?[k] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>());

            bool InBase(string list, string id) => baseDoc[list] is JArray a && a.Any(x => Text(x["id"]) == id);

            // ---------- shape: what Parse lets through ----------

            public void Shape()
            {
                foreach (var p in patch.Properties().Where(p => !Sections.Contains(p.Name)))
                    Add(ModCheck.Shape, $"\"{p.Name}\" isn't a section of a mod patch; the sections are {string.Join(", ", Sections.OrderBy(s => s, StringComparer.Ordinal))}.");
                Ids("spell", PatchSpells);
                Ids("critter", PatchCritters);
                Ids("reaction rule", PatchRules);
            }

            void Ids(string what, IEnumerable<JObject> entries)
            {
                var seen = new HashSet<string>();
                foreach (var e in entries)
                {
                    var id = Text(e["id"]);
                    if (string.IsNullOrEmpty(id)) Add(ModCheck.Shape, $"Every {what} needs an id, and one has none.");
                    else if (!seen.Add(id)) Add(ModCheck.Shape, $"The {what} {id} is in the patch twice; give each one its own id.");
                }
            }

            // ---------- the looks: every new thing names a family the view draws ----------

            JObject fauna; // the species in force with the mod on; null if it didn't compose

            // A patch's entry names only what it changes, so its look is checked as it composes.
            public void Looks(LookFamilies looks, JObject composed)
            {
                fauna = composed?[FaunaTables.SpeciesKey] as JObject;
                var tables = new[] { ("terrain", "Terrain", true), ("covers", "Cover", false), ("gases", "Gas", false), ("entities", "Entity", false), ("items", "Item", false) };
                foreach (var (table, what, required) in tables)
                    if (patch[table] is JObject entries)
                        foreach (var p in entries.Properties().Where(p => p.Value is JObject))
                            LookOf(looks, table, $"{what} {p.Name}", composed?[table]?[p.Name] as JObject ?? (JObject)p.Value, required && baseDoc[table]?[p.Name] == null);
                foreach (var s in PatchSpells) LookOf(looks, "spells", $"Spell {Text(s["id"]) ?? "?"}", SpellIn(composed, s), !InBase("spells", Text(s["id"])));
                if (patch["elements"] is JObject elements)
                    foreach (var p in elements.Properties().Where(p => p.Value is JObject))
                        LookOf(looks, "elements", $"Element {p.Name}", composed?[Rules.ElementLooksKey]?[p.Name] as JObject ?? (JObject)p.Value, false);
                ThingsHaveLooks(composed);
            }

            static JObject SpellIn(JObject composed, JObject s) =>
                (composed?["spells"] as JArray)?.OfType<JObject>().FirstOrDefault(c => Text(c["id"]) == Text(s["id"])) ?? s;

            // A kind the rules came with has its baked prefab; a new one has only its look.
            void ThingsHaveLooks(JObject composed)
            {
                foreach (var row in (patch["world"]?["add"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    string kind = Text(row["kind"]);
                    if (kind == null || baseDoc["entities"]?[kind] != null || baseDoc["items"]?[kind] != null) continue;
                    var entry = composed?["items"]?[kind] ?? composed?["entities"]?[kind];
                    if (entry != null && !(entry["look"] is JObject)) Add(ModCheck.Looks, $"Thing {kind} is a new kind with no look, so the view can't draw it; give it a \"look\" with family creature.");
                }
            }

            // Covers and gases have no family drawn yet, so a new one needn't name one, and may give
            // only a colour (a bare look; an element's is always bare); if it names one, the name must be right.
            void LookOf(LookFamilies looks, string table, string who, JObject entry, bool required)
            {
                if (!(entry["look"] is JObject look))
                {
                    if (required) Add(ModCheck.Looks, $"{who} has no look, so the view can't draw it; give it a \"look\" naming a family ({looks.DrawnNames(table)}).");
                    return;
                }
                string name = Text(look["family"]);
                Scoped(table, who, look);
                if (name == null)
                {
                    if (looks.BareTables.Contains(table)) Params(look, who, "a look with no family", looks.BareParams(table), table);
                    return; // else Rules.Check says so
                }
                if (table == "elements") return; // Rules.Check: an element names no family
                var family = looks.Find(table, name);
                if (family == null)
                    Add(ModCheck.Looks, $"{who} names the look family \"{name}\", which the view doesn't know; the families for it are {looks.DrawnNames(table)} (rules/looks.json).");
                else if (!family.Drawn)
                    Add(ModCheck.Looks, $"{who} names the look family \"{name}\", which the view can't draw from data yet (it needs {family.Needs}).");
                else if (family.Forms.Count > 0 && !family.Forms.Contains(Text(look["form"])))
                    Add(ModCheck.Looks, $"{who}'s look family \"{name}\" takes a form, one of {string.Join(", ", family.Forms)}.");
                else if (family.Params.Contains("species") && !(Text(look["species"]) is string species && (fauna?[species] ?? baseDoc[FaunaTables.SpeciesKey]?[species]) != null))
                    Add(ModCheck.Looks, $"{who}'s look family \"{name}\" takes a species, a row of rules/fauna.json or one the mod adds.");
                else
                {
                    Params(look, who, $"the look family \"{name}\"", family.Params, table);
                    double maxScale = budgets.Number("things.maxScale", 20);
                    if (look["scale"] != null && !(IsNumber(look["scale"]) && (double)look["scale"] > 0 && (double)look["scale"] <= maxScale))
                        Add(ModCheck.Budget, $"{who}'s look has a scale of {look["scale"]}; a thing's scale is above 0 and at most {Num(maxScale)}.");
                }
            }

            // Every parameter a look gives is one its family takes, or the view would silently ignore it. A
            // mix and scoped looks go wherever a colour does, on an element, cover, gas or terrain.
            void Params(JObject look, string who, string what, IReadOnlyList<string> takes, string table)
            {
                bool colours = takes.Contains("color") && LookScopes.Tables.Contains(table);
                bool byPlayer = takes.Contains("color") && LookScopes.ByTables.Contains(table);
                foreach (var key in look.Properties().Select(p => p.Name).Where(k => !LookFamilies.Naming.Contains(k) && !takes.Contains(k)))
                {
                    if ((colours && (key == "mix" || key == "blend" || key == "scoped")) || (byPlayer && key == "scoped")) continue;
                    string why = (key == "mix" || key == "scoped") && takes.Contains("color") ? $" ({key} is for an element's, cover's, gas's or terrain's look; a spell's colours only its own show)" : "";
                    Add(ModCheck.Looks, $"{who}'s look gives {key}, which {what} doesn't take; it takes {(takes.Count == 0 ? "nothing" : string.Join(", ", takes))} (rules/looks.json){why}.");
                }
            }

            // A scoped look's place is one the world has, and its reach and count, and a mix's colours, are within the budgets.
            void Scoped(string table, string who, JObject look)
            {
                double maxMix = budgets.Number("looks.maxMix", 8), maxScoped = budgets.Number("looks.maxScoped", 8), maxWithin = budgets.Number("looks.maxWithin", 64);
                if (look["mix"] is JArray mix && mix.Count > maxMix) Add(ModCheck.Budget, $"{who}'s look mixes {mix.Count} colours; a mix has at most {Num(maxMix)}.");
                if (!(look["scoped"] is JArray scoped)) return;
                if (scoped.Count > maxScoped) Add(ModCheck.Budget, $"{who}'s look has {scoped.Count} scoped looks; a look has at most {Num(maxScoped)}.");
                foreach (var s in scoped.OfType<JObject>())
                {
                    if (s["by"] != null && !LookScopes.ByTables.Contains(table))
                        Add(ModCheck.Looks, $"{who}'s look is given by player, which only what a cast makes (an element, cover or gas) or a spell's own show can be.");
                    if (s["where"] != null && !LookScopes.Tables.Contains(table))
                        Add(ModCheck.Looks, $"{who}'s look is scoped to a place, but a spell's look colours its own show wherever it's cast; scope its element's look, or give the spell's look by player.");
                    if (s["mix"] is JArray m && m.Count > maxMix) Add(ModCheck.Budget, $"{who}'s scoped look mixes {m.Count} colours; a mix has at most {Num(maxMix)}.");
                    var where = s["where"] as JObject;
                    if (where?["within"] is JValue within && IsNumber(within) && (double)within > maxWithin)
                        Add(ModCheck.Budget, $"{who}'s scoped look reaches {within} cells; a scope reaches at most {Num(maxWithin)} cells.");
                    if (Text(where?["place"]) is string place && budgets.Places.Find(place) == null)
                        Add(ModCheck.References, $"{who}'s scoped look names the place \"{place}\", which the world doesn't have; the places are {budgets.Places.Ids} (world/places.json).");
                }
            }

            // ---------- the design rules ----------

            public void Design(JObject composed)
            {
                foreach (var s in PatchSpells)
                {
                    var id = Text(s["id"]) ?? "?";
                    if (!InBase("spells", id) && s["group"] == null)
                        Add(ModCheck.Design, $"Spell {id} needs a group (its element on the spell radial, from groups.json).");
                    // What it names on top of a spell the rules have keeps that spell's element or effect.
                    var whole = SpellIn(composed, s);
                    if (whole["element"] == null && whole["effect"] == null && whole["place"] == null && (composed != null || !InBase("spells", id)))
                        Add(ModCheck.Design, $"Spell {id} has neither an element nor an effect, and places nothing, so it does nothing; spells act through elements and the material verbs, or place something.");
                    FiveE(id, s);
                }
                NewElements(composed);
            }

            // Spells stay inside the 5E-compatible frame: SRD 5.2.1 levels and damage dice.
            void FiveE(string id, JObject s)
            {
                var level = s["level"];
                double maxLevel = budgets.Number("spells.maxLevel", 9);
                if (level != null && !(IsWhole(level) && (double)level >= 0 && (double)level <= maxLevel))
                    Add(ModCheck.Design, $"Spell {id} is level {level}; SRD 5.2.1 spells are level 0 (a cantrip) to {Num(maxLevel)}.");
                if (!(s["damage"] is JArray d) || d.Count != 2 || !IsWhole(d[0]) || !IsWhole(d[1])) return;
                var sides = (budgets.Json["spells"]?["diceSides"] as JArray)?.Select(x => (double)x).ToList() ?? new List<double> { 4, 6, 8, 10, 12 };
                if (!sides.Contains((double)d[1]))
                    Add(ModCheck.Design, $"Spell {id} deals {d[0]}d{d[1]}; damage dice are {string.Join(", ", sides.Select(x => "d" + Num(x)))}.");
                double maxDice = budgets.Number("spells.maxDice", 20);
                if ((double)d[0] < 1 || (double)d[0] > maxDice)
                    Add(ModCheck.Design, $"Spell {id} rolls {d[0]} dice; a spell rolls 1 to {Num(maxDice)}.");
            }

            // No new element for one spell: a new one needs its own reactions and more than one spell or critter using it.
            void NewElements(JObject composed)
            {
                var baseElements = new HashSet<string>((baseDoc["elements"] as JArray)?.Select(x => (string)x) ?? Enumerable.Empty<string>());
                var added = ModPatch.ElementNames(patch["elements"]).Where(e => !baseElements.Contains(e)).Distinct().ToList();
                double maxNew = budgets.Number("mod.maxNewElements", 1);
                if (added.Count > maxNew)
                    Add(ModCheck.Budget, $"The mod adds {added.Count} new elements; a mod may add at most {Num(maxNew)}.");
                if (composed == null) return;
                foreach (var e in added)
                {
                    if (!composed["cellRules"].Any(r => Text(r["element"]) == e))
                        Add(ModCheck.Design, $"The new element {e} has no reactions, so it does nothing when it reaches a cell; add rules for it or use an existing element.");
                    var users = composed["spells"].Where(s => Text(s["element"]) == e || Text(s["core"]) == e || Text(s["leaves"]) == e).Select(s => "spell " + Text(s["id"]))
                        .Concat(composed["critters"].Where(c => Text(c["element"]) == e).Select(c => "critter " + Text(c["id"]))).ToList();
                    if (users.Count < 2)
                        Add(ModCheck.Design, $"The new element {e} is made for {(users.Count == 0 ? "nothing" : users[0])}; an element must serve more than one spell or critter, so use an existing element instead.");
                }
            }

            // ---------- determinism ----------

            public void Determinism()
            {
                var wholeKeys = WholeKeys(baseDoc);
                // A building's numbers are the view's or checked whole by Rules.Check, and its seed is its own; a creature's are the view's.
                var checkedHere = new JObject(patch.Properties().Where(p => p.Name != "world" && p.Name != BuildingTables.Key && !FaunaTables.Holds(p.Name)).Select(p => new JProperty(p.Name, p.Value)));
                Walk(checkedHere, "", (key, path, value) =>
                {
                    if (RandomKeys.Contains(key.ToLowerInvariant()))
                        Add(ModCheck.Determinism, $"{Where(path)} sets \"{key}\"; all randomness comes from the world's seeded generator, through a rule's chance.");
                    if (!IsNumber(value)) return;
                    double n = (double)value;
                    if ((key == "chance" || key.EndsWith("Chance", StringComparison.Ordinal)) && (n < 0 || n > 1))
                        Add(ModCheck.Determinism, $"{Where(path)} is a chance of {Num(n)}; a chance is rolled on the world's generator and must be from 0 to 1.");
                    else if (wholeKeys.Contains(key) && !IsWhole(value))
                        Add(ModCheck.Determinism, $"{Where(path)} is {Num(n)}, but the core keeps {key} as a whole number (integer state); use a whole number.");
                });
            }

            /// <summary>Keys that hold numbers in the base rules and never a fraction: the core keeps them as integers.</summary>
            static HashSet<string> WholeKeys(JObject doc)
            {
                var whole = new HashSet<string>();
                // A group's order only sorts the spell radial (MODDING.md slots Frost in at 2.5).
                var fraction = new HashSet<string> { "order" };
                // The creature tables' numbers are the view's: their radius says nothing of a spell's.
                var shared = new JObject(doc.Properties().Where(p => !FaunaTables.Holds(p.Name)).Select(p => new JProperty(p.Name, p.Value)));
                Walk(shared, "", (key, path, value) =>
                {
                    if (!IsNumber(value)) return;
                    (IsWhole(value) ? whole : fraction).Add(key);
                });
                whole.ExceptWith(fraction);
                return whole;
            }

            /// <summary>Calls back with (key, path, value) for every property below; list items take their id (or index) into the path.</summary>
            static void Walk(JToken t, string path, Action<string, string, JToken> visit)
            {
                if (t is JObject o)
                {
                    // A look's numbers are the view's, not integer state.
                    foreach (var p in o.Properties().Where(p => p.Name != "look"))
                    {
                        string sub = path.Length == 0 ? p.Name : path + "." + p.Name;
                        visit(p.Name, sub, p.Value);
                        Walk(p.Value, sub, visit);
                    }
                }
                else if (t is JArray a)
                {
                    for (int i = 0; i < a.Count; i++)
                        Walk(a[i], $"{path}[{(a[i] is JObject item ? Text(item["id"]) : null) ?? i.ToString(CultureInfo.InvariantCulture)}]", visit);
                }
            }

            /// <summary>A patch path as words: "spells[fireball].radius" reads "Spell fireball's radius".</summary>
            static string Where(string path)
            {
                int open = path.IndexOf('[');
                int close = path.IndexOf(']');
                if (open < 0 || close < open) return $"The {path} setting";
                string section = path.Substring(0, open);
                string kind = section.EndsWith('s') ? section.Substring(0, section.Length - 1) : section;
                if (section.StartsWith("reactions", StringComparison.Ordinal)) kind = "Rule";
                else kind = char.ToUpperInvariant(kind[0]) + kind.Substring(1);
                string id = path.Substring(open + 1, close - open - 1);
                string rest = path.Substring(close + 1).TrimStart('.');
                return rest.Length == 0 ? $"{kind} {id}" : $"{kind} {id}'s {rest}";
            }

            // ---------- temporary materials (World.Lapse.cs) ----------

            /// <summary>
            /// The materials the mod adds: how many, their lapse (only a material the mod adds lapses, within the
            /// budget, into one that stays), no thing made of one, and no other mod's temporary material named.
            /// </summary>
            public void Temporary(IReadOnlyDictionary<string, string> temporary)
            {
                var added = new HashSet<string>(Rows(Rules.MaterialTables).Where(r => baseDoc[r.table]?[r.row.Name] == null).Select(r => r.row.Name));
                double max = budgets.Number("materials.maxPerMod", 8);
                if (added.Count > max) Add(ModCheck.Budget, $"The mod adds {added.Count} materials; a mod may add at most {Num(max)}.");
                foreach (var (table, row) in Rows(Rules.MaterialTables))
                    if (row.Value[Rules.LapseKey] is JObject lapse) LapseOf(table, row.Name, lapse, added);
                foreach (var (_, row) in Rows(new[] { "entities", "items" }))
                    if (Text(row.Value["material"]) is string m && added.Contains(m))
                        Add(ModCheck.Design, $"{row.Name} is made of {m}, a material the mod adds; a thing outlives a mod, so it can't be made of one that goes with it.");
                if (temporary != null && temporary.Count > 0) Names(temporary);
            }

            // The patch's rows in these tables, table by table in the patch's order.
            IEnumerable<(string table, JProperty row)> Rows(IEnumerable<string> tables) =>
                tables.SelectMany(t => ((patch[t] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()).Select(p => (t, p)));

            void LapseOf(string table, string id, JObject lapse, HashSet<string> added)
            {
                string what = char.ToUpperInvariant(table[0]) + (table == "gases" ? "as" : table.Substring(1).TrimEnd('s')) + " " + id;
                if (!added.Contains(id))
                {
                    Add(ModCheck.Design, $"{what} is in the rules already, so it can't lapse; only a material the mod adds lapses.");
                    return;
                }
                double maxAfter = budgets.Number("materials.maxLapse", 21600);
                if (IsNumber(lapse["after"]) && (double)lapse["after"] > maxAfter)
                    Add(ModCheck.Budget, $"{what} lapses after {lapse["after"]} ticks; a material lapses within {Num(maxAfter)} ticks (an hour) at most, or give no after and it lasts while the mod is on.");
                if (Text(lapse["into"]) is string into && added.Contains(into))
                    Add(ModCheck.Design, $"{what} lapses into {into}, which the mod adds too and goes with it; lapse into a material the rules already have.");
            }

            // Another mod's temporary material, named anywhere in the patch: when that mod goes, the name would dangle.
            void Names(IReadOnlyDictionary<string, string> temporary)
            {
                var named = new SortedSet<string>(Rows(ModPatch.Tables).Select(r => r.row.Name).Where(temporary.ContainsKey), StringComparer.Ordinal);
                foreach (var v in patch.Descendants().OfType<JValue>())
                    if (v.Type == JTokenType.String && temporary.ContainsKey((string)v)) named.Add((string)v);
                foreach (var id in named)
                    Add(ModCheck.References, $"{id} is a temporary material of the mod {temporary[id]}; only that mod may name it, as it goes when that mod does.");
            }

            // ---------- budgets ----------

            public void Budgets(JObject composed)
            {
                NewEntries();
                foreach (var s in PatchSpells) SpellBudget(Text(s["id"]) ?? "?", s);
                foreach (var r in PatchRules) RuleBudget(Text(r["id"]) ?? "?", r);
                double minEvery = budgets.Number("critters.minEvery", 4);
                foreach (var c in PatchCritters.Where(c => IsNumber(c["every"]) && (double)c["every"] < minEvery))
                    Add(ModCheck.Budget, $"Critter {Text(c["id"])} acts every {c["every"]} ticks; a mod's critter acts at most every {Num(minEvery)} ticks.");
                SimBudget();
                double maxThings = budgets.Number("things.maxPerMod", 16);
                if (patch["world"]?["add"] is JArray things && things.Count > maxThings)
                    Add(ModCheck.Budget, $"The mod adds {things.Count} things to the world; a mod may add at most {Num(maxThings)}.");
                double maxLaid = budgets.Number("lay.maxCells", 256);
                int laid = (patch["world"]?["lay"] as JArray)?.OfType<JObject>().Sum(l => (l["cells"] as JArray)?.Count ?? 0) ?? 0;
                if (laid > maxLaid) Add(ModCheck.Budget, $"The mod lays {laid} cells; a mod may lay at most {Num(maxLaid)}.");
                if (composed == null) return;
                TriggerBudget(composed);
                FaunaBudget(composed);
                BuildingBudget(composed);
                MaterialBudget(composed);
                ChainDepth(composed);
            }

            // A mod's triggers (Rules.Triggers.cs): how many, how many actions each, how strong a spell they cast, how much they place.
            void TriggerBudget(JObject composed)
            {
                if (!(patch[Rules.TriggersKey] is JObject rows)) return;
                var given = rows.Properties().Where(p => p.Value is JObject).ToList();
                double maxPerMod = budgets.Number("triggers.maxPerMod", 4), maxActions = budgets.Number("triggers.maxActions", 2), maxLevel = budgets.Number("triggers.maxLevel", 7);
                if (given.Count > maxPerMod) Add(ModCheck.Budget, $"The mod has {given.Count} triggers; a mod may have at most {Num(maxPerMod)}.");
                foreach (var row in given) TriggerRowBudget(composed, row.Name, maxActions, maxLevel);
            }

            void TriggerRowBudget(JObject composed, string id, double maxActions, double maxLevel)
            {
                var actions = (composed[Rules.TriggersKey]?[id]?["do"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                if (actions.Count > maxActions) Add(ModCheck.Budget, $"Trigger {id} does {actions.Count} things; a trigger does at most {Num(maxActions)}.");
                foreach (var a in actions)
                {
                    var spell = (composed["spells"] as JArray)?.OfType<JObject>().FirstOrDefault(s => Text(s["id"]) == Text(a["cast"]));
                    if (spell != null && IsNumber(spell["level"]) && (double)spell["level"] > maxLevel)
                        Add(ModCheck.Budget, $"Trigger {id} casts {Text(spell["id"])}, level {spell["level"]}; a trigger casts spells of level {Num(maxLevel)} at most (Meteor).");
                    if (a["place"] is JObject place)
                        foreach (var line in SpellCaps(budgets, id, new JObject { ["place"] = place }))
                            Add(ModCheck.Budget, "Trigger" + line.Substring("Spell".Length));
                }
            }

            void NewEntries()
            {
                int count = ModPatch.ElementNames(patch["elements"]).Count(e => !(baseDoc["elements"] as JArray ?? new JArray()).Any(x => Text(x) == e));
                foreach (var table in ModPatch.Tables)
                    if (patch[table] is JObject entries) count += entries.Properties().Count(p => baseDoc[table]?[p.Name] == null);
                foreach (var table in BuildingTables.Tables)
                    if (patch[BuildingTables.Key]?[table] is JObject rows) count += rows.Properties().Count(p => baseDoc[BuildingTables.Key]?[table]?[p.Name] == null);
                count += PatchSpells.Count(s => !InBase("spells", Text(s["id"])));
                count += PatchCritters.Count(c => !InBase("critters", Text(c["id"])));
                count += PatchRules.Count(r => !InBase("cellRules", Text(r["id"])) && !InBase("entityRules", Text(r["id"])));
                double max = budgets.Number("mod.maxNewEntries", 12);
                if (count > max) Add(ModCheck.Budget, $"The mod adds {count} new entries; a mod may add at most {Num(max)}, so keep it to one idea.");
            }

            void SpellBudget(string id, JObject s)
            {
                foreach (var message in SpellCaps(budgets, id, s)) Add(ModCheck.Budget, message);
            }

            void RuleBudget(string id, JObject rule)
            {
                if (!(rule["do"] is JArray effects)) return;
                foreach (var e in effects.OfType<JObject>())
                    foreach (var p in e.Properties().Where(p => IsNumber(p.Value)))
                    {
                        string cap = RuleSchema.Effects.Field(p.Name)?.Cap;
                        if (cap == null) continue;
                        double max = budgets.Number(cap, double.MaxValue);
                        if ((double)p.Value > max)
                            Add(ModCheck.Budget, $"Rule {id}'s {p.Name} is {p.Value}; the most a mod's rule may give is {Num(max)}, so it can't flood every cell.");
                    }
            }

            void SimBudget()
            {
                if (!(patch["sim"] is JObject sim)) return;
                foreach (var (path, min, max) in budgets.SimBounds)
                {
                    var v = sim.SelectToken(path);
                    if (IsNumber(v) && ((double)v < min || (double)v > max))
                        Add(ModCheck.Budget, $"sim {path} is {Num((double)v)}; a mod may set it from {Num(min)} to {Num(max)}.");
                }
            }

            // What one building may be (builders.md section 4), and how many a mod may bring.
            // A species is drawn only, but a giant should still be a giant and not a mountain (#61): a variant's
            // scale, and a size against the row's in the base rules (or the reader's default), within things.maxScale.
            void FaunaBudget(JObject composed)
            {
                double max = budgets.Number("things.maxScale", 20);
                var given = (patch[FaunaTables.SpeciesKey] as JObject)?.Properties().ToList() ?? new List<JProperty>();
                foreach (var p in given)
                    if (composed[FaunaTables.SpeciesKey]?[p.Name] is JObject row)
                    {
                        SpeciesBudget(p.Name, row, max);
                        GraftBudget(composed[FaunaTables.SpeciesKey] as JObject, p.Name, row);
                    }
                double maxNew = budgets.Number("fauna.maxPerMod", 4);
                int added = given.Count(p => baseDoc[FaunaTables.SpeciesKey]?[p.Name] == null);
                if (added > maxNew) Add(ModCheck.Budget, $"The mod adds {added} species; a mod may add at most {Num(maxNew)}.");
            }

            // A graft (FaunaGraft): how many donors, how many slots once flattened, and how far a donor's slots grow or shrink.
            void GraftBudget(JObject species, string id, JObject row)
            {
                if (!FaunaGraft.IsGraft(row)) return;
                var donors = (row[FaunaGraft.FromKey] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                double maxDonors = budgets.Number("fauna.maxDonors", 3), maxSlots = budgets.Number("fauna.maxSlots", 40);
                double minScale = budgets.Number("fauna.minScale", 0.25), maxScale = budgets.Number("fauna.maxScale", 4);
                if (donors.Count > maxDonors) Add(ModCheck.Budget, $"Species {id} grafts from {donors.Count} donors; a graft has at most {Num(maxDonors)}.");
                foreach (var d in donors.Where(d => IsNumber(d["scale"]) && ((double)d["scale"] < minScale || (double)d["scale"] > maxScale)))
                    Add(ModCheck.Budget, $"Species {id} grafts {Text(d["species"]) ?? "a donor"} at a scale of {d["scale"]}; a donor's scale is {Num(minScale)} to {Num(maxScale)}.");
                int slots = (FaunaGraft.Flatten(species, id)?["parts"] as JObject)?.Count ?? 0;
                if (slots > maxSlots) Add(ModCheck.Budget, $"Species {id} has {slots} slots once flattened; a body has at most {Num(maxSlots)} (each is drawn for every animal out).");
            }

            void SpeciesBudget(string id, JObject row, double max)
            {
                if (IsNumber(row["scale"]) && (double)row["scale"] > max)
                    Add(ModCheck.Budget, $"Species {id} has a scale of {row["scale"]}; a species is at most {Num(max)} times the one it's built on.");
                if (row["size"] is JArray size && size.Count == 2 && size.All(IsNumber) && Oversized(size, baseDoc[FaunaTables.SpeciesKey]?[id]?["size"] as JArray, max))
                    Add(ModCheck.Budget, $"Species {id} has a size of {size.ToString(Newtonsoft.Json.Formatting.None)}, more than {Num(max)} times its normal size.");
            }

            // Either end of a size past max times the base row's (the reader's default 1.1 for a new row).
            static bool Oversized(JArray size, JArray normal, double max)
            {
                bool known = normal != null && normal.Count == 2 && normal.All(IsNumber);
                return Enumerable.Range(0, 2).Any(i => (double)size[i] > (known ? (double)normal[i] : 1.1) * max + 1e-9);
            }

            void BuildingBudget(JObject composed)
            {
                if (!(patch["world"]?["buildings"] is JObject rows)) return;
                double max = budgets.Number("buildings.maxPerMod", 4);
                if (rows.Count > max) Add(ModCheck.Budget, $"The mod brings {rows.Count} buildings; a mod may bring at most {Num(max)}.");
                var tables = BuildingTables.Parse(composed);
                foreach (var p in rows.Properties().Select(p => (id: p.Name, row: BuildingRow.Resolved((JObject)p.Value, tables))))
                {
                    void Cap(string what, int count, string key)
                    {
                        double cap = budgets.Number("buildings." + key, double.MaxValue);
                        if (count > cap) Add(ModCheck.Budget, $"Building {p.id} has {count} {what}; a building has at most {Num(cap)}.");
                    }
                    Cap("joins", (p.row["joins"] as JObject)?.Count ?? 0, "maxJoins");
                    Cap("walls", (p.row["walls"] as JObject)?.Count ?? 0, "maxWalls");
                    Cap("openings", (p.row["walls"] as JObject)?.PropertyValues().Sum(w => (w["openings"] as JArray)?.Count ?? 0) ?? 0, "maxOpenings");
                    Cap("parts", (p.row["parts"] as JArray)?.Count ?? 0, "maxParts");
                    Cap("storeys", p.row["storeys"] != null && IsWhole(p.row["storeys"]) ? (int)p.row["storeys"] : 1, "maxStoreys");
                    SiteSize(p.id, p.row);
                }
            }

            void SiteSize(string id, JObject row)
            {
                BuiltSize(id, row);
                if (!(row["joins"] is JObject joins) || joins.Count == 0 || joins.PropertyValues().Any(j => !(j is JArray a) || a.Count != 2 || !IsWhole(a[0]) || !IsWhole(a[1]))) return;
                double max = budgets.Number("buildings.maxCells", 24);
                double wide = (joins.PropertyValues().Max(j => (double)j[0]) - joins.PropertyValues().Min(j => (double)j[0])) / 2;
                double deep = (joins.PropertyValues().Max(j => (double)j[1]) - joins.PropertyValues().Min(j => (double)j[1])) / 2;
                if (wide > max || deep > max) Add(ModCheck.Budget, $"Building {id} is {Num(wide)} by {Num(deep)} cells; a building is at most {Num(max)} by {Num(max)}.");
            }

            // A row's built cells (a cabin's, carried along in an edit) stay the size of a building too.
            void BuiltSize(string id, JObject row)
            {
                double reach = 2 * budgets.Number("buildings.maxCells", 24);
                var numbers = (row["built"] as JObject)?.PropertyValues().SelectMany(t => t).SelectMany(r => r) ?? Enumerable.Empty<JToken>();
                if (numbers.Any(n => !IsWhole(n) || Math.Abs((double)n) > reach))
                    Add(ModCheck.Budget, $"Building {id}'s built cells must be whole numbers within {Num(reach)} cells of its at.");
            }

            // Covers that float spread over open water: a reach past the budget would cover a lake.
            void MaterialBudget(JObject composed)
            {
                if (!(patch["covers"] is JObject covers)) return;
                double max = budgets.Number("materials.maxFloatReach", 8);
                foreach (var p in covers.Properties())
                {
                    var reach = composed["covers"]?[p.Name]?["floatReach"];
                    if (IsNumber(reach) && (double)reach > max)
                        Add(ModCheck.Budget, $"Cover {p.Name} floats {reach} cells over water; a mod's cover may float at most {Num(max)}.");
                    SpreadBudget(p.Name, composed["covers"]?[p.Name]);
                }
            }

            // Covers that spread creep on with no cast: a slow turn and a small chance keep a mod's from covering the land.
            void SpreadBudget(string id, JToken cover)
            {
                if (cover?["spreadEvery"] is JToken every && IsNumber(every))
                {
                    double min = budgets.Number("materials.minSpreadEvery", 30), most = budgets.Number("materials.maxSpreadChance", 0.5);
                    var chance = cover["spreadChance"];
                    double odds = IsNumber(chance) ? (double)chance : 1;
                    if ((double)every < min) Add(ModCheck.Budget, $"Cover {id} spreads every {every} ticks; a mod's cover spreads every {Num(min)} ticks or slower.");
                    if (odds > most) Add(ModCheck.Budget, $"Cover {id} spreads with a chance of {Num(odds)}; a mod's cover spreads with a chance of {Num(most)} at most (give spread.chance).");
                }
                var reach = cover?["spreadReach"];
                double far = budgets.Number("materials.maxSpreadReach", 64);
                if (IsNumber(reach) && (double)reach > far) Add(ModCheck.Budget, $"Cover {id} spreads {reach} cells; a mod's cover spreads at most {Num(far)}.");
            }

            /// <summary>
            /// Changes that run on their own, with no spell: a terrain ripening into its grow.into, and a gas
            /// shedding an element whose rules lay a cover, gas or terrain. A loop would run forever; a long
            /// chain is one cast setting off more than the tick budget expects.
            /// </summary>
            void ChainDepth(JObject composed)
            {
                // Only what the mod brings: a loop or long chain the base rules already have isn't its doing.
                var already = new HashSet<string>(Chains(baseDoc));
                foreach (var message in Chains(composed).Where(m => !already.Contains(m))) Add(ModCheck.Budget, message);
            }

            IEnumerable<string> Chains(JObject rules)
            {
                var next = ChainEdges(rules);
                double max = budgets.Number("chainDepth", 3);
                var done = new Dictionary<string, List<string>>();
                foreach (var start in next.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var loop = new List<string>();
                    var longest = Longest(start, next, done, new List<string>(), loop);
                    if (loop.Count > 0)
                    {
                        yield return $"{string.Join(" → ", loop)} change into each other on their own forever; break the loop.";
                        yield break;
                    }
                    if (longest.Count - 1 > max)
                        yield return $"{string.Join(" → ", longest)} is a chain of {longest.Count - 1} changes that run on their own; the most a mod may chain is {Num(max)}.";
                }
            }

            static Dictionary<string, List<string>> ChainEdges(JObject r)
            {
                var next = new Dictionary<string, List<string>>();
                void Edge(string from, string to)
                {
                    // A gas laying more of itself only lasts longer (it still decays): not a loop.
                    if (from == null || to == null || from == to) return;
                    if (!next.TryGetValue(from, out var list)) next[from] = list = new List<string>();
                    if (!list.Contains(to)) list.Add(to);
                }
                foreach (var p in (r["terrain"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (Text(p.Value["growsTo"]) is string to) Edge("terrain " + p.Name, "terrain " + to);
                foreach (var p in (r["gases"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    var element = Text(p.Value["sheds"]?["element"]);
                    if (element == null) continue;
                    foreach (var rule in r["cellRules"].Where(x => Text(x["element"]) == element))
                        foreach (var e in (rule["do"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                        {
                            Edge("gas " + p.Name, Text(e["gas"]) is string g ? "gas " + g : null);
                            Edge("gas " + p.Name, Text(e["setCover"]) is string c ? "cover " + c : null);
                            Edge("gas " + p.Name, Text(e["setTerrain"]) is string t ? "terrain " + t : null);
                            Edge("gas " + p.Name, Text(e["grow"]) is string gr ? "terrain " + gr : null);
                        }
                }
                return next;
            }

            // The longest path from a node; a loop found on the way is written into loop.
            static List<string> Longest(string node, Dictionary<string, List<string>> next, Dictionary<string, List<string>> done, List<string> path, List<string> loop)
            {
                if (done.TryGetValue(node, out var known)) return known;
                int at = path.IndexOf(node);
                if (at >= 0)
                {
                    loop.AddRange(path.Skip(at).Append(node));
                    return new List<string> { node };
                }
                path.Add(node);
                var best = new List<string> { node };
                foreach (var to in next.TryGetValue(node, out var list) ? list : new List<string>())
                {
                    var sub = Longest(to, next, done, path, loop);
                    if (loop.Count > 0) return best;
                    if (sub.Count + 1 > best.Count) best = new[] { node }.Concat(sub).ToList();
                }
                path.RemoveAt(path.Count - 1);
                done[node] = best;
                return best;
            }
        }
    }
}
