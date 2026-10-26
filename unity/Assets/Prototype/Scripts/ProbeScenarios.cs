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
            "out over the water. Checks: the shore was found, the wet squares in the beam froze, the ice is drawn at or above " +
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
        new Entry("river", typeof(RiverProbe), 40f,
            "The river from the foothills into the lake (river terrain refills itself): the wizard goes to it east of the yard and digs " +
            "from its edge with Lower Earth, charged. Checks: a river square near world (120, 30), full; the dug square fills from it; " +
            "the river full again after, no console errors."),
        new Entry("edge", typeof(EdgeProbe), 90f,
            "The wizard runs at each end of the land (where the valley's terrains stop: north, east, south, west) from 15 m inside it, " +
            "for 10 s. Checks per edge: the wizard is still over the land and standing on the ground, no console errors."),
        new Entry("sky", typeof(SkyProbe), 90f,
            "The clock and the weather, switched as a mod switches them: noon, then 22:00, then a storm at 13:00 with lightning " +
            "every half second, then fog. Checks: the Sky object is built, the sun is bright at noon and the clock line says so, " +
            "night is dark with the moon up, the storm is in force (wind too) with rain falling, strikes logged and a bolt drawn, " +
            "the sun dimmed, fog closes the haze in, no console errors."),
        new Entry("blast", typeof(BlastProbe), 40f,
            "Fireball cast from the bar on the meadow east of the yard. Checks: its crater is in the cells (two steps down in the " +
            "middle, one round it), the terrain is drawn at least 0.6 m lower there, the blast's edge still burns, no console errors."),
        new Entry("slant", typeof(SlantProbe), 40f,
            "Earthen Trench aimed about 20 degrees south of east, through the wizard's own controls on a virtual gamepad (crosshair " +
            "and right trigger). Checks: it dug six squares or more, it slants (more columns than rows, two rows or more), every dug " +
            "square shares a side with another, no console errors."),
        new Entry("swim", typeof(SwimProbe), 40f,
            "The wizard drops into the deepest part of the lake. Checks: deep water found, it floats with its feet about a metre " +
            "under the surface (not on the bed) and stays there, swims over 1 m in 2 s when pushed, jump kicks it up over 1.5 m " +
            "and it floats again after, no console errors."),
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
        var spawner = FindFirstObjectByType<ThingSpawner>();
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
