// The MCP face of the Workbench: one method per tool, typed parameters, and
// every failure returned to the caller as one sentence (docs/RULES_MCP.md). Two
// hosts serve these: core/Rules.Mcp over stdio, and the game on localhost (McpRpc,
// RulesMcpServer). Each reads the tools from the RulesTool attributes.
using System;
using System.ComponentModel;

#pragma warning disable S107 // a tool's parameters are its schema, so add_spell and add_reaction take many

namespace Chem.Mcp
{
    /// <summary>Marks a method of RulesTools as a tool of that name.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RulesToolAttribute : Attribute
    {
        public RulesToolAttribute(string name) { Name = name; }

        public string Name { get; }
        /// <summary>The tool only reads: nothing in the game or on disk changes.</summary>
        public bool ReadOnly { get; set; }
    }

    public sealed class RulesTools
    {
        /// <summary>What a client is told when it connects: how to use the tools. Both hosts send it.</summary>
        public const string Instructions =
            "These tools change the rules of Spell Chemistry Valley, a game about how spells and materials react. When the player asks for a change, write it yourself: " +
            "read what's there (list, describe, explain), build a new draft with set_spell, set_material, set_reaction, tune, add_reaction, add_material or add_spell, " +
            "check it with validate, and where a scenario fits, run simulate and compare the numbers with what the player meant, since a change can do the opposite of what it seems to " +
            "(less fuel makes a fire spread less). Then apply the draft with a short title the players will read. Numbers are enough: a spell's and a material's looks follow them by themselves. " +
            "Don't look for a saved mod that's close: use one from rules/mods only when the player names it. After apply, tell the player what changed, from its answer.";

        const string DraftHelp = "The draft to add to; leave it out to start a new one. Returns the draft id and the patch so far.";
        const string ValueHelp = "The new value as JSON (0.5, true, \"ash\", [8, 6], {\"catch\": 0.4}); null removes the field.";

        readonly Workbench bench;
        readonly Func<string, Exception> failure;

        // A server may run calls at once; drafts are shared state, so one call at a time.
        readonly object gate = new object();

        /// <param name="bench">What the tools do.</param>
        /// <param name="failure">The exception a host wants for a refusal, with its sentence; RulesToolException when left out.</param>
        public RulesTools(Workbench bench, Func<string, Exception> failure = null)
        {
            this.bench = bench;
            this.failure = failure ?? (message => new RulesToolException(message));
        }

        string Do(Func<string> act)
        {
            try
            {
                lock (gate) return act();
            }
            catch (RulesToolException e)
            {
                throw failure(e.Message);
            }
        }

        // ---------- read ----------

        [RulesTool("list", ReadOnly = true), Description("Ids and one-line summaries for a rule table: materials (or terrain, covers, gases), elements, reactions, spells, groups, sim, critters, entities, items, machines. Read these to build a draft for what the player asks.")]
        public string List([Description("The table.")] string table) => Do(() => bench.List(table));

        [RulesTool("describe", ReadOnly = true), Description("The full entry as written in rules/, plus everything in the rules that refers to it. For sim, the id is a dotted path such as fire.spreadChance.")]
        public string Describe([Description("The table.")] string table, [Description("The entry's id.")] string id) => Do(() => bench.Describe(table, id));

        [RulesTool("explain", ReadOnly = true), Description("What happens when an element reaches a cell of a material (a terrain, cover, gas, water or ice): the reaction rules that can hold, top to bottom, and which one decides.")]
        public string Explain([Description("The element, such as fire or cold.")] string element, [Description("The material, such as grass, oil, steam, water or ice.")] string material,
            [Description("Optional: explain under this draft's rules.")] string draft = null) => Do(() => bench.Explain(element, material, draft));

        // ---------- draft ----------

        [RulesTool("add_material"), Description("Adds a new terrain, cover or gas to a draft. Its behaviour comes from material verb groups (burn, soak, dig, melt, freeze, float, dissolve, grow and the rest; rules/README.md). A new terrain also needs a look: {\"look\": {\"family\": \"block\", \"color\": [0.6, 0.7, 1], \"height\": 1.2}} in verbs, with a family from rules/looks.json.")]
        public string AddMaterial([Description("terrain, cover or gas.")] string kind, [Description("A new id, lowercase with dashes.")] string id, [Description("Its name as players see it.")] string name,
            [Description("Tags such as ground, blocking, slippery.")] string[] tags = null,
            [Description("Verb groups as a JSON object, such as {\"burn\": {\"catch\": 0.4, \"fuel\": 10, \"leaves\": \"dirt\"}}.")] string verbs = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.AddMaterial(draft, kind, id, name, tags, verbs).Show());

        [RulesTool("set_material"), Description("Changes one field of a terrain, cover or gas in a draft, by dotted path such as burn.catch. The rest of the entry is kept.")]
        public string SetMaterial([Description("The material's id.")] string id, [Description("The field, such as burn.catch or tags.")] string field, [Description(ValueHelp)] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.SetMaterial(draft, id, field, value).Show());

        [RulesTool("add_reaction"), Description("Adds a reaction rule: when the element reaches a cell where every condition holds, the effects run in order. New rules go to the top of their element's list, so they win. Conditions: cover, fire, fireBelow, water, frozen, melts, gas, terrainTag, notTerrainTag, conductive, power, chance. Effects: ignite, extinguish, boil, melt, freeze, condense, addWater, gas+n, pushGas, clearGas, setCover, spreadCover, break, setTerrain, raise, grow, conduct, fan, mark, and the rest in rules/README.md.")]
        public string AddReaction([Description("A new rule id, such as steam-damps-fire.")] string id, [Description("The element that sets it off.")] string element,
            [Description("The effects as a JSON list, such as [{\"extinguish\": true}].")] string effects,
            [Description("The conditions as a JSON object, such as {\"gas\": \"steam\"}.")] string conditions = null,
            [Description("The line logged when it happens.")] string log = null,
            [Description("Stop the rules after this one for this cell.")] bool stop = false,
            [Description("Log it even when it happens indirectly.")] bool notable = false,
            [Description("How often it applies, 0 to 1, rolled on the world's generator.")] double? chance = null,
            [Description("An entity rule (statuses on creatures and things) rather than a cell rule.")] bool entity = false,
            [Description(DraftHelp)] string draft = null) =>
            Do(() => bench.AddReaction(draft, id, element, effects, conditions, log, stop, notable, chance, entity).Show());

        [RulesTool("set_reaction"), Description("Changes one field of a reaction rule (if.chance, do, log, stop) in a draft. It keeps its place in its element's list.")]
        public string SetReaction([Description("The rule's id.")] string id, [Description("The field, such as if.chance or do.")] string field, [Description(ValueHelp)] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.SetReaction(draft, id, field, value).Show());

        [RulesTool("add_spell"), Description("Adds a spell. It acts through an element (or an effect such as terraform) and sits in a group of the spell radial; SRD 5.2.1 levels and damage dice. It needs a look in extra: {\"look\": {\"family\": \"blast\", \"color\": [1, 0.4, 0.1]}}, with a family from rules/looks.json.")]
        public string AddSpell([Description("A new id, such as ice-knife.")] string id, [Description("Its name.")] string name, [Description("The radial group: fire, water, storm, earth, arcane, nature or time.")] string group,
            [Description("0 (a cantrip) to 9.")] int level, [Description("point, line, square, burst, target or cone.")] string shape,
            [Description("The element it applies, such as cold.")] string element = null, [Description("Or an effect: terraform, levitate, carry, teleport, haste, weather, shapeshift, light.")] string effect = null,
            [Description("The element's power.")] int? power = null, [Description("A burst's radius in cells.")] int? radius = null, [Description("A line's or cone's length in cells.")] int? length = null,
            [Description("A square's size in cells.")] int? size = null, [Description("How hard it throws things back.")] int? push = null,
            [Description("self, or leave out to land where aimed.")] string origin = null, [Description("Damage dice, such as 3d6.")] string damage = null,
            [Description("One or two sentences for players.")] string blurb = null,
            [Description("More fields as a JSON object, such as {\"crater\": {\"radius\": 1, \"depth\": 1}}.")] string extra = null,
            [Description(DraftHelp)] string draft = null) =>
            Do(() => bench.AddSpell(draft, new Workbench.SpellArgs
            {
                Id = id,
                Name = name,
                Group = group,
                Level = level,
                Shape = shape,
                Element = element,
                Effect = effect,
                Power = power,
                Radius = radius,
                Length = length,
                Size = size,
                Push = push,
                Origin = origin,
                Damage = damage,
                Blurb = blurb,
                Extra = extra,
            }).Show());

        [RulesTool("set_spell"), Description("Changes one field of a spell (radius, power, damage, crater.depth...) in a draft. The rest of the spell is kept.")]
        public string SetSpell([Description("The spell's id.")] string id, [Description("The field.")] string field, [Description(ValueHelp + " Damage may be written 8d6.")] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.SetSpell(draft, id, field, value).Show());

        [RulesTool("tune"), Description("Sets a sim parameter by dotted path (fire.spreadChance, push.slideCells...) within the bounds rules/budgets.json declares.")]
        public string Tune([Description("The parameter's path; list sim to see them.")] string param, [Description("The value, usually a number.")] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.Tune(draft, param, value).Show());

        [RulesTool("remove"), Description("Takes back something the draft added or changed (a material, reaction, spell, element or sim parameter). It can't remove base rules.")]
        public string Remove([Description("The draft.")] string draft, [Description("The table: terrain, covers, gases, reactions, spells, elements or sim.")] string table,
            [Description("The id, or the sim parameter's path.")] string id) => Do(() => bench.Remove(draft, table, id).Show());

        // ---------- check, run, save ----------

        [RulesTool("validate", ReadOnly = true), Description("Checks a draft (or a mod in rules/mods, or patch JSON): shape, references, the design rules, determinism and the budgets. Every problem is one sentence.")]
        public string Validate([Description("A draft id, a mod name from rules/mods, or the patch JSON.")] string draft) =>
            Do(() => Workbench.Report(bench.Validate(draft)));

        [RulesTool("simulate", ReadOnly = true), Description("Runs the core headless on a scenario (fire-on-grass, fireball, flood, forest-fire, storm) with the draft on and without it, and says what changed: cells by material, cells burnt, peak counts, ticks per second against the budget, the cell hash.")]
        public string Simulate([Description("A draft id, a mod name from rules/mods, or the patch JSON.")] string draft, [Description("fire-on-grass (a Fire Bolt in a meadow), fireball (a Fireball in a meadow, for spell size), flood, forest-fire (a Fireball in the valley's forest) or storm.")] string scenario,
            [Description("Ticks to run (6 a second); 300 when left out.")] int? ticks = null) => Do(() => bench.Simulate(draft, scenario, ticks));

        [RulesTool("save"), Description("Writes the draft as a mod in rules/mods ({\"title\", \"idea\", \"patch\"}), once it validates.")]
        public string Save([Description("The draft.")] string draft, [Description("A short title.")] string title, [Description("The idea in a sentence or two: what people vote on.")] string idea,
            [Description("The file name without .json; from the title when left out.")] string name = null, [Description("Replace a mod with that name.")] bool overwrite = false) =>
            Do(() => $"Saved {bench.Save(draft, title, idea, name, overwrite)}.");

        // ---------- the room ----------

        [RulesTool("room"), Description("The room's change mode (open, host-only or locked) and the mods on in it, in order, with who added each and the tick. With a mode, first makes a new room in that mode, as a room is made (not in a running game, whose mode was chosen when it opened).")]
        public string Room([Description("Optional: open, host-only or locked, to make a new room.")] string mode = null) => Do(() => bench.Room(mode));

        [RulesTool("apply"), Description("Switches a draft you built on in the room, at the next tick, and says what it changed. Build a draft for what the player asks; pass a saved mod's name only when the player names that mod. The room's change mode decides whose changes go in, and the mod is checked first (validate); a refusal comes back as one sentence, and the world log says who changed what.")]
        public string Apply([Description("A draft id, or a mod name from rules/mods if the player named it, or the patch JSON.")] string mod,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null,
            [Description("The name players read in the log, such as Bigger fireballs. A draft needs one.")] string title = null) => Do(() => bench.Apply(mod, peer, title));

        [RulesTool("revert"), Description("Takes a mod off in the room, at the next tick; the other mods stay on, in order. The room's change mode decides who may.")]
        public string Revert([Description("The mod's id, as room lists it.")] string mod,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null) => Do(() => bench.Revert(mod, peer));
    }
}
