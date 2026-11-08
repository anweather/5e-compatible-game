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
        /// <summary>
        /// What a client is told when it connects: how to go about it. Both hosts send it. Claude Code carries this in the first
        /// message, which every prompt writes again, and the tools' descriptions in the block that stays cached: so what a tool
        /// needs goes in its description, and this stays short (docs/telemetry/chat-context-2026-11-02.md).
        /// </summary>
        public const string Instructions =
            "These tools change the rules of Spell Chemistry Valley, a game about how spells and materials react, in the game the player is watching. When the player asks for a change, write it yourself: " +
            "read what's on now (whats_on, then list, describe, explain), build a new draft, check it with validate, run simulate if validate says to, then apply it with a short title the players will read. " +
            "Each tool's description says what it needs: colours are always set_look. Before you try or refuse what may not be in the rules, read outside_rules, so you can say what's nearest. " +
            "After apply, tell the player what changed, from its answer, and claim nothing it doesn't list. Don't save unless the player asks. " +
            "A mod is a rule that keeps holding; something that happens once (20 trees here, three centaurs near me, a meteor there, a golf ball and a cup to play with) is act, with no draft and no mod. A player's own speed, size, jump and jumps are set_player; something that keeps falling (constant meteors) is add_trigger on tick, a shower with a floor on how often.";

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

        [RulesTool("list", ReadOnly = true), Description("Ids and one-line summaries for a rule table: materials (or terrain, covers, gases), elements, reactions, spells, groups, sim, critters, entities, items, machines, fauna (the kinds of animal, how each is built and whether it comes out on its own: add_species and set_species change them), parts (the shapes animals are built from: each part's role, segments and who wears it), dragon (the dragon that resets the world, its fields and what each does), looks (the look families, the parameters each takes, and what takes no colour), the world's buildings, the things mods have added (things), the triggers (rules that react when a player steps or a golf ball is holed), or a building table (openings, floors, roofs, details, styles). Read these to build a draft for what the player asks.")]
        public string List([Description("The table.")] string table) => Do(() => bench.List(table));

        [RulesTool("whats_on", ReadOnly = true), Description("What's on now, in plain words: the room's change mode, each mod on with who added it and what it changes from what was under it, then the clock and the weather. Read this first: it's what the players are seeing. All the read tools read the rules in force, every mod on included.")]
        public string WhatsOn() => Do(() => bench.WhatsOn());

        [RulesTool("outside_rules", ReadOnly = true), Description("What can't be changed through these tools yet because it isn't in the rules (heat glow, player size, slippery terrain, weather's cover, area fill, colours part by part, a building's rooms), and the nearest thing that can be done for each. Read it before trying or refusing any of those.")]
        public string OutsideRules() => Do(Workbench.Outside);

        [RulesTool("time", ReadOnly = true), Description("The clock: the time of day, the day, the light, and when dawn and dusk are. The clock and the weather aren't rules: read them here and with weather, and change them with set_time and set_weather, not by moving day.startHour.")]
        public string Time() => Do(() => bench.TimeNow());

        [RulesTool("weather", ReadOnly = true), Description("The weather: what it is and how strong, the wind, whether it's held and for how long, or what it turns to next.")]
        public string Weather() => Do(() => bench.WeatherNow());

        [RulesTool("describe", ReadOnly = true), Description("The full entry in force now (the base rules with every mod on), which mods on changed which of its values, everything in the rules that refers to it, for a spell, material or element its look (its mix and scoped looks too) and what set_look may change, for a spell how a player reaches it, for a species (fauna) its shape in words (legs, arms, heads; how tall and long) and every slot flattened (its part and role, what it hangs on, where it sits in body units and in metres at rest), and for a part its segments and every slot that wears it. With a draft, the entry as the draft has it. For sim, the id is a dotted path such as fire.spreadChance.")]
        public string Describe([Description("The table.")] string table, [Description("The entry's id.")] string id,
            [Description("Optional: the entry as this draft has it, its look's mix and scoped looks included.")] string draft = null) => Do(() => bench.Describe(table, id, draft));

        [RulesTool("explain", ReadOnly = true), Description("What happens when an element reaches a cell of a material (a terrain, cover, gas, water or ice): the reaction rules that can hold, top to bottom, and which one decides.")]
        public string Explain([Description("The element, such as fire or cold.")] string element, [Description("The material, such as grass, oil, steam, water or ice.")] string material,
            [Description("Optional: explain under this draft's rules.")] string draft = null) => Do(() => bench.Explain(element, material, draft));

        [RulesTool("places", ReadOnly = true), Description("The world's named places (villages, the lake, the river, the sea, courses, puzzles): each one's id, name, kind and cells. A look's where names one by id. Read-only: mods can't change places.")]
        public string Places() => Do(() => bench.ListPlaces());

        [RulesTool("here", ReadOnly = true), Description("Where the player asking stands: their name and cell, the place they're in, the named places near them with distance and direction, and what they look at. For \"near me\" or \"this village\".")]
        public string Here([Description("How far to look for places, in cells; 120 when left out.")] int? within = null) => Do(() => bench.DescribeHere(within ?? 120));

        [RulesTool("find", ReadOnly = true), Description("Finds what stands in the world: buildings (with the id a mod edits one by) and things (props and items), each with what it is and how far off, nearest first. For \"the house by the lake\".")]
        public string Find([Description("building, thing, or a thing's kind or a building's style, such as barrel or cabin; everything when left out.")] string kind = null,
            [Description("What to measure from: a place's id (places lists them), a building's id, \"me\" or [x, y] as JSON; the player asking when left out.")] string near = null,
            [Description("How far to look, in cells; 64 when left out.")] int? within = null,
            [Description("Only a place's side: north, east, south or west, such as the lake's north shore.")] string side = null,
            [Description("shore for only the edge of a place, such as the sea's beach.")] string edge = null) => Do(() => bench.Find(kind, near, within, side, edge));

        [RulesTool("find_site", ReadOnly = true), Description("Finds where a building of this size can stand: open ground, dry, clear of things and buildings, out of the villages and courses. Gives cells to pass to add_building, nearest first. \"Build a house on the beach\" is this with its size, near sea and edge shore, then add_building at a cell it gave. Read-only: a site is taken only when the mod switches on.")]
        public string FindSite([Description("Its width west to east, in whole cells (a cell is 1.5 m).")] int width, [Description("Its depth north to south, in whole cells.")] int depth,
            [Description("What to be near: a place's id, a building's id, \"me\" or [x, y] as JSON; the player asking when left out.")] string near = null,
            [Description("Only a place's side: north, east, south or west.")] string side = null,
            [Description("shore for the edge of a place: near sea, edge shore is the beach.")] string edge = null,
            [Description("How far from it, in cells; 32 when left out.")] int? within = null) => Do(() => bench.FindSite(width, depth, near, side, edge, within));

        // ---------- draft ----------

        [RulesTool("act"), Description("Does something once, now, for everyone, with no mod: lays a terrain or cover, places a thing, brings out an animal (a species from list fauna) or casts a spell, on open ground near a place, a building, a cell or the player asking. \"Add 20 trees near me\" is act(what: tree, count: 20, spacing: 2); asked again, it's act again, so the mod list stays as it was. \"Spawn three centaurs near me\" is act(what: centaur, count: 3), once the species is there. \"Make a canyon here\" is act(what: land, profile: canyon, along: east, radius: 32, rise: 30), then act(what: tree) along its rim: land reads best dressed. \"Put a ball and a cup near me\" is act(what: cup, within: 20) then act(what: golf-ball): spell golf, a new round. Any spell landing on the ball moves it, players take turns, and fireworks go off when it goes in. Use it for anything that happens rather than a rule that holds: no draft, validate or apply. undo_act takes back ground and things; animals, casts, the ball and the cup stay.")]
        public string Act([Description("A terrain (tree), a cover, a thing's kind, a species, a spell, golf-ball or cup for spell golf (ball alone is the court's kicking ball), or land to shape the land's height.")] string what,
            [Description("How many; 1 when left out. Up to budgets.json lay.maxCells for ground, place.maxLive for things, place.maxSummoned for animals, triggers.maxPerTick for casts.")] int? count = null,
            [Description("What to be near: a place's id, a building's id, \"me\" or [x, y] as JSON; the player asking when left out.")] string near = null,
            [Description("How far from it, in cells; 12 when left out, 32 at most.")] int? within = null,
            [Description("The fewest cells between two; 0 when left out.")] int? spacing = null,
            [Description("A side of what it's near: north, south, east or west.")] string side = null,
            [Description("shore for the edge of water.")] string edge = null,
            [Description("Picks other cells; each act picks afresh when left out.")] int? seed = null,
            [Description("Only on terrain with one of these tags; open ground when left out.")] string[] onto = null,
            [Description("A thing's life in ticks; it stays when left out.")] int? lasts = null,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null,
            [Description("With what: land, the shape: flat, dome, cone, ridge, canyon or cliff; dome when left out. A canyon is a floor between steep walls of rock, a cliff one steep wall; both grow boulders and loose stones by themselves, and need along. For a gorge use canyon, not a hollow: a hollow is a shallow bowl.")] string profile = null,
            [Description("With what: land, how far it reaches, in cells; 8 when left out, 32 at most.")] int? radius = null,
            [Description("With what: land, metres at its middle, below 0 for a hollow; 100 at most either way, but a hollow that would take land below -17 m is refused. At most 1.5 m a cell of radius (land.steepest): a steeper ask is cut to that, so a tall stamp needs a wide radius: 100 m needs radius 67, past the 32 a stamp may have, so the tallest one stamp rises is 48 m, at radius 32. Tell the player when it's less than they asked. For a canyon or cliff, the height of its walls: 20 is tall, 40 huge; a canyon cuts down at most 14 m and raises the rest as its rim. A cliff's high side is left of along (north of east); below 0 it's the other side.")] int? rise = null,
            [Description("With what: land, how uneven, 0 to 100 percent; 40 when left out, 0 for flat. Leave it unless asked for smooth land or wilder land.")] int? rough = null,
            [Description("With a ridge, canyon or cliff, its line: north, east, northeast or southeast.")] string along = null) =>
            Do(() => bench.Act(what, count, near, within, spacing, side, edge, seed, onto, lasts, peer,
                new Workbench.LandAsk { Profile = profile ?? LandStamp.Dome, Along = along, Radius = radius, Rise = rise, Rough = rough }));

        [RulesTool("call_act"), Description("Fires a named act at a place, now, for everyone: a trigger with on: called from a mod that's on, each of its rows done as act would do it, as one act undo_act takes back whole. The reply says what landed row by row: tell the player what didn't. Defined once with add_trigger(on: called, do: rows like {\"act\": \"tree\", \"count\": 5, \"within\": 6} or {\"act\": \"land\", \"radius\": 12, \"rise\": 20}) and kept by saving the mod.")]
        public string CallAct([Description("The named act's id, from list triggers.")] string id,
            [Description("What to be near: a place's id, a building's id, \"me\" or [x, y] as JSON; the player asking when left out.")] string near = null,
            [Description("A side of what it's near: north, south, east or west.")] string side = null,
            [Description("shore for the edge of water.")] string edge = null,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null) =>
            Do(() => bench.CallAct(id, near, side, edge, peer));

        [RulesTool("undo_act"), Description("Takes back an act that laid ground or placed things: the player's last when none is named. Ground goes back to what stood under, unless something has changed it since. Animals and casts can't be taken back.")]
        public string UndoAct([Description("The act's number, from act's answer; the player's last when left out.")] int? act = null,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null) =>
            Do(() => bench.UndoAct(act, peer));

        [RulesTool("scatter"), Description("For a mod to keep or share; to do it now and be done, use act. Scatters a terrain, cover or thing over open ground near a place, a building, a cell or you, in a draft: \"50 trees near the lake\" is scatter(what: tree, count: 50, near: lake, edge: shore, within: 12, spacing: 2), not a building of tree. The cells are picked now from the world's seed and written into the draft (a terrain or cover as a lay row of its world section, laid fresh at the switch and taken back by a revert; a thing as add rows), so every peer gets the same. Another seed picks other cells; the same what and seed replace the row. Laid trees and bushes (brush) are the real terrain: they block, burn and topple. At most 256 cells a mod, within 32 cells.")]
        public string Scatter([Description("A terrain (tree, brush), a cover or a thing's kind.")] string what, [Description("How many.")] int count,
            [Description("What to be near: a place's id, a building's id, \"me\" or [x, y] as JSON; the player asking when left out.")] string near = null,
            [Description("How far from it, in cells; 12 when left out, 32 at most.")] int? within = null,
            [Description("The fewest cells between two; 0 when left out.")] int? spacing = null,
            [Description("Only a place's side: north, east, south or west.")] string side = null,
            [Description("shore for the edge of a place: near lake, edge shore is round its water.")] string edge = null,
            [Description("Which pick: another seed, other cells. 1 when left out.")] int? seed = null,
            [Description("Only onto ground with one of these terrain tags, such as soil or turf; open ground when left out.")] string[] onto = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.Scatter(draft, what, count, near, within, spacing, side, edge, seed, onto));

        [RulesTool("add_building"), Description("Adds a building to a draft: a box of walls with a door and windows, width by depth cells from the cell at, or a whole row in full for any other shape (joins, walls with openings, roofs; docs/design/builders.md). Hollow by default, so a broken door leaves a room to walk into. Says if the site won't stand. Give a new material or style in the same draft (add_material, add_building_part). Cells follow the walls; heights, finishes, roofs and details are only drawn. A site is checked again on every peer when it switches on.")]
        public string AddBuilding([Description("Its north-west cell as JSON, [x, y]; find_site gives these. Leave out with row.")] string at = null, [Description("Width west to east in whole cells, 3 or more.")] int? width = null, [Description("Depth north to south in whole cells, 3 or more.")] int? depth = null,
            [Description("A style's id (list styles, or one add_building_part put in the draft); plain when left out.")] string style = null,
            [Description("The side its door faces: north, east, south or west; south when left out.")] string front = null,
            [Description("A new id, such as beach-house#1; made from where it stands when left out.")] string id = null,
            [Description("solid for a block with no inside; hollow when left out.")] string fill = null,
            [Description("Or the whole row as JSON, with its id and at.")] string row = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.Sited(bench.AddBuilding(draft, at, width, depth, style, front, id, fill, row)));

        [RulesTool("add_wing"), Description("Adds a wing to a building by id in a draft: new walls that meet its wall on one side, its openings there go, and its roof grows. The whole row goes in the draft, so it's one edit. Picks a side where the site stands when none is given. \"Add a wing to the house by the lake\" is find with kind building and near lake, then this with its id.")]
        public string AddWing([Description("The building's id; find gives these.")] string building, [Description("How wide along the side, in whole cells, 3 or more; 4 when left out.")] int? width = null, [Description("How far out, in whole cells, 3 or more; 4 when left out.")] int? depth = null,
            [Description("north, east, south or west; left out picks one the site allows.")] string side = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.Sited(bench.AddWing(draft, building, width ?? 4, depth ?? 4, side)));

        [RulesTool("set_building"), Description("Changes one field of a building in a draft, by dotted path, such as fill, wall.material, wall.finish.color, storeys, roofs.main.shape or walls.north.openings. The whole row goes in the draft, as a mod replaces a row whole.")]
        public string SetBuilding([Description("The building's id.")] string id, [Description("The field.")] string field, [Description(ValueHelp)] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.Sited(bench.SetBuilding(draft, id, field, value)));

        [RulesTool("add_building_part"), Description("Adds or replaces a row of a building table in a draft: a style (defaults for wall, fill, floor and roof, and a recipe), an opening (cells and what they become), a floor, a roof shape or a detail. list openings, floors, roofs, details or styles shows what's there.")]
        public string AddBuildingPart([Description("openings, floors, roofs, details or styles.")] string table, [Description("The row's id.")] string id, [Description("The whole row as JSON.")] string row,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.AddBuildingPart(draft, table, id, row).Show());

        [RulesTool("add_item"), Description("Adds a new kind of thing to carry to a draft: a name, a mass in kg, what it's made of, and a look so the game can draw it with no prefab: {\"family\": \"creature\", \"species\": <one from list looks>, \"color\": [1, 0.8, 0.2], \"scale\": 1}. It isn't in the world until add_thing puts it there. A new kind with no look isn't drawn.")]
        public string AddItem([Description("A new id, lowercase with dashes.")] string id, [Description("Its name as players see it; the log uses it when someone finds it.")] string name,
            [Description("Its mass in kg.")] double? mass = null, [Description("A terrain it's made of, such as flesh or metal.")] string material = null,
            [Description("Its look as a JSON object.")] string look = null,
            [Description("More fields as a JSON object: use, light, zoom, reach, strength, message, breaks, components.")] string extra = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.AddItem(draft, id, name, mass, material, look, extra).Show());

        [RulesTool("add_entity"), Description("Adds a new kind of prop (pushed, not carried) to a draft: a name, hit points, a whole mass, what it's made of, and a look so the game can draw it with no prefab: {\"family\": \"creature\", \"species\": <one from list looks>, \"color\": [1, 0.8, 0.2], \"scale\": 1}. It isn't in the world until add_thing puts it there. A new kind with no look isn't drawn.")]
        public string AddEntity([Description("A new id, lowercase with dashes.")] string id, [Description("Its name as players see it.")] string name,
            [Description("Its hit points, whole.")] int? hp = null, [Description("Its mass, whole.")] int? mass = null, [Description("A terrain it's made of, such as wood or flesh.")] string material = null,
            [Description("Its look as a JSON object.")] string look = null,
            [Description("More fields as a JSON object: str, ac, components, explodes.")] string extra = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.AddEntity(draft, id, name, hp, mass, material, look, extra).Show());

        [RulesTool("add_thing"), Description("Puts a thing in the world in a draft: appended to its chunk's list, so no other thing's id moves. The spot is yours to pick: find_site with 1 by 1 gives open ground, places names the world's places. Says if the cell won't do, and a revert takes it away. A new thing for players to find is one draft: add_item for the new kind, with its look, a spot, then this, and apply with a title. It sits still and can be picked up, and the log names whoever picks it up first. Its chance makes it maybe there, maybe not, decided once from the world's seed, so several candidates at 0.2 each is a rare find.")]
        public string AddThing([Description("The kind: an entity or item, in the rules or added earlier in this draft (add_item, add_entity).")] string kind, [Description("The world cell as JSON, [x, y]; open dry ground with nothing on it.")] string at,
            [Description("Above 0 and at most 1: how likely it is to be in this world at all, decided once from the world's seed and this thing, the same on every peer. Always there when left out.")] double? chance = null,
            [Description("Its turn about the vertical, in degrees.")] double? rot = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.Sited(bench.AddThing(draft, kind, at, chance, rot)));

        [RulesTool("add_trigger"), Description("Adds a trigger to a draft: a rule that reacts when a player steps onto another cell, (on: holed) a golf ball goes in the cup, or (on: tick) time passes, and casts a spell or places something there, the cup's cell for holed. \"Constant meteors\" is a shower: on tick, every 12 or more ticks (2 seconds), do [{\"cast\": \"meteor\", \"at\": \"around\", \"within\": 12, \"delay\": 6}], round each player it's for (each for everyone) until the mod is reverted. \"A meteor at every step I take\" is add_trigger(id: meteor-steps, do: [{\"cast\": \"meteor\", \"at\": \"ahead\", \"by\": 2, \"delay\": 6}]): the meteor lands 2 cells ahead, under a glowing mark, a second later. \"Vines behind me as I walk\" is do: [{\"place\": {\"cover\": \"vines\", \"count\": 1}, \"at\": \"behind\", \"by\": 1}]. Whose steps: the player asking's when left out, a name, or each for everyone. It fires at most once every 3 ticks for a player (twice a second at a run), at most 4 a tick in the room; a trigger casts spells of level 7 at most and does one or two things; a mod has at most 4. An action raises no event, so nothing chains. Then validate and simulate: it walks a loop and counts what fired.")]
        public string AddTrigger([Description("A new id, such as meteor-steps.")] string id,
            [Description("The actions as JSON, one or two: {\"cast\": spell, \"at\": here, ahead, behind or around, \"by\": cells ahead or behind, \"within\": cells for around, \"delay\": ticks before it lands} or {\"place\": {thing, animal, terrain or cover, count, within, spacing, lasts, onto, as a spell's place}, \"at\": ...}.")] string @do,
            [Description("Whose steps fire it (or round whom a tick trigger falls): a player's name, me, or each for everyone; the player asking when left out.")] string who = null,
            [Description("The fewest ticks between two firings for one player, 3 or more (12 or more on tick); 3 when left out (12 on tick).")] int? every = null,
            [Description("Above 0 and at most 1: how likely a step fires it, the same on every peer; every time when left out.")] double? chance = null,
            [Description("The event: step (a player stepping onto another cell), tick (time passing, a shower round each player it's for), holed (a golf ball going in the cup; everyone's when who is left out), or called (a named act, fired by call_act; its do rows are what act takes, {\"act\": what, ...}, up to 8).")] string on = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.AddTrigger(draft, id, on, who, @do, every, chance));

        [RulesTool("add_species"), Description("Adds a new kind of animal to a draft: a row of the fauna table (list fauna) built on a species there is, so it has that one's body and gait and wanders, flees, grazes and comes out in the wilds as it does, with its own name, size, colours and habits. \"A giant rabbit that hops about\" is basedOn rabbit, scale 10, behave {\"play\": 0.6}. Or a body made from other species' slots (a graft): give from instead of basedOn, and it moves and behaves as its first donor. A centaur is from [{\"species\": \"deer\", \"drop\": [\"neck\", \"head\", \"ears\", \"eyes\"]}, {\"species\": \"human\", \"take\": [\"human-torso\"], \"at\": [0, 0.45, 0.8], \"scale\": 1.1, \"coat\": \"own\"}]; describe fauna on each donor lists its slots, and on the new one says where each sits. It's an animal that moves: add_entity with a creature look is a still figure, for a statue or a find. A mod adds at most 12 new entries and 4 species.")]
        public string AddSpecies([Description("A new id, lowercase with dashes.")] string id, [Description("Its name as players see it.")] string name,
            [Description("The species it's built on (list fauna), whose body, gait and habits it takes. Leave out with from.")] string basedOn = null,
            [Description("Its size against the species it's built on (with from, its first donor): 2 is twice as big, 0.5 half. Above 0 and at most budgets.json things.maxScale. 1 when left out.")] double? scale = null,
            [Description("Its coat's main colour as [r, g, b], each 0 to 1. Its species' coat when left out.")] string color = null,
            [Description("Its coat's second colour (belly, markings) as [r, g, b].")] string accent = null,
            [Description("How much its markings glow, 0 to 1.")] double? glow = null,
            [Description("Habits over its species', as a JSON object: active (day, night or always), graze, thirst, play, laze, hunger (shares, 0 to 1), chase (seconds), hunts (species ids).")] string behave = null,
            [Description("Animals per hectare in the wilds (the valley's rabbits are 1.8, deer 0.7); 0 and it never comes out on its own. Half its species' when left out.")] double? density = null,
            [Description(GraftHelp)] string from = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.AddSpecies(draft, id, name, basedOn, scale, color, accent, glow, behave, density, from).Show());

        const string GraftHelp = "Donors as a JSON list, at most 3; each {\"species\": id, \"take\": [slot ids] or \"drop\": [slot ids] (a slot brings every slot hung on it; all of them when neither is given), \"at\": [x, y, z] body units where its first loose slot goes (a mirrored pair keeps its spread), \"dir\": [pitch, yaw, roll] added, \"scale\": 0.25 to 4, \"coat\": \"own\" to keep its own colours}. A slot id already taken gets the donor's id before it. At most 40 slots once flattened.";

        [RulesTool("set_species"), Description("Changes a kind of animal in a draft (list fauna): its coat (\"make the turkeys blue\" is color; every one of that species, as one animal alone can't be picked), how many there are in the wilds (density 0 and no more come out, as for \"turn all deer into centaurs\" with add_species), its size against its normal size, so \"make the deer three times bigger\" is scale 3 and 1 puts it back, how fast it moves (\"rabbits 20 times faster\" is speed 20, cut to 5, the most: say so), its habits, and the donors a body takes slots from (from: \"six legs on the bear\" is from [{\"species\": \"black-bear\", \"take\": [\"fore\"], \"at\": [0.55, -0.45, 0]}], its own slots kept). Every animal of that species changes, in the wilds and in the villages.")]
        public string SetSpecies([Description("The species' id, such as deer or turkey.")] string id,
            [Description("Its size against its normal size in the base rules. Above 0 and at most budgets.json things.maxScale.")] double? scale = null,
            [Description("Habits as a JSON object, merged over its own: active (day, night or always), graze, thirst, play, laze, hunger, chase, hunts; a species not built on another also takes wander and flee (metres) and idle and group ([low, high]).")] string behave = null,
            [Description(GraftHelp + " Replaces the row's from; its own slots stay, first.")] string from = null,
            [Description("Its coat's main colour as [r, g, b], each 0 to 1.")] string color = null,
            [Description("Its coat's second colour (belly, markings) as [r, g, b].")] string accent = null,
            [Description("How much its markings glow, 0 to 1.")] double? glow = null,
            [Description("Animals per hectare in the wilds; 0 and no more come out on their own.")] double? density = null,
            [Description("How fast it walks and runs against its normal speed: 2 is twice as fast, 0.5 half; 0.2 to 5, and past that it's cut to it and the reply says so.")] double? speed = null,
            [Description(DraftHelp)] string draft = null) =>
            Do(() => bench.SetSpecies(draft, id, scale, behave, from, color, accent, glow, density, speed).Show() + (speed.HasValue ? bench.SpeedNote(speed.Value) : ""));

        [RulesTool("add_material"), Description("Adds a new terrain, cover or gas to a draft. Its behaviour comes from material verb groups (burn, soak, dig, melt, freeze, float, dissolve, grow and the rest; rules/README.md). A new terrain also needs a look: {\"look\": {\"family\": \"block\", \"color\": [0.6, 0.7, 1], \"height\": 1.2}} in verbs, with a family from rules/looks.json. It lasts while its mod is on; another mod may name it while it is, and goes when it does; {\"lapse\": {\"after\": 3600}} takes the mod off by itself that many ticks later (an hour, 21600, at most), and lapse.into says what a cell of it turns into (a material that stays). When the mod goes, every cell of it is swept. At most 8 a mod, 32 live in a room.")]
        public string AddMaterial([Description("terrain, cover or gas.")] string kind, [Description("A new id, lowercase with dashes.")] string id, [Description("Its name as players see it.")] string name,
            [Description("Tags such as ground, blocking, slippery.")] string[] tags = null,
            [Description("Verb groups as a JSON object, such as {\"burn\": {\"catch\": 0.4, \"fuel\": 10, \"leaves\": \"dirt\"}}.")] string verbs = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.AddMaterial(draft, kind, id, name, tags, verbs).Show());

        [RulesTool("set_material"), Description("Changes one field of a terrain, cover or gas in a draft, by dotted path such as burn.catch, keeping the rest of it as it is in force now. For what burns: burn.catch is how readily it takes fire, so how fast fire spreads through it; burn.fuel is how long a cell burns, so less fuel starves a fire rather than speeding it. Burning faster is more catch, not less fuel. An unknown field is refused at once with the fields it has. A colour goes through set_look.")]
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

        [RulesTool("add_spell"), Description("Adds a spell, and says whether a player can reach it and how. It acts through an element (or an effect such as terraform) and sits in a group of the spell radial; SRD 5.2.1 levels and damage dice. A mod's spell may have at most radius 6, length 24, size 5, power 6, push 6, raise 4 and dig 4 (past that it's refused: see outside_rules for area fill), and at most 20 dice of d4 to d12. Its look only turns its art's hue, so white, black, grey or pale doesn't show. It needs a look in extra: {\"look\": {\"family\": \"blast\", \"color\": [1, 0.4, 0.1]}}, with a family from rules/looks.json. A thrown ball or blast is a burst with a radius (1 is 9 cells), not a point, which is one cell. To bring something out where it lands, give a place in extra, one of thing, animal, terrain or cover: {\"place\": {\"animal\": \"human-child\", \"count\": 1, \"within\": 0}} (a child that walks off), {\"place\": {\"thing\": \"barrel\", \"count\": 3, \"within\": 2, \"lasts\": 600}}, {\"place\": {\"terrain\": \"tree\", \"count\": 5, \"spacing\": 2, \"onto\": [\"soil\"]}}; count and within at most 8; such a spell needs no element. Tell the player how they reach it. A mod adds at most 12 new entries.")]
        public string AddSpell([Description("A new id, such as ice-knife.")] string id, [Description("Its name.")] string name, [Description("The radial group: fire, water, storm, earth, arcane, nature or time.")] string group,
            [Description("0 (a cantrip) to 9.")] int level, [Description("point (one cell), line, square, burst (a ball, by radius), target or cone.")] string shape,
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

        [RulesTool("set_look"), Description("Changes one parameter of the look of a spell, terrain, cover, gas, element, entity or item in a draft, by id: the only way to change a colour. On an entity or item, family creature takes species, color, glow and scale (list looks has the species), and draws a thing with no prefab. Pick the numbers for what the player says (blue is about [0.25, 0.45, 1]); sizes need no look, as they follow the numbers. " +
            "An element's color is everything that element makes in the world and every spell of that element, each of which has its own colour: this one call sets them all, and the reply names them (spells false leaves the spells as they are). So \"blue fire everywhere\" or \"all fire spells blue\" goes on the element, \"a blue fireball\" on the spell, which colours only its own show (what it leaves on cells is its element's), and a material's on the material. " +
            "A spell's look, and fire, flames, embers, steam and smoke, only turn their art's hue: white, black, grey, a pale colour or a darker shade of the same hue doesn't show on them, and the reply says so: tell the player and offer the nearest hue. " +
            "The parameters: color (three numbers 0 to 1, or \"#rrggbb\"), mix (several colours, with weights if wanted, each cell picking one), blend (true: the mix's colours mixed into one like paint, blue and yellow giving green, red and blue purple, red and yellow orange; false: the patchwork), and any other parameter its look family lists (height, glow, roughness, form...; list looks has the families, describe what an entry takes). " +
            "With where, an element's, cover's, gas's or terrain's color or mix holds only there. With by, an element's, cover's, gas's or spell's colours only what one player casts, or each player's in their own colour. by wins over a spell's own colour, that over where, that over the plain look.")]
        public string SetLook([Description("The spell's, material's or element's id.")] string id, [Description("The parameter: color, mix, or another its family takes, such as height.")] string param,
            [Description("The new value as JSON: [0.25, 0.45, 1] or \"#4073ff\" for a colour; [\"#ff4020\", \"#2050ff\", \"#30d040\"] or [{\"color\": \"#ff4020\", \"weight\": 2}, ...] for a mix; 1.4 for a number. null puts back the family's own, or with where or by takes that look back.")] string value,
            [Description("spell, terrain, cover, gas, element, entity or item: needed when the id names more than one (fire is an element; frost a spell and a cover).")] string table = null,
            [Description(DraftHelp)] string draft = null,
            [Description("Optional: where the color or mix holds, as JSON: {\"place\": \"<id from places>\", \"within\": 20} round a place, {\"at\": [x, y], \"within\": 12} round a cell, or {\"around\": \"me\", \"within\": 10} round the player asking (here() says where they are). Elsewhere the plain look holds; where scoped looks overlap they mix like paint, and a fire caught from two colours burns their mix. \"The fire near me green\" is fire color green with {\"around\": \"me\", \"within\": 10}. \"Fire round the village in three colours\" is places (or here) for its id, then fire mix [three colours] with {\"place\": \"<id>\", \"within\": 20}.")] string where = null,
            [Description("Optional: whose casts the color or mix colours: \"me\" (the player asking), a player's slot (0 the host, then each in join order; here() says yours), or \"each\" with a mix, one colour per player in join order. What they cast keeps it as it spreads. \"The fire I create blue\" is by \"me\"; \"each player gets their own fire colour\" is a mix of one colour per player, as many as may play, with by \"each\" (the fire element for what burns; a spell, table spell, for its own show).")] string by = null,
            [Description("false leaves an element's spells with their own colours, for \"the flames blue but Fireball as it is\"; true or left out sets them with the element's color.")] bool? spells = null) => Do(() =>
            {
                var d = bench.SetLook(draft, id, param, value, table, out var said, (where, by, spells));
                return said + d.Brief();
            });

        [RulesTool("tune"), Description("Sets a sim parameter by dotted path (fire.spreadChance, push.slideCells...) within the bounds rules/budgets.json declares. The dragon is sim too: dragon.budget is how much of the world may change before it comes by itself (never, or 0, and it never does), dragon.speed, dragon.warningTicks, dragon.scale and the rest in list dragon.")]
        public string Tune([Description("The parameter's path; list sim to see them.")] string param, [Description("The value, usually a number.")] string value,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.Tune(draft, param, value).Show());

        [RulesTool("set_player"), Description("Changes a player's stats in a draft: speed (times the wizard's walk and sprint, 0.5 to 3), size (times its height and width, 0.5 to 2), jump (times the height of every jump, 0.5 to 4) or jumps (how many before landing, 1 to 8: 2 is the double jump, 4 a quadruple jump). For the player asking when who is left out, a slot, or everyone. \"Make me faster\" is set_player(speed, 2); \"quadruple jump\" is set_player(jumps, 4); \"make us all tiny\" is set_player(size, 0.5, everyone). A value past its range is cut to it and the reply says so: tell the player. normal takes it back. One stat a call, into the same draft for more; then apply. list players says what each has.")]
        public string SetPlayer([Description("speed, size, jump or jumps.")] string stat, [Description("The number, or normal to take it back.")] string value,
            [Description("Whose: me (the player asking; when left out), a player's slot (0 the host, then each in join order), or everyone.")] string who = null,
            [Description(DraftHelp)] string draft = null) => Do(() => bench.SetPlayer(draft, stat, value, who));

        [RulesTool("remove"), Description("Takes back something the draft added or changed (a material, reaction, spell, element, its look, or a sim parameter). It can't remove base rules.")]
        public string Remove([Description("The draft.")] string draft, [Description("The table: terrain, covers, gases, entities, items, reactions, spells, elements, sim, buildings, things (the id is the cell, x,y), lay (the id is what#seed, as scatter says), triggers, openings, floors, roofs, details or styles.")] string table,
            [Description("The id, or the sim parameter's path.")] string id) => Do(() => bench.Remove(draft, table, id).Show());

        // ---------- check, run, save ----------

        [RulesTool("validate", ReadOnly = true), Description("Checks a draft (or a mod in rules/mods, or patch JSON): shape, references, the design rules, determinism and the budgets. Every problem is one sentence. Says if it changes how the world behaves, which wants a simulate before apply.")]
        public string Validate([Description("A draft id, a mod name from rules/mods, or the patch JSON.")] string draft) => Do(() =>
            {
                var problems = bench.Validate(draft);
                return Workbench.Report(problems) + (problems.Count == 0 ? bench.SimulateNote(draft) : "") + bench.UnseenNote(bench.PatchJson(draft));
            });

        [RulesTool("simulate", ReadOnly = true), Description("Runs the core headless on a scenario (fire-on-grass, fireball, flood, storm, walk) with the draft on and without it, and says what changed: cells by material, cells burnt, peak counts, ticks per second against the budget, the cell hash. Use it to see a change goes the way the player meant: one can do the opposite of what it seems (less fuel makes a fire spread less). Its numbers show the direction, not a multiple, so don't tune a value to fit them. A draft with a trigger on steps walks: its player runs a loop, a cell every 2 ticks, and it says how many times the triggers fired.")]
        public string Simulate([Description("A draft id, a mod name from rules/mods, or the patch JSON.")] string draft, [Description("fire-on-grass (a Fire Bolt in a meadow), fireball (a Fireball in a meadow, for spell size), flood, storm, or walk (only the walk); walk when left out with a trigger on steps.")] string scenario = null,
            [Description("Ticks to run (6 a second); 300 when left out.")] int? ticks = null,
            [Description("Whether a player runs a loop as it goes, so triggers on steps fire; on when the draft has one.")] bool? walk = null) => Do(() => bench.Simulate(draft, scenario, ticks, walk));

        [RulesTool("save"), Description("Keeps a draft, or a mod that's on, as a mod file in rules/mods ({\"title\", \"idea\", \"patch\"}), which the Mods page then lists, once it validates. Only when the player asks to save it: apply is what changes the game, and it needs no save. From the chat, a save is refused unless the player's own message says save.")]
        public string Save([Description("A draft's id, or the id of a mod on now, as room lists it.")] string draft, [Description("A short title.")] string title, [Description("The idea in a sentence or two: what people vote on.")] string idea,
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

        [RulesTool("apply"), Description("Switches a draft you built on in the room, at the next tick, and says what it changed from the rules in force. Build a draft for what the player asks; don't look for a saved mod that's close, and pass one's name only when the player names it. If the player replaces an earlier change, revert that mod too. The room's change mode decides whose changes go in, and the mod is checked first (validate); a refusal comes back as one sentence, and the world log says who changed what.")]
        public string Apply([Description("A draft id, or a mod name from rules/mods if the player named it, or the patch JSON.")] string mod,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null,
            [Description("The name players read in the log, such as Bigger fireballs. A draft needs one.")] string title = null) => Do(() => bench.Apply(mod, peer, title));

        [RulesTool("revert"), Description("Takes a mod off in the room, at the next tick; the other mods stay on, in order. The room's change mode decides who may.")]
        public string Revert([Description("The mod's id, as room lists it.")] string mod,
            [Description("The peer asking, by name; the host when left out. A running game always uses its own player.")] string peer = null) => Do(() => bench.Revert(mod, peer));
    }
}
