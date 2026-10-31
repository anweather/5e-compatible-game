using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's feel scenario: what a hand on the controls should feel, measured
// through the wizard's own controls on a virtual pad and a virtual keyboard
// (UX pass 4). Running onto ice and Frost's glaze keeps its speed and slides; snow
// and a bank slow it; the punch on F and Square on a pane, a crate and a ball;
// charged casts on keys and pad (Plant Growth on burnt ground, Wall of Stone held to
// a ramp); Frost over a swimmer, and Left Ctrl held climbing out of the water.
public class FeelProbe : SpellLooksProbe
{
    const int LaneLong = 30, LaneWide = 7, RunUp = 12;
    const float StopSeconds = 2.5f;
    Gamepad pad;
    Keyboard keys;
    PlayerControls controls;
    string me;

    protected override IEnumerator Run()
    {
        controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        pad = InputSystem.AddDevice<Gamepad>("Feel pad");
        keys = InputSystem.AddDevice<Keyboard>("Feel keys");
        me = caster.CastPlayer;
        try
        {
            yield return Footing();
            yield return Punches();
            yield return Charged();
            yield return Swimmer();
        }
        finally
        {
            puppet.Move(Vector2.zero);
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
            InputSystem.RemoveDevice(keys);
        }
    }

    // --- footing ---

    IEnumerator Footing()
    {
        var o = Spot(LaneLong, LaneWide);
        Patch(o, LaneLong, LaneWide, TerrainIds.Dirt);
        float free = 0f, stop = 0f;
        yield return RunOn(o, null, (s, d) => (free, stop) = (s, d));
        Metric("dirt_speed_ms", free);
        Metric("dirt_stop_m", stop);
        Check("runs_on_dirt", free > 2f, $"{free:0.0} m/s");

        yield return Slides(o, "ice", (x, y) =>
        {
            var c = World.At(x, y);
            c.Water = 2;
            c.Frozen = true;
        }, free, stop);
        yield return Slides(o, "glaze", (x, y) => World.SetCover(x, y, CoverIds.Frost), free, stop);
        yield return Slows(o, "snow", World.SnowCover, free, 0.85f);
        yield return Slows(o, "bank", World.BankCover, free, 0.7f);
    }

    IEnumerator Slides(Vector2Int o, string name, System.Action<int, int> lay, float free, float stopOnDirt)
    {
        float speed = 0f, stop = 0f;
        yield return RunOn(o, lay, (s, d) => (speed, stop) = (s, d));
        Metric(name + "_speed_ms", speed);
        Metric(name + "_stop_m", stop);
        Check(name + "_keeps_speed", speed > free * 0.9f, $"{speed:0.0} m/s on it, {free:0.0} on dirt");
        Check(name + "_slides", stop > stopOnDirt * 2f && stop > 1f, $"stops in {stop:0.0} m, {stopOnDirt:0.0} on dirt");
    }

    IEnumerator Slows(Vector2Int o, string name, string cover, float free, float share)
    {
        float speed = 0f;
        yield return RunOn(o, (x, y) => World.SetCover(x, y, cover), (s, _) => speed = s);
        Metric(name + "_speed_ms", speed);
        Metric(name + "_speed_share", speed / free);
        Check(name + "_slows", Mathf.Abs(speed / free - share) < 0.1f, $"{speed / free:0.00} of its speed, the rules say {share}");
    }

    // A run east along the lane with this underfoot past the run-up: its speed there, and how far it runs on after letting go.
    IEnumerator RunOn(Vector2Int o, System.Action<int, int> lay, System.Action<float, float> measured)
    {
        Patch(o, LaneLong, LaneWide, TerrainIds.Dirt);
        for (int y = 0; y < LaneWide; y++)
            for (int x = RunUp; x < LaneLong; x++)
                if (lay != null) lay(o.x + x, o.y + y);
        yield return null;
        var lane = o.y + LaneWide / 2;
        PlaceCell(o.x + 1, lane);
        var start = grid.CellCenter(o.x + 1, lane);
        Cut(start + V(-5f, 3f, 0f), start + V(4f, 1f, 0f));
        yield return Wait(0.2f);
        puppet.Move(Vector2.up);
        float edge = grid.CellCenter(o.x + RunUp, lane).x + 1f;
        for (float t = 0f; t < 6f && wizard.transform.position.x < edge; t += Time.deltaTime) yield return null;
        var from = wizard.transform.position;
        yield return Wait(0.7f);
        float speed = Vector3.Distance(from, wizard.transform.position) / 0.7f;
        Shot("run");
        puppet.Move(Vector2.zero);
        var let = wizard.transform.position;
        yield return Wait(StopSeconds);
        measured(speed, Vector3.Distance(let, wizard.transform.position));
    }

    // --- the punch ---

    IEnumerator Punches()
    {
        yield return PunchPane();
        var o = Spot(8, 5);
        Patch(o, 8, 5, TerrainIds.Dirt);
        var stand = new Vector2Int(o.x + 3, o.y + 2);
        PlaceCell(stand.x, stand.y);
        var ahead = grid.CellCenter(stand.x, stand.y) + Vector3.right;
        Cut(ahead + V(-4f, 3f, -5f), ahead);
        puppet.Face(ahead + Vector3.right * 5f);
        yield return Wait(0.3f);
        var crate = NearestCrate(ahead);
        Check("crate_near", crate != null, "a crate");
        if (crate != null) yield return PunchThing(crate, ahead, "crate_f", () => Tap(Key.F));
        if (crate != null) yield return PunchThing(crate, ahead, "crate_square", () => Button(GamepadButton.West));
        var ball = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None).FirstOrDefault(p => p.kind == "ball" && p.State != null);
        Check("ball_found", ball != null, "the court's ball");
        if (ball == null) yield break;
        // In its court: dropped outside it, the court would put it back. No stick, so it isn't kicked.
        var at = ball.transform.position;
        var by = at + Vector3.left * 1f;
        Teleport(new Vector3(by.x, grid.GroundAt(by) + 0.05f, by.z));
        puppet.Face(at);
        Cut(at + V(-3f, 3f, -5f), at);
        yield return Wait(0.5f);
        var from = ball.transform.position;
        yield return Tap(Key.F);
        float went = 0f;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            went = Mathf.Max(went, Vector3.Distance(from, ball.transform.position));
            yield return null;
        }
        Metric("ball_f_m", went);
        Check("ball_f_knocked", went > 0.2f, $"it went {went:0.0} m in 0.8 s");
        Shot("ball");
        yield return null;
        yield return null;
    }

    IEnumerator PunchThing(PhysicsProp thing, Vector3 at, string name, System.Func<IEnumerator> press)
    {
        Drop(thing, new Vector3(at.x, grid.GroundAt(at) + 0.6f, at.z));
        yield return Wait(0.8f);
        var from = thing.transform.position;
        yield return press();
        yield return Wait(1.2f);
        float went = Vector3.Distance(from, thing.transform.position);
        Metric(name + "_m", went);
        Check(name + "_knocked", went > 0.4f, $"it went {went:0.0} m");
        Shot(name);
        yield return null;
        yield return null;
    }

    IEnumerator PunchPane()
    {
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).FirstOrDefault(v => v.villageName == "Hollowmere");
        var house = village != null ? village.GetComponentsInChildren<VillageHouse>().FirstOrDefault(h => h.walls == WallKind.Stone) : null;
        var panes = house != null ? house.GetComponent<WindowPanes>() : null;
        Check("pane_found", panes != null && panes.panes.Length > 0, "a stone house's window");
        if (panes == null || panes.panes.Length == 0) yield break;
        foreach (var pane in panes.panes)
        {
            var at = panes.transform.TransformPoint(pane.at);
            var facing = panes.transform.TransformDirection(pane.facing);
            var cell = PaneCell(at);
            if (cell == null || Physics.Linecast(at + facing * 0.3f, at + facing * 2.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
            var stand = at + facing * 0.85f;
            Teleport(new Vector3(stand.x, grid.GroundAt(stand) + 0.05f, stand.z));
            Cut(at + facing * 3.5f + Vector3.up, at);
            puppet.Face(at);
            yield return Wait(0.5f);
            Shot("pane-whole");
            var c = World.At(cell.Value.x, cell.Value.y);
            yield return Tap(Key.F);
            yield return Wait(0.7f);
            Check("first_punch_cracks", c.T == TerrainIds.Pane && c.Cracked != 0, $"{c.T}, cracked {c.Cracked}");
            Shot("pane-cracked");
            yield return null;
            yield return Tap(Key.F);
            yield return Wait(0.7f);
            Check("second_punch_breaks", c.T != TerrainIds.Pane, c.T);
            Shot("pane-broken");
            yield return null;
            yield break;
        }
        Check("pane_reachable", false, "no pane with room in front of it");
    }

    Vector2Int? PaneCell(Vector3 point)
    {
        var at = grid.CellAt(point);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (World.InBounds(at.x + dx, at.y + dy) && World.At(at.x + dx, at.y + dy).T == TerrainIds.Pane) return new Vector2Int(at.x + dx, at.y + dy);
        return null;
    }

    // --- charged casts ---

    IEnumerator Charged()
    {
        yield return PlantGrowth("keys", Key.C, Key.X, default, default);
        yield return Ramp("keys", Key.C, default);
        yield return PlantGrowth("pad", Key.None, Key.None, new GamepadState { leftTrigger = 1f }, new GamepadState().WithButton(GamepadButton.RightShoulder));
        yield return Ramp("pad", Key.None, new GamepadState { leftTrigger = 1f });
    }

    // Stands west of a patch with the crosshair on its middle.
    IEnumerator Aimed(Vector2Int o, int w, int h)
    {
        var mid = grid.CellCenter(o.x + w / 2, o.y + h / 2);
        mid.y = grid.GroundAt(mid);
        var stand = grid.CellAt(mid - Vector3.right * 8f);
        PlaceCell(stand.x, stand.y);
        Cut(mid + V(-11f, 6f, 0f), mid);
        puppet.Face(mid);
        yield return Wait(0.5f);
    }

    IEnumerator PlantGrowth(string on, Key first, Key last, GamepadState firstPad, GamepadState lastPad)
    {
        var o = Spot(11, 11);
        Patch(o, 11, 11, TerrainIds.Dirt);
        for (int y = 0; y < 11; y++)
            for (int x = 0; x < 11; x++) World.SetCover(o.x + x, o.y + y, CoverIds.Ash);
        yield return Aimed(o, 11, 11);
        yield return Pair(first, last, firstPad, lastPad, 3);
        yield return Wait(1f);
        var cells = Enumerable.Range(0, 121).Select(i => World.At(o.x + i % 11, o.y + i / 11)).ToList();
        int shoots = cells.Count(c => c.T == TerrainIds.Sprouts || c.T == TerrainIds.Sapling || c.T == TerrainIds.Grass);
        Metric($"plant_{on}_shoots", shoots);
        Metric($"plant_{on}_ash_left", cells.Count(c => c.Cover == CoverIds.Ash));
        Check($"plant_growth_charged_{on}", shoots >= 40, $"{shoots} cells of shoots on burnt ground");
        Shot("plant-" + on);
        yield return null;
        yield return null;
    }

    IEnumerator Ramp(string on, Key earth, GamepadState earthPad)
    {
        var o = Spot(13, 9);
        Patch(o, 13, 9, TerrainIds.Dirt);
        yield return Aimed(o, 13, 9);
        int[] before = Enumerable.Range(0, 117).Select(i => World.At(o.x + i % 13, o.y + i / 13).Height).ToArray();
        yield return Pair(earth, earth, earthPad, earthPad, 3);
        yield return Wait(1.5f);
        var raised = Enumerable.Range(0, 117).Select(i => World.At(o.x + i % 13, o.y + i / 13).Height - before[i]).Where(d => d > 0).ToList();
        Metric($"ramp_{on}_cells", raised.Count);
        Check($"wall_held_to_ramp_{on}", raised.Distinct().Count() >= 2, $"{raised.Count} cells raised, heights {string.Join(",", raised.Distinct().OrderBy(d => d))}");
        Shot("ramp-" + on);
        yield return null;
        yield return null;
    }

    // Two elements, the first tapped and the last held to this charge, then let go, on the keys or on the pad.
    IEnumerator Pair(Key first, Key last, GamepadState firstPad, GamepadState lastPad, int charge)
    {
        bool onKeys = first != Key.None;
        if (onKeys) yield return Tap(first);
        else yield return Hold(firstPad, 0.1f);
        yield return Wait(0.1f);
        if (onKeys) InputSystem.QueueStateEvent(keys, new KeyboardState(last));
        else InputSystem.QueueStateEvent(pad, lastPad);
        yield return Wait(ElementHold.SecondsPerLevel * (charge - 1) + 0.3f);
        Metric(onKeys ? "held_charge_keys" : "held_charge_pad", caster.HoldCharge);
        if (onKeys) InputSystem.QueueStateEvent(keys, new KeyboardState());
        else InputSystem.QueueStateEvent(pad, new GamepadState());
        for (float t = 0f; t < 4f && World.CastOf(me).Casting; t += Time.deltaTime) yield return null;
    }

    // --- swimming ---

    IEnumerator Swimmer()
    {
        var surface = wizard.GetComponent<SurfaceEffects>();
        var roll = wizard.GetComponent<DodgeRoll>();
        var deep = SwimProbe.DeepestLake(grid, out _);
        var at = grid.CellCenter(deep.x, deep.y);
        float top = grid.WaterSurfaceY(deep.x, deep.y);
        Teleport(new Vector3(at.x, top + 0.5f, at.z));
        Cut(at + V(-7f, 5f, -7f), at + V(0f, top - at.y, 0f));
        yield return Wait(2.5f);
        Check("swims", surface.Swimming, "afloat in the lake");
        // Frost along the row over the swimmer, cast from the side as another player's would be.
        caster.Cast(SpellIds.Frost, deep.x - 4, deep.y, 1, 1);
        yield return Wait(1.5f);
        var water = World.At(deep.x, deep.y);
        Metric("frost_froze_over", water.Frozen ? 1 : 0);
        Metric("swimmer_y_under_top_m", top - wizard.transform.position.y);
        Check("frost_freezes_over_swimmer", water.Frozen, $"the cell over the swimmer frozen: {water.Frozen}");
        Check("swimmer_under_ice", surface.UnderIce, $"UnderIce {surface.UnderIce}, swimming {surface.Swimming}");
        Shot("frost-over-swimmer");
        yield return null;
        yield return null;
        foreach (var c in World.Cells.Where(c => c.Frozen && c.Water > 0)) c.Frozen = false;
        yield return Wait(1f);
        Check("back_up_after_a_thaw", surface.Swimming && !surface.UnderIce, $"swimming {surface.Swimming}, under ice {surface.UnderIce}");

        yield return ClimbOut(surface, roll, deep);
    }

    // Left Ctrl held (the dive) and pressed again while swimming to the bank and kicking out: it never rolls.
    IEnumerator ClimbOut(SurfaceEffects surface, DodgeRoll roll, Vector2Int deep)
    {
        int x = deep.x;
        while (World.At(x + 1, deep.y).Water > 0 && !World.IsBlocking(World.At(x + 1, deep.y))) x++;
        var near = grid.CellCenter(x - 3, deep.y);
        Teleport(new Vector3(near.x, grid.WaterSurfaceY(x - 3, deep.y) + 0.2f, near.z));
        var bank = grid.CellCenter(x + 3, deep.y);
        Cut(near + V(-5f, 3f, 0f), near + V(4f, 0f, 0f));
        puppet.Face(bank);
        yield return Wait(1.2f);
        int rolls = roll.Count;
        InputSystem.QueueStateEvent(keys, new KeyboardState(Key.LeftCtrl));
        puppet.Move(Vector2.up);
        for (float t = 0f; t < 4f && surface.Swimming; t += Time.deltaTime)
        {
            if (t > 1f) surface.Kick();
            yield return null;
        }
        int rollsHeld = roll.Count - rolls;
        Check("left_the_water", !surface.Swimming, "out of the lake");
        // Let go and pressed again as it lands on the bank, as a hand does.
        InputSystem.QueueStateEvent(keys, new KeyboardState());
        yield return null;
        yield return Tap(Key.LeftCtrl);
        yield return null;
        Metric("rolls_climbing_out", roll.Count - rolls);
        Check("no_roll_climbing_out", roll.Count == rolls, $"{rollsHeld} rolls held, {roll.Count - rolls} with the press as it comes out");
        puppet.Move(Vector2.zero);
        Shot("climbed-out");
        yield return Wait(2f);
        yield return Tap(Key.LeftCtrl);
        yield return Wait(0.2f);
        Check("rolls_again_on_land", roll.Count > rolls, $"{roll.Count - rolls} rolls with a press 2 s later");
    }

    // --- presses ---

    IEnumerator Tap(Key key)
    {
        InputSystem.QueueStateEvent(keys, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keys, new KeyboardState());
        yield return null;
        yield return null;
    }

    IEnumerator Button(GamepadButton button) => Hold(new GamepadState().WithButton(button), 0.05f);

    IEnumerator Hold(GamepadState state, float seconds)
    {
        InputSystem.QueueStateEvent(pad, state);
        yield return Wait(seconds);
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
    }
}
