using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The probe scenarios by name, for `unity command probe list` and `probe run`
/// (docs/FAST_LOOP.md). Each runs in the Chemistry Yard, in yard coordinates
/// taken from DemoDirector and ValleyShowcase. Directions: 0 N, 1 E, 2 S, 3 W.
/// </summary>
public static class ProbeScenarios
{
    public sealed class Entry
    {
        public string Name { get; }
        public string Description { get; }
        /// <summary>The longest a run may take in Play mode before the editor gives up on it.</summary>
        public float MaxSeconds { get; }
        public Type Type { get; }
        /// <summary>Players in the run: more than 1 means Multiplayer Play Mode virtual players (Player 2 on), started before Play mode.</summary>
        public int Players { get; }

        public Entry(string name, Type type, float maxSeconds, string description, int players = 1)
        {
            Name = name;
            Type = type;
            MaxSeconds = maxSeconds;
            Description = description;
            Players = players;
        }
    }

    public static readonly IReadOnlyList<Entry> All = new[]
    {
        new Entry("smoke", typeof(SmokeProbe), 90f,
            "Walks a few steps, casts each Elements spell once on the yard, then Shape Earth. " +
            "Checks: every prop and item of world/things.json spawned on its cell, the wizard moved, every spell is on the bar and was cast (World log), fire started, " +
            "Levitate lifted a prop, reactions logged, Shape Earth changed the ground, no console errors."),
        new Entry("fire", typeof(FireProbe), 60f,
            "Fire Bolt on the meadow west of the old yard. Counts burning cells at t+10 s and t+20 s. " +
            "Checks: the fire has spread (more than one burning cell at t+10 s), no console errors."),
        new Entry("earth", typeof(EarthProbe), 60f,
            "Earthen Trench west from the pond's bank, then Wall of Stone across it. " +
            "Checks: water in the trench by t+8 s, the wall's cells raised to a wall, no console errors."),
        new Entry("perf", typeof(PerfProbe), 330f,
            "Holds a fixed camera over the valley for seconds=N (default 10) and reports the frame times " +
            "(Editor Play mode, not a player build). Checks: no console errors."),
        new Entry("online", typeof(OnlineProbe), 280f,
            "This Editor hosts and a Multiplayer Play Mode virtual player (Player 2, started if it isn't running) joins on 127.0.0.1; among the rest, the host strikes its wizard till a hit and a miss are rolled, the damage on the right wizard, moved a cell, checksums equal. It joins on " +
            "127.0.0.1. Each casts Fire Bolt, Earthen Trench and Wall of Stone through the network; the virtual player's " +
            "Thunderwave throws a crate and the host's wizard, and the host's wizard falls 10 m. Checks: it joined, every 30-tick checksum (cells and rules) equal on both, the same world " +
            "at the sample tick, no desync seen, the client's terrain drawn as low as the host's under the trenches, the same " +
            "water (cells and drawn surface) and walls, the crate and the host's wizard each within 5 cm on both, no console errors on either, " +
            "then the host resets the world (a crate broken and the wizard walked off first): the countdown heard, both players still in, every thing back with its id, " +
            "the wizard at its start, the clock from 0, and every 30-tick checksum after the reset equal on both (3 or more); and tag passing from the host to the virtual player on touch.",
            players: 2),
        new Entry("menu", typeof(MenuProbe), 60f,
            "The Esc menu with a controller: Start on a virtual gamepad opens it, each sub menu holds its entries, Game then Mods, A opens the " +
            "Mods page and switches the first mod on and off, B goes back, then Credits and Join by code (letters picked, B back). " +
            "Checks: each step landed on the right page, 6 mods listed, the mod switched, the Sound page's − and + step the effects volume, Settings' Play log switches and keeps the choice, B and Start close the menu and the " +
            "wizard's controls come back, no console errors."),
        new Entry("radial", typeof(RadialProbe), 60f,
            "The spell radial with a controller, on a virtual gamepad: hold LB and push the right stick at Storm, then at " +
            "Thunderwave in its arc, release; tap LB; open on Earth and press B; d-pad right; RB. Checks: LB opens it on Storm, " +
            "the arc opens with Thunderwave highlighted, release picks it and camera look comes back, a tap swaps back to " +
            "Fire Bolt, B cancels without changing the spell, the d-pad steps to the next element and RB to the next spell " +
            "in it, no console errors."),
        new Entry("spelllist", typeof(SpellListProbe), 60f,
            "The spell list, on a virtual keyboard: Tab opens it on the spell's element and not the wheel, the arrows move the " +
            "highlight and the tabs, Enter picks and closes, Tab and Esc close, every spell off the wheel can be picked and cast, Q changes element and E spell, " +
            "and LB on a virtual gamepad still opens the wheel. Shots at 1080p and at a 4K scale."),
        new Entry("looks", typeof(LooksProbe), 120f,
            "The material effects staged in the yard at noon, one picture each (--shots): rust, sand and glass, a glow ball breaking, ice shards, mud, hot metal, " +
            "Acid Splash and its stain, oil on the pond and fire across it, the shock flash, a window pane before, shattering and after. Checks: lightning fuses sand, " +
            "a ball survives a drop, breaks on a hard throw and leaves nothing behind, mud slows the wizard, hot metal burns it, acid eats a cover, fire runs over oil on water, the charge runs " +
            "through the pond, a crate tossed softly leaves a pane whole and one thrown hard breaks it, no console errors."),
        new Entry("castlooks", typeof(CastLooksProbe), 200f,
            "The new casts' looks alone: spelllooks' second half, for working on them. Shots with --shots."),
        new Entry("movers", typeof(MoversProbe), 60f,
            "The wave and the boulder aimed off the grid's four ways on flat dirt (issues #11, #21). Shots with --shots. Checks: each " +
            "goes along the aim (within 8 degrees), a crate rides the wave along it, the wave leaves water, no console errors."),
        new Entry("fogcost", typeof(FogCostProbe), 150f,
            "What fog weather costs: frame times over the same view in clear weather and under the weather's fog, with the cells of gas and " +
            "of FogBank drawn. Shots clear, foggy and low in it with --shots. Checks: the fog lies, its thin veil isn't drawn cell by cell, no console errors."),
        new Entry("golf", typeof(GolfProbe), 150f,
            "Spell golf's hole played through the caster (GolfCourse, World.Golf): the tee, flag, hole and ball drawn, the HUD's stroke line, Gust of Wind " +
            "to the hole; then the meadow coil beside it: its cells laid, no tree on its pond, a bolt into the pond opens the gate. Shots with --shots."),
        new Entry("cup", typeof(CupProbe), 90f,
            "A ball and a cup placed through the real MCP server (act) by the wizard: the course moves to them, Fire Bolt moves the ball, and it goes in with fireworks over the cup. Shots with --shots."),
        new Entry("feel", typeof(FeelProbe), 280f,
            "What a hand on the controls should feel, on a virtual pad and keyboard: running onto ice and Frost's glaze keeps the speed and slides, snow and a bank slow " +
            "the wizard to the rules' share; F cracks then breaks a pane; F and Square knock a crate, F the ball, by CastFeel's strength, the arm reaching it, a hit-stop, one landing (a miss too, and off still), the same ticks with and without the stop; Plant Growth charged on burnt ground and Wall of " +
            "Stone held to a ramp, on keys and pad; Frost over a swimmer; Left Ctrl held and pressed swimming to a bank never rolls. Shots with --shots."),
        new Entry("aimlooks", typeof(AimLooksProbe), 120f,
            "The aim marker of each kind of cast, one picture each (--shots): lanes at an angle, a ring growing as Cast is held, the outlines of what rises, Spike Growth's area, " +
            "all on flat ground, on water and on a slope, and the lock-on on a deer. Checks: each spell's kind, one mesh draws it, the ring grows with the charge, no console errors."),
        new Entry("spelllooks", typeof(SpellLooksProbe), 400f,
            "The combo table's new spells staged in the yard, one picture or more each (--shots): Forge on sand, mud and metal; Falling Star's mark, fall, impact and embers; Whirlwind " +
            "carrying sand, water, fire, smoke and snow; a dust cloud flashing in fire; Lava Pool; Rain Cloud; Sand Blast; Light at night; the pick's swing; cracked glass, ice and a frozen " +
            "raider. Checks: what each does to the cells, no console errors."),
        new Entry("dragon", typeof(DragonProbe), 260f,
            "One whole flight of the Dragon, staged quicker than a real one (DragonView, World.Dragon). Checks: the world keeps a start, R summons it, the sky darkens and it's seen, " +
            "a wizard in the open is downed and one behind a built wall isn't, every chunk is freshened, and the Gauntlet's lava, a meadow cell, a felled tree, a moved crate and a broken one " +
            "are back after it; no console errors. Metrics: keep_start_ms, palette, start_state_kb, flight_seconds, look_breath and look_clear frame times in one view."),
        new Entry("roomopen", typeof(RoomOpenProbe), 200f,
            "Live rule changes in an Open room, this Editor hosting and the virtual player joined: the mod on solo stays on; the client's Big fireball on at one tick for both, the host reverts it, a bad patch refused alike, a reset keeps the mods, the client starts the defense waves through the host, the client's wizard punches the host's (the same punch logged on both), a late joiner gets the mods, the waves and the punch. Checks: the same line, room and rules on both after each step, every 30-tick checksum equal.", 2),
        new Entry("faunaresize", typeof(FaunaResizeProbe), 150f,
            "set_species deer scale 3 through the rules MCP in an Open room (#80), this Editor hosting and the virtual player joined: each side puts the same three deer by its wizard. " +
            "Checks: the same three scales on both peers before, three times them on both once the mod is on, and back once it's reverted; every deer out on each side at its row's size each time; " +
            "pace one for one at 0.35 and 1.25 and 1.5 at 3; checksums equal, no client errors. Metrics: pace_at_3 (how much a 3× animal's speed and flee distance grow). With --shots, a film of the three deer from the host's side.", players: 2),
        new Entry("triggerview", typeof(TriggerViewProbe), 420f,
            "Two step triggers through the rules MCP in an Open room (bolt-steps: a Fire Bolt 2 behind, every 6; meteor-steps: a Meteor 2 ahead after 6 ticks, every 12; each player's), " +
            "this Editor hosting and the virtual player joined; each wizard walks 12 cells. Checks: every triggered cast plays one show on each peer (one projectile a bolt, one meteor a meteor, no small star), " +
            "the meteor falls for its delay, the log names Player 1 and Player 2, a late joiner plays no old shows; after a long walk (a loop, a step a tick) a second late joiner fires every cast the host did " +
            "(metrics late_joiner_strike_history and long_walk_strike_history: the stands, strikes and leavings each joiner's history held, #83); nothing fires or shows once reverted, checksums equal. " +
            "With --shots, a film from behind the host's wizard.", players: 2),
        new Entry("handlooks", typeof(HandLooksProbe), 240f,
            "What each hand holds, shown on the other peer's wizard (HandHeld): this Editor hosts, the virtual player joins, each attunes differently, an empty hand among them, and each peer shows both wizards' hands. " +
            "Between, both cast at once with their different hands (D16): a charge, a merge, one hand firing while the other charges; both heard, the same ticks run, checksums equal.", players: 2),
        new Entry("airpeers", typeof(AirPeersProbe), 240f,
            "The air spells on two peers (D16, aimed-spells): this Editor hosts, the virtual player joins and starts the waves, and both put the same flyer and two raiders down after the same net tick. " +
            "Lightning from the host at the flyer; Gust of Wind on a raider tapped from the host, held from the client, then a third shove it braces against; a Whirlwind from the client on the other. Checks: each lands, the checksums from the raiders on are equal, no desync.", players: 2),
        new Entry("peervanish", typeof(PeerVanishProbe), 360f,
            "The other player's wizard stays drawn (playtest 2026-11-08 B1): this Editor hosts, the virtual player joins beside the host, a Hold run starts (the first-wave moment), " +
            "the keep is put on its last point at one net tick on both and the raiders take it, the host resets the world, and the client walks off and back. " +
            "Checks at each step: each peer draws the other's wizard (its body's renderers on, not forced off or shadows only, on a drawn layer, its bounds round it) where its owner has it; checksums equal.", players: 2),
        new Entry("roomruin", typeof(RoomRuinProbe), 300f,
            "A burning row house through an Open room: this Editor hosts, the virtual player joins, the host's fireball takes it down, and both, and a late joiner, hold the same ruin and the same checksums.", players: 2),
        new Entry("productsview", typeof(ProductsViewProbe), 300f,
            "What a material leaves, and a mod's own covers, drawn from the rules (primitives P3 and step 2) on two peers: this Editor hosts an Open room, the virtual player joins. " +
            "Base covers wear films as before and the base ash colour is today's; a Fire Bolt on a grass lot leaves base ash, charred with no film. A mod through the real MCP (add_material blue-soot, set_material grass burn.ash) " +
            "and a Fire Bolt on a second lot: the soot is charred and its film is the soot's look, the base ash unchanged, both peers alike, checksums equal; reverted, the soot is swept. " +
            "Then black ice cast in a slick (lapse after 120 ticks): drawn as a film in its colour on both, the host's wizard skids across it, the panel shows time left; at the lapse every slick goes in every chunk on both, " +
            "and the view's rules and the mods list follow. With --shots: products-view-before.jpg, products-view-after.jpg and black-ice/ frames at 720p, off screen.", players: 2),
        new Entry("glowmoss", typeof(GlowMossProbe), 300f,
            "A look's glow (primitives P4) on P2's glowing moss, on two peers: this Editor hosts an Open room, the virtual player joins. The base glows nowhere. Through the real MCP, " +
            "set_time dusk and the moss mod (a cover with look.glow 0.3 spreading onto stone and rock, laid on two river stones): it creeps, every moss cell glows and nothing else, its film is its colour as before. " +
            "At midnight a moss cell's glow passes 0.15 and a bare stone's is 0, in the moss's colour; the other peer has the same moss and glowing cells, checksums equal; at noon under 0.03; reverted, no glow is left. " +
            "With --shots: glow-moss/ frames of the moss creeping at dusk, 720p, off screen.", players: 2),
        new Entry("modthing", typeof(ModThingProbe), 90f,
            "A mod's new kinds of thing, solo (docs/RULES_MCP.md \"Things\"): an item and a prop spawn on their cells dressed as animals, resting on the ground; the first pick-up logs a find and the second doesn't; a revert takes the item out of the hand and the world, and the same mod brings them back. With --shots, a picture standing and one in hand."),
        new Entry("roomthing", typeof(RoomThingProbe), 150f,
            "A mod's new kinds of thing through an Open room: the client adopts them dressed, finds one (the line on both), a late joiner gets them dressed and knows the find, and a revert takes one out of the client's hand. Checks as roomopen.", 2),
        new Entry("graftcoats", typeof(GraftCoatsProbe), 200f,
            "A centaur, a faun and a six-legged bear by add_species with from, each with a donor in its own coat (graft-coats, J-067), on through the rules MCP in an Open room, " +
            "this Editor hosting and the virtual player joined; each side puts the same three by its wizard. Checks: every segment of each draws in the colours its donor's own animal " +
            "of the same individual wears, what the animator writes is those colours, the own-coat slots differ from the row's coat, the same on both peers; a revert takes them away; " +
            "checksums equal. With --shots, a film of the three walking and a picture of each.", players: 2),
        new Entry("placeview", typeof(PlaceViewProbe), 300f,
            "Placing in play through an Open room (docs/design/events-and-placing.md): a mod's world section lays trees and brambles, and its spells place barrels, crates that lapse, a summoned child, " +
            "trees and brambles. Checks: on both peers the same things on the same cells, resting on the ground; the child out on its cell; the new tree and bramble cells dressed; the crates gone on both " +
            "once they lapse; a late joiner with the same; the quiet frame's cost (no garbage); checksums equal. With --shots, a film of the site from the switch to the crates lapsing.", players: 2),
        new Entry("roomhost", typeof(RoomHostProbe), 90f,
            "A Host-only room: the client's change and revert refused with the sentence on both, the host's goes in. Checks as roomopen.", 2),
        new Entry("roomlocked", typeof(RoomLockedProbe), 90f,
            "A Locked room: every change refused on both, the mods it started with stay. Checks as roomopen.", 2),
        new Entry("roommcp", typeof(RoomMcpProbe), 60f,
            "The game's rules MCP over real HTTP, solo: the handshake, tools/list, room, apply and revert with the room's line back and the rules changed, an unknown mod a sentence, a web page's Origin refused."),
        new Entry("roomclaude", typeof(RoomClaudeProbe), 330f,
            "Holds the game open for seconds=N (at least 30) while a real Claude, outside, adds a mod through the MCP. Checks: a mod came on, its line is in the log."),
        new Entry("roomclip", typeof(RoomClipProbe), 330f,
            "Films both screens, into the host's Temp/Probe/online/room-clip: a Fireball, then a real Claude outside adds Big fireball through the MCP within seconds=N, then the next Fireball. Metrics: when each happened, for ffmpeg.", 2),
        new Entry("roomasks", typeof(RoomAsksProbe), 480f,
            "Three asks no saved mod covers, written by a real Claude outside as new mods within seconds=N: a bigger fireball, grass burning twice as fast, a new reaction of fire on water. Each is measured on a stage against the base rules. --shots for each."),
        new Entry("playlog", typeof(PlayLogProbe), 40f,
            "The frame-rate log (PlayLog) writing to a folder of its own: lines a second on another thread, no garbage from a frame, a hitch, a mark with its picture, the last ten logs kept, the summary line."),
        new Entry("downed", typeof(DownedProbe), 30f,
            "The punch on a virtual gamepad's Circle, being struck (open ground, a wall, water), and a wizard at 0 health: down, what it carries dropped, then back at the yard, full."),
        new Entry("castfeel", typeof(CastFeelProbe), 200f,
            "The beats of a cast by elements (CastBeats): a single, a pair, one charged and one cancelled, and their sounds (CastSounds). Checks: each beat and sound in order, the thump at contact, the hum rising by level and gone after a cancel, pause, fall, Shapeshift, reset or quit, the hands at their targets once settled, " +
            "the slam about 0.1 s, a charged cast's beats to the frame as they were, an uncharged cast's wind-up and timing, casts on each other's heels all going on time, a pulse at each charge level, the slower walk, the recoil only drawn, hit-stop leaving the tick and hash alone, shake and rumble by size and distance on a virtual pad, off still, the motors stopping, no console errors. With --shots, the wizard from the side at each beat, before and after."),
        new Entry("castclip", typeof(CastClipProbe), 90f,
            "Films a single, a pair and a charged cast from the side, none of fire, as the game is now, with the mixed sound, into --shots: frames, sound.wav and timeline.txt, for ffmpeg."),
        new Entry("punchclip", typeof(PunchClipProbe), 60f,
            "Films the punch knocking a crate, from the side, then a miss, with the mixed sound, into --shots: frames and sound.wav, for ffmpeg. Metrics: how far the crate went, and where its start line goes."),
        new Entry("dragonclip", typeof(DragonClipProbe), 200f,
            "Films the Dragon for review, into --shots: frames and sound.wav, for ffmpeg. The summons, the sky going dark and the crossing and dive on a short warning, a cut to a middle pass at the real speed, its wall of fire."),
        new Entry("chimeclip", typeof(ChimeClipProbe), 60f,
            "Films the casts that used to chime (Scalding Steam, Mud, Rain Cloud, a d-pad cast) with the HUD and the mixed sound, into --shots: frames and sound.wav, for ffmpeg. Checks: each cast started, frames and sound were written."),
        new Entry("millclip", typeof(MillClipProbe), 90f,
            "Films, for Andrew's board, into --shots: three deer backed into a corner of the valley by the wizard, then two leashed deer in the meadow with the wizard just outside the leash. Same corner and cameras each run. Checks: the corner found, the film saved with its sound. Metrics: each deer's moving share, metres walked and reversals a second."),
        new Entry("uxclip", typeof(UxClipProbe), 120f,
            "Films what plain shots can't show, with the HUD, into a folder each under --shots (frames for ffmpeg): grimoire/ (d-pad down, Back, then the menu's button and Back), " +
            "castbar/ (three casts pressed late between ticks) and house/ (a walk into a house's wall, the follow camera behind). Checks: each runs; a metric for the camera arm's shortest."),
        new Entry("combo", typeof(ComboProbe), 85f,
            "Casting by elements on a virtual gamepad, with the HUD on: water then earth loaded in the window, the meter draining, the cast landing at the crosshair and joining the " +
            "grimoire; walking at the rules' factor under the meter; B cancels with an item in hand and doesn't throw it; d-pad right cycles, up casts the selected spell, down opens the " +
            "grimoire. Shots of each and of the whole HUD (--shots). Checks: each of those, no console errors."),
        new Entry("frost", typeof(FrostProbe), 40f,
            "Ray of Frost from the lake's shore (the dry square outside the yard nearest it with lake water two squares south) " +
            "out over the water. Checks: the shore was found, while it plays one frost beam (FrostBeam) runs from the wizard to the end " +
            "of the line with no IceLance sphere or projectile, the wet squares in the beam froze, the ice is drawn at or above " +
            "the lake's surface and is solid (a ray down hits the Ice collider), Sleet Storm on open water freezes a wide circle, no console errors."),
        new Entry("explore", typeof(ExploreProbe), 60f,
            "The wizard goes to meadow 3 chunks or more from the yard (outside the old chemistry area) and casts Fire Bolt there. " +
            "Checks: the chemistry covers the whole valley, the chunks around the wizard woke (25 at most) and the yard's went to sleep, " +
            "the fire spreads there, the sleeping yard is unchanged, and it wakes when the wizard comes back, no console errors."),
        new Entry("collision", typeof(CollisionProbe), 40f,
            "The invisible blockers against what is drawn: the nearest valley tree north of the yard, then a Wall of Stone. " +
            "Checks: a ray through the trunk hits its blocker and one 1 m beside it passes, the wall's blocker tops out within " +
            "25 cm of where the wall is drawn, no console errors."),
        new Entry("wildfire", typeof(WildfireProbe), 60f,
            "A lot of fire, for frame times: the Wildfire mod on, Fire Bolts at five spots in the meadow north of the yard, 20 s of " +
            "burning with the camera on it. Metrics: the burning peak, frame mean and p95 while it burns, and the grid's step, " +
            "cell drawing and fire lights per frame. Checks: the fire spread (over 150 squares), p95 within 33 ms, no console errors."),
        new Entry("charge", typeof(ChargeProbe), 40f,
            "Holding Cast charges a spell, on a virtual gamepad through the wizard's own controls, the camera aimed at the meadow: " +
            "Raise Earth held on the right trigger for 1.1 s, then Lower Earth tapped. Checks: the held cast raised its square three " +
            "steps, the tap dug one, no console errors."),
        new Entry("hits", typeof(HitsProbe), 40f,
            "Spells catching a wizard, applied as if cast by someone a few metres off (a caster never catches itself): Fire Bolt on " +
            "the wizard's square, Ray of Frost through it, Shocking Grasp on it. Checks: the fire throws it back over 0.5 m, the frost " +
            "slows it, the lightning stops it a moment and knocks it, no console errors."),
        new Entry("ball", typeof(BallProbe), 40f,
            "The court east of the yard and its ball: the wizard runs at the ball from 6 m away (Kicker). Checks: the court is " +
            "built (rails and goals), the ball spawned, the run kicks it over 2 m and it stays inside the court, no console errors."),
        new Entry("court", typeof(CourtProbe), 60f,
            "The court's floor, gate and score (BallCourt, CourtGate): the wizard walks from 9 m west of the gate onto the court, " +
            "then the ball is sent into the east goal and later left outside the south rail. Checks: the centre spot is a dirt " +
            "cell painted bare earth with no grass, the gate is shut, opens wide as the wizard comes up and lets it 3 m onto the " +
            "court, shuts once it has gone; the goal counts one at the east end (logged, on the board) and the ball is back on " +
            "the centre spot after it and after going out, no console errors."),
        new Entry("river", typeof(RiverProbe), 90f,
            "The river from the falls into the lake (river terrain refills itself) and the bridge over it: the river measured across at " +
            "three places down its course, the wizard dropped into its middle, then walked over the bridge from the west landing to the " +
            "east, then digging from the river's edge east of the yard with Lower Earth, charged. Checks: 5 squares across or more and " +
            "over 1.8 m of water in the middle everywhere measured, the wizard swims there, the bridge is built over the river with its deck " +
            "over 1.2 m above the water, the wizard crosses it without swimming, over the deck, to within 1.5 m of the east landing; a " +
            "river square near world (120, 30), full; the dug square fills from it; the river full again after, no console errors."),
        new Entry("edge", typeof(EdgeProbe), 90f,
            "The wizard runs at each end of the land (where the valley's terrains stop: north, east, south, west) from 15 m inside it, " +
            "for 10 s. Checks per edge: the wizard is still over the land and standing on the ground, no console errors."),
        new Entry("sky", typeof(SkyProbe), 90f,
            "The clock and the weather, switched as a mod switches them: noon, sunset, then 22:00, then a storm at 13:00 with lightning " +
            "every half second, then fog. Checks: the Sky object is built, the sun is bright at noon and the clock line says so, " +
            "night is dark with the moon up, the storm is in force (wind too) with rain falling, strikes logged and a bolt drawn, " +
            "the clouds over 70% of the sky in it, " +
            "the sun dimmed, a meteor shower at 23:00 with meteors drawn falling, snow at noon (over 2000 flakes in the air, over half " +
            "the puddles laid in the yard frozen), fog closes the haze in, no console errors."),
        new Entry("apikey", typeof(ApiKeyProbe), 60f,
            "A player's own key against ApiStandIn, no real key or call: no key stays on Claude Code, a good key turns the chat to ApiChat, a message runs through the server and the loop, the spend cap stops the next one. " +
            "Checks: the check doesn't block, the key is in no record, console, environment or save, Remove goes back to Claude Code, no console errors."),
        new Entry("groundbuild", typeof(GroundBuildProbe), 30f,
            "What GroundSwitch put on the terrains (unity/Temp/ground-builder says on or land; no file is off) and what the ground's Bind took: compare runs. Checks: the chunks are built or baked as switched, each collider holds what's drawn, the terrains keep TerrainRock, no console errors."),
        new Entry("landshapes", typeof(LandShapesProbe), 240f,
            "A canyon and a cliff through the real MCP server with the wizard on them: the ground drops under the feet and comes back, the walls are walls, rocks grow on the faces and go with an undo, and the terrain draws with the rock shader."),
        new Entry("stamplooks", typeof(StampLooksProbe), 480f,
            "Land stamps seen small and with crates on them (#147, #148): a radius 3 dome is a bump (no rock paint, no boulders), then ten crates on a 15 m dome as it rises, against the same crates set on the still dome. " +
            "Checks: none buried, the carried crates go no higher or much further than on the still dome, the carry query's cost a frame with crates on the stamp, how far each slid and how fast, and where they end after the undo, no console errors."),
        new Entry("castlecliff", typeof(CastleCliffProbe), 240f,
            "The castle on its cliff (the Hold's pad reaching north of the castle and dropping off as a crag, buildings.json) with its stable, and the canyon beside the river north of it (world/maps/valley/stamps.json), seen from the town, over the pad, along the walk from the gate to the lip, from the cliff's foot and from the canyon's floor. " +
            "Checks: the canyon laid, the pad and road unmoved, no tree on rock, the crag laid, one height from the gate to the lip by the ground and by the wizard walking it, a drop below the lip, the cliff a rock wall the wizard can't walk up, rocks on its faces, the stable standing, the canyon deep with a rock floor and a dirt path, no console errors."),
        new Entry("cliffclip", typeof(CastleCliffClipProbe), 120f,
            "Films the castle on its cliff from the town, then an eye-height walk from the castle's gate past the stable to the cliff's lip and the view down its face, into --shots: frames and sound.wav, for ffmpeg (ClipFilm). Checks: filmed."),
        new Entry("stampclip", typeof(StampClipProbe), 120f,
            "Films ten crates riding a dome stamp as it rises (#147; 15 cells across, 4 m high, so no crag boulders hide them), into --shots: frames and sound.wav, for ffmpeg (ClipFilm, not the Recorder). Checks: filmed, the crates rode up."),
        new Entry("act", typeof(ActProbe), 60f,
            "Acts through the real MCP server in a running game (docs/design/world-acts.md): 20 trees near the wizard twice leave the mod list as it was, three deer come out, undo_act takes the second trees back and refuses animals, and set_species turns the turkeys blue. Checks: each of those, no console errors."),
        new Entry("chat", typeof(ChatProbe), 40f,
            "The chat box to Claude (J-060), with a stand-in for claude: Enter opens it and the wizard's controls go off, and come back; a message reaches one process on stdin, with the game's tools only and dontAsk; its words and tools show; a second message waits; notes.md stays bounded; " +
            "a failure shows, a sign-in failure says to sign in, and with no claude the install line shows. With --shots, a film of the box."),
        new Entry("chatprompts", typeof(ChatPromptsProbe), 2400f,
            "A fixed set of prompts (docs/telemetry/chat-prompts.json) through the real chat, each on a fresh world, keeping the raw stream and watching what the frames show beside the stamp. " +
            "With a stand-in unless --real-claude true --out <folder> is given, which spends real prompts on the signed-in Claude (J-064: at most 8 a run, and it stops at a sign-in failure or a limit). " +
            "--model, --context bare and --also <args> change how claude starts, for that run only; --only <kind> sends one prompt, and --set <file> another set of prompts. Each prompt's world is kept beside its record for tools/chat-strict.mjs. " +
            "With --shots, replays the recorded patches and films them, spending nothing. --replay <folders> casts each run's recorded spell on the lake, spending nothing."),
        new Entry("dictation", typeof(DictationProbe), 240f,
            "The real Windows recogniser on the real microphone, three holds with the level meter and three without: no press may stop the game, and every hold must end; no Windows setting voice needs is off; the player's -voice-check runs short and each of its passes ends."),
        new Entry("voice", typeof(VoiceProbe), 70f,
            "Talking to Claude (J-061), with a stand-in recogniser and a stand-in for claude: a hold opens the box, takes the controls and tells the player once where their voice goes; the words fill the box as they come; letting go waits for the recogniser, then sends once; " +
            "a recogniser that can't says why in a line and sends nothing; closing the box drops the words; the keys show on the Controls page's table; a hold played from a sample in tools/voice-samples/out on its real timing, word by word, letting go early, and with Claude busy, the words queued, in order, capped, on one status line. Needs make.ps1 run first. With --shots, a film of a hold. Real dictation is left to a real voice."),
        new Entry("transcribe", typeof(TranscribeProbe), 240f,
            "A transcriber (Whisper's kind) with the stub transcriber standing in for a model: the speech check from a WAV file with the stub busy 1.5 s on its own thread, frame times idle and meanwhile, no hitch; " +
            "one slower than its timeout ends at the timeout with a line, and the next works; a silent file says nothing within the timeout; a hold through GameVoice with -speech's choice set to the stub puts the words in the chat once; " +
            "--only microphone opens and closes the default microphone instead (on a Mac that open once blocked the Editor for minutes). No model, no recording kept, no Claude."),
        new Entry("rowlight", typeof(RowLightProbe), 240f,
            "A stucco house and a stone one through the MCP beside a village house, by day, at dusk and at night: no wall of theirs glows, and with --shots each is filmed close."),
        new Entry("holdclip", typeof(HoldClipProbe), 150f,
            "The hold-batch clip, with the HUD, on a virtual pad: in a run at the water tower the right trigger attunes the hand, then casts, the wave timer counting down; then high in the open keep raiders strike the crystal as it cracks and shatters. Into --shots: frames, sound.wav and timeline.txt."),
        new Entry("holdbodies", typeof(HoldBodiesProbe), 480f,
            "The Hold's raider kinds from the valley's animals, and the folk off the raiders' road in a run (hold-bodies): a run starts from the plate, the workers whose rounds go up the road walk to their stops for a run and none is left near it, then a mixed wave comes up the road, each kind drawn with its row's body, and strikes the crystal. With --shots, the clip: frames, sound.wav and timeline.txt."),
        new Entry("towers", typeof(TowersProbe), 150f,
            "Attuning at the castle's towers (hold-batch), on a virtual pad in a run: away from them the trigger attunes nothing; at each tower whose element the wizard has, the right trigger attunes the hand to it in its moment, the HUD names the tower, then the same trigger casts. Each tower shows its element. With --shots, each tower."),
        new Entry("crystal", typeof(CrystalProbe), 120f,
            "The keep's crystal at the Hold's heart (hold-batch): raiders beside the heart strike it, and as the keep's health is stepped down it cracks in stages, shatters at 0 and comes back whole for the next run. With --shots, pictures from up on the walls at each stage."),
        new Entry("hold", typeof(HoldProbe), 400f,
            "The valley's laid row (the-hold.md): its cells and drawing, no tree on it, a walk through the gate into the keep and its room, " +
            "up a stair along the wall walk over the gate, a cast from there, the gate shut and burnt, and the frames from the floor and the walls."),
        new Entry("hands", typeof(HandsProbe), 300f,
            "The hands (D16, unified-casting.md; the-hold.md, phase 3) on a virtual pad, mouse and keyboard: on outside the Hold, attuning from the spell list (a moment; one element on both hands), " +
            "a held trigger charging to the most and firing on its release, both held merging on screen and firing once, one hand tapping while the other charges, attuning in the room, " +
            "each hand's thrust, throw and sweep, the same gesture on both, different gestures together, the second button a moment late, a cooling hand refused, a spell aimed past its range, " +
            "an empty hand's acts, and the D-pad's kit."),
        new Entry("aimed", typeof(AimedProbe), 240f,
            "Aimed spells (D16, unified-casting.md) in a Hold run on the north wall, the hands on a virtual pad: Lightning at a flyer a little off the crosshair, " +
            "missing with lock-on and pitch off as the game was, then locking on and hitting with them on; Gust of Wind knocking a raider below back; a Whirlwind lifting one and flinging it. " +
            "With --shots, the before and after pictures."),
        new Entry("aimedclip", typeof(AimedClipProbe), 240f,
            "The aimed scenario filmed with the HUD into --shots for the demo reel: frames and sound.wav, for ffmpeg."),
        new Entry("mergeclip", typeof(MergeClipProbe), 120f,
            "Films three different merges with the HUD on a virtual pad (D16, \"The merge\"): each pair's icons slam into its own glyph as both hands charge, then it goes off. Scalding Steam, Lightning, Wall of Stone; the attuning between is cut. Into --shots: frames, sound.wav and timeline.txt."),
        new Entry("pairglyphs", typeof(PairGlyphsProbe), 30f,
            "Every merged pair's icon (D16, \"The merge\") on one sheet, as the HUD draws it, with the spell's name: --shots for a before and after."),
        new Entry("handsmenu", typeof(HandsMenuProbe), 150f,
            "The pad's hands menu (pad-hands-menu) on a virtual pad: hold the d-pad's down, each stick points its hand at an element, letting go attunes both (the second after the first), B lets go without; the hands' icons show only while a hand is at work; in a Hold run nothing picks away from the towers and the tower's element does at one, then casts. With --shots, films the free-roam part."),
        new Entry("handsclip", typeof(HandsClipProbe), 90f,
            "Films casting with the hands, with the HUD, on a virtual pad into --shots: a full charge, a merge, and one hand firing again and again while the other charges; frames, sound.wav and timeline.txt, for ffmpeg."),
        new Entry("progress", typeof(ProgressProbe), 600f,
            "The Hold's progress on screen (the-hold.md, phase 5), F7 playing from the save: a locked hand and gesture saying so, attuning only to what's unlocked, " +
            "the offer in words after wave 1, the d-pad picking Tough and the health it adds, the run's end and points earned, the save on disk, and a reset keeping them."),
        new Entry("roomprogress", typeof(RoomProgressProbe), 600f,
            "Two peers with different points in one Hold run (the-hold.md, phase 5): the host from its save (F7), a virtual player with everything; both peers' hands as the core has them, " +
            "wave 1 beaten by Fireballs, the host's pick and the joiner's picked for it, and every checksum alike.", players: 2),
        new Entry("moments", typeof(MomentsProbe), 200f,
            "Every Hold moment by name (rules/hold.json) through HoldMoments, or just MOMENT if the environment names one: the wave, the hands, the keep's health, the wizard at the castle, and raiders coming."),
        new Entry("rowburn", typeof(RowBurnProbe), 240f,
            "A stucco house through the MCP, close up from a wizard's height, then a fireball: its walls follow their cells to a ruin (RowRuin), with the frame numbers while it burns."),
        new Entry("rowhouses", typeof(RowHousesProbe), 180f,
            "Buildings drawn from their rows (RowHouses, builders.md section 4), through the real MCP over HTTP: find_site on the lake's north shore, a stucco house applied, a player on a new wall moved, the door a door cell, a wing, a shack in worn timber with a shed roof, all reverted with the ground back; " +
            "a cabin's wing beside its own model, an edited village house replacing the scene's and coming back; a dozen houses with frame time before and after. With --shots, a film holding on each."),
        new Entry("terminal", typeof(TerminalProbe), 30f,
            "The Claude terminal's folder (ClaudeTerminal, J-056): Claude off then on again gives an address; .mcp.json is valid and names it, with no secret in it; CLAUDE.md and the first screen say Win+H; a client at that address lists the tools. The window itself is left to a real player."),
        new Entry("scopelooks", typeof(ScopeLooksProbe), 200f,
            "Scoped and mixed looks on the world, each through the real tools and a room mod (docs/design/world-tools.md): the fire round the village blue and past it plain; the fire near me green, following the wizard through a burning strip and changing back behind them; " +
            "a blue fire meeting a yellow one burning green between them; three colours as a patchwork; gases, covers, ground and water in a scope; here() names the real player. With --shots, a film of each in a folder of its own."),
        new Entry("worldlooks", typeof(WorldLooksProbe), 150f,
            "The world's colours come from the rules in force (docs/RULES_MCP.md, \"Looks\"): the colours as they ship are today's to the number; the fire element's colour reaches what's burning and a burning lake, " +
            "one Fireball that chose its own colour keeps it (J-052); gases, covers, the ground and the water each take theirs; the mods off puts it all back. With --shots, a film of each in a folder of its own."),
        new Entry("waterlook", typeof(WaterLookProbe), 200f,
            "A look on water costs at its switch, not every frame (#70): Andrew's \"Purple Create Water\" (a look by player 0) and \"All water purple\", on and off " +
            "through the room over Hollowmere's square, quiet and then after a minute of his meteor mod's shower. Checks: ChemGrid.Render a frame with the looks on " +
            "within 1 ms or 20% of the stretches with them off, quiet and after the shower; the shower costs more than quiet; the cells' water drawn purple; no " +
            "console errors. Metrics: each stretch's view markers, frame and water counts, a frame."),
        new Entry("showerstorm", typeof(ShowerStormProbe), 420f,
            "The view's cost after a meteor shower and through the storm that follows (#70), as Andrew's evening went: over Hollowmere's square, quiet, then a minute of his " +
            "meteor mod's shower, then a storm for three minutes, the camera still. Logs each stretch's markers, tick stages and view counts a frame (PROBE WATERLOOK lines); " +
            "checks that the storm came and that redrawn water and slick tiles keep their outline's noise (under 2 points a tile cell). Metrics: each stretch's view markers, frame and water counts, a frame."),
        new Entry("looksmod", typeof(LooksModProbe), 120f,
            "The view draws from the rules in force (docs/RULES_MCP.md, \"Looks\"): a Fireball, the Big fireball mod switched on mid-game, " +
            "a Fireball beside it, the Moonstone mod and its new material, then a look-only switch. --shots films it (ClipFilm). Checks: blast, fire " +
            "area and crater bigger in the cells and as drawn, the moonstone drawn by its family with its look's height, colour and glow, a look-only " +
            "switch redraws and keeps the fingerprint, no scene reload, no console errors. Metrics: each number with the mod off and on."),
        new Entry("lookfamilies", typeof(LookFamiliesProbe), 420f,
            "Every effect family, and what the hand family draws, in a colour no spell ships with, set through the rules MCP over HTTP (set_spell look.color, apply, " +
            "revert) with nothing rebuilt; with --shots a film (ClipFilm) with a beat for each. Checks: each cast's show drawn in the look, each revert, no console errors."),
        new Entry("lookfx", typeof(SpellLookFxProbe), 180f,
            "Whether a spell's effects take its look: a Fireball cast by elements as it ships, its colour changed mid-game through the rules MCP over HTTP (set_spell look.color, apply) and a Fireball " +
            "beside it, every stage read and, with --shots, filmed (ClipFilm); then the mod reverted with the blue fire still burning. Checks: orbs, " +
            "bead, burst, debris, flames, embers and fire light blue with the mod and as authored without, a fire spreading keeps the look, the switch " +
            "redraws what burns, no scene reload, the fingerprint unchanged; then only the fire element's colour set (set_look) and a Fireball that follows it at every stage, and one that chose " +
            "green and keeps it among that fire; no console errors."),
        new Entry("blast", typeof(BlastProbe), 40f,
            "Fireball cast from the bar on the meadow east of the yard. Checks: its crater is in the cells (two steps down in the " +
            "middle, one round it), the terrain is drawn at least 0.6 m lower there, the blast's edge burns (burning, or burnt to dirt), no console errors."),
        new Entry("slant", typeof(SlantProbe), 40f,
            "Earthen Trench aimed about 20 degrees south of east, through the wizard's own controls on a virtual gamepad (crosshair " +
            "and right trigger). Checks: it dug six squares or more, it slants (more columns than rows, two rows or more), every dug " +
            "square shares a side with another, no console errors."),
        new Entry("topple", typeof(ToppleProbe), 40f,
            "Move Earth on the valley tree nearest the yard, then Fire Bolt on it; then Move Earth on the cabin west of the yard. " +
            "Checks: the tree toppled (deadfall) and is drawn fallen, no blocker left in its square, it burns, the cabin's walls cave " +
            "in and the cabin falls, no console errors."),
        new Entry("meteor", typeof(MeteorProbe), 40f,
            "Meteor cast from the bar on the meadow east of the yard. Shots as it falls, lands and after. Checks: it starts over 50 m up, " +
            "with its light; the ground waits for it to land; its crater is three steps down in the middle with eight or more squares " +
            "round it burning or burnt (out past the crater), no console errors."),
        new Entry("falls", typeof(FallsProbe), 40f,
            "The waterfall at the head of the river: the wizard goes to the pond at its foot. Checks: the falls is built (its water, " +
            "twenty or more rocks on the cliff), the pond's middle is river and full, river squares run on south of it, splashes " +
            "go up at the foot, the roar plays, the cliff's rocks are solid (a ray from above lands on them), the scree ramp beside it is built and the " +
            "controller walks up it to within 6 m of the lip, no console errors."),
        new Entry("life", typeof(LifeProbe), 30f,
            "The coast's small life (AmbientLife): gulls, crabs and shoals placed (3+ gull anchors, 15+ crab spots, 10+ shoals), and a crab the wizard " +
            "walks up to bolts over 0.8 m or burrows into the sand, no console errors."),
        new Entry("current", typeof(CurrentProbe), 75f,
            "The water carries things (WaterCurrents): the wizard dropped mid-river and left still is swept over 2 m downstream in 3 s, dropped in the surf off the beach is " +
            "carried over 1.5 m toward the sand in 5 s, dropped in the surf off the island's mainland side is carried over 1.5 m toward the island in 5 s (the nearest land, ShoreMap), and a crate floated on the river drifts over 2 m downstream in 4 s, no console errors."),
        new Entry("sit", typeof(SitProbe), 40f,
            "The wizard sits on the bench nearest the yard, then lies back on the nearest beach lounger (Seat, Sitter, SitPose), and gets up from each. Checks: ten or more " +
            "seats in the world, the seat in reach beside it (E goes to it), held on the seat with the pose on and the hips within 0.2 m of the seat point, and free again after standing, no console errors."),
        new Entry("swim", typeof(SwimProbe), 50f,
            "The wizard drops into the deepest part of the lake. Checks: deep water found, the swim pose on (SwimPose), it floats with its feet about a metre " +
            "under the surface (not on the bed) and stays there, swims over 1 m in 2 s when pushed, leaning into the stroke over 30 degrees with the head out and the hands moving, jump kicks it up over 1.5 m " +
            "and it floats again after; held, the dive takes the head under over 0.8 m down, breath runs down, and let go it comes back up with its breath full again; no console errors."),
        new Entry("flare", typeof(FlareProbe), 180f,
            "The wizard from all round, 30 to 60 degrees down, near and far, in each headwear, standing and walking: every view drawn raw into a " +
            "float target and its pixels read. `--shots` saves it walking through the post effects. Checks: no NaN or infinite pixel, no wizard's " +
            "renderer far over the eyes' brightness, no console errors. If any, a `culprit` check names the renderer that clears the worst view."),
        new Entry("housecam", typeof(HouseCameraProbe), 160f,
            "The wizard walks into four of Hollowmere's houses from each side with the follow camera behind it. Checks: the camera's arm holds at " +
            "75% of its length or more (the detail names what the arm's check hit at the shortest), then the same at two houses in the smallest animal's shape, no console errors."),
        new Entry("flames", typeof(FlamesProbe), 40f,
            "A field of grass alight at noon (FlameBank, Prototype/Flame). `--shots` saves it close and from above. Checks: flames in the camera's " +
            "picture, every burning cell drawn, flames off the cell's middle, sizes, speeds and seeds that vary, no two cells alike, thatch, wood and oil each keeping their own look and each level its flipbook, no particle " +
            "system a flame, no console errors."),
        new Entry("look", typeof(LookProbe), 45f,
            "The wizard's outfit (WizardOutfit, WizardLook): dressed from the saved seed, then three new looks, portraits from the front and the side. " +
            "Checks: the outfit part and its material, dressed in the saved look, six parts or more on the bones with the hat on the head, the same " +
            "seed giving the same look and two seeds two looks, New look changing it, the head inside the hat or cowl over 200 seeds, no console errors. The player's own look is put back."),
        new Entry("firelooks", typeof(FireLooksProbe), 150f,
            "Fire by material: a row of thatch, a row of log wall and a strip of oil in the yard set alight, a picture of them burning, then a crate " +
            "stood in fire. Numbers: how long a thatch cell and a log wall cell burn, when the crate catches and is gone. Checks: thatch burns 2 to 6 s, " +
            "wood over three times as long, the crate catches."),
        new Entry("thatch", typeof(ThatchProbe), 150f,
            "Thatch and wood burn differently: a Fire Bolt on a wall of a thatch-roofed timber house. Numbers: how soon the thatch catches, how long " +
            "it burns, how long the log walls burn. Checks: the thatch catches and burns out within 3 s each, it lights more of the walls, the wood burns over 20 s."),
        new Entry("roofs", typeof(RoofsProbe), 120f,
            "Walls and roofs fail separately: a Fire Bolt on a timber house with a slate roof, and grease set alight beside a stone house with a " +
            "thatch roof, 50 s. Checks: the log walls burn down and the slate falls onto them, the thatch burns away, the stone stands, sooted. Before and after pictures."),
        new Entry("houses", typeof(HousesProbe), 120f,
            "The materials step: a Fire Bolt on one of Hollowmere's timber houses and one of its stone houses, clear noon, 45 s. Checks: each has " +
            "wall cells of its material (world/villages.json), the timber house's log walls burn down, the stone house's stand, no console errors."),
        new Entry("soak", typeof(SoakProbe), 120f,
            "Andrew, 2026-10-28: Create Water on open ground in the yard at noon, clear, then a minute. Checks: water poured, under half of it left " +
            "(thin water soaks in and dries up, World.Soak), the river's and lake's deep water kept, no console errors."),
        new Entry("slope", typeof(SlopeProbe), 60f,
            "Playtest #31: Create Water on the steepest dry hillside near the start (a 1.5 to 6 m fall over 6 cells of the valley's baked ground), " +
            "12 s later a picture from the side. Checks: a hillside found, the water reached over 1 m below where it was cast, no console errors."),
        new Entry("papercuts", typeof(PaperCutsProbe), 180f,
            "Andrew's playtest paper cuts of 2026-10-27, framed for before/after pictures (the same script with or without the fixes). Checks: the " +
            "signs drawn with SignText (one-sided), a villager sitting on the seat, the house roof solid and landed on, the well wall stopping you, " +
            "the beach off-limits to wild animals, the island's south pier, and a burnt-down inn's door light out at night."),
        new Entry("jobs", typeof(JobsProbe), 120f,
            "People with work to go to (FolkJobs, jobs.json): each job's worker stands where the clock says when a wizard comes near; a wizard walks out along the lake house's dock and stays on it. With --shots, a worker at each job, the dock and the lake house."),
        new Entry("villagers", typeof(VillagersProbe), 120f,
            "Hollowmere's folk going about their day (ValleyFauna.Folk): a clear afternoon with the camera over the square, then dusk. Checks: " +
            "the folk come out (half or more), walk about between the doors, the well and the benches, someone sits on a bench within 40 s, they run from " +
            "the house nearest the square when a Fire Bolt sets it alight, " +
            "and at dusk they all go indoors within 45 s and stay in, no console errors."),
        new Entry("wet", typeof(WetProbe), 75f,
            "Rain wets the ground (WetGround, J-024): the meadow north of the yard at noon, clear, then heavy rain, then clear. Checks: dry in the sun, " +
            "soaked (wetness over 0.8) after 14 s of rain, still drying 20 s after it stops, with a dry and a wet shot from the same place, no console errors."),
        new Entry("night", typeof(NightProbe), 90f,
            "The night's view (Nightfall, World.Night): Hollowmere's square at 16:00, then dusk (18:30) and night (21:30), clear, the wizard out on open ground. " +
            "Checks: the rules' light bright, dim and dark in turn, the village's door lamps off by day and lit from dusk with the windows glowing, the stars " +
            "out at night, the fireflies out by the hour, the wizard told how to see in the dark, no console errors."),
        new Entry("cloth", typeof(ClothProbe), 90f,
            "The wizard's robe as cloth (RobeCloth): walking and running across the yard, a dodge roll, sitting on a bench, lying on a lounger and swimming " +
            "in the lake, each filmed from the side (WizardOutfit.RigidRobe set first gives the old rigid robe, for comparing). Checks: the robe is cloth, " +
            "it's stepping, and it holds together (no particle over 1.5 m from the hips), no console errors."),
        new Entry("swell", typeof(SwellProbe), 30f,
            "The wizard dropped into the sea 30 m out from the shore, floating for ten seconds. Checks: it swims, and it rises and falls " +
            "with the swell under it (Ocean.WaveOffset, the waves the shader draws): over half the swell's height, in step with it, no console errors."),
        new Entry("sleep", typeof(SleepProbe), 60f,
            "Props sleep with their chunk (PhysicsProp, ChemGrid.ChunkSwitched): a crate levitated in the yard, then the wizard goes beyond the keep distance and back (the crate hangs in the air while asleep and falls once awake; the camera stays on it). Checks: the yard's " +
            "props awake while it's active, asleep and kinematic once it deactivates and none asleep in an active chunk, held still for 2 s, awake " +
            "again when it activates, and a shoved one moves, no console errors."),
        new Entry("fauna", typeof(FaunaProbe), 130f,
            "The valley's animals (ValleyFauna, from Punchy's creatures): 10 s for them to come out round the camera, then a deer put 8 m north " +
            "from the wizard, then a deer, a rabbit, a turkey and a fox walking through the meadow. Checks: ValleyFauna is built with 10 " +
            "species or more, 10 animals or more are out, 20 s later two kinds of activity or more under way (grazing, resting, drinking, " +
            "sleeping, play, hunting), the deer bolts (over 15 m from the wizard after 3 s), a deer backed into a corner of the valley mills about " +
            "(on the move at least 25% of the time, no more than 2 reversals a second), a deer on a 7 m leash stays in reach of home running from the wizard and mills about there, no console errors."),
        new Entry("liquidsview", typeof(LiquidsViewProbe), 60f,
            "A mod's honey and red paint poured beside plain water (CellWaterMesh), colours read back from the tiles' meshes; with --shots films the pours for ffmpeg. " +
            "Checks: plain water stays white, each liquid wears its look's colour, water beside them is untouched, kept and per-point drawing agree, a look change redraws, the mod off leaves no colour, no console errors."),
        new Entry("faunarows", typeof(FaunaRowsProbe), 40f,
            "A creature row changed in a running game (rules/fauna.json, CreatureBodies): a deer and a fox put by the wizard, a mod turns the deer red and doubles its walk, then goes off. " +
            "Checks: only the deer's body is built again, the new numbers are in force, the deer keeps its place and seed, the fingerprint doesn't move, switching off brings it back, no console errors."),
        new Entry("crowd", typeof(CrowdProbe), 60f,
            "What a full valley costs a frame: 120 animals and folk of a fixed mix round the wizard, 6 s to settle, 20 s measured. Metrics: the main thread's p50 and p95, and ValleyFauna's Update, Think and Draw markers, for the before and after of a change to the creatures."),
        new Entry("people", typeof(PeopleProbe), 90f,
            "The valley's peoples (rules/fauna.json, from Punchy's f3141f6), films into --shots <folder>: Hollowmere's folk come out, workers take up their posts, and a line of humans, a child, a dwarf, an elf, a goblin and an ogre walks past the camera. " +
            "Checks: at least two workers at their posts, every people is in the rows and shows at its own size, no console errors."),
        new Entry("spawners", typeof(SpawnersProbe), 160f,
            "Which spawners really turn up: the wilds opened up (a big cap, a fast spawn rate) for 40 s at each of three places, the start, the lake and far out. Metric: how many of each species came out; a check that every bird with a spawn row did, no console errors."),
        new Entry("waterlife", typeof(WaterLifeProbe), 60f,
            "The valley's water from its rows, filmed into --shots <folder>: water deep enough to swim in near the wizard, with ducks, trout, koi, lily pads and reeds put there. Checks: the water is found, every row exists, no console errors."),
        new Entry("closeup", typeof(CloseupProbe), 60f,
            "Models close up at a clear noon (issue #47): a deer across the follow camera, then a deer, a valley cabin and a village log house, each from the same " +
            "cameras (`--shots`). Checks: a deer at the lens is drawn for its shadow alone, and again once past it, no limb ends in the picture, the cabin's gable closed, " +
            "no mesh in the scene inside out, no console errors."),
        new Entry("blink", typeof(BlinkProbe), 45f,
            "Misty Step aimed as a player aims it (the camera's ray through the crosshair) at open ground all round the wizard, " +
            "3 m out to just inside its range, then a few blinks for real, then six casts through the caster two frames apart (out " +
            "and back onto the mist it left, one straight after a Thunderwave), then one held to full charge past a tap's range. " +
            "Checks: 80% or more of the spots can be landed on (the detail tallies why the rest can't), every real blink arrives " +
            "where it was aimed, every cast in a row arrives (the detail names what was in the way if not), the held cast lands " +
            "further than a tap would and arrives."),
        new Entry("charges", typeof(ChargesProbe), 60f,
            "Every spell is stronger held: casts through the caster at a tap and at full charge, compared. Checks: every spell " +
            "charges (SpellCaster.Chargeable), and held does half as much again or more: Create Water's water, Grease's greased " +
            "squares, Ray of Frost's beam length, Thunderwave's shove on the crate by yard (15, 6), Levitate's lift on the props " +
            "by yard (17, 6), Misty Step's and Mage Hand's reach and Mage Hand's lift, no console errors. Metrics: every spell's " +
            "squares at a tap and held."),
        new Entry("shapeshift", typeof(ShapeshiftProbe), 60f,
            "Shapeshift in the meadow: a deer put 8 m in front of the wizard (held still), aimed at through a ray from the wizard's eyes and cast " +
            "at; the wizard walks as a deer, tries Fire Bolt, a second deer is put 6 m away, then Cast changes it back; shifted again, a heavy blow. " +
            "Checks: the ray finds the deer (and the sky fizzles), the wizard's model is hidden and a deer's body drawn where it stands, the controller " +
            "has the deer's speeds, walking moves it with a gait, Fire Bolt is refused, the near deer doesn't bolt, changing back restores the model " +
            "and speeds, the blow changes it back, no console errors. `--shots` saves the wizard as a deer."),
        new Entry("sprint", typeof(SprintProbe), 30f,
            "A virtual controller walks the wizard north, clicks the left stick, lets go of the stick and pushes again, then holds the left trigger. " +
            "Checks: the click runs (over 1.3 times the walk), letting go of the stick stops the run, the left trigger doesn't sprint, no console errors."),
        new Entry("roll", typeof(RollProbe), 30f,
            "A virtual controller walks the wizard north and pulls the left trigger (DodgeRoll), then pulls it again 0.55 s in, during the wait " +
            "before the next roll. Checks: it rolls, over 3 m in under 0.6 s, the body turns once head over heels (300 to 420 degrees, hips to " +
            "head) and ends upright (within 25 degrees), the second pull does nothing, no console errors."),
        new Entry("dither", typeof(DitherProbe), 30f,
            "The wizard put behind the terrain tree nearest it, the camera cut to look at it through the tree, then cut round to the open side " +
            "(OccluderFade). Checks: the tree is found in the way, the dither fades in (fade over 0.9, the global cut over 0.5) with the " +
            "see-through figure on; from the open side nothing is in the way, the dither is off, the figure off and every material back, no console errors."),
        new Entry("snow", typeof(SnowProbe), 240f,
            "Heavy snow at noon (fast settling), a wait of up to 150 s for its paint, then the wizard walks south through the yard, then a Fire Bolt lands in the snow. " +
            "Checks: over 500 squares under snow, over 20 banks against walls and trees, the ground over a bank stands 0.25 m higher, " +
            "4 footprints or more from the walk, the bolt melts the snow where it lands and nothing catches, no console errors."),
        new Entry("critters", typeof(CrittersProbe), 60f,
            "The ambient fireflies and butterflies (AmbientCritters, from Punchy): a clear 22:00 over the meadow toward the pond, then " +
            "a clear noon close to the grass. Checks: AmbientCritters is built with both materials, fireflies are out at night (and no " +
            "butterflies) with over 50 homes near, butterflies by day (and no fireflies) with over 50 homes near, no console errors."),
        new Entry("sleet", typeof(SleetProbe), 40f,
            "Sleet Storm cast from the bar on the meadow east of the yard, after a Fire Bolt lit its middle and puddles were laid round " +
            "the fire. Checks: a storm (SleetStorm: cloud, sleet, pellets) over the target the burst's width each way, its cloud overhead, " +
            "no projectile or ice impact, nothing frozen before the sleet lands, over 500 streaks falling with those near the ground over " +
            "the spell's squares, every puddle frozen and the fire out once it lands, the storm weaker near its end and gone after, no console errors."),
        new Entry("gestures", typeof(GesturesProbe), 40f,
            "The casting gestures (CastPose, CastGestures), each cast from the bar with the camera beside the wizard: Gust of Wind, Raise Earth, " +
            "Fireball, Meteor. Checks: each plays its gesture (Shove, Raise, Throw, CallDown); both hands push forward over 0.4 m for Gust; both hands " +
            "go below 0.95 m then over the head for Raise Earth; the casting hand goes back behind the shoulder then over 0.3 m forward for Fireball; " +
            "both hands go over the head then below the shoulders for Meteor, the meteor appearing at the pull; no console errors."),
        new Entry("timeweather", typeof(TimeWeatherProbe), 60f,
            "The Time & Weather spells cast from the bar on a clear morning: Hasten Time (and again to settle it), Control Weather four times, " +
            "then Fog Cloud in the yard with a Misty Step aimed through it, a Fire Bolt into it and a Gust of Wind through it. Checks: the clock " +
            "runs 90 game minutes or more in 6 s and back to its own pace after the second cast, the weather turns rain, snow, storm, clear " +
            "with WeatherFx drawing the rain and the snow, two thirds of the fog's squares thick and drawn (FogBank), no line of sight through " +
            "it and the blink refused, the bolt burns the fog off where it lands, the gust blows the fog east, no console errors."),
        new Entry("nature", typeof(NatureProbe), 120f,
            "A Fireball burns the meadow round the yard's tree at (5, 8), then Plant Growth (charge 2) lands on it; later Spike Growth on the " +
            "south meadow, the wizard walking through it, and a Fire Bolt into it. Checks: the fire leaves ash and the tree a stump, Plant Growth " +
            "clears the ash into green shoots that are grass six seconds on (grass counted before and after), the stump sprouts a sapling that " +
            "grows into a tree drawn on its square, thorns on 60 squares or more with their clumps drawn, the wizard slower through them " +
            "(under 0.75 of its speed on open grass) and losing health, the Fire Bolt sets them alight and they burn to ash, no console errors."),
        new Entry("tower", typeof(TowerProbe), 60f,
            "The climbing tower south-west of the yard (ValleyTower, TowerCourse) and the double jump: a moving platform watched for 1 s, a phasing " +
            "one for a whole cycle, the wizard put on a collapsing one, a single jump and a double jump off the start pad, then the start pad, off it, " +
            "and the summit by the flag. Checks: 3 or more platforms of each kind (static, moving, collapsing, phasing), the summit 30 to 45 m up, the " +
            "moving one moves over 0.1 m, the phasing one is solid and a ghost in turn, the collapsing one drops within 1.5 s and is back within 6 s, " +
            "the second jump is taken and goes over 0.6 m higher than a single, the clock starts off the pad, the flag sets off fireworks and logs " +
            "\"You reached the top in m:ss!\", no console errors."),
        new Entry("defense", typeof(DefenseProbe), 150f,
            "The defense field north of the yard (DefenseFieldBuilder, DefenseField, World.Defense.cs): the wizard stands on the start plate, " +
            "watches the first raiders come through the gate, casts a Fireball a square ahead of the leader, raises a Wall of Stone across the " +
            "path 8 squares short of the flag and waits for a raider to get through. Checks: the field is built, its path one row of dirt 50 to " +
            "70 m long, the plate starts the waves, a raider spawns within 10 s and is over 2 squares nearer the flag 3 s on, raiders are drawn, " +
            "the Fireball beats at least one, the wall stands 4 squares or more and the next raider passes its column off the wall's rows (the way " +
            "costs more), a raider reaching the flag costs a life (banner and HUD line show it), no console errors. Shots: the field, mid-wave, " +
            "the wall, the end."),
    };

    public static Entry Find(string name) =>
        All.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}

// --- smoke ---------------------------------------------------------------------------

public class SmokeProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        int startTick = World.Tick;
        CheckThingsSpawned();

        // Walk: from the player start toward the pond, as DemoDirector does.
        var from = wizard.transform.position;
        yield return WalkFor(20, 11, 3f);
        float walked = Vector3.Distance(Flat(from), Flat(wizard.transform.position));
        Metric("walked_m", walked);
        Check("walked", walked > 1f, $"{walked:0.0} m");
        Shot("walk");

        // Every Elements spell once (DemoDirector's targets; Thunderwave and Levitate by the crate).
        yield return CastLogged(SpellIds.FireBolt, 19, 3, 0, 1.5f);        // the oil spill
        Metric("burning_after_fire_bolt", BurningCells());
        Check("fire_started", BurningCells() > 0, $"{BurningCells()} burning cells");
        yield return CastLogged(SpellIds.CreateWater, 19, 3, 0, 1.5f);
        yield return CastLogged(SpellIds.RayOfFrost, 20, 10, 0, 1.5f);
        yield return CastLogged(SpellIds.ShockingGrasp, 24, 9, 1, 1.5f);
        yield return CastLogged(SpellIds.Grease, 10, 12, 3, 1f);
        yield return CastLogged(SpellIds.GustOfWind, 13, 12, 3, 1.5f);
        Place(15, 6);
        yield return CastLogged(SpellIds.Thunderwave, 15, 6, 1, 1.5f);
        // Levitate moves things, not cells: World.Cast logs nothing for it, but the lifted prop says so (EventLog).
        int mark = ConsoleMark;
        yield return CastFromBar(SpellIds.Levitate, 17, 6, 1, 1.5f);
        Check("levitate_lifted", ConsoleSaid("floats up", mark), "a prop's \"... floats up\" line in the console");
        Shot("elements");

        // Shape Earth digs its square.
        var cells = SpellCells(SpellIds.MoldEarth, 6, 8, 0);
        int before = cells.Sum(c => World.At(c.x, c.y).Height);
        yield return CastLogged(SpellIds.MoldEarth, 6, 8, 0, 1.5f);
        int after = cells.Sum(c => World.At(c.x, c.y).Height);
        Metric("mold_earth_height_change", after - before);
        Check("mold_earth_shaped", after != before, $"height sum {before} -> {after}");

        // The fire spells past Fire Bolt: a cone from the hands, and a blast.
        yield return CastLogged(SpellIds.BurningHands, 4, 14, 3, 1f);
        yield return CastLogged(SpellIds.Fireball, 22, 1, 0, 1.5f);

        var reactions = LogSince(startTick).Where(t => !t.StartsWith("You cast", StringComparison.Ordinal)).ToList();
        Metric("reaction_log_lines", reactions.Count);
        Check("reactions_logged", reactions.Count > 0, string.Join(" | ", reactions.Take(5)));
        Metric("burning_cells_end", BurningCells());
    }

    // Casts from the bar and checks the World log has "You cast <name>" since the cast.
    IEnumerator CastLogged(string spellId, int x, int y, int dir, float wait)
    {
        int tick = World.Tick;
        yield return CastFromBar(spellId, x, y, dir, wait);
        string line = $"You cast {World.Rules.Spell(spellId).Name}";
        Check($"cast_{spellId}", LogSince(tick).Contains(line), line);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // Every prop and item of world/things.json spawned by ThingSpawner, on its own cell, with its own id.
    void CheckThingsSpawned()
    {
        var spawner = FindAnyObjectByType<ThingSpawner>();
        var live = spawner != null ? spawner.Live.ToList() : new List<(Chem.ThingId id, PhysicsProp prop)>();
        int placed = live.Count(t =>
        {
            var data = World.ThingsNow.Find(t.id);
            return data != null && data.Kind == t.prop.kind && grid.CellAt(t.prop.transform.position) == new Vector2Int(data.CellX, data.CellY);
        });
        Metric("things_spawned", live.Count);
        Check("things_spawned", live.Count == World.ThingsNow.Count && placed == live.Count,
            $"{live.Count}/{World.ThingsNow.Count} things spawned from world/things.json, {placed} on their own cell with their kind");
    }
}

// --- fire ----------------------------------------------------------------------------

public class FireProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        // ValleyShowcase's fire: low over the meadow at the old yard's west edge.
        Place(5, 13);
        var target = Yard(2, 12);
        Cut(target + V(8f, 13f, -16f), target + V(-6f, 0f, 3f));
        Metric("burning_t0", BurningCells());
        Metric("ash_cells_t0", AshCells());
        yield return CastFromBar(SpellIds.FireBolt, 2, 12, 3, 10f);
        int at10 = BurningCells();
        Metric("burning_t10", at10);
        Shot("t10");
        yield return Wait(10f);
        int at20 = BurningCells();
        Metric("burning_t20", at20);
        Metric("ash_cells_t20", AshCells());
        Shot("t20");
        Check("fire_spread_t10", at10 > 1, $"{at10} burning cells at t+10 s");
    }

    int AshCells() => World.Cells.Count(c => c.Cover == "ash");
}

// --- earth ---------------------------------------------------------------------------

public class EarthProbe : ProbeScenario
{
    const float TrenchFillSeconds = 8f;

    protected override IEnumerator Run()
    {
        // ValleyShowcase's water chapter: the wizard on the pond's west bank, the trench cut west.
        Place(16, 5);
        Cut(V(19f, 2.2f, 6.5f) + V(0f, 4f, -3f), V(20f, 0f, 15f));
        var trench = SpellCells(SpellIds.EarthenTrench, 15, 5, 3);
        yield return CastFromBar(SpellIds.EarthenTrench, 15, 5, 3, TrenchFillSeconds);
        int wet = trench.Count(c => World.At(c.x, c.y).Water > 0);
        int units = trench.Sum(c => World.At(c.x, c.y).Water);
        int dug = trench.Count(c => World.At(c.x, c.y).Height < 0);
        Metric("trench_cells", trench.Count);
        Metric("trench_cells_dug", dug);
        Metric("trench_cells_wet_t8", wet);
        Metric("trench_water_units_t8", units);
        Check("trench_dug", dug > 0, $"{dug}/{trench.Count} cells dug");
        Check("trench_has_water_t8", wet > 0, $"{wet}/{trench.Count} cells wet, {units} units");
        Shot("trench");

        // A Wall of Stone across it, north from (12, 7).
        Place(10, 6);
        var wall = SpellCells(SpellIds.WallOfStone, 12, 7, 0);
        yield return CastFromBar(SpellIds.WallOfStone, 12, 7, 0, 3f);
        int raised = wall.Count(c => World.IsRaisedWall(World.At(c.x, c.y)));
        Metric("wall_cells", wall.Count);
        Metric("wall_cells_raised", raised);
        Check("wall_raised", wall.Count > 0 && raised == wall.Count, $"{raised}/{wall.Count} cells a wall");
        Shot("wall");
    }
}

// --- perf ----------------------------------------------------------------------------

public class PerfProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        // Where ValleyShowcase's flyover ends: over the meadow toward the pond, the valley beyond.
        Cut(V(40f, 10f, 1f), V(28f, 0f, 15f));
        Metric("hold_seconds", Seconds);
        Shot("view");
        yield return Wait(Seconds);
    }
}
