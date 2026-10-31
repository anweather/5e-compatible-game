// Checks a mod before anyone switches it on (docs/RULES_MCP.md, ADR 0013): the
// patch's shape (ModPatch.Parse), its references and numbers (Rules.WithMods),
// then what those don't cover: the design rules, determinism and the budgets
// in rules/budgets.json. The rules MCP server (core/Rules.Mcp) calls it; the
// game can too. Every problem is one plain sentence in the glossary's words.
// C# only: the sandbox has no counterpart yet.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>Which of validate's checks a problem comes from.</summary>
    public enum ModCheck { Shape, References, Design, Determinism, Budget }

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

        public static ModBudgets Parse(string json) => new ModBudgets(JObject.Parse(json));

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
        static readonly HashSet<string> Sections = new HashSet<string>(ModPatch.Tables.Concat(new[] { "elements", "reactions", "spells", "critters", "sim", "combos" }));
        static readonly string[] RandomKeys = { "seed", "random", "rng" };

        /// <summary>
        /// Every problem with a patch (bare or {"title", "patch"}) over these base rules; empty means
        /// it may switch on. Stops after the shape check if the patch can't be read.
        /// </summary>
        public static IReadOnlyList<ModProblem> Validate(Rules baseRules, ModBudgets budgets, string patchJson)
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
            return problems;
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

            // ---------- the design rules ----------

            public void Design(JObject composed)
            {
                foreach (var s in PatchSpells)
                {
                    var id = Text(s["id"]) ?? "?";
                    if (!InBase("spells", id) && s["group"] == null)
                        Add(ModCheck.Design, $"Spell {id} needs a group (its element on the spell radial, from groups.json).");
                    if (s["element"] == null && s["effect"] == null)
                        Add(ModCheck.Design, $"Spell {id} has neither an element nor an effect, so it does nothing to cells; spells act through elements and the material verbs.");
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
                var added = ((patch["elements"] as JArray) ?? new JArray()).Select(Text).Where(e => e != null && !baseElements.Contains(e)).Distinct().ToList();
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
                Walk(patch, "", (key, path, value) =>
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
                Walk(doc, "", (key, path, value) =>
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
                    foreach (var p in o.Properties())
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
                if (composed == null) return;
                MaterialBudget(composed);
                ChainDepth(composed);
            }

            void NewEntries()
            {
                int count = ((patch["elements"] as JArray) ?? new JArray()).Count(e => !(baseDoc["elements"] as JArray ?? new JArray()).Any(x => Text(x) == Text(e)));
                foreach (var table in ModPatch.Tables)
                    if (patch[table] is JObject entries) count += entries.Properties().Count(p => baseDoc[table]?[p.Name] == null);
                count += PatchSpells.Count(s => !InBase("spells", Text(s["id"])));
                count += PatchCritters.Count(c => !InBase("critters", Text(c["id"])));
                count += PatchRules.Count(r => !InBase("cellRules", Text(r["id"])) && !InBase("entityRules", Text(r["id"])));
                double max = budgets.Number("mod.maxNewEntries", 12);
                if (count > max) Add(ModCheck.Budget, $"The mod adds {count} new entries; a mod may add at most {Num(max)}, so keep it to one idea.");
            }

            void SpellBudget(string id, JObject s)
            {
                foreach (var (key, cap) in new[] { ("power", "maxPower"), ("radius", "maxRadius"), ("length", "maxLength"), ("size", "maxSize"), ("push", "maxPush"), ("raise", "maxRaise"), ("dig", "maxDig") })
                {
                    double max = budgets.Number("spells." + cap, double.MaxValue);
                    if (IsNumber(s[key]) && (double)s[key] > max)
                        Add(ModCheck.Budget, $"Spell {id} has a {key} of {s[key]}; a mod's spell may have at most {Num(max)}, so one cast can't cover a chunk or stall the tick.");
                }
            }

            void RuleBudget(string id, JObject rule)
            {
                if (!(rule["do"] is JArray effects)) return;
                foreach (var e in effects.OfType<JObject>())
                    foreach (var p in e.Properties().Where(p => IsNumber(p.Value)))
                    {
                        double max = budgets.Number("effects." + p.Name, double.MaxValue);
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
                }
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
