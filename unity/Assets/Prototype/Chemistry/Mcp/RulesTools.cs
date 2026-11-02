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
            "read what's on now (whats_on, then list, describe, explain: all of them read the rules in force, every mod on included, and describe says which mod changed what), build a new draft with set_spell, set_material, set_reaction, set_look, tune, add_reaction, add_material or add_spell, " +
            "check it with validate, and where a scenario fits, run simulate and compare the numbers with what the player meant, since a change can do the opposite of what it seems to " +
            "(less fuel makes a fire spread less). Then apply the draft with a short title the players will read. Sizes follow the numbers by themselves (a bigger radius draws a bigger blast), " +
            "but a colour is a look: change it with set_look, picking the numbers for what the player says (blue is about [0.25, 0.45, 1]). " +
            "A whole-element change (\"blue fire everywhere\") goes on the element, one spell's (\"a blue fireball\") on the spell, and a material's on the material; " +
            "a spell's look colours its own show, and what it leaves on cells is its element's. describe shows an entry's look and what it takes; list looks the families. " +
            "A colour can hold in one place only: set_look with where, on the element, cover, gas or terrain. \"Make the fire near me green\" is set_look fire color green with where {\"around\": \"me\", \"within\": 10}: it follows the player as they move. " +
            "\"Fire round the village in three colours\" is: places (or here) to find the village's id, then set_look fire mix [three colours] with where {\"place\": \"<id>\", \"within\": 20}. " +
            "A mix gives several colours, each cell picking one; without where it holds everywhere. A colour a spell chose for its own fire still wins over a scoped look. " +
            "Colours mix like paint: blue and yellow give green, red and blue purple, red and yellow orange. Where scoped looks overlap they mix, and a fire caught from two colours burns their mix. " +
            "\"Blend blue and yellow fire\" is set_look fire mix [blue, yellow] then set_look fire blend true (one green); without blend it's a patchwork. If set_look says a mix is pale, tell the player it shows as its nearest hue. " +
            "A colour can also go with who cast it, with by: \"make the fire I create blue\" is set_look fire color blue with by \"me\"; \"each player gets their own fire colour\" is set_look fire mix [one colour per player, as many as may play] with by \"each\" " +
            "(the fire element for what burns; a spell, table spell, for its own show). by wins over everything: a spell's own colour, then where, then the element's colour. " +
            "Limits, before you promise anything: a spell's look, and fire, flames, embers, steam and smoke, only turn their art's hue, so white, black, grey, a pale colour or a darker shade of the same hue doesn't show on them (a white fireball stays as it was: offer the nearest hue). " +
            "A mod's spell may have at most radius 6, length 24, size 5, power 6, push 6, raise 4 and dig 4, and level 0 to 9 with at most 20 dice of d4, d6, d8, d10 or d12; a mod adds at most 12 new entries. " +
            "Some things aren't in the rules at all yet (heat glow, player size, fauna size, slippery terrain, weather's cover, area fill, buildings): read outside_rules before trying or refusing, so you can say what's nearest. " +
            "The clock and the weather aren't rules: read them with time and weather, and change them with set_time and set_weather, not by moving day.startHour. A new spell isn't much use unreachable: add_spell says how a player reaches it, so tell them. " +
            "If set_look says a colour won't show, tell the player so and offer one that will. Don't look for a saved mod that's close: use one from rules/mods only when the player names it. After apply, tell the player what changed, from its answer.";

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
                lock (gate)
                {
                    bench.Refresh();
                    return act();
                }
            }
            catch (RulesToolException e)
            {
                throw failure(e.Message);
            }
        }

        // ---------- read ----------

        [RulesTool("list", ReadOnly = true), Description("Ids and one-line summaries for a rule table: materials (or terrain, covers, gases), elements, reactions, spells, groups, sim, critters, entities, items, machines, or looks (the look families, the parameters each takes, and what takes no colour). Read these to build a draft for what the player asks.")]
        public string List([Description("The table.")] string table) => Do(() => bench.List(table));

        [RulesTool("whats_on", ReadOnly = true), Description("What's on now, in plain words: the room's change mode, each mod on with who added it and what it changes from what was under it, then the clock and the weather. Read this first: it's what the players are seeing.")]
        public string WhatsOn() => Do(() => bench.WhatsOn());

        [RulesTool("outside_rules", ReadOnly = true), Description("What can't be changed through these tools yet because it isn't in the rules (heat glow, player size, fauna size, slippery terrain, weather's cover, area fill, buildings), and the nearest thing that can be done for each.")]
        public string OutsideRules() => Do(Workbench.Outside);

        [RulesTool("time", ReadOnly = true), Description("The clock: the time of day, the day, the light, and when dawn and dusk are.")]
        public string Time() => Do(() => bench.TimeNow());

        [RulesTool("weather", ReadOnly = true), Description("The weather: what it is and how strong, the wind, whether it's held and for how long, or what it turns to next.")]
        public string Weather() => Do(() => bench.WeatherNow());

        [RulesTool("describe", ReadOnly = true), Description("The full entry in force now (the base rules with every mod on), which mods on changed which of its values, everything in the rules that refers to it, for a spell, material or element its look (its mix and scoped looks too) and what set_look may change, and for a spell how a player reaches it. With a draft, the entry as the draft has it. For sim, the id is a dotted path such as fire.spreadChance.")]
        public string Describe([Description("The table.")] string table, [Description("The entry's id.")] string id,
            [Description("Optional: the entry as this draft has it, its look's mix and scoped looks included.")] string draft = null) => Do(() => bench.Describe(table, id, draft));

        [RulesTool("explain", ReadOnly = true), Description("What happens when an element reaches a cell of a material (a terrain, cover, gas, water or ice): the reaction rules that can hold, top to bottom, and which one decides.")]
        public string Explain([Description("The element, such as fire or cold.")] string element, [Description("The material, such as grass, oil, steam, water or ice.")] string material,
            [Description("Optional: explain under this draft's rules.")] string draft = null) => Do(() => bench.Explain(element, material, draft));

        [RulesTool("places", ReadOnly = true), Description("The world's named places (villages, the lake, the river, the sea, courses, puzzles): each one's id, name, kind and cells. A look's where names one by id. Read-only: mods can't change places.")]
        public string Places() => Do(() => bench.ListPlaces());

        [RulesTool("here", ReadOnly = true), Description("Where the player asking stands: their name and cell, the place they're in, the named places near them with distance and direction, and what they look at. For \"near me\" or \"this village\".")]
        public string Here([Description("How far to look for places, in cells; 120 when left out.")] int? within = null) => Do(() => bench.DescribeHere(within ?? 120));

        // ---------- draft ----------

        [RulesTool("add_material"), Description("Adds a new terrain, cover or gas to a draft. Its behaviour comes from material verb groups (burn, soak, dig, melt, freeze, float, dissolve, grow and the rest; rules/README.md). A new terrain also needs a look: {\"look\": {\"family\": \"block\", \"color\": [0.6, 0.7, 1], \"height\": 1.2}} in verbs, with a family from rules/looks.json.")]
        public string AddMaterial([Description("terrain, cover or gas.")] string kind, [Description("A new id, lowercase with dashes.")] string id, [Description("Its name as players see it.")] string name,
            [Description("Tags such as ground, blocking, slippery.")] string[] tags = null,
            [Description("Verb groups as a JSON object, such as {\"burn\": {\"catch\": 0.4, \"fuel\": 10, \"leaves\": \"dirt\"}}.")] string verbs = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.AddMaterial(draft, kind, id, name, tags, verbs).Show());

        [RulesTool("set_material"), Description("Changes one field of a terrain, cover or gas in a draft, by dotted path such as burn.catch, keeping the rest of it as it is in force now. An unknown field is refused at once with the fields it has. A colour goes through set_look.")]
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

        [RulesTool("set_reaction"), Description("Changes one field of a reaction rule (if.chance, do, log, stop) in a draft. It keeps its place in its element's list. An unknown field, condition or effect is refused at once.")]
        public string SetReaction([Description("The rule's id.")] string id, [Description("The field, such as if.chance or do.")] string field, [Description(ValueHelp)] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.SetReaction(draft, id, field, value).Show());

        [RulesTool("add_spell"), Description("Adds a spell, and says whether a player can reach it and how. It acts through an element (or an effect such as terraform) and sits in a group of the spell radial; SRD 5.2.1 levels and damage dice. A mod's spell may have at most radius 6, length 24, size 5, power 6, push 6, raise 4 and dig 4 (past that it's refused: see outside_rules for area fill), and at most 20 dice of d4 to d12. Its look only turns its art's hue, so white, black, grey or pale doesn't show. It needs a look in extra: {\"look\": {\"family\": \"blast\", \"color\": [1, 0.4, 0.1]}}, with a family from rules/looks.json.")]
        public string AddSpell([Description("A new id, such as ice-knife.")] string id, [Description("Its name.")] string name, [Description("The radial group: fire, water, storm, earth, arcane, nature or time.")] string group,
            [Description("0 (a cantrip) to 9.")] int level, [Description("point, line, square, burst, target or cone.")] string shape,
            [Description("The element it applies, such as cold.")] string element = null, [Description("Or an effect: terraform, levitate, carry, teleport, haste, weather, shapeshift, light.")] string effect = null,
            [Description("The element's power, 6 at most.")] int? power = null, [Description("A burst's radius in cells, 6 at most.")] int? radius = null, [Description("A line's or cone's length in cells, 24 at most.")] int? length = null,
            [Description("A square's size in cells, 5 at most.")] int? size = null, [Description("How hard it throws things back, 6 at most.")] int? push = null,
            [Description("self, or leave out to land where aimed.")] string origin = null, [Description("Damage dice, such as 3d6.")] string damage = null,
            [Description("One or two sentences for players.")] string blurb = null,
            [Description("More fields as a JSON object, such as {\"crater\": {\"radius\": 1, \"depth\": 1}}.")] string extra = null,
            [Description(DraftHelp)] string draft = null) =>
            Do(() => AddedSpell(new Workbench.SpellArgs
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
            }, draft));

        string AddedSpell(Workbench.SpellArgs spell, string draft)
        {
            var d = bench.AddSpell(draft, spell);
            return bench.ReachOf(d, spell.Id.Trim()) + "\n" + d.Show();
        }

        [RulesTool("set_spell"), Description("Changes one field of a spell (radius, power, damage, crater.depth, radial...) in a draft, keeping the rest of it as it is in force now. An unknown field is refused at once, and so is a number past a mod's caps: radius 6, length 24, size 5, power 6, push 6, raise 4, dig 4 (see outside_rules for area fill). A colour goes through set_look.")]
        public string SetSpell([Description("The spell's id.")] string id, [Description("The field.")] string field, [Description(ValueHelp + " Damage may be written 8d6.")] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.SetSpell(draft, id, field, value).Show());

        [RulesTool("set_look"), Description("Changes one parameter of the look of a spell, terrain, cover, gas or element in a draft, by id. A spell's look, and fire, flames, embers, steam and smoke, only turn their art's hue: white, black, grey, a pale colour or a darker shade of the same hue doesn't show on them, and the reply says so. The parameters: color (three numbers 0 to 1, or \"#rrggbb\"), mix (several colours, with weights if wanted, each cell picking one: fire in three colours), blend (true: the mix's colours mixed into one like paint, blue and yellow giving green; false: the patchwork), and any other parameter its look family lists (height, glow, roughness, form...). Says what that family takes, and refuses anything else with the list. An element's look is the colour of everything that element makes in the world (blue fire everywhere); a spell's colours only its own show (a blue fireball). With where, an element's, cover's, gas's or terrain's color or mix holds only there: round a place (places lists them), a cell, or around the player asking, which follows them as they move. With by, an element's, cover's, gas's or spell's color or mix colours only what one player casts (by me: the fire I create), or each player's in their own colour (by each, with a mix); by wins over a spell's own colour and over where.")]
        public string SetLook([Description("The spell's, material's or element's id.")] string id, [Description("The parameter: color, mix, or another its family takes, such as height.")] string param,
            [Description("The new value as JSON: [0.25, 0.45, 1] or \"#4073ff\" for a colour; [\"#ff4020\", \"#2050ff\", \"#30d040\"] or [{\"color\": \"#ff4020\", \"weight\": 2}, ...] for a mix; 1.4 for a number. null puts back the family's own, or with where or by takes that look back.")] string value,
            [Description("spell, terrain, cover, gas or element: needed when the id names more than one (fire is an element; frost a spell and a cover).")] string table = null,
            [Description(DraftHelp)] string draft = null,
            [Description("Optional: where the color or mix holds, as JSON: {\"place\": \"<id from places>\", \"within\": 20} round a place, {\"at\": [x, y], \"within\": 12} round a cell, or {\"around\": \"me\", \"within\": 10} round the player asking (here() says where they are). Elsewhere the plain look holds; where scoped looks overlap they mix like paint.")] string where = null,
            [Description("Optional: whose casts the color or mix colours: \"me\" (the player asking), a player's slot (0 the host, then each in join order; here() says yours), or \"each\" with a mix, one colour per player in join order. What they cast keeps it as it spreads.")] string by = null) => Do(() =>
            {
                var d = bench.SetLook(draft, id, param, value, table, out var said, (where, by));
                return said + d.Show();
            });

        [RulesTool("tune"), Description("Sets a sim parameter by dotted path (fire.spreadChance, push.slideCells...) within the bounds rules/budgets.json declares.")]
        public string Tune([Description("The parameter's path; list sim to see them.")] string param, [Description("The value, usually a number.")] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.Tune(draft, param, value).Show());

        [RulesTool("remove"), Description("Takes back something the draft added or changed (a material, reaction, spell, element, its look, or a sim parameter). It can't remove base rules.")]
        public string Remove([Description("The draft.")] string draft, [Description("The table: terrain, covers, gases, reactions, spells, elements or sim.")] string table,
            [Description("The id, or the sim parameter's path.")] string id) => Do(() => bench.Remove(draft, table, id).Show());

        // ---------- check, run, save ----------

        [RulesTool("validate", ReadOnly = true), Description("Checks a draft (or a mod in rules/mods, or patch JSON): shape, references, the design rules, determinism and the budgets. Every problem is one sentence.")]
        public string Validate([Description("A draft id, a mod name from rules/mods, or the patch JSON.")] string draft) =>
            Do(() => Workbench.Report(bench.Validate(draft)));

        [RulesTool("simulate", ReadOnly = true), Description("Runs the core headless on a scenario (fire-on-grass, fireball, flood, storm) with the draft on and without it, and says what changed: cells by material, cells burnt, peak counts, ticks per second against the budget, the cell hash.")]
        public string Simulate([Description("A draft id, a mod name from rules/mods, or the patch JSON.")] string draft, [Description("fire-on-grass (a Fire Bolt in a meadow), fireball (a Fireball in a meadow, for spell size), flood or storm.")] string scenario,
            [Description("Ticks to run (6 a second); 300 when left out.")] int? ticks = null) => Do(() => bench.Simulate(draft, scenario, ticks));

        [RulesTool("save"), Description("Writes the draft as a mod in rules/mods ({\"title\", \"idea\", \"patch\"}), once it validates.")]
        public string Save([Description("The draft.")] string draft, [Description("A short title.")] string title, [Description("The idea in a sentence or two: what people vote on.")] string idea,
            [Description("The file name without .json; from the title when left out.")] string name = null, [Description("Replace a mod with that name.")] bool overwrite = false) =>
            Do(() => $"Saved {bench.Save(draft, title, idea, name, overwrite)}.");

        // ---------- the room ----------

        [RulesTool("set_time"), Description("Moves the clock on to a time of day, for everyone, at the next tick: it only runs forward, to the next time the day reaches it. The room's change mode decides who may; the answer is what the room said and the time now.")]
        public string SetTime([Description("09:30, 21, noon, midnight, dawn or dusk.")] string time,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null) => Do(() => bench.SetTime(time, peer));

        [RulesTool("set_weather"), Description("Holds a weather, for everyone, from the next tick, ramping in and out; natural lets the weather go its own way again. What the weather lays on the ground isn't in the rules (outside_rules). The room's change mode decides who may; the answer is what the room said and the weather now.")]
        public string SetWeather([Description("clear, cloudy, windy, fog, rain, storm or snow, or natural.")] string weather,
            [Description("How long to hold it, in minutes of play, 1 to 60; 8 when left out.")] int? minutes = null,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null) => Do(() => bench.SetWeather(weather, minutes, peer));

        [RulesTool("room"), Description("The room's change mode (open, host-only or locked) and the mods on in it, in order, with who added each and the tick. With a mode, first makes a new room in that mode, as a room is made (not in a running game, whose mode was chosen when it opened).")]
        public string Room([Description("Optional: open, host-only or locked, to make a new room.")] string mode = null) => Do(() => bench.Room(mode));

        [RulesTool("apply"), Description("Switches a draft you built on in the room, at the next tick, and says what it changed from the rules in force. Build a draft for what the player asks; pass a saved mod's name only when the player names that mod. The room's change mode decides whose changes go in, and the mod is checked first (validate); a refusal comes back as one sentence, and the world log says who changed what.")]
        public string Apply([Description("A draft id, or a mod name from rules/mods if the player named it, or the patch JSON.")] string mod,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null,
            [Description("The name players read in the log, such as Bigger fireballs. A draft needs one.")] string title = null) => Do(() => bench.Apply(mod, peer, title));

        [RulesTool("revert"), Description("Takes a mod off in the room, at the next tick; the other mods stay on, in order. The room's change mode decides who may.")]
        public string Revert([Description("The mod's id, as room lists it.")] string mod,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null) => Do(() => bench.Revert(mod, peer));
    }
}
