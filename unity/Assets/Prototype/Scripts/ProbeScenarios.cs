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
        new Entry("online", typeof(OnlineProbe), 180f,
            "This Editor hosts and a Multiplayer Play Mode virtual player (Player 2, started if it isn't running) joins on " +
            "127.0.0.1. Each casts Fire Bolt, Earthen Trench and Wall of Stone through the network; the virtual player's " +
            "Thunderwave throws a crate and the host's wizard, and the host's wizard falls 10 m. Checks: it joined, every 30-tick checksum (cells and rules) equal on both, the same world " +
            "at the sample tick, no desync seen, the client's terrain drawn as low as the host's under the trenches, the same " +
            "water (cells and drawn surface) and walls, the crate and the host's wizard each within 5 cm on both, no console errors on either, " +
            "reset on the host refused with both players still in, and tag passing from the host to the virtual player on touch.",
            players: 2),
        new Entry("menu", typeof(MenuProbe), 60f,
            "The Esc menu with a controller: Start on a virtual gamepad opens it, the highlight moves down to Mods, A opens the " +
            "Mods page and switches the first mod on and off, B goes back, then Credits and Join by code (letters picked, B back). " +
            "Checks: each step landed on the right page, 6 mods listed, the mod switched, the Sound page's − and + step the effects volume, B and Start close the menu and the " +
            "wizard's controls come back, no console errors."),
        new Entry("radial", typeof(RadialProbe), 60f,
            "The spell radial with a controller, on a virtual gamepad: hold LB and push the right stick at Storm, then at " +
            "Thunderwave in its arc, release; tap LB; open on Earth and press B; d-pad right; RB. Checks: LB opens it on Storm, " +
            "the arc opens with Thunderwave highlighted, release picks it and camera look comes back, a tap swaps back to " +
            "Fire Bolt, B cancels without changing the spell, the d-pad steps to the next element and RB to the next spell " +
            "in it, no console errors."),
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
        new Entry("current", typeof(CurrentProbe), 40f,
            "The water carries things (WaterCurrents): the wizard dropped mid-river and left still is swept over 2 m downstream in 3 s, dropped in the surf off the beach is " +
            "carried over 1.5 m toward the sand in 5 s, and a crate floated on the river drifts over 2 m downstream in 4 s, no console errors."),
        new Entry("sit", typeof(SitProbe), 40f,
            "The wizard sits on the bench nearest the yard, then lies back on the nearest beach lounger (Seat, Sitter, SitPose), and gets up from each. Checks: ten or more " +
            "seats in the world, the seat in reach beside it (E goes to it), held on the seat with the pose on and the hips within 0.2 m of the seat point, and free again after standing, no console errors."),
        new Entry("swim", typeof(SwimProbe), 40f,
            "The wizard drops into the deepest part of the lake. Checks: deep water found, the swim pose on (SwimPose), it floats with its feet about a metre " +
            "under the surface (not on the bed) and stays there, swims over 1 m in 2 s when pushed, leaning into the stroke over 30 degrees with the head out and the hands moving, jump kicks it up over 1.5 m " +
            "and it floats again after, no console errors."),
        new Entry("look", typeof(LookProbe), 30f,
            "The wizard's outfit (WizardOutfit, WizardLook): dressed from the saved seed, then three new looks, portraits from the front and the side. " +
            "Checks: the outfit part and its material, dressed in the saved look, six parts or more on the bones with the hat on the head, the same " +
            "seed giving the same look and two seeds two looks, New look changing it, no console errors. The player's own look is put back."),
        new Entry("swell", typeof(SwellProbe), 30f,
            "The wizard dropped into the sea 30 m out from the shore, floating for ten seconds. Checks: it swims, and it rises and falls " +
            "with the swell under it (Ocean.WaveOffset, the waves the shader draws): over half the swell's height, in step with it, no console errors."),
        new Entry("sleep", typeof(SleepProbe), 60f,
            "Props sleep with their chunk (PhysicsProp, ChemGrid.ChunkSwitched): the wizard goes beyond the keep distance and back. Checks: the yard's " +
            "props awake while it's active, asleep and kinematic once it deactivates and none asleep in an active chunk, held still for 2 s, awake " +
            "again when it activates, and a shoved one moves, no console errors."),
        new Entry("fauna", typeof(FaunaProbe), 90f,
            "The valley's animals (ValleyFauna, from Punchy's creatures): 10 s for them to come out round the camera, then a deer put 8 m north " +
            "from the wizard, then a deer, a rabbit, a turkey and a fox walking through the meadow. Checks: ValleyFauna is built with 10 " +
            "species or more, 10 animals or more are out, 20 s later two kinds of activity or more under way (grazing, resting, drinking, " +
            "sleeping, play, hunting), the deer bolts (over 15 m from the wizard after 3 s), no console errors."),
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
        new Entry("snow", typeof(SnowProbe), 60f,
            "Heavy snow at noon (fast settling), then the wizard walks south through the yard, then a Fire Bolt lands in the snow. " +
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
            var data = grid.Things.Find(t.id);
            return data != null && data.Kind == t.prop.kind && grid.CellAt(t.prop.transform.position) == new Vector2Int(data.CellX, data.CellY);
        });
        Metric("things_spawned", live.Count);
        Check("things_spawned", live.Count == grid.Things.Count && placed == live.Count,
            $"{live.Count}/{grid.Things.Count} things spawned from world/things.json, {placed} on their own cell with their kind");
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
