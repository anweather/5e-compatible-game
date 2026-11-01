using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's feel scenario: what a hand on the controls should feel, measured
// through the wizard's own controls on a virtual pad and a virtual keyboard
// (UX pass 4). Running onto ice and Frost's glaze keeps its speed and slides; snow
// and a bank slow it; the punch on F and Circle on a pane, a crate and a ball, and
// Square not punching, its strength, arm, hit-stop and landing, a miss, off still, and
// the same ticks with and without its hit-stop; charged casts on keys and pad (their timing, Plant Growth on
// burnt ground, Wall of Stone held to a ramp, Frost held to Sleet Storm's slippery
// field); Frost over a swimmer, and Left Ctrl held climbing out of the water.
public class FeelProbe : SpellLooksProbe
{
    const int LaneLong = 30, LaneWide = 7, RunUp = 12;
    const float StopSeconds = 2.5f;
    // A dropped crate has stopped by this speed (m/s), within this long (s): it can still be rocking 0.8 s on.
    const float Settled = 0.02f, SettleLimit = 3f;
    // How far a crate went before the punch was made 2.5 times as strong (0.35 to 0.42 m), and how near the hand must come (m): the fist's length and a bit.
    const float CrateWentBefore = 0.42f, FistTolerance = 0.25f;
    Gamepad pad;
    Keyboard keys;
    PlayerControls controls;
    string me;
    ItemHolder fists;
    PunchPose arm;
    CastBeats beats;
    int landings;
    ItemHolder.PunchLanding landed;

    protected override IEnumerator Run()
    {
        controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        pad = InputSystem.AddDevice<Gamepad>("Feel pad");
        keys = InputSystem.AddDevice<Keyboard>("Feel keys");
        me = caster.CastPlayer;
        fists = wizard.GetComponent<ItemHolder>();
        arm = wizard.GetComponent<PunchPose>();
        beats = wizard.GetComponent<CastBeats>();
        float shakeWas = GameFeel.Shake;
        fists.PunchLanded += Landed;
        try
        {
            GameFeel.Shake = 1f;
            yield return Footing();
            yield return Punches();
            yield return Charged();
            yield return Swimmer();
            yield return PunchTicks();
        }
        finally
        {
            GameFeel.Shake = shakeWas;
            fists.PunchLanded -= Landed;
            puppet.Move(Vector2.zero);
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
            InputSystem.RemoveDevice(keys);
        }
    }

    void Landed(ItemHolder.PunchLanding landing)
    {
        landings++;
        landed = landing;
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
        if (crate != null) yield return PunchThing(crate, ahead, "crate_circle", () => Button(GamepadButton.East));
        if (crate != null) yield return SquareLeavesIt(crate, ahead);
        if (crate != null) yield return OffIsStill(crate, ahead);
        yield return Miss();
        var ball = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None).FirstOrDefault(p => p.kind == "ball" && p.State != null);
        Check("ball_found", ball != null, "the court's ball");
        if (ball == null) yield break;
        // In its court: dropped outside it, the court would put it back. No stick, so it isn't kicked.
        var at = ball.transform.position;
        var by = at + Vector3.left * 1f;
        Teleport(new Vector3(by.x, grid.GroundAt(by) + 0.05f, by.z));
        puppet.Face(at);
        // The fist goes where the camera aims, within the wizard's front arc: straight at the ball.
        Cut(at + V(-6f, 3f, 0f), at);
        yield return Wait(0.5f);
        var from = ball.transform.position;
        yield return Button(GamepadButton.East);
        yield return Wait(0.2f);
        Metric("ball_fist_m", arm.NearestFist);
        float went = 0f;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            went = Mathf.Max(went, Vector3.Distance(from, ball.transform.position));
            yield return null;
        }
        Metric("ball_circle_m", went);
        Check("ball_circle_knocked", went > 0.2f, $"it went {went:0.0} m in 0.8 s");
        Shot("ball");
        yield return null;
        yield return null;
    }

    IEnumerator PunchThing(PhysicsProp thing, Vector3 at, string name, System.Func<IEnumerator> press)
    {
        Drop(thing, new Vector3(at.x, grid.GroundAt(at) + 0.6f, at.z));
        yield return Wait(0.8f);
        var from = thing.transform.position;
        int landingsWere = landings, stops = beats.Stops, kicks = CastImpact.Kicks;
        float scale = Time.timeScale, step = Time.fixedDeltaTime;
        var body = thing.GetComponent<Rigidbody>();
        float fastest = 0f;
        yield return press();
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
            yield return null;
        }
        // A push sends a crate off at 2.5 m/s (Forces); the frame or two before the speed is read, it slows a little.
        float asked = beats.feel.punchShove * Forces.ImpulsePerPush / body.mass;
        Metric(name + "_speed_ms", fastest);
        Check(name + "_strength_from_the_tuning", Mathf.Abs(fastest - asked) < asked * 0.2f, $"{fastest:0.0} m/s, the tuning's {beats.feel.punchShove} pushes make {asked:0.0}");
        Metric(name + "_fist_m", arm.NearestFist);
        Check(name + "_arm_reaches", arm.NearestFist < FistTolerance, $"the hand came {arm.NearestFist:0.00} m from {arm.Goal}");
        Check(name + "_lands_once", landings == landingsWere + 1, $"{landings - landingsWere} landings");
        Check(name + "_sends_the_strike", World.Stands[me].NextStrike > World.Tick - 2, $"may strike again at tick {World.Stands[me].NextStrike}, now {World.Tick}");
        Check(name + "_landing_says_who", landed.Prop == thing && landed.Puncher == fists && landed.Hit, $"{landed.Prop}, by {landed.Puncher}");
        Check(name + "_hit_stop", beats.Stops == stops + 1 && !beats.Stopped, $"{beats.Stops - stops} stops");
        Check(name + "_kick", CastImpact.Kicks == kicks + 1 && CastImpact.LastKick <= 0.06f, $"{CastImpact.Kicks - kicks} kicks, {CastImpact.LastKick:0.000} m");
        Check(name + "_leaves_time_alone", Mathf.Approximately(Time.timeScale, scale) && Mathf.Approximately(Time.fixedDeltaTime, step), $"time scale {Time.timeScale}");
        yield return Wait(1.6f);
        float went = Vector3.Distance(from, thing.transform.position);
        Metric(name + "_m", went);
        Check(name + "_at_least_twice_as_far", went >= CrateWentBefore * 2f, $"it went {went:0.00} m, {CrateWentBefore} before");
        Shot(name);
        yield return null;
        yield return null;
    }

    // Square only picks up and sits: no item or seat in reach, it does nothing to a crate.
    IEnumerator SquareLeavesIt(PhysicsProp crate, Vector3 at)
    {
        Drop(crate, new Vector3(at.x, grid.GroundAt(at) + 0.6f, at.z));
        yield return Wait(0.8f);
        var body = crate.GetComponent<Rigidbody>();
        for (float t = 0f; t < SettleLimit && body.linearVelocity.sqrMagnitude > Settled * Settled; t += Time.deltaTime) yield return null;
        var from = crate.transform.position;
        yield return Button(GamepadButton.West);
        yield return Wait(0.8f);
        float went = Vector3.Distance(from, crate.transform.position);
        Metric("crate_square_m", went);
        Check("square_doesnt_punch", went < 0.1f, $"it went {went:0.00} m");
    }

    // The Shake and rumble slider at off: the same punch, the same knock, no stop and no kick.
    IEnumerator OffIsStill(PhysicsProp crate, Vector3 at)
    {
        Drop(crate, new Vector3(at.x, grid.GroundAt(at) + 0.6f, at.z));
        yield return Wait(0.8f);
        var from = crate.transform.position;
        int landingsWere = landings, stops = beats.Stops, kicks = CastImpact.Kicks;
        GameFeel.Shake = 0f;
        yield return Tap(Key.F);
        yield return Wait(0.8f);
        GameFeel.Shake = 1f;
        float went = Vector3.Distance(from, crate.transform.position);
        Check("off_punch_still_lands", landings == landingsWere + 1 && went >= CrateWentBefore, $"{landings - landingsWere} landings, it went {went:0.00} m");
        Check("off_punch_is_still", beats.Stops == stops && CastImpact.Kicks == kicks, $"{beats.Stops - stops} stops, {CastImpact.Kicks - kicks} kicks");
    }

    // Nothing in reach: the arm still goes out, straight ahead, and it lands once, on nothing, with no stop.
    IEnumerator Miss()
    {
        var west = wizard.transform.position + Vector3.left * 3f;
        puppet.Face(west);
        Cut(wizard.transform.position + V(3f, 2.5f, -1f), west);
        yield return Wait(0.8f);
        int landingsWere = landings, stops = beats.Stops;
        yield return Tap(Key.F);
        yield return Wait(0.35f);
        Metric("miss_fist_m", arm.NearestFist);
        Check("miss_lands_once_on_nothing", landings == landingsWere + 1 && !landed.Reached, $"{landings - landingsWere} landings, reached {landed.Reached}");
        Check("miss_arm_reaches", arm.NearestFist < FistTolerance, $"the hand came {arm.NearestFist:0.00} m from where it aimed");
        Check("miss_no_hit_stop", beats.Stops == stops, $"{beats.Stops - stops} stops");
        yield return Wait(0.5f);
        Check("arm_handed_back", !arm.Playing, "the pose is over");
    }

    // The same world run on with and without the hit-stop of a punch on a crate: the same ticks, the same cells, in the same time.
    IEnumerator PunchTicks()
    {
        (int tick, string hash, float seconds) settled = default, with = default, without = default;
        FixClock(true);
        GameFeel.Shake = 0f;
        yield return PunchTickRun(r => settled = r);
        yield return PunchTickRun(r => without = r);
        int stops = beats.Stops;
        GameFeel.Shake = 1f;
        yield return PunchTickRun(r => with = r);
        int stopped = beats.Stops - stops;
        FixClock(false);
        Check("punch_tick_run_had_a_stop", stopped == 1, $"{stopped} stops");
        Check("punch_hit_stop_leaves_the_tick_alone", with.tick == without.tick && with.hash == without.hash, $"first {settled.hash}, tick {with.tick} {with.hash} / tick {without.tick} {without.hash}");
        Check("punch_hit_stop_leaves_the_tick_rate_alone", Mathf.Abs(with.seconds - without.seconds) < 0.25f, $"{with.seconds:0.00} s, {without.seconds:0.00} s");
    }

    IEnumerator PunchTickRun(System.Action<(int, string, float)> done)
    {
        const int PunchAt = 12, TakeAt = 20;
        grid.ResetWorld();
        yield return null;
        yield return null;
        var ahead = wizard.transform.position + Vector3.right;
        puppet.Face(ahead + Vector3.right * 5f);
        Cut(ahead + V(-4f, 3f, -5f), ahead);
        var crate = NearestCrate(ahead);
        Drop(crate, new Vector3(ahead.x, grid.GroundAt(ahead) + 0.6f, ahead.z));
        while (World.Tick < PunchAt || World.TickInProgress) yield return null;
        float from = Time.time;
        yield return Tap(Key.F);
        while (World.Tick < TakeAt || World.TickInProgress) yield return null;
        done((World.Tick, World.CellHash(), Time.time - from));
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
        yield return Timing();
        yield return Sleet();
    }

    // A level every World.SecondsPerLevel past the meter, and the whole held cast, first press to landing.
    IEnumerator Timing()
    {
        var o = Spot(13, 9);
        Patch(o, 13, 9, TerrainIds.Dirt);
        yield return Aimed(o, 13, 9);
        float start = Time.time;
        yield return Tap(Key.C);
        yield return Wait(0.1f);
        InputSystem.QueueStateEvent(keys, new KeyboardState(Key.C));
        float second = 0f, third = 0f;
        for (float t = 0f; t < 5f && third == 0f; t += Time.deltaTime)
        {
            if (second == 0f && caster.HoldCharge >= 2) second = Time.time;
            if (caster.HoldCharge >= 3) third = Time.time;
            yield return null;
        }
        float full = Time.time - start;
        Shot("bar-full-charge");
        yield return null;
        Check("held_marker_shown", caster.Marker != null, $"{caster.Marker?.Kind}");
        InputSystem.QueueStateEvent(keys, new KeyboardState());
        for (float t = 0f; t < 4f && World.CastOf(me).Casting; t += Time.deltaTime) yield return null;
        float landedAt = Time.time - start;
        Metric("seconds_a_level", third - second);
        Metric("full_charge_s", full);
        Metric("held_cast_landed_s", landedAt);
        Check("a_level_takes_half_a_second", Mathf.Abs(third - second - (float)World.SecondsPerLevel) < 0.12f, $"{third - second:0.00} s, the rules say {World.SecondsPerLevel:0.00}");
        Check("full_charge_in_two_to_three_seconds", full >= 1.8f && full <= 3.2f, $"{full:0.0} s to full charge, landed at {landedAt:0.0} s");
        yield return Wait(1f);
    }

    // Water then air held to full is Sleet Storm: a field that slides the wizard.
    IEnumerator Sleet()
    {
        var o = Spot(13, 13);
        Patch(o, 13, 13, TerrainIds.Dirt);
        for (int y = 0; y < 13; y++)
            for (int x = 0; x < 13; x++) World.At(o.x + x, o.y + y).Water = 1;
        yield return Aimed(o, 13, 13);
        yield return Pair(Key.X, Key.V, default, default, 3);
        yield return Wait(2.5f);
        var cells = Enumerable.Range(0, 169).Select(i => World.At(o.x + i % 13, o.y + i / 13)).ToList();
        int slick = cells.Count(World.IsSlippery);
        Metric("sleet_slippery_cells", slick);
        Check("sleet_field_slippery", slick >= 30, $"{slick} of 169 cells slippery");
        var mid = grid.CellCenter(o.x + 6, o.y + 6);
        // On the ground: put a little above it, the wizard isn't grounded for a frame, so not slipping,
        // and SurfaceEffects takes the jump here for that frame's momentum (100 m/s, off the field and onto dirt).
        Teleport(new Vector3(mid.x - 4f, grid.GroundAt(mid), mid.z));
        Cut(mid + V(-9f, 3f, 0f), mid);
        yield return Wait(0.3f);
        puppet.Move(Vector2.up);
        yield return Wait(0.8f);
        puppet.Move(Vector2.zero);
        var let = wizard.transform.position;
        yield return Wait(StopSeconds);
        float slid = Vector3.Distance(let, wizard.transform.position);
        Metric("sleet_stop_m", slid);
        Check("sleet_slides_the_wizard", slid > 1f, $"it slid {slid:0.0} m");
        Shot("sleet");
        yield return null;
        yield return null;
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
        for (float t = 0f; t < 5f && caster.HoldCharge < charge; t += Time.deltaTime) yield return null;
        yield return Wait(0.1f);
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
