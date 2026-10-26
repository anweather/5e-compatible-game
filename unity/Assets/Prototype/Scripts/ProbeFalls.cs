using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's falls scenario (docs/FAST_LOOP.md): the waterfall at the head
// of the river (ValleyBuilder.BuildWaterfall, WaterfallFx). The wizard goes
// to the pond at its foot. Checks the falls is built (its water, rocks and
// splashes), the pond is river water and full, the river runs on from it,
// splashes go up where the water lands, the roar plays. Shots from the pond
// and from the cliff top.
public class FallsProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var falls = GameObject.Find("Falls");
        Check("falls_built", falls != null && falls.GetComponent<WaterfallFx>() != null && GameObject.Find("Falls water") != null,
            "the Falls object with its WaterfallFx and its sheet of water");
        if (falls == null) yield break;
        var fx = falls.GetComponent<WaterfallFx>();
        int rocks = falls.GetComponentsInChildren<LODGroup>().Length;
        Metric("cliff_rocks", rocks);
        Check("cliff_clad", rocks >= 20, $"{rocks} rocks on the cliff");

        yield return Switch(At(12f));
        var pond = grid.CellAt(fx.foot);
        PlaceCell(pond.x + 6, pond.y + 7);
        var at = fx.foot;
        Cut(at + V(-16f, 9f, -32f), at + V(0f, 9f, 4f));
        yield return Wait(3f);
        var c = World.At(pond.x, pond.y);
        Check("pond_is_river", c.T == TerrainIds.River && c.Water >= 4, $"the pond's middle is {c.T} with {c.Water} units");
        int downstream = 0;
        for (int dy = 8; dy <= 20; dy++)
            for (int dx = -6; dx <= 10; dx++)
                if (World.At(pond.x + dx, pond.y + dy).T == TerrainIds.River) downstream++;
        Check("river_runs_on", downstream > 10, $"{downstream} river squares south of the pond");
        int splashes = fx.transform.Cast<Transform>().Count(t => t.name.StartsWith("BigSplash", System.StringComparison.Ordinal));
        Metric("splashes_now", splashes);
        Check("splashes", splashes > 0, $"{splashes} splashes going up at the foot");
        var roar = fx.GetComponentInChildren<AudioSource>();
        Check("roars", roar != null && roar.isPlaying, "the falls' roar");
        Shot("from-the-pond");
        yield return Wait(0.3f);

        var lip = GameObject.Find("Falls water").transform.position;
        Cut(lip + V(3f, 14f, 22f), lip + V(0f, -14f, -12f));
        yield return Wait(1f);
        Shot("from-the-top");
    }
}
