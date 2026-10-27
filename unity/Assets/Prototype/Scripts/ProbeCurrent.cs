using System.Collections;
using UnityEngine;

// The probe's current scenario (docs/FAST_LOOP.md): the water carries things
// (WaterCurrents). The wizard is dropped mid-river and left still: it should be
// swept downstream. Then into the surf off the beach: the breakers should carry
// it up toward the sand. Then a crate floated on the river should drift down it.
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
        Teleport(start + Vector3.up * 0.5f + new Vector3(-downstream.y, 0f, downstream.x) * 8f);
        yield return Wait(2f);
        var crateFrom = crate.transform.position;
        yield return Wait(4f);
        var drift = crate.transform.position - crateFrom;
        float down = Vector2.Dot(new Vector2(drift.x, drift.z), downstream);
        Metric("crate_drift_m", down);
        Check("crate_drifts_downstream", down > 2f, $"{down:0.0} m downstream in 4 s");
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
