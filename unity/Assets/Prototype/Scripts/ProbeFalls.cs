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

        // The cliff's rocks are solid: a ray from above lands on them, not through to the ground.
        var solid = falls.GetComponentsInChildren<MeshCollider>();
        int landed = solid.Count(mc => Physics.Raycast(mc.bounds.center + Vector3.up * (mc.bounds.extents.y + 5f), Vector3.down, out var hit, 60f) && hit.collider.transform.IsChildOf(falls.transform));
        Check("rocks_solid", solid.Length >= 20 && landed >= solid.Length * 0.8f, $"{landed} of {solid.Length} rock colliders under a ray from above");

        // The scree ramp beside the falls walks up to the top: the controller, stepped up it.
        var ramp = GameObject.Find("Scree ramp");
        Check("ramp_built", ramp != null && ramp.transform.childCount > 6, "the scree ramp up the east flank, lined with rocks");
        if (ramp == null || ramp.transform.childCount < 2) yield break;
        var stones = ramp.transform.Cast<Transform>().OrderBy(t => t.position.y).ToArray();
        Vector3 low = stones[0].position, high = stones[stones.Length - 1].position;
        var up = new Vector3(high.x - low.x, 0f, high.z - low.z);
        var body = wizard.GetComponent<CharacterController>();
        var mover = wizard.GetComponent("ThirdPersonController") as Behaviour;
        if (mover != null) mover.enabled = false;
        // The ramp's crown runs between the two lines of rocks: start in the middle at the bottom.
        var start = (stones[0].position + stones[1].position) / 2f;
        Teleport(start + Vector3.up * 1.5f);
        yield return Wait(0.5f);
        float from = wizard.transform.position.y;
        for (int i = 0; i < Mathf.RoundToInt(up.magnitude / 0.08f) + 20; i++) body.Move(up.normalized * 0.08f + Vector3.down * 0.1f);
        float climbed = wizard.transform.position.y - from;
        if (mover != null) mover.enabled = true;
        Metric("ramp_climb_m", climbed);
        Check("ramp_walks_up", wizard.transform.position.y > lip.y - 6f, $"climbed {climbed:0.0} m to {wizard.transform.position.y:0.0} (the lip is at {lip.y:0.0})");
        Shot("up-the-ramp");
    }
}
