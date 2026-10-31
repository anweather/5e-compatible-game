using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's dragon scenario (docs/BACKLOG.md 26): one whole flight, staged faster than a
// real one by turning sim.json's dragon up (a short warning, quicker passes; the real
// numbers are the ones `perf` and the player measure). R summons it; the sky darkens and the
// dragon is seen; a wizard in the open is knocked down and one behind a built wall isn't;
// every chunk comes back: its cells, the Gauntlet's lava, a felled tree, a thing that broke
// and a thing moved. It ends with what the flight cost in a view of the breath against
// the same view clear, and the size of the starting state.
public class DragonProbe : ProbeScenario
{
    const float MostSeconds = 200f, LookSeconds = 4f;
    // Sim params for a staged flight: warning, turns, leaving, and a pass at three times the speed.
    const int ShortWarning = 60, ShortTurn = 12, ShortLeaving = 60, QuickSpeed = 18, QuickChunks = 2;
    // Cells ahead of the lead where a wizard waits for the breath.
    const int Ahead = 70;
    // A cell of the Gauntlet's lava pit (world/gauntlet.json) and of the wizard's meadow, to wreck.
    const int LavaX = 303, LavaY = 433;

    DragonView view;
    SkyLight sky;
    Vitals vitals;
    ThingSpawner spawner;
    readonly List<ChunkFresh> freshened = new();
    int realSpeed;
    float fastest;

    protected override IEnumerator Run()
    {
        view = FindAnyObjectByType<DragonView>();
        sky = FindAnyObjectByType<SkyLight>();
        vitals = wizard.GetComponent<Vitals>();
        spawner = FindAnyObjectByType<ThingSpawner>();
        Check("view_in_scene", view != null, "the yard has a DragonView (rebuild the yard if not)");
        if (view == null) yield break;
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        Measured();
        var p = World.Rules.Sim.Dragon;
        realSpeed = p.Speed;
        (p.WarningTicks, p.TurnTicks, p.LeavingTicks, p.Speed, p.ChunksPerTick) = (ShortWarning, ShortTurn, ShortLeaving, QuickSpeed, QuickChunks);

        var damage = new Damage(this);
        yield return damage.Do();
        grid.ChunkFreshened += freshened.Add;
        try
        {
            yield return Flight();
        }
        finally
        {
            grid.ChunkFreshened -= freshened.Add;
        }
        damage.Checked();
        Check("no_dragon_after", World.Dragon == null && World.DragonFlights == 1, $"flights {World.DragonFlights}");
        Check("no_console_errors", Result.Console.ErrorCount == 0, $"{Result.Console.ErrorCount} errors");
    }

    // The starting state's size on this map, and how long it took.
    void Measured()
    {
        Check("world_keeps_a_start", World.HasStart, "ChemGrid.StartWorld");
        Metric("keep_start_ms", ChemGrid.KeepStartMs);
        Metric("palette", World.StartShapes);
        Metric("cells", World.W * World.H);
        Metric("chunks", World.ChunkCoords.Count());
        Metric("changed_at_the_start", World.ChangedCount);
        // CellBytes each in the palette (Cell.cs's fields and an object's header), and an int a cell for the index.
        Metric("start_state_kb", (World.StartShapes * CellBytes + World.W * World.H * sizeof(int)) / 1024.0);
        int deep = World.Cells.Count(c => c.Water >= World.Rules.Sim.Dragon.ShelterWater && !c.Frozen);
        Metric("cells_deep_enough_to_shelter", deep);
    }

    const int CellBytes = 120;

    IEnumerator Flight()
    {
        float sunBefore = sky.sun.intensity, ambientBefore = RenderSettings.ambientIntensity;
        int mark = ConsoleMark;
        EventLog.ResetYard();
        Check("r_summons_it", World.Dragon?.Phase == DragonPhase.Warning, $"phase {World.Dragon?.Phase}, flights {World.DragonFlights}");
        float started = Time.time;
        bool seenInWarning = false;
        bool shot = false;
        while (World.Dragon?.Phase == DragonPhase.Warning && Time.time - started < MostSeconds)
        {
            seenInWarning |= view.Seen;
            if (!shot && sky.Menace > 0.6f)
            {
                shot = true;
                Shot("warning");
            }
            yield return null;
        }
        Check("seen_in_the_warning", seenInWarning, "the high crossing or the dive");
        Check("sky_darkens", sky.Menace > 0.3f && sky.sun.intensity < sunBefore * 0.9f, $"menace {sky.Menace:0.00}, sun {sunBefore:0.00} to {sky.sun.intensity:0.00}");
        Metric("sun_before", sunBefore);
        Metric("sun_in_the_warning", sky.sun.intensity);
        Metric("ambient_before", ambientBefore);

        bool open = false, sheltered = false, looked = false;
        StartCoroutine(WatchTheFlight());
        StartCoroutine(WatchThePerch());
        var lookFrames = new List<float>();
        int staged = -1;
        Vector3 lookEye = default, lookAt = default;
        while (World.Dragon != null && Time.time - started < MostSeconds)
        {
            var d = World.Dragon;
            if (d.Phase == DragonPhase.Passes && d.Until == 0 && d.Travelled > 0)
            {
                if (d.Pass != staged)
                {
                    staged = d.Pass;
                    if (!open) yield return InTheOpen(result => open = result);
                    else if (!sheltered) yield return Sheltered(result => sheltered = result);
                    else if (!looked && d.Pass > 1)
                    {
                        looked = true;
                        var p = World.Rules.Sim.Dragon;
                        int quick = p.Speed;
                        p.Speed = realSpeed;
                        (lookEye, lookAt) = LookAtTheBreath();
                        yield return LookFor(lookEye, lookAt, lookFrames);
                        var chase = view.ShownAt - view.Way * 150f + Vector3.Cross(Vector3.up, view.Way).normalized * 60f + Vector3.up * 55f;
                        Cut(chase, view.ShownAt + view.Way * 90f + Vector3.down * 25f);
                        yield return null;
                        Shot("final");
                        yield return null;
                        yield return null;
                        ShootTheDragon();
                        yield return null;
                        p.Speed = quick;
                    }
                }
            }
            yield return null;
        }
        Metric("flight_seconds", Time.time - started);
        Metric("fastest_shown_m_s", fastest);
        float pace = QuickSpeed * World.Rules.Sim.TicksPerSecond * grid.cellSize;
        Check("flies_smoothly", fastest < pace * 4f, $"at most {fastest:0} m/s on a pass of {pace:0} m/s: no jumps between ticks");
        Check("flight_ended", World.Dragon == null, $"{Time.time - started:0} s");
        Check("breath_was_drawn", view.Breath != null && looked, $"looked {looked}");
        Check("open_wizard_downed", open, "");
        Check("sheltered_wizard_not", sheltered, "");
        Check("every_chunk_freshened", freshened.Select(f => (f.CX, f.CY)).Distinct().Count() == World.ChunkCoords.Count(),
            $"{freshened.Select(f => (f.CX, f.CY)).Distinct().Count()} of {World.ChunkCoords.Count()} chunks, {grid.FreshInactiveDrawn} drawn while inactive");
        Metric("inactive_chunks_drawn", grid.FreshInactiveDrawn);
        Check("logs_the_flight", ConsoleSaid("dives out of the clouds", mark) || LogSince(0).Any(l => l.Contains("dives out of the clouds")), "core's log");
        if (lookFrames.Count == 0) yield break;
        // The same view, clear, once the sky has come back.
        while (sky.Menace > 0.02f && Time.time - started < MostSeconds) yield return null;
        var clear = new List<float>();
        yield return LookFor(lookEye, lookAt, clear);
        Metric("look_breath_mean_ms", lookFrames.Average());
        Metric("look_breath_worst_ms", lookFrames.Max());
        Metric("look_clear_mean_ms", clear.Average());
        Metric("look_clear_worst_ms", clear.Max());
        Metric("look_cost_ms", lookFrames.Average() - clear.Average());
    }

    // The fastest the shown dragon moves on a pass, frame to frame: a jump at each tick would show here.
    IEnumerator WatchTheFlight()
    {
        var was = view.ShownAt;
        var frameEnd = new WaitForEndOfFrame();
        while (World.Dragon != null)
        {
            yield return frameEnd;
            var d = World.Dragon;
            bool flying = d != null && d.Phase == DragonPhase.Passes && d.Until == 0 && d.Travelled > 0;
            if (flying && view.Seen && Time.unscaledDeltaTime < 0.1f) fastest = Mathf.Max(fastest, (view.ShownAt - was).magnitude / Mathf.Max(Time.deltaTime, 0.001f));
            was = view.ShownAt;
        }
    }

    IEnumerator WatchThePerch()
    {
        while (World.Dragon != null && !view.Perched) yield return null;
        Check("lands_to_roar", view.Perched, "it stands on the mountain as it leaves");
        yield return Wait(0.8f);
        if (World.Dragon == null) yield break;
        var ground = grid.GroundAt(view.ShownAt);
        Metric("perch_above_ground_m", view.ShownAt.y - ground);
        Cut(view.ShownAt - view.Way * -90f + Vector3.Cross(Vector3.up, view.Way).normalized * 60f + Vector3.up * 16f, view.ShownAt + Vector3.up * 14f);
        yield return null;
        Shot("perch");
    }

    // A close look at it, for the eye: from the side, a little above.
    void ShootTheDragon()
    {
        var side = Vector3.Cross(Vector3.up, view.Way).normalized;
        Cut(view.ShownAt + side * 110f + view.Way * 20f + Vector3.up * 14f, view.ShownAt);
        Shot("close");
    }

    // A camera high and ahead of the breath and off to one side, looking back at it over the valley.
    (Vector3 eye, Vector3 at) LookAtTheBreath()
    {
        var d = World.Dragon;
        World.BreathBox(out int x0, out int y0, out int x1, out int y1);
        int mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        var way = new Vector2(d.AlongX ? 1 : 0, d.AlongX ? 0 : 1) * (World.PassLine(d, d.Pass).back ? -1 : 1);
        int ex = mx + (int)(way.x * 190f), ey = my + (int)(way.y * 190f);
        var at = grid.CellCenter(Mathf.Clamp(mx, 0, World.W - 1), Mathf.Clamp(my, 0, World.H - 1));
        var eye = grid.CellCenter(Mathf.Clamp(ex, 0, World.W - 1), Mathf.Clamp(ey, 0, World.H - 1));
        return (eye + new Vector3(60f, grid.GroundAt(eye) + 120f, 40f), at + Vector3.up * (grid.GroundAt(at) + 10f));
    }

    IEnumerator LookFor(Vector3 eye, Vector3 at, List<float> frames)
    {
        Cut(eye, at);
        float until = Time.time + LookSeconds;
        bool shot = false;
        while (Time.time < until)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000f);
            if (shot || World.Dragon == null || Time.time < until - LookSeconds * 0.5f) continue;
            shot = true;
            Shot("breath");
        }
    }

    // The next pass's way, a cell of the strip this far ahead of the lead that passes the test, or null.
    (int x, int y)? AheadOfTheBreath(int ahead, System.Func<int, int, bool> good)
    {
        var d = World.Dragon;
        if (!World.BreathBox(out int x0, out int y0, out int x1, out int y1)) return null;
        var (_, back) = World.PassLine(d, d.Pass);
        int lead = d.AlongX ? (back ? x0 : x1) : (back ? y0 : y1), dir = back ? -1 : 1;
        int length = d.AlongX ? World.W : World.H;
        for (int along = lead + dir * ahead; along > 0 && along < length - 1; along += dir)
            for (int across = d.AlongX ? y0 : x0; across < (d.AlongX ? y1 : x1); across += 3)
            {
                int x = d.AlongX ? along : across, y = d.AlongX ? across : along;
                if (good(x, y)) return (x, y);
            }
        return null;
    }

    bool Walkable(int x, int y)
    {
        var c = World.At(x, y);
        return c != null && !World.IsBlocking(c);
    }

    IEnumerator WaitForTheBreath(int pass, int x, int y, System.Action<bool> caught)
    {
        while (World.Dragon?.Pass == pass && !World.BreathBox(out _, out _, out _, out _)) yield return null;
        bool down = false;
        while (World.Dragon?.Pass == pass && !down)
        {
            down = vitals.IsDown;
            if (!Passed(x, y)) { yield return null; continue; }
            break;
        }
        caught(down);
    }

    // Whether the breath's tail has gone by a cell.
    bool Passed(int x, int y)
    {
        var d = World.Dragon;
        if (d == null || !World.BreathBox(out int x0, out int y0, out int x1, out int y1)) return true;
        bool back = World.PassLine(d, d.Pass).back;
        int at = d.AlongX ? x : y, tail = d.AlongX ? (back ? x1 : x0) : (back ? y1 : y0);
        return back ? tail < at : tail > at;
    }

    IEnumerator InTheOpen(System.Action<bool> done)
    {
        var cell = AheadOfTheBreath(Ahead, (x, y) => Walkable(x, y) && World.At(x, y).T == TerrainIds.Grass && !World.ShelteredFromBreath(x, y));
        if (cell is not { } at) yield break;
        PlaceCell(at.x, at.y);
        int pass = World.Dragon.Pass, mark = ConsoleMark;
        Metric("open_cell_x", at.x);
        Metric("open_cell_y", at.y);
        yield return WaitForTheBreath(pass, at.x, at.y, r => done(r));
        Check("breath_downs_in_the_open", vitals.IsDown && vitals.FellLine.Contains("dragon"), $"at {at}: down {vitals.IsDown}: {vitals.FellLine}");
        Check("the_log_says_so", ConsoleSaid("The dragon's breath knocks you down", mark), "");
        yield return Wait((float)World.Rules.Sim.Downed.Seconds + 0.5f);
        var start = grid.CellCenter(grid.PlayerStart.x, grid.PlayerStart.y);
        var flat = wizard.transform.position - start;
        flat.y = 0f;
        Check("back_at_the_yard", !vitals.IsDown && flat.magnitude < 3f, $"{flat.magnitude:0.0} m from the start, down {vitals.IsDown}");
    }

    IEnumerator Sheltered(System.Action<bool> done)
    {
        var cell = AheadOfTheBreath(Ahead, (x, y) => Walkable(x, y) && World.ShelteredFromBreath(x, y));
        if (cell is not { } at) yield break;
        PlaceCell(at.x, at.y);
        int pass = World.Dragon.Pass;
        var here = grid.CellAt(wizard.transform.position);
        yield return WaitForTheBreath(pass, at.x, at.y, down => done(!down && here == new Vector2Int(at.x, at.y)));
        Check("breath_spares_the_sheltered", !vitals.IsDown, $"at {at}, standing on {here}, on {World.At(at.x, at.y)?.T}, down {vitals.IsDown}");
    }

    // What the flight must put back, set up before it comes and checked after.
    sealed class Damage
    {
        readonly DragonProbe probe;
        ThingId moved, broken;
        Vector3 movedHome;
        (int x, int y) meadow, tree;
        string lavaAsBuilt, meadowAsBuilt;
        int golfBefore;

        public Damage(DragonProbe on) => probe = on;

        public IEnumerator Do()
        {
            var world = probe.World;
            lavaAsBuilt = world.At(LavaX, LavaY).T;
            probe.Check("the_gauntlet_is_built", lavaAsBuilt == TerrainIds.Lava, $"({LavaX}, {LavaY}) is {lavaAsBuilt}");
            world.Paint(LavaX, LavaY, TerrainIds.Stone);

            var from = probe.grid.CellAt(probe.wizard.transform.position);
            meadow = Find(world, from, c => c.T == TerrainIds.Grass);
            meadowAsBuilt = world.At(meadow.x, meadow.y).T;
            world.Paint(meadow.x, meadow.y, TerrainIds.Dirt);
            world.At(meadow.x, meadow.y).Height += 2;

            tree = Find(world, from, c => c.T == TerrainIds.Tree, (x, y) => probe.grid.valley.TreeStands(x, y));
            world.Paint(tree.x, tree.y, TerrainIds.Stump);

            var things = probe.spawner.Live.Where(t => t.prop.State != null && !t.prop.Held && t.prop.kind == EntityIds.Crate).OrderBy(t => (probe.wizard.transform.position - t.prop.transform.position).sqrMagnitude).Take(2).ToList();
            probe.Check("things_to_wreck", things.Count == 2, $"{things.Count} crates in reach");
            if (things.Count == 2)
            {
                moved = things[0].id;
                movedHome = things[0].prop.transform.position;
                things[0].prop.BackAsBuilt(movedHome + new Vector3(4f, 1f, 3f), Quaternion.Euler(0f, 40f, 0f));
                broken = things[1].id;
                things[1].prop.Hurt(9999);
            }
            yield return new WaitForSeconds(1f);
            probe.Check("tree_felled_first", !probe.grid.valley.TreeStands(tree.x, tree.y), $"at {tree}");
            probe.Check("thing_broke_first", probe.spawner.Find(broken) == null, $"{broken}");
        }

        static (int x, int y) Find(World world, Vector2Int from, System.Func<Cell, bool> what, System.Func<int, int, bool> also = null)
        {
            for (int r = 1; r < 200; r++)
                for (int i = -r; i <= r; i++)
                    foreach (var (x, y) in new[] { (from.x + i, from.y - r), (from.x + i, from.y + r), (from.x - r, from.y + i), (from.x + r, from.y + i) })
                        if (world.InBounds(x, y) && what(world.At(x, y)) && (also == null || also(x, y))) return (x, y);
            return (from.x, from.y);
        }

        public void Checked()
        {
            var world = probe.World;
            probe.Check("gauntlet_back", world.At(LavaX, LavaY).T == lavaAsBuilt, $"({LavaX}, {LavaY}) is {world.At(LavaX, LavaY).T}");
            var m = world.At(meadow.x, meadow.y);
            probe.Check("meadow_back", m.T == meadowAsBuilt && m.Height == 0, $"{m.T}, height {m.Height}");
            probe.Check("tree_back", world.At(tree.x, tree.y).T == TerrainIds.Tree && probe.grid.valley.TreeStands(tree.x, tree.y), $"at {tree}: {world.At(tree.x, tree.y).T}");
            var thing = probe.spawner.Find(moved);
            float off = thing != null ? (thing.transform.position - movedHome).magnitude : float.PositiveInfinity;
            probe.Check("moved_thing_home", off < 1.5f, $"{off:0.0} m from where it stood");
            probe.Check("broken_thing_back", probe.spawner.Find(broken) != null, $"{broken}");
            probe.Check("nothing_changed_left", world.ChangedCount < 50, $"{world.ChangedCount} cells differ from the start");
            probe.Metric("changed_after", world.ChangedCount);
        }
    }
}
