using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's looks scenario (the host UX pass, 2026-10-29): the material
// effects written without a GPU, each staged in the yard on a clear noon and
// framed for one picture: rust, sand and glass, glow balls breaking, ice shards,
// mud, hot metal, oil on water and fire across it, the shock flash, Acid Splash,
// window glass. It edits the yard's cells directly, so one script takes the
// before and the after. Beside the pictures it checks what each effect does to
// the wizard and the cells.
public class LooksProbe : ProbeScenario
{
    SurfaceEffects surface;
    Vitals vitals;

    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        surface = wizard.GetComponent<SurfaceEffects>();
        vitals = wizard.GetComponent<Vitals>();
        var o = grid.YardCell(24, 30);
        yield return Rust(o);
        yield return SandAndGlass(new Vector2Int(o.x, o.y + 6));
        yield return Balls(new Vector2Int(o.x + 10, o.y));
        yield return Mud(new Vector2Int(o.x + 10, o.y + 6));
        yield return HotMetal(new Vector2Int(o.x, o.y + 12));
        yield return Acid(new Vector2Int(o.x + 16, o.y));
        yield return Pond();
        yield return Window();
    }

    IEnumerator Rust(Vector2Int o)
    {
        Patch(o, 3, 4, TerrainIds.Metal);
        Patch(new Vector2Int(o.x + 3, o.y), 3, 4, TerrainIds.RustedMetal);
        yield return Picture("rust", Mid(o, 6, 4));
    }

    IEnumerator SandAndGlass(Vector2Int o)
    {
        Patch(o, 4, 4, TerrainIds.Sand);
        Patch(new Vector2Int(o.x + 4, o.y), 4, 4, TerrainIds.Glass);
        yield return Picture("sand-glass", Mid(o, 8, 4));
        // Lightning fuses sand into glass, in place.
        var hit = new Vector2Int(o.x + 1, o.y + 1);
        Cut(Mid(o, 8, 4) + V(-5f, 4f, -6f), Mid(o, 8, 4));
        caster.Cast(SpellIds.ShockingGrasp, hit.x, hit.y, 1);
        yield return Wait(1.5f);
        Check("lightning_fuses_sand", World.At(hit.x, hit.y).T == TerrainIds.Glass, $"the struck cell is {World.At(hit.x, hit.y).T}");
    }

    // A glow ball dropped from the hand's pace bounces; one hurled at the ground shatters. Then a raider's ice.
    IEnumerator Balls(Vector2Int o)
    {
        Patch(o, 4, 4, TerrainIds.Dirt);
        var at = Mid(o, 4, 4);
        PlaceCell(o.x - 7, o.y);
        Cut(at + V(-3.4f, 2.2f, -3.4f), at + Vector3.up * 0.5f);
        var balls = FindObjectsByType<Carryable>(FindObjectsSortMode.InstanceID)
            .Where(c => c.Holder == null && c.GetComponent<Fragile>() != null && Glow(c.GetComponent<PhysicsProp>().kind)).ToList();
        Check("two_glow_balls", balls.Count >= 2, $"{balls.Count} glow balls");
        if (balls.Count < 2) yield break;

        Hurl(balls[0], at + Vector3.up * 1f, Vector3.down * 4.5f);
        yield return Wait(1f);
        Check("hand_drop_survives", balls[0] != null, "a ball at 4.5 m/s stays whole");
        // The whole ball is gone, its light too: the one that survived goes out of frame first, then
        // another leaves the hand as a player's does and meets the ground hard.
        Hurl(balls[0], at + V(-10f, 1f, 10f), Vector3.zero);
        var hurled = balls[1];
        var stand = at + V(-1.5f, 0f, 0f);
        Teleport(new Vector3(stand.x, grid.GroundAt(stand) + 0.05f, stand.z));
        yield return Wait(0.3f);
        hands.PickUp(hurled);
        yield return Wait(0.3f);
        hands.Drop();
        hurled.GetComponent<Rigidbody>().linearVelocity = (at - hurled.transform.position).normalized * 11f;
        for (float t = 0f; t < 1.5f && hurled != null; t += Time.deltaTime) yield return null;
        Check("hard_throw_breaks", hurled == null, "a ball leaving the hand at 11 m/s breaks on the ground");
        yield return Wait(0.12f);
        Shot("glow-ball");
        yield return null;
        yield return Wait(2.5f);
        int things = FindObjectsByType<Carryable>(FindObjectsSortMode.None).Count(c => (c.transform.position - at).sqrMagnitude < 16f);
        var lamps = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.name == "Light" && l.transform.parent == null || l.GetComponentInParent<Carryable>() != null)
            .Where(l => (l.transform.position - at).sqrMagnitude < 25f).ToList();
        var others = FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Point && (l.transform.position - at).sqrMagnitude < 25f).Select(l => $"{l.transform.root.name}/{l.name}");
        Check("nothing_left_of_ball", things == 0 && lamps.Count == 0, $"{things} items, {lamps.Count} item lights; other lights near: {string.Join(", ", others)}");
        Shot("glow-ball-gone");
        yield return null;
        yield return null;

        // What DefenseField.Shatter shows for a frozen raider that's struck.
        ShardBurst.At(at + Vector3.up * 0.5f, new Color(0.7f, 0.88f, 1f), 40);
        GameSounds.Play(SoundIds.Shatter, at);
        yield return Wait(0.15f);
        Shot("ice-shards");
        yield return null;
        yield return Wait(2f);
    }

    static bool Glow(string kind) => kind == ItemIds.GlowBallAmber || kind == ItemIds.GlowBallAzure || kind == ItemIds.GlowBallRose;

    static void Hurl(Carryable ball, Vector3 from, Vector3 velocity)
    {
        var body = ball.GetComponent<Rigidbody>();
        body.isKinematic = false; // it may have been asleep in a chunk that isn't active
        body.position = from;
        ball.transform.position = from;
        body.linearVelocity = velocity;
    }

    IEnumerator Mud(Vector2Int o)
    {
        Patch(o, 8, 4, TerrainIds.Mud);
        // Mud dries on the next soak pass unless the cell is damp, as it is after rain or water.
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++) World.At(o.x + x, o.y + y).DampUntil = World.Tick + 600;
        var yard = grid.yardCells;
        var mid = Mid(o, 8, 4);
        Place(o.x - yard.x - 7, o.y + 1 - yard.y);
        Cut(mid + V(-6f, 3.5f, -6.5f), mid);
        yield return Wait(0.3f);
        var start = wizard.transform.position;
        yield return WalkFor(o.x - yard.x - 2, o.y + 1 - yard.y, 1.2f);
        float onGrass = Speed(start) / 1.2f;
        Place(o.x - yard.x + 1, o.y + 1 - yard.y);
        yield return Wait(0.2f);
        start = wizard.transform.position;
        yield return WalkFor(o.x - yard.x + 7, o.y + 1 - yard.y, 1.2f);
        float inMud = Speed(start) / 1.2f;
        Metric("walk_speed_grass", onGrass);
        Metric("walk_speed_mud", inMud);
        Check("mud_slows", surface.InMud && inMud < onGrass * 0.7f, $"{onGrass:0.00} m/s, then {inMud:0.00} m/s in mud");
        Shot("mud");
        yield return null;
        yield return null;
    }

    float Speed(Vector3 from)
    {
        var moved = wizard.transform.position - from;
        moved.y = 0f;
        return moved.magnitude;
    }

    IEnumerator HotMetal(Vector2Int o)
    {
        Patch(o, 6, 4, TerrainIds.Metal);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 3; x++) World.Apply(o.x + x, o.y + y, "fire", 3, -1, false);
        PlaceCell(o.x + 1, o.y + 1);
        float health = vitals.Health;
        var mid = Mid(o, 6, 4);
        Cut(mid + V(-5f, 3.5f, -5.5f), mid);
        yield return Wait(0.8f);
        int heat = World.At(o.x, o.y).Heat;
        Metric("heat_after_fire", heat);
        Check("metal_heats", heat > 0, $"heat {heat}");
        Check("hot_metal_burns", surface.OnHotMetal && vitals.Health < health, $"on it {surface.OnHotMetal}, health {health:0.#} -> {vitals.Health:0.#}");
        Shot("hot-metal");
        yield return null;
        yield return null;
        PlaceCell(o.x - 3, o.y - 1);
        yield return Wait(0.3f);
    }

    IEnumerator Acid(Vector2Int o)
    {
        Patch(o, 5, 5, TerrainIds.Dirt);
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++) World.SetCover(o.x + x, o.y + y, x % 2 == 0 ? CoverIds.Oil : CoverIds.Thorns);
        var hit = new Vector2Int(o.x + 2, o.y + 2);
        PlaceCell(o.x - 5, o.y + 2);
        var mid = Mid(o, 5, 5);
        Cut(mid + V(-4f, 3.2f, -4.5f), mid);
        yield return Wait(0.4f);
        Shot("acid-before");
        yield return null;
        puppet.Face(grid.CellCenter(hit.x, hit.y));
        caster.Cast(SpellIds.AcidSplash, hit.x, hit.y, 1);
        for (float t = 0f; t < 3f && World.At(hit.x, hit.y).Cover != null; t += Time.deltaTime) yield return null;
        Check("acid_eats_cover", World.At(hit.x, hit.y).Cover == null, $"the cover there is {World.At(hit.x, hit.y).Cover ?? "gone"}");
        yield return Wait(0.12f);
        Shot("acid-splash");
        yield return null;
        yield return Wait(2.5f);
        Shot("acid-stain");
        yield return null;
        yield return null;
    }

    // Oil laid on the yard's pond and set alight, then lightning in the water.
    IEnumerator Pond()
    {
        var yard = grid.yardCells;
        var wet = Enumerable.Range(0, yard.width * yard.height)
            .Select(i => new Vector2Int(yard.x + i % yard.width, yard.y + i / yard.width))
            .Where(c => World.At(c.x, c.y).Water >= 2).ToList();
        Check("pond_found", wet.Count >= 16, $"{wet.Count} wet cells in the yard");
        if (wet.Count < 16) yield break;
        var middle = wet[wet.Count / 2];
        var at = grid.CellCenter(middle.x, middle.y);
        at.y = grid.WaterSurfaceY(middle.x, middle.y);
        var bank = wet.OrderByDescending(c => (c - middle).sqrMagnitude).First();
        PlaceCell(bank.x - 1, bank.y);
        Cut(at + V(-7f, 6f, -7f), at);

        foreach (var c in wet.Where(c => (c - middle).sqrMagnitude <= 9)) World.SetCover(c.x, c.y, CoverIds.Oil);
        yield return Wait(1f);
        Shot("oil-slick");
        yield return null;
        World.Apply(middle.x, middle.y, "fire", 3, -1, false);
        yield return Wait(2f);
        Check("fire_on_water", wet.Count(c => World.At(c.x, c.y).Fire > 0) > 1, $"{wet.Count(c => World.At(c.x, c.y).Fire > 0)} cells of water alight");
        Shot("oil-fire");
        yield return null;
        yield return Wait(6f);
        PutOutFires();
        yield return Wait(0.5f);

        // The fire boils some of the pond off: strike what's still wet.
        wet = wet.Where(c => World.At(c.x, c.y).Water > 0).ToList();
        var strike = wet.OrderBy(c => (c - bank).sqrMagnitude).Skip(3).First();
        caster.Cast(SpellIds.ShockingGrasp, strike.x, strike.y, 1);
        for (float t = 0f; t < 2f && World.At(strike.x, strike.y).Charge == 0; t += Time.deltaTime) yield return null;
        int charged = wet.Count(c => World.At(c.x, c.y).Charge > 0);
        Metric("charged_water_cells", charged);
        Check("charge_runs_through_water", charged > wet.Count / 2, $"{charged} of {wet.Count} wet cells charged");
        Shot("shock-flash");
        yield return null;
        yield return null;
    }

    // A house's window: glass, a crate thrown hard at it, then the gap. A crate tossed softly leaves the glass whole.
    IEnumerator Window()
    {
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).FirstOrDefault(v => v.villageName == "Hollowmere");
        var house = village != null ? village.GetComponentsInChildren<VillageHouse>().FirstOrDefault(h => h.walls == WallKind.Stone) : null;
        var panes = house != null ? house.GetComponent<WindowPanes>() : null;
        Check("house_with_panes", panes != null && panes.panes.Length > 0, panes != null ? $"{panes.panes.Length} panes" : "no stone house");
        if (panes == null || panes.panes.Length == 0) yield break;
        // A pane on a cell with a clear view of it.
        var pane = panes.panes.FirstOrDefault(p => Near(panes.transform.TransformPoint(p.at), out _) && Clear(panes.transform.TransformPoint(p.at), panes.transform.TransformDirection(p.facing)));
        var at = panes.transform.TransformPoint(pane.at);
        var facing = panes.transform.TransformDirection(pane.facing);
        Check("window_in_view", Near(at, out var cell), $"pane cell {cell}");
        var side = Vector3.Cross(Vector3.up, facing);
        var stand = grid.CellAt(at + facing * 8f + side * 3f);
        PlaceCell(stand.x, stand.y);
        Cut(at + facing * 3.2f + side * 1.2f + Vector3.up * 0.2f, at);
        yield return Wait(1.5f);
        Shot("window-glass");
        yield return null;

        var crate = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None).Where(p => p.kind == EntityIds.Crate && p.State != null)
            .OrderBy(p => (p.transform.position - at).sqrMagnitude).FirstOrDefault();
        Check("crate_near", crate != null, crate != null ? $"{Vector3.Distance(crate.transform.position, at):0} m away" : "no crate");
        if (crate != null)
        {
            WindowPanes.HardestHit = 0f;
            yield return HitWith(crate, at, facing, 1f);
            Metric("crate_hit_ns_1ms", WindowPanes.HardestHit);
            Check("soft_toss_leaves_pane", World.At(cell.x, cell.y).T == TerrainIds.Pane, $"the cell is {World.At(cell.x, cell.y).T} after 1 m/s");
            WindowPanes.HardestHit = 0f;
            yield return HitWith(crate, at, facing, 2.5f);
            Metric("crate_hit_ns_2_5ms", WindowPanes.HardestHit);
            Check("nudge_leaves_pane", World.At(cell.x, cell.y).T == TerrainIds.Pane, $"the cell is {World.At(cell.x, cell.y).T} after 2.5 m/s");
            WindowPanes.HardestHit = 0f;
            yield return HitWith(crate, at, facing, 8f);
            Metric("crate_hit_ns_8ms", WindowPanes.HardestHit);
            Check("hard_hit_breaks_pane", World.At(cell.x, cell.y).T != TerrainIds.Pane, $"the cell is {World.At(cell.x, cell.y).T} after 8 m/s");
        }
        if (World.At(cell.x, cell.y).T == TerrainIds.Pane)
            ChemEffects.Request(grid, new EffectCommand(EffectKind.Shatter, cell.x, cell.y, "", ThingId.None));
        yield return Wait(0.2f);
        Shot("window-shatter");
        yield return null;
        yield return Wait(2.5f);
        Shot("window-gap");
        yield return null;
        yield return null;
    }

    static IEnumerator HitWith(PhysicsProp crate, Vector3 at, Vector3 facing, float speed)
    {
        var body = crate.GetComponent<Rigidbody>();
        var from = at + facing * 1.6f;
        body.isKinematic = false;
        body.position = from;
        crate.transform.position = from;
        body.linearVelocity = -facing * speed;
        yield return Wait(0.5f);
    }

    // Whether nothing stands between a pane and the camera's spot before it.
    static bool Clear(Vector3 at, Vector3 facing) => !Physics.Linecast(at + facing * 0.4f, at + facing * 3.5f, ~0, QueryTriggerInteraction.Ignore);

    // The pane cell at a point, or the one beside it (a wall's line runs near a cell's edge).
    bool Near(Vector3 point, out Vector2Int cell)
    {
        var at = grid.CellAt(point);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                cell = new Vector2Int(at.x + dx, at.y + dy);
                if (World.InBounds(cell.x, cell.y) && World.At(cell.x, cell.y).T == TerrainIds.Pane) return true;
            }
        cell = default;
        return false;
    }

    IEnumerator Picture(string label, Vector3 mid)
    {
        Cut(mid + V(-4f, 3f, -4.5f), mid);
        PlaceCell(grid.CellAt(mid).x - 9, grid.CellAt(mid).y);
        yield return Wait(0.8f);
        Shot(label);
        yield return null;
        yield return null;
    }

    Vector3 Mid(Vector2Int o, int w, int h)
    {
        var a = grid.CellCenter(o.x, o.y);
        var b = grid.CellCenter(o.x + w - 1, o.y + h - 1);
        var mid = (a + b) / 2f;
        mid.y = grid.GroundAt(mid);
        return mid;
    }

    void Patch(Vector2Int o, int w, int h, string terrain)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                World.Paint(o.x + x, o.y + y, terrain);
                World.SetCover(o.x + x, o.y + y, null);
            }
    }
}
