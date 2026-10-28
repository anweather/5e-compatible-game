using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's firelooks scenario (Andrew, 2026-10-28): fire by material. In
// the yard, a row of thatch beside a row of log wall and a strip of oil, all
// set alight: a picture of the thatch flaring next to the wood fire (and the
// oil's), and how long a thatch cell and a log wall cell burn. Then a crate
// set in fire: how long it takes to catch and to burn away (entities.json
// material, timber). It edits the yard's cells directly, so it runs with or
// without the fire looks: the before and after pictures come from one script.
public class FireLooksProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var o = grid.YardCell(24, 30);
        for (int i = 0; i < 4; i++)
        {
            Lay(o.x + i, o.y, "thatch", null);
            Lay(o.x + i, o.y + 3, TerrainIds.LogWall, null);
            Lay(o.x + i, o.y + 6, TerrainIds.Dirt, CoverIds.Oil);
        }
        var mid = grid.CellCenter(o.x + 2, o.y + 3);
        mid.y = grid.GroundAt(mid);
        Teleport(mid + new Vector3(-9f, 0.1f, 0f));
        Cut(mid + new Vector3(-7f, 5f, -7f), mid + Vector3.up * 0.8f);
        float lit = Time.time;
        for (int i = 0; i < 4; i++)
        {
            World.Apply(o.x + i, o.y, "fire", 3, -1, false);
            World.Apply(o.x + i, o.y + 3, "fire", 3, -1, false);
            World.Apply(o.x + i, o.y + 6, "fire", 3, -1, false);
        }
        yield return Wait(1.2f);
        Shot("thatch-wood-oil");
        yield return null;
        yield return null;
        float thatchOut = -1f, logOut = -1f;
        while (Time.time - lit < 60f && (thatchOut < 0f || logOut < 0f))
        {
            yield return Wait(0.25f);
            if (thatchOut < 0f && World.At(o.x, o.y).T != "thatch") thatchOut = Time.time - lit;
            if (logOut < 0f && World.At(o.x, o.y + 3).T != TerrainIds.LogWall) logOut = Time.time - lit;
        }
        Metric("thatch_burn_s", thatchOut);
        Metric("log_wall_burn_s", logOut);
        Check("thatch_burns_seconds", thatchOut >= 2f && thatchOut <= 6f, $"a thatch cell burned {thatchOut:0.0} s");
        Check("wood_burns_longer", logOut < 0f || logOut > thatchOut * 3f, logOut < 0f ? "the log wall still burning after 60 s" : $"a log wall cell burned {logOut:0.0} s");
        yield return Crate();
    }

    // A crate stood in fire: when it catches and when it's gone.
    IEnumerator Crate()
    {
        var crate = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None).FirstOrDefault(p => p.kind == EntityIds.Crate && p.State != null);
        if (crate == null)
        {
            Check("crate_found", false, "no crate");
            yield break;
        }
        var cell = grid.CellAt(crate.transform.position);
        Teleport(crate.transform.position + new Vector3(-6f, 0.1f, 0f));
        float start = Time.time, caught = -1f;
        while (Time.time - start < 60f && crate != null && !crate.State.Destroyed)
        {
            // Oil under it, kept alight: the dirt it may stand on doesn't burn.
            if (World.At(cell.x, cell.y).Fire == 0)
            {
                World.SetCover(cell.x, cell.y, CoverIds.Oil);
                World.Apply(cell.x, cell.y, "fire", 2, -1, false);
            }
            if (caught < 0f && crate.State.Burning) caught = Time.time - start;
            yield return Wait(0.25f);
        }
        float gone = Time.time - start;
        Metric("crate_catch_s", caught);
        Metric("crate_gone_s", gone);
        Check("crate_catches", caught > 0f, $"the crate caught after {caught:0.0} s in fire, gone after {gone:0.0} s");
    }

    // The sandbox's paint brushes: a terrain, then a cover on it (or none).
    void Lay(int x, int y, string terrain, string cover)
    {
        World.Paint(x, y, terrain);
        if (cover != null) World.SetCover(x, y, cover);
    }
}
