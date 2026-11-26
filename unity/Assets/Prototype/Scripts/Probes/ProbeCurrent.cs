using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The probe's current scenario (docs/FAST_LOOP.md): the water carries things
// (WaterCurrents). The wizard is dropped mid-river and left still: it should be
// swept downstream. Then into the surf off the beach: the breakers should carry
// it up toward the sand. Then into the surf off the island, on its mainland side,
// where only the nearest land can explain a push away from the mainland. Then a
// crate floated on the river should drift down it. A still view of the waves off
// the island gives the sea's frame times, and --shots saves the views to judge
// the waves by.
public class CurrentProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var currents = WaterCurrents.Instance;
        Check("currents", currents != null && currents.river.Length > 1, "WaterCurrents with the river's line");
        if (currents == null || currents.river.Length < 2) yield break;

        // Mid-river, a third of the way down from the falls.
        int i = currents.river.Length / 3;
        var a = currents.river[i];
        var downstream = (currents.river[i + 1] - a).normalized;
        var start = Ground(a);
        Teleport(start + Vector3.up * 0.5f);
        Cut(start + V(-downstream.y * 9f, 6f, downstream.x * 9f), start);
        yield return Wait(1f);
        var from = wizard.transform.position;
        yield return Wait(3f);
        var moved = wizard.transform.position - from;
        float along = Vector2.Dot(new Vector2(moved.x, moved.z), downstream);
        Metric("river_carry_m", along);
        Check("river_carries_wizard", along > 2f, $"{along:0.0} m downstream in 3 s");
        Shot("river");

        // In the surf off the beach: the breakers shove it ashore.
        var shore = -currents.seaward.normalized;
        var surf = SurfSpot(currents, 2.2f);
        Check("surf_found", surf.HasValue, "water about 2 m deep off the beach");
        if (surf.HasValue)
        {
            Teleport(surf.Value + Vector3.up * 0.3f);
            Cut(surf.Value + new Vector3(shore.y * 10f, 5f, -shore.x * 10f), surf.Value);
            yield return Wait(1f);
            from = wizard.transform.position;
            yield return Wait(5f);
            moved = wizard.transform.position - from;
            float ashore = Vector2.Dot(new Vector2(moved.x, moved.z), shore);
            Metric("surf_carry_m", ashore);
            Check("surf_carries_wizard_ashore", ashore > 1.5f, $"{ashore:0.0} m toward the beach in 5 s");
            Shot("surf");
        }

        // In the surf off the island, on its mainland side: the island is the nearest land.
        var sideways = Vector2.Perpendicular(currents.seaward.normalized);
        var island = IslandSurf(currents, 2.2f);
        Check("island_surf_found", island.HasValue, "water about 2 m deep off the island's mainland side");
        if (island.HasValue)
        {
            var toIsland = currents.seaward.normalized;
            Teleport(island.Value + Vector3.up * 0.3f);
            Cut(island.Value + new Vector3(sideways.x * 10f, 5f, sideways.y * 10f), island.Value);
            yield return Wait(1f);
            from = wizard.transform.position;
            yield return Wait(5f);
            moved = wizard.transform.position - from;
            float carried = Vector2.Dot(new Vector2(moved.x, moved.z), toIsland);
            Metric("island_carry_m", carried);
            Check("surf_carries_wizard_to_island", carried > 1.5f, $"{carried:0.0} m toward the island in 5 s");
            Shot("island-surf");
            Teleport(start + Vector3.up * 0.5f);
            yield return SeaViews(currents, island.Value);
        }

        // A crate on the river floats downstream.
        // A crate (not the court's ball, which the court puts back in play).
        PhysicsProp crate = null;
        foreach (var prop in FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None))
            if (prop.name.StartsWith("crate", System.StringComparison.Ordinal)) crate = prop;
        Check("crate_found", crate != null, "a physics prop to float");
        if (crate == null) yield break;
        var b = currents.river[i + 3];
        var on = Ground(b) + Vector3.up * 2.5f;
        var rb = crate.GetComponent<Rigidbody>();
        rb.position = on;
        rb.linearVelocity = Vector3.zero;
        crate.transform.position = on;
        var across = Vector2.Perpendicular(downstream);
        Teleport(start + Vector3.up * 0.5f + new Vector3(across.x, 0f, across.y) * 8f);
        yield return Wait(2f);
        var crateFrom = crate.transform.position;
        yield return Wait(4f);
        var drift = crate.transform.position - crateFrom;
        float down = Vector2.Dot(new Vector2(drift.x, drift.z), downstream);
        Metric("crate_drift_m", down);
        Check("crate_drifts_downstream", down > 2f, $"{down:0.0} m downstream in 4 s");
    }

    // The waves off the island at noon, in clear weather, the wizard out of the way: a still view
    // for the frame times, then the views to look at for seams.
    IEnumerator SeaViews(WaterCurrents currents, Vector3 surf)
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var dir = currents.seaward.normalized;
        var out3 = new Vector3(dir.x, 0f, dir.y);
        var side = new Vector3(-dir.y, 0f, dir.x);
        Cut(surf - out3 * 34f + side * 14f + Vector3.up * 13f, surf + out3 * 18f);
        yield return Wait(2f);
        var ms = new List<float>();
        var main = new List<float>();
        var render = new List<float>();
        var gpu = new List<float>();
        for (float end = Time.realtimeSinceStartup + 3f; Time.realtimeSinceStartup < end;)
        {
            ms.Add(Time.unscaledDeltaTime * 1000f);
            FpsProbe.ReadTiming(main, render, gpu);
            yield return null;
        }
        Metric("sea_frame_ms", Median(ms));
        Metric("sea_gpu_ms", Median(gpu));
        Shot("waves");
        yield return Wait(0.2f);
        if (string.IsNullOrEmpty(ScreenshotDir)) yield break;

        // Close above the beach, two shots a second apart: the crests should move toward the sand.
        Cut(surf + out3 * 4f + Vector3.up * 26f, surf + out3 * 6f + side * 0.5f);
        yield return Wait(1f);
        Shot("crests-a");
        yield return Wait(1f);
        Shot("crests-b");
        yield return Wait(0.2f);

        // Down on the strait, where the mainland's shore and the island's are about as near.
        var strait = surf - out3 * 55f;
        Cut(strait + side * 10f - out3 * 40f + Vector3.up * 150f, strait);
        yield return Wait(1f);
        Shot("strait");
        yield return Wait(0.2f);

        // Round the island's far side.
        Cut(surf + out3 * 210f + side * 90f + Vector3.up * 70f, surf + out3 * 90f);
        yield return Wait(1f);
        Shot("far-side");
        yield return Wait(0.2f);

        // Where the shore map ends: the waves die away before its eastern edge, with no line there.
        var map = currents.shoreMap;
        if (map == null) yield break;
        var eastEdge = new Vector3(map.origin.x + map.size, currents.seaLevel, map.origin.y + map.size * 0.5f);
        Cut(eastEdge + new Vector3(-430f, 240f, -60f), eastEdge + new Vector3(-150f, 0f, 0f));
        yield return Wait(1f);
        Shot("map-edge");
        yield return Wait(0.2f);
    }

    static float Median(List<float> values)
    {
        if (values.Count == 0) return -1f;
        values.Sort();
        return values[values.Count / 2];
    }

    // The water about this deep off the island: the last such spot on a line out from the mainland
    // before the line comes ashore again. Off the middle of the island, clear of the dock.
    Vector3? IslandSurf(WaterCurrents currents, float depth)
    {
        var dir = currents.seaward.normalized;
        var start = currents.shoreOrigin - Vector2.Perpendicular(dir) * 15f;
        Vector3? last = null;
        for (float d = 40f; d < 300f; d += 1f)
        {
            var at = start + dir * d;
            var cell = grid.CellAt(new Vector3(at.x, 0f, at.y));
            float deep = currents.seaLevel - grid.valley.BaseHeight(cell.x, cell.y);
            if (deep <= 0f) return last;
            if (deep >= depth) last = new Vector3(at.x, currents.seaLevel - 1.2f, at.y);
        }
        return null;
    }

    // A point at the water's surface over the given (x, z): ground of the cell, lifted to the water.
    Vector3 Ground(Vector2 at)
    {
        var p = new Vector3(at.x, 0f, at.y);
        var cell = grid.CellAt(p);
        return new Vector3(at.x, grid.WaterSurfaceY(cell.x, cell.y) - 1.2f, at.y);
    }

    // Out from the coast along the seaward line until the water is about this deep.
    Vector3? SurfSpot(WaterCurrents currents, float depth)
    {
        var dir = currents.seaward.normalized;
        for (float d = -40f; d < 120f; d += 1f)
        {
            var at = currents.shoreOrigin + dir * d;
            var cell = grid.CellAt(new Vector3(at.x, 0f, at.y));
            var c = World.At(cell.x, cell.y);
            if (c == null || c.Water <= 0) continue;
            float ground = grid.valley.BaseHeight(cell.x, cell.y);
            if (currents.seaLevel - ground >= depth) return new Vector3(at.x, currents.seaLevel - 1.2f, at.y);
        }
        return null;
    }
}
